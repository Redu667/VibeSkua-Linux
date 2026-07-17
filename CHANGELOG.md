# Changelog

## v1.1.1

Visual polish using the real Skua artwork:

- The app icon now appears in the main window header and the About tab.
- The AppImage/desktop icon is the actual Skua icon (it was a blank
  placeholder — your taskbar/dock entry now looks right).
- README: logo header, corrected status/links (including pointing the
  upstream VibeSkua links at the real repo).

## v1.1.0

Feature parity with Windows Skua. Everything below existed on Windows and was
missing or broken on Linux until now.

**Hotkeys work.** Assign gestures in the HotKeys panel (same format and
defaults as Windows, e.g. `F6` = Lag Killer) and they fire while the app has
focus. Previously no hotkey did anything on Linux.

**Army control works across clients.** Army hotkeys — start/stop scripts on
all clients, login/logout all, and the army-wide option toggles (Lag Killer,
Headless, Hide Players, Disable FX, Infinite Range, Streamer Mode, …) — now
reach every running client through a new cross-process bus, along with army
jump, load-script, and scheduler-playlist broadcasts.

**Pop-out panels work.** `Console`, `Script Repo`, `Plugins`, and the rest of
the managed-window registry open in their own windows (close hides them; they
reopen instantly), including via the OpenConsole/SearchScripts hotkeys.

**Tray icon + notifications.** Minimize VibeSkua to the tray (click to
toggle, menu with Show/Hide and Exit); while hidden or minimized, script
stopped / script error / relogin events raise desktop notifications.

**Plugins load.** The plugin manager initializes at startup, so plugins
(e.g. DailyTracker) are actually available.

**Content auto-updates at launch** (manager window): bot scripts, advanced
skill sets, quest data, and junk items check for updates and download per your
Options > Application settings; the app also checks for new releases.

**Theme editing.** Save named themes (dark/light + accent color), apply and
remove them from the Themes panel, and set a custom accent — persisted across
restarts.

**Screenshots.** Discord-webhook screenshots capture the real window instead
of coming back empty.

**CLI.** `--gh-token` and `--use-theme` are honored, matching Windows.

## v1.0.0

First stable release of VibeSkua for Linux — native, no Wine, no VM.

**Highlights**

- Native Linux AppImage; bundles the .NET runtime + `libskua_flash.so`
  (Ruffle embedded). No .NET or Flash install required.
- Flash → Ruffle via a Rust ExternalInterface bridge; `skua.swf` boots as the
  root movie and loads AQW into itself (the Windows architecture), so the
  bot's `game` reference resolves and scripts drive the character in live AQW.
- Full Avalonia tabbed client with the in-window wgpu game surface.
- Scripts: Roslyn compilation, `//cs_include` shared libraries, auto-download
  of missing includes, case-insensitive include resolution, and a versioned
  compiled-script cache.
- Army: launch a client per account; each auto-logs in and runs its script;
  unfocused windows pause rendering (the bot keeps running).
- Modal dialogs with working buttons.
- CI-built AppImage releases (Velopack).

**Run**

```bash
chmod +x VibeSkuaLinux.AppImage
./VibeSkuaLinux.AppImage
```
