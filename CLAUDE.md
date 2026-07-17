# VibeSkua — Native Linux Port

Port the VibeSkua AQW bot (fork of Skua) to run **natively on Linux**. No Wine, no VM.
Target: Linux first (AppImage), public repo with releases. Existing Windows/WPF build stays separate.

---

## Architecture: 3 layers

| Layer | What | Status |
|---|---|---|
| **1. Core engine** (~40k LOC) | `Skua.Core`, `.Interfaces`, `.Models`, `.Utils`, `.Generators` | ✅ **DONE — compiles native `net10.0` on Linux** |
| **2. UI** (~9.5k LOC XAML) | `Skua.WPF` → new `Skua.Avalonia` | ⬜ Not started. Mechanical. |
| **3a. Flash runtime** | Ruffle replaces Flash ActiveX | ✅ **PROVEN — AQW renders in Ruffle on Linux** |
| **3b. ExternalInterface bridge** | Rust `ruffle_core` host ↔ C# | ✅ **DONE — embeds real `ruffle_core`, round-trips both directions** (`--features ruffle`; `tests/ruffle_roundtrip.rs`). **Blocked next step:** same-domain `skua.swf` injection (what lets the bot *drive* the game) needs `ruffle_core` internals that are **private modules** — not an egress problem; requires a vendored/forked `ruffle_core` with a `pub Player::inject_swf_same_domain`. See `LINUX.md` + `src/ruffle_runtime.rs::inject_swf_same_domain`. |

---

## PROVEN FACTS (verified, do not re-litigate)

### Layer 1 is done — 3 lines
`Skua.Core` and `Skua.Core.Interfaces`: `net10.0-windows` → `net10.0`, and drop the dead
`CoreHook` PackageReference from `Skua.Core`. That's it. See `layer1-linux-core.patch`.

Result: **`dotnet build Skua.Core -c Release -p:Platform=AnyCPU` → 0 errors, 0 warnings on Linux.**
All 5 Core assemblies build. Zero C# code changes were needed.

Why it was so easy:
- All **89 ViewModels live in `Skua.Core`** using `CommunityToolkit.Mvvm` — UI-framework-agnostic, Avalonia-ready as-is.
- `IScriptPlayer.cs`'s `using System.Drawing` is fine — `System.Drawing.Point` is in `System.Drawing.Primitives` (cross-platform). Only `System.Drawing.Common`/GDI+ is Windows-only.
- `HotKeys.cs`'s `user32.dll` P/Invokes **compile fine** (P/Invoke binds lazily at call time). They only need runtime stubs — see Layer 2 tasks.
- `CoreHook` (Windows API hooking) is referenced by `Skua.Core.csproj` but only *used* in `Skua.WPF/Flash/EoLHook.cs`. Dead ref in Core.

### Layer 3a: Ruffle runs AQW
Verified by running the game to the login screen, rendered correctly, on a GPU-less Linux
container via llvmpipe software rasterization. Zero AVM2 errors.

```bash
ruffle "https://game.aq.com/game/gamefiles/Loader3.swf?ver=a" \
  --tcp-connections allow --player-version 9 --width 960 --height 580
```

**Two non-obvious gotchas — both cost hours if rediscovered:**

1. **USE A CURRENT RUFFLE NIGHTLY.** The Artix Game Launcher ships Ruffle
   `nightly-2025-05-06`, which throws `TypeError #1009` on boot and fails.
   `0.4.0-nightly.2026.7.12` throws **nothing**. 14 months of AVM2 work is the whole difference.
   Strongly suspected reason Artix has `enableRuffle = false` hardcoded in their launcher —
   they tested against a version that genuinely didn't work and never revisited.

2. **SERVE FROM HTTPS.** AQW calls `SharedObject.getLocal(secure: true)`. Over any `http://`
   origin Ruffle correctly refuses, returns `null`, and `Game.onAddedToStage()` instantly
   dereferences it → `Error #1009`. If you set up a dev/caching proxy, it MUST be https or
   you will reintroduce this bug and think Ruffle is broken.

