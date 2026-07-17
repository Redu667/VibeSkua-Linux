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
| **3b. Ruffle runtime** | embed `ruffle_core`, real `ExternalInterfaceProvider` | ✅ **built & round-tripping against real `ruffle_core`** — `src/ruffle_runtime.rs` embeds a `ruffle_core::Player` (on a dedicated thread, behind a command channel, since `Player` is `!Send`) and `tests/ruffle_roundtrip.rs` drives a real AVM2 SWF end-to-end: **host→AS3** `call_internal_interface` round-trips a value, **AS3→host** `ExternalInterface.call` reaches our provider. `cargo build --release --features ruffle` → 13 MB `libskua_flash.so` with ruffle_core embedded. Only loading the **remote** AQW game SWF + same-domain `skua.swf` injection is unproven (game.aq.com egress-blocked here). |
| **2. UI** (~9.5k LOC XAML) | `Skua.WPF` → new `Skua.Avalonia` | ✅ **whole UI ported (61 views), app runs & hosts the full engine** — Avalonia app + DI + ViewLocator + navigable shell, **all 11 Linux platform services**, the complete main-app ViewModel graph, **all 6 dialogs**, and the **Skua.Manager multi-account launcher** (accounts/groups, script/client updaters, options) — all wired via `Skua.Core.AppStartup` + `RuffleFlashUtil`. 16 headless tests, 0 failures. Only live game rendering waits on linking real Ruffle. |
| **Packaging** | Velopack AppImage + bundled Ruffle nightly | ⬜ **not started** |

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

## Roadmap

1. **Compile `RuffleRuntime` (`native/skua-flash-bridge/src/ruffle_runtime.rs`).**
   The runtime is written behind the bridge's `FlashRuntime` trait and the
   `ruffle` cargo feature, but `ruffle_core` is a git-only dependency
   (`github.com/ruffle-rs/ruffle`, not on crates.io) so it is excluded from the
   default build. In an environment where github is reachable: pin a *current*
   nightly, wire the deps (`Cargo.toml` has the block), and reconcile the
   `// RUFFLE API:` sites. Gotchas (serve `Loader3.swf` over **https**; inject
   `skua.swf` into the same `ApplicationDomain`) are in
   `native/skua-flash-bridge/README.md`. Definition of done: `world.strMapName`
   round-trips from the *real* game instead of the mock.
2. **Port the remaining Avalonia views.** The app, DI, theming, `ViewLocator`,
   and the first view (`AboutView`) are done and verified by headless tests; all
   89 ViewModels already live in `Skua.Core` on `CommunityToolkit.Mvvm`, so what
   remains is a mechanical rewrite of the ~85 other XAML views over them. Replace
   `NHotkey.Wpf` with SharpHook/X11 grabs and the `WM_COPYDATA` hotkey forwarding
   with a `NamedPipeServerStream` (Unix domain socket) behind an
   `IHotkeyForwarder`.
3. **Packaging.** Velopack already supports Linux AppImage; bundle a current
   Ruffle nightly (Apache-2.0/MIT — legal to redistribute). Never bundle
   `libpepflashplayer.so` (proprietary).

## Avalonia UI (Layer 2) — complete

The whole WPF UI is ported to Avalonia: **61 views** across the main bot app,
the six dialogs, and the Skua.Manager multi-account launcher, all driven by the
portable `Skua.Core` ViewModels through a convention `ViewLocator` (with a flat
fallback for VMs in sub-namespaces like `.Manager`). The app boots on Linux,
hosts the full `IScriptInterface` engine graph via `Skua.Core.AppStartup` DI,
and every panel's ViewModel resolves against that graph. **16 headless tests
(Avalonia.Headless.XUnit) pass with 0 failures.**

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
  / `--account <name>`), the app opens a standalone `GameClientWindow` — a single
  `GameView` on its own live Ruffle player, no manager chrome — and auto-starts
  the game. The instance name shows in the window title/status so army members
  are distinguishable.
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

### Bot *control* — WORKS (same-domain `skua.swf` injection)

The game **renders**, the ExternalInterface bridge **round-trips both ways**
(host→AS3 `Call`, AS3→host `FlashCall`; `tests/ruffle_roundtrip.rs`), **and the
bot can drive it**: `skua.swf` loads into the game's `ApplicationDomain`, its
document-class constructor runs, and it registers the `ExternalInterface`
callbacks (`getGameObject`, `callGameFunction`, `sendPacket`, …) that reach into
`world` via `getDefinition`.

**How.** The published `ruffle_core` exposes no host-callable "load a secondary
SWF into the running domain" method, and the AVM2 loader types it needs
(`LoaderInfoObject`, `LoaderDisplay`, `Avm2Domain`, …) live in private modules —
so we add exactly one method, `pub Player::inject_swf_same_domain`, via
`native/ruffle-skua-inject.patch` (96 lines, one function). It builds a
`flash.display.Loader`, attaches it (invisibly) to the stage so the loaded movie
ticks, and calls `LoadManager::load_movie_into_clip_bytes` into a child of the
game's root-movie domain. `native/prepare-ruffle-fork.sh` clones ruffle at the
pinned rev, applies the patch, and appends the `[patch]` block to the bridge's
`Cargo.toml` (not committed — it would break the offline build). The csproj and
CI run it automatically before the `ruffle`/`ruffle-render` cargo build.

**Verified end to end, no live game** — `tests/ruffle_inject.rs`: a probe SWF
whose document class (`extends MovieClip`) fires `ExternalInterface.call("foo")`
from its constructor is injected into a running player; after injection the call
reaches the host handler, proving the injected SWF's code executes inside the
game's domain and the bot bridge is live. (The injected document class must
extend `Sprite`/`MovieClip` — as real skua.swf does — or ruffle's root-link check
rejects it; a class extending `Object` was the red herring behind a long
debugging detour.)

Still remote-only: fetching AQW's live `Loader3.swf` over https needs game.aq.com
egress. Feeding real credentials from `--client --account` into the injected
skua.swf for per-account auto-login is the remaining glue on top of this.

**Services (11):** `ProcessService`, `ClipboardService`, `DispatcherService`,
`SettingsService`, `DialogService`, `SoundService`, `WindowService`,
`FileDialogService`, `ScreenshotService`, `ThemeService`, `HotKeyService`.

Remaining UI work is polish, not porting: the panels render live game data once
real Ruffle is linked (against the mock runtime they render defaults), and
`IDialogService.ShowDialog` still returns synchronously while Avalonia dialogs
are async — the dialog *views* exist; wiring modal results back to the sync
`Skua.Core` callers needs a nested-loop dialog host (a design task, not XAML).

## CI

`.github/workflows/linux-build.yml` runs on `ubuntu-latest`: it tests and builds
the Rust bridge, builds `Skua.Linux.sln`, and runs the headless smoke test on
every push/PR touching the Linux surface.
