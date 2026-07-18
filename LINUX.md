# VibeSkua on Linux — native, no Wine, no VM

This is the Linux port of VibeSkua (a fork of Skua, the AQW automation tool). It
runs the engine as native `net10.0` and replaces the Windows Flash ActiveX
control with [Ruffle](https://ruffle.rs) via a small Rust bridge. The existing
Windows/WPF build is unchanged and lives alongside this work.

## Status

| Layer | What | Status |
|-------|------|--------|
| **1. Core engine** (~40k LOC) | `Skua.Core`, `.Interfaces`, `.Models`, `.Utils`, `.Generators` | ✅ **builds native `net10.0` on Linux, 0 errors** |
| **3b. Flash transport** | `libskua_flash.so` — ExternalInterface XML pipes + C ABI | ✅ **done + tested** (`native/skua-flash-bridge`) |
| **3b. C# Flash backend** | `RuffleFlashUtil : IFlashUtil`, P/Invokes the bridge | ✅ **done, round-trips end to end** (`Skua.Flash.Linux`) |
| **Script engine** | Roslyn/Westwind compiler | ✅ **compiles + runs scripts on Linux** |
| **Full engine graph** | entire `IScriptInterface` bot (~40 services) | ✅ **resolves + runs on Linux** via real `Skua.Core.AppStartup` DI |
| **3b. Ruffle runtime** | embed `ruffle_core`, real `ExternalInterfaceProvider` | ✅ **built & round-tripping against real `ruffle_core`** — `src/ruffle_runtime.rs` embeds a `ruffle_core::Player` (on a dedicated thread, behind a command channel, since `Player` is `!Send`) and `tests/ruffle_roundtrip.rs` drives a real AVM2 SWF end-to-end: **host→AS3** `call_internal_interface` round-trips a value, **AS3→host** `ExternalInterface.call` reaches our provider. `cargo build --release --features ruffle` → 13 MB `libskua_flash.so` with ruffle_core embedded. **Live-verified on real hardware (v0.1.28):** the game renders, login works, and a real bot script drives the character. |
| **2. UI** (~9.5k LOC XAML) | `Skua.WPF` → new `Skua.Avalonia` | ✅ **whole UI ported (61 views), app runs & hosts the full engine** — Avalonia app + DI + ViewLocator + navigable shell, **all 11 Linux platform services**, the complete main-app ViewModel graph, **all 6 dialogs** (modal, hosted in `HostDialogWindow`), and the **Skua.Manager multi-account launcher** (accounts/groups, script/client updaters, options) — all wired via `Skua.Core.AppStartup` + `RuffleFlashUtil`. Headless test suite, 0 failures. |
| **Packaging** | Velopack AppImage + embedded Ruffle | ✅ **done** — `packaging/build-appimage.sh` (Velopack `vpk`, self-contained publish, real-ruffle `.so` by default), published by `.github/workflows/release-linux.yml` with notes from `CHANGELOG.md` |

### What works today, verified on Linux

`Skua.App.Console` is a headless smoke test that exercises the whole native
stack. It reports **12/12** checks — including that the **entire Skua Bot
(`IScriptInterface`) resolves and runs on Linux**: the full engine service graph
(all ~40 `IScript*` services — player, quest, shop, combat, inventory, map,
drops, …) is built from the real `Skua.Core.AppStartup` DI registrations, backed
by the Linux `RuffleFlashUtil`, and round-trips game data. Only two platform
services the engine needs live outside Core (`ISettingsService`, `IDialogService`),
both trivial. This establishes that the whole engine — not just the Flash seam —
is Linux-ready. Sample checks:

```
[ok]   InitializeFlash() — libskua_flash.so loaded and ABI matched
[ok]   GetGameObject<string>("world.strMapName") == "battleon"
[ok]   GetGameObject<int>("world.myAvatar.objData.intHP") == 1000
[ok]   IsNull("world") == false / IsNull("world.doesNotExist") == true
[ok]   IsWorldLoaded == true
[ok]   FlashCall event delivered (AS3 -> host)
[ok]   Script compiled + ran (Skua's Compiler on Linux); Compute(2,3)=5
```

That path is the real one Skua scripts use: `IFlashUtil.GetGameObject<T>` →
`RuffleFlashUtil.Call` → P/Invoke into `libskua_flash.so` → back to C# →
JSON-deserialize. The only piece standing between this and a live game is the
Ruffle runtime behind the bridge's `FlashRuntime` trait (see Roadmap).

## Build & run

Prerequisites: **.NET 10 SDK** and a **Rust toolchain**.

```bash
# On Debian/Ubuntu the SDK is packaged:
sudo apt-get install -y dotnet-sdk-10.0
# Rust: https://rustup.rs

# Build everything Linux-buildable (also builds the Rust .so):
dotnet build Skua.Linux.sln -c Release

# Run the headless native smoke test:
$(find Skua.App.Console/bin -name Skua.App.Console -type f | head -1)

# Rust bridge tests on their own (offline mock runtime, stable Rust):
cd native/skua-flash-bridge && cargo test

# Real Ruffle runtime — embeds ruffle_core, round-trips ExternalInterface both
# ways against a real AVM2 SWF (needs a nightly toolchain + network):
rustup toolchain install nightly
cd native/skua-flash-bridge && cargo +nightly test --features ruffle --test ruffle_roundtrip

# Build the app/console with the real-ruffle .so bundled (for releases):
dotnet build Skua.Avalonia -c Release -p:SkuaRuffle=true   # else defaults to the mock .so

# Avalonia UI (Layer 2) — build and run the app, or run its headless tests:
dotnet run --project Skua.Avalonia            # opens the window (needs a display)
dotnet test Skua.Avalonia.Tests               # headless, no display required
```

`Skua.Linux.sln` deliberately contains only the projects that build on Linux.
The WPF/WinForms projects (`Skua.WPF`, `Skua.App.WPF*`) are excluded — their XAML
markup compiler is Windows-only and they cannot build here at all.

## How the port fits together

`Skua.Core` never touches Flash directly; it goes through `IFlashUtil`, and the
entire contract reduces to two XML pipes plus SWF loading (see `CLAUDE.md`). On
Windows that interface is implemented against `AxShockwaveFlash`; on Linux it is
implemented against Ruffle:

```
Windows:  Skua.Core ──IFlashUtil──> Skua.WPF/Flash/FlashUtil.cs ──> AxShockwaveFlash (ActiveX)
Linux:    Skua.Core ──IFlashUtil──> Skua.Flash.Linux/RuffleFlashUtil.cs ──DllImport──> libskua_flash.so (ruffle_core)
```

Both sides speak the identical ExternalInterface XML wire format, so **the same
`skua.swf` and all of `IFlashUtil`'s default methods work unchanged** — no C#
changes were needed in `Skua.Core` for Layer 1.

- `native/skua-flash-bridge/` — the Rust `cdylib`. Zero external crates; the
  ExternalInterface codec is hand-rolled so it builds and tests fully offline.
  See its `README.md` for the C ABI and the ruffle_core wiring plan.
- `Skua.Flash.Linux/` — the C# `IFlashUtil` backend + P/Invoke bindings.
- `Skua.App.Console/` — headless host / smoke test.

## Roadmap (feature parity / polish — the port itself works end to end)

Done: modal owner-centered dialogs; army multi-client (`--client` window per
account) with render-pause on unfocus; per-account auto-login +
`--run-script`; background render-pause / status-throttle perf work.

Remaining: Avalonia view/panel polish, driven by concrete user-reported rough
edges (don't speculatively rewrite working views).

Never bundle `libpepflashplayer.so` (proprietary); Ruffle is Apache-2.0/MIT and
ships embedded.

## Avalonia UI (Layer 2) — complete

The whole WPF UI is ported to Avalonia: **61 views** across the main bot app,
the six dialogs, and the Skua.Manager multi-account launcher, all driven by the
portable `Skua.Core` ViewModels through a convention `ViewLocator` (with a flat
fallback for VMs in sub-namespaces like `.Manager`). The app boots on Linux,
hosts the full `IScriptInterface` engine graph via `Skua.Core.AppStartup` DI,
and every panel's ViewModel resolves against that graph. **The headless test
suite (Avalonia.Headless.XUnit) passes with 0 failures.**

**Main app** — navigable shell + panels: stats, current/to-pickup drops, boosts,
console, auto, jump, registered quests, fast travel (+ editor), loadouts, script
scheduler, packet spammer/logger/interceptor, script repo/loader, runtime
helpers, advanced skills (+ saved), themes (settings/background/color-scheme),
hotkeys, logs, loader, grabber, plugins, game/application options, CoreBots
(+ options / class-select / equipment / loadout / other), junk items, notify
drop, about, changelogs, goals, skill rules (+ aura check), advanced-skill
editor, GitHub auth, launcher, app updater.

**Dialogs (6):** `MessageBoxDialog`, `CustomDialog`, `AssignHotKeyDialog`,
`SkillRuleEditorDialog`, `FastTravelEditorDialog`, `SelectGroupDialog` — hosted
via `ContentControl` + `ViewLocator`.

**Skua.Manager (multi-account launcher / army control):** `ManagerMainView`
tab host + `AccountManagerView` (accounts, groups, add-account form, server
select, start/save controls), `ScriptUpdaterView`, `ClientUpdatesView`,
`ManagerOptionsView`. On Windows this is a separate executable; on Linux it
rides in the same Avalonia app and is registered via the new public factories
`Services.CreateManagerMainViewModel` / `CreateManagerOptionsViewModel`.

### Multi-client / army (pop-out client windows)

The bot runs as an **army of independent client windows**, matching the Windows
manager→clients model without a second binary:

- **`--client` mode.** Launched with `--client` (plus optional `--instance <name>`
  / `--account <name>`), the app opens the main window scoped to a bot client
  (`ApplyScope(client:true)` drops the manager/army tabs), opening on the Game
  tab with the game auto-started on its own live Ruffle player. The instance
  name shows in the window title so army members are distinguishable.
- **`IClientLauncher` seam** (`Skua.Core.Interfaces`). The Linux `ClientLauncher`
  relaunches **this** executable (prefers `$APPIMAGE`, else `Environment.ProcessPath`)
  with `--client`. `LauncherViewModel.LaunchSkua` and `AccountManagerViewModel`'s
  per-account start now go through it (each forwards `-u/-p/-s`, theme and
  `--run-script` args for future auto-login), falling back to the legacy
  `./Skua.exe` launch when no launcher is registered — so the WPF app is
  unchanged. Spawned processes are tracked/killable in the Launcher's process list.

So **"Launch Skua"** (or starting an account) pops out a new game window; do it N
times for an N-account army. Per-account auto-login into the game itself lands
with `skua.swf` injection (the forwarded credentials args are already plumbed).

### Bot *control* — WORKS (root-movie boot, the Windows way)

**Live-verified end to end (v0.1.28, real hardware):** the game renders, login
works, a real bot script (CoreBots + includes) compiles, starts, and drives the
character in live AQW.

**Architecture (corrected from the earlier injection approach).** Injection —
loading `skua.swf` *beside* an already-running game — registers the
`ExternalInterface` callbacks but leaves `Main.instance.game` null: `skua.swf`
is *designed to load the game itself* (`loadClient` → gameversion API →
`Loader.load` → `this.game = loader.content`), and every API call resolves
through that `game` reference (a tester confirmed the symptom: scripts "ran"
but did nothing). So, exactly like the Windows client, the Linux client boots
**`skua.swf` as the ROOT movie from local bytes**
(`RenderHost::create_from_bytes`, nominal origin
`https://game.aq.com/game/skua.swf` — https matters for
`SharedObject.getLocal(secure)`), and `RuffleFlashUtil.BindRenderer` performs
the Windows startup handshake: poll `isTrue` until skua.swf's callbacks
register, then call `loadClient` exactly once. `skua.swf` is resolved via
`AppContext.BaseDirectory` (not the CWD — an AppImage's CWD is wherever the
user launched from). Verified by
`tests/ruffle_render.rs::host_boots_root_movie_from_bytes_and_answers_the_bot`.

The `inject_swf_same_domain` fork patch (`native/ruffle-skua-inject.patch`,
applied by `native/prepare-ruffle-fork.sh`) remains — tests use it — but it is
no longer the bot path.

**Services (11):** `ProcessService`, `ClipboardService`, `DispatcherService`,
`SettingsService`, `DialogService`, `SoundService`, `WindowService`,
`FileDialogService`, `ScreenshotService`, `ThemeService`, `HotKeyService`.

Dialogs are fully modal: typed dialogs (`ShowDialog<TViewModel>`) are hosted in
`HostDialogWindow` (the Linux twin of WPF's `HostDialog` — ViewModel in, the
ViewLocator supplies the view, buttons close the host with a result), shown
modally over the client window. The synchronous `Skua.Core` contract is kept by
pumping a nested dispatcher frame until the user answers, so callers get the
real `bool?` back. Message boxes work the same way. Covered by headless tests
(`DialogHostTests`).

## CI

`.github/workflows/linux-build.yml` runs on `ubuntu-latest`: it tests and builds
the Rust bridge, builds `Skua.Linux.sln`, and runs the headless smoke test on
every push/PR touching the Linux surface.
