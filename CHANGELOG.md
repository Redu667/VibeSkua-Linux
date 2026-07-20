# Changelog

## v1.1.7

- **Scripts stop re-buying/re-farming things you already own.** The bot was
  reading your inventory as *empty* even with a full bag, so ownership and
  completion checks all failed — FarmerJoeDoAll would start over and re-buy
  items like Oracle Hood. Cause: Ruffle sends item IDs as `685089465.0` (with a
  trailing `.0`) where the old Flash sent `685089465`, and the number parser
  choked on the decimal and silently threw the **entire** inventory away. Game
  reads now accept Ruffle's number format, so inventory, quests, shop, and stat
  checks work. (After updating, `Bot.Inventory.Contains("...")` returns true for
  items you own.) This was a systemic read bug, not just an inventory one — a
  lot of "script doesn't detect it's done" behavior traces back to it.

## v1.1.6

- **Game UI text renders instead of showing blank labels.** Ruffle has no
  built-in fonts, so the fonts AQW asks for (HelveticaNeue, Calibri, Times New
  Roman, and the generic `_sans`/`_serif`) had nothing to fall back to — the
  game log filled with hundreds of "text will be missing" warnings and buttons
  and labels came up empty. The client now registers a system font from your
  machine (Noto / DejaVu / Liberation, covering CachyOS/Arch, Debian, and
  Fedora paths, with a scan of the font folders as a fallback) as the default,
  so game text draws. If no system font is found the game still runs and logs a
  hint to install `noto-fonts` or `ttf-dejavu`.

## v1.1.5

- **In-app update works now.** The updater was checking the wrong repository
  (the Windows project) so it never found Linux releases. It now checks the
  correct release repo. If that repo is private, enter a GitHub token in the
  GitHub Auth tab and the updater will use it; otherwise make the repo public.
- **Minimize to tray is less finicky.** Clicking the tray icon (or the new
  Show / Hide menu entries) now properly restores a minimized window instead of
  hiding it further. Note: single-click on the tray icon isn't delivered by
  every desktop environment (e.g. GNOME needs an extension) — the Show/Hide
  menu always works.
- **Troubleshooting:** launching with `SKUA_TRACE_GETOBJ=1` logs every game
  read that comes back empty (with its path) to the game log — for diagnosing
  scripts that don't detect inventory/quest state.

## v1.1.4

- **Logs no longer pile up forever.** Each launch now writes its own
  timestamped log files into a `logs/` folder (under `~/.config/Skua/`),
  named by role, account, date and time — one set per manager launch and per
  client launch — instead of every run appending to the same handful of files
  that grew to hundreds of thousands of lines. Logs older than 14 days are
  cleaned up automatically.
- **Fewer shop/bank failures.** A server packet with an unexpected shape could
  throw during parsing and cause the bot to drop that packet entirely, which
  made buying and bank actions time out and fail. The packet is now always
  forwarded even if parsing hiccups, and the specific failure is logged (in the
  new per-session log) so any remaining cases are diagnosable.
- Stopped a harmless shutdown error from flooding the crash log.

## v1.1.3

- **Accounts and themes now survive a restart.** They were being written to
  the wrong place and silently discarded, so they vanished when you relaunched
  the AppImage. Fixed — they persist now. (One-time note: anything you added
  before this update wasn't saved to disk, so you'll need to re-add your
  accounts and theme once; they'll stick from here on.)

## v1.1.2

Fixes for features that were built but not reachable from the UI.

- **Configurable scripts work again.** Scripts that show an options window
  (e.g. FarmerJoeDoAll) errored on start with "View not found for
  OptionContainerViewModel" — the options dialog had no view. It's now a real
  dialog with the right editor per option (checkbox / dropdown / text).
- **Accounts / army setup is reachable.** Add accounts and groups, pick a
  server, and start each one (auto-login) from the new **Accounts** tab in the
  manager window — plus Launcher, Updates, Script Updater, Client Files, and
  Manager Options as their own tabs.
- **Fast Travel add/edit/remove works.** The editor row is shown (fill it or
  "Get current location", then Add), and each saved entry has Edit and Remove
  buttons.
- **No more duplicate tabs.** Removed the redundant second "Theme" tab and the
  standalone "Skill Editor" (both were already inside other tabs).
- **Hotkeys tab shows in the manager window too**, so army hotkeys can be
  assigned there.

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
