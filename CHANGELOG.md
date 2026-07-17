# Changelog

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