Benign stubs seen (safe to ignore): `flash.system.Security.allowDomain()`,
`flash.utils.Dictionary` weak keys, `flash.display.Loader.load()` addChild timing.

### Licensing (decides the runtime choice)
- **Ruffle = Apache-2.0 / MIT → bundle it in releases.** ✅
- `libpepflashplayer.so` (Adobe Pepper Flash, shipped in the Artix launcher, v32.0.0.344) is
  **proprietary and NOT redistributable**. It works — it's what AQW actually uses today via
  Electron 8 / Chromium 80 — but it cannot go in a public release. Ruffle working means we
  never need this fallback.

---

## The `IFlashUtil` seam (this is the whole port)

`Skua.Core` **never touches Flash directly.** It goes through `IFlashUtil`
(`Skua.Core.Interfaces/Flash/IFlashUtil.cs`). The Windows implementation using
`AxShockwaveFlash` (ActiveX/COM) is quarantined in `Skua.WPF/Flash/FlashUtil.cs`.

The entire contract reduces to **two XML pipes**:

- **Host → AS3:** `CallFunction("<invoke name=...><arguments>...</arguments></invoke>")` → returns XML
- **AS3 → Host:** the `FlashCall` event, also XML
- plus: load `skua.swf` bytes into the player

Everything else in `IFlashUtil` (`GetGameObject`, `SetGameObject`, `CallGameFunction`, …) is
**default interface methods built on top of `Call()`**. So a Linux backend only has to satisfy
those two pipes and everything above it works unchanged.

---

## PROGRESS (native Linux port — see `LINUX.md` for the full status)

- **Layer 1 done & verified on Linux:** `dotnet build Skua.Core -c Release` → 0 errors (`net10.0`).
- **Layer 3b transport done & tested:** `native/skua-flash-bridge` → `libskua_flash.so`
  (zero-dep Rust cdylib: ExternalInterface XML codec + C ABI + `FlashRuntime` trait + offline mock).
- **Layer 3b C# backend done:** `Skua.Flash.Linux/RuffleFlashUtil.cs` (`IFlashUtil` via `[DllImport]`).
- **End-to-end verified:** `Skua.App.Console` → 10/10 native checks, incl. `GetGameObject<string>("world.strMapName")`
  round-tripping through the `.so`, the `FlashCall` event, and Skua's Roslyn `Compiler` running a script.
- **Build:** `dotnet build Skua.Linux.sln -c Release` (excludes WPF). CI: `.github/workflows/linux-build.yml`.
- **Ruffle runtime DONE:** `RuffleRuntime : FlashRuntime` embeds a real `ruffle_core::Player` and
  round-trips ExternalInterface both ways against a real AVM2 SWF (`cargo test --features ruffle`).
  Build the ruffle `.so` with a nightly Rust toolchain: `cargo build --release --features ruffle`.
- **Game view + army DONE:** offscreen wgpu render → Avalonia `GameView` (real pixels, verified with
  lavapipe), a minimal HTTP+socket navigator so the live game loads and reaches the server (the
  socket/timer pump fix lets AQW get past "Connecting to game server…"), and multi-client `--client`
  windows so the manager can run an army (`IClientLauncher` relaunches the AppImage per account).
