# VibeSkua Linux

> **Note:** This project is **Vibe Coded** — built through AI-assisted development and pure momentum.

**VibeSkua Linux** is a **native Linux port** of [VibeSkua](https://github.com/Durstronaut/Linux-SKUA) (itself a feature-rich fork of [auqw/skua](https://github.com/auqw/skua)) — the same advanced AQW automation, army control, and scripting, running **natively on Linux with no Wine and no VM**.

It swaps the two Windows-only pieces of Skua for native equivalents:

- **Flash ActiveX → [Ruffle](https://ruffle.rs).** The proprietary Flash Player ActiveX control is replaced by Ruffle (`ruffle_core`), embedded directly in-process. AQW itself is Flash/AVM2; Ruffle runs it natively on Linux.
- **WPF → [Avalonia](https://avaloniaui.net).** The WPF UI is rebuilt in Avalonia. Every one of Skua's 89 ViewModels already lives in `Skua.Core` on `CommunityToolkit.Mvvm` and is UI-framework-agnostic, so the port is a pure view rewrite over the *same* engine.

The engine, scripts, and behavior are unchanged — this is the same bot, hosted on a Linux-native stack.

---

## Status

| Layer | What | Status |
| :--- | :--- | :--- |
| **1. Core engine** (~40k LOC) | `Skua.Core`, `.Interfaces`, `.Models`, `.Utils`, `.Generators` | ✅ Builds native `net10.0` on Linux, 0 errors. All 89 ViewModels + the full `IScriptInterface` bot graph resolve and run. |
| **2. UI** (~9.5k LOC) | `Skua.WPF` → `Skua.Avalonia` | ✅ Whole UI ported — 61 views (main bot app, all dialogs, and the Skua.Manager multi-account launcher), 16 headless tests. |
| **3. Flash runtime** | Flash ActiveX → embedded `ruffle_core` | ✅ Real `ruffle_core` embedded in `libskua_flash.so`; ExternalInterface round-trips both directions against a real AVM2 SWF (verified in CI). |
| **Packaging** | Velopack AppImage | ✅ `vpk`-built `.AppImage`, self-contained (no .NET install needed). |

The one remaining runtime step is loading the **live** AQW game SWF from `game.aq.com` and injecting `skua.swf` into its ApplicationDomain — the code path is in place and rides on the proven ExternalInterface plumbing; it just needs a live game session to exercise. See [`LINUX.md`](LINUX.md) for the full engineering status.

---

## Install / Run (AppImage)

Download the latest `VibeSkuaLinux.AppImage` from [Releases](https://github.com/Durstronaut/Linux-SKUA/releases), then:

```bash
chmod +x VibeSkuaLinux.AppImage
./VibeSkuaLinux.AppImage
```

The AppImage is self-contained — it bundles the .NET runtime, the Avalonia UI, and `libskua_flash.so` (with Ruffle embedded), so no separate .NET or Flash install is required. On distros without FUSE, run `./VibeSkuaLinux.AppImage --appimage-extract` and launch the extracted `AppRun`.

Velopack provides in-app auto-update from GitHub Releases.

---

## Building from source

**Requirements:** .NET 10 SDK, a Rust toolchain (nightly, for the Ruffle runtime), and `squashfs-tools` (for AppImage packaging).

```bash
# Run the headless engine + Flash-bridge smoke test (no display needed):
dotnet run --project Skua.App.Console -c Release

# Build & run the Avalonia app:
dotnet run --project Skua.Avalonia -c Release

# Headless UI tests:
dotnet test Skua.Avalonia.Tests -c Release
```

The Rust Flash bridge (`libskua_flash.so`) builds automatically as part of the C# build. It has two backends:

- **default** — an offline mock of AQW's `world` object, so the whole pipe runs and tests with no network or game (any stable Rust).
- **`ruffle`** — the real embedded `ruffle_core` engine. Build the app with the real runtime bundled:

```bash
rustup toolchain install nightly       # ruffle_core needs a nightly toolchain
dotnet build Skua.Avalonia -c Release -p:SkuaRuffle=true
```

### Packaging an AppImage

```bash
dotnet tool install -g vpk
./packaging/build-appimage.sh 0.1.0     # publishes + packs -> packaging/Releases/VibeSkuaLinux.AppImage
```

`Skua.Linux.sln` deliberately contains only the projects that build on Linux. The WPF/WinForms projects (`Skua.WPF`, `Skua.App.WPF*`, `Skua.Manager`) are excluded — their XAML markup compiler is Windows-only. The original Windows build is unaffected and lives alongside this port.

---

## What carries over from VibeSkua

All of VibeSkua's features are engine/ViewModel-level and therefore portable as-is — Discord integration, headless mode, script scheduling with silent/unattended profiles, the multi-account army control + grid view, function-based skills, loadouts manager, smart quest sync, custom script loading, hotkeys, and the performance/stability work. The Linux port hosts the identical engine; only the rendering (Ruffle) and windowing (Avalonia) layers are new. See the [VibeSkua feature list](https://github.com/Durstronaut/Linux-SKUA) for the full breakdown.

---

### Copyright & Disclaimer

**Educational & Personal Use Only:** This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is provided "as-is" under the MIT License. I do not claim ownership of the original assets, game data, or the intellectual property of the game developers. Ruffle is bundled under its Apache-2.0 / MIT license.

**Disclaimer:** Use of this software may violate the Terms of Service of the associated game. The author assumes no responsibility for any account actions, bans, or other consequences taken by game developers against users of this software. By using this tool, you acknowledge that you do so entirely at your own risk. If your PC decides to commit a toaster bath, that is not my problem.