- **Bot *control* — ARCHITECTURE CORRECTED to root-movie boot (the Windows way):** injection
  (loading skua.swf BESIDE an already-running game) registers callbacks but leaves
  `Main.instance.game` null — skua.swf is *designed to load the game itself*
  (`loadClient` → fetches gameversion API → `Loader.load` → `this.game = loader.content`),
  and every API call resolves through that `game` reference. A tester confirmed the symptom:
  scripts "ran" but did nothing. The Linux client now boots **skua.swf as the ROOT movie from
  local bytes** (`RenderHost::create_from_bytes`, nominal origin `https://game.aq.com/game/skua.swf`
  — https matters for `SharedObject.getLocal(secure)`), and `RuffleFlashUtil.BindRenderer` performs
  the Windows startup handshake: poll `isTrue` until skua.swf's callbacks register, then call
  `loadClient` exactly once. skua.swf must be resolved via `AppContext.BaseDirectory` (NOT a
  CWD-relative path — an AppImage's CWD is wherever the user launched from). Verified by
  `tests/ruffle_render.rs::host_boots_root_movie_from_bytes_and_answers_the_bot`.
  The `inject_swf_same_domain` fork patch remains (tests use it) but is no longer the bot path.

## 🎉 MILESTONE (2026-07-17, v0.1.28): THE PORT WORKS END TO END

**Verified on a tester's real machine (RTX 2060, Vulkan): the game renders, login
works, a real bot script (HollowbornOrbQuests — CoreBots + 7 includes) compiles,
starts, and DRIVES THE CHARACTER in live AQW.** Native Linux, no Wine, Ruffle
instead of Flash. Two consecutive clean boots logged
(`bridge ready → loadClient → pre-load → loaded → World load check passed`).

Hard-won lessons already fixed — do NOT regress these (each has a test):
- **Zero-arg EI invokes** (`isTrue`, `loadClient`) have NO `<arguments>` block —
  `parse_invoke` must accept that form (tests: roundtrip.rs + real-skua handshake).
- **FlashCall events dispatch on a dedicated thread** (never the render worker —
  handlers re-enter `Call()`; multicast delegates invoked per-subscriber so one
  throwing handler can't eat events).
- **Single game instance**: `GameSession.StartAsync` is single-flight (the view's
  auto-start double-fires; two players = user watches a black twin).
- **`ScriptLoadContext` resolves from the VERSIONED cache dir** and cache-hit
  includes load eagerly into each run's context (runtime FileNotFound otherwise).
- **Includes resolve case-insensitively with backtracking** (Windows-cased
  `//cs_include` paths + phantom wrong-case dirs from old builds).
- **Script source is neutralized (Windows gates) BEFORE cache hashing.**
- Telemetry lives in `~/.config/Skua/vibeskua-{game,client,ruffle,crash}.log`
  (timestamped + PID); the game view's status line narrates the boot.

## NEXT: tweak & fix (feature parity / polish)

- MessageBox/dialog windows spawn as scattered top-level windows — make them
  modal children of the client window (real Skua parity).
- Army at scale: multiple `--client` windows, render-pause on unfocus, manager UX.
- Auto-login / account manager flow end-to-end.
- Performance tuning (tick rate, capture cadence, lag options in scripts).
- Remaining Avalonia views/panels polish (Layer 2 grind).

---

## Layer 2 (Avalonia) — start after 3b, or in parallel

- 86 XAML files, ~9.5k LOC in `Skua.WPF` / `Skua.App.WPF`. Pure view rewrite — **ViewModels are already portable.**
- `NHotkey.Wpf` → SharpHook (or X11 grabs).
- `HotKeys.cs::ExecuteOrForward` uses `WM_COPYDATA` (`SendMessage`) to forward hotkeys to child
  instances (Army Control). Replace with `NamedPipeServerStream` — works on Linux over Unix
  domain sockets. Put it behind an `IHotkeyForwarder` interface so WPF keeps WM_COPYDATA.
- `MemoryUtils` working-set trimming (`SetProcessWorkingSetSize`) → just delete on Linux.
- Headless mode already exists (`GameContainerUserControl` collapses the viewport to 1x1), so
  rendering fidelity is not critical for farm/army use.

## Releases
- `Velopack` is **already a dependency** and supports Linux AppImage. Reuse it.
- Bundle a current Ruffle nightly (Apache-2.0/MIT — legal). Never bundle `libpepflashplayer.so`.

## Environment
- .NET 10 SDK (`10.0.301` verified working)
- Rust toolchain (for the ruffle_core host)
- Note: WPF projects **cannot build on Linux at all** (the XAML markup compiler is Windows-only).
  Keep `Skua.WPF` / `Skua.App.WPF` out of the Linux solution filter.
