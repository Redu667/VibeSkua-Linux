# skua-flash-bridge (Layer 3b)

The Linux Flash bridge for the VibeSkua native port. It gives `Skua.Core` the two
XML pipes it needs to drive AQW's Flash content on Linux — no Wine, no VM — as a
C-ABI shared library (`libskua_flash.so`) that Skua's Linux `IFlashUtil`
`[DllImport]`s into its own process. Same single-process shape as the Windows
`AxShockwaveFlash` path, just with Ruffle instead of the Flash ActiveX control.

```
Skua (Avalonia/console)                 <- the host process
  └─ libskua_flash.so                    <- loaded via DllImport
       └─ ruffle_core + AQW Loader3.swf + skua.swf
```

## The seam

`Skua.Core` never touches Flash directly; it goes through `IFlashUtil`, and
CLAUDE.md establishes that the whole contract reduces to two XML pipes plus SWF
loading:

| Direction | Windows (`AxShockwaveFlash`) | Linux (this crate) |
|-----------|------------------------------|--------------------|
| host → AS3 | `CallFunction("<invoke.../>")` → value XML | `skua_flash_call()` |
| AS3 → host | `FlashCall` event (XML) | `SkuaFlashCallback` |
| load SWF   | `OcxState` byte injection | `skua_flash_load_swf()` |

Everything else in `IFlashUtil` (`GetGameObject`, `SetGameObject`,
`CallGameFunction`, …) is a C# default-interface method built on `Call()`, so a
backend only has to satisfy these three things and the rest works unchanged.

## Layout

| File | Responsibility | Status |
|------|----------------|--------|
| `src/value.rs`   | ExternalInterface value model (`FlashValue`) | ✅ done + tested |
| `src/xml.rs`     | ExternalInterface XML codec (dependency-free) | ✅ done + tested |
| `src/ffi.rs`     | C ABI (`skua_flash_*`) | ✅ done + tested |
| `src/runtime.rs` | `FlashRuntime` trait + offline `MockRuntime` | ✅ done + tested |
| `include/skua_flash.h` | C header for the ABI | ✅ done |
| `src/ruffle_runtime.rs` | `RuffleRuntime`: embeds real `ruffle_core`, real `ExternalInterfaceProvider` | ✅ built + round-trips both directions (`--features ruffle`) |

The XML codec is hand-rolled precisely so this crate has **zero external
dependencies** and builds + tests with no network access. The value XML it
emits/consumes byte-matches `Skua.WPF/Flash/FlashUtil.cs` (`ToFlashXml` /
`FromFlashXml`), so the same `skua.swf` answers identically on both platforms.

## Build & test

```bash
cargo test              # 13 tests, all offline
cargo build --release   # -> target/release/libskua_flash.so
```

The default build backs the ABI with `MockRuntime`, an in-memory stand-in for
AQW's `world` object. That makes the entire pipe runnable today — including
Layer 3b's definition-of-done, reading `world.strMapName` from "AS3" back into
the host (see `tests/roundtrip.rs::round_trips_world_strmapname_through_mock_runtime`
and the `ffi_*` tests that drive the raw C ABI).

## The real Ruffle runtime (`--features ruffle`)

`src/ruffle_runtime.rs` embeds a **real `ruffle_core::Player`** and satisfies the
same `FlashRuntime` trait as the mock. It is behind the optional `ruffle`
feature so the default build stays offline and dependency-free.

```bash
# ruffle_core is a git-only dep and uses nightly Rust features (let-chains /
# if-let guards), so build the ruffle runtime with a nightly toolchain:
rustup toolchain install nightly
cargo +nightly build  --release --features ruffle          # -> libskua_flash.so (~13 MB, ruffle_core embedded)
cargo +nightly test           --features ruffle --test ruffle_roundtrip
```

What it does:

- **Marshalling** — `FlashValue ⇄ ruffle_core::external::Value` is a 1:1 map
  (both are `Null|Undefined|Bool|Number|String|Array/List|Object`).
- **AS3 → host** — `SkuaExternalInterfaceProvider` implements
  `ExternalInterfaceProvider::call_method`; it re-encodes the call as the same
  `<invoke>` XML the Windows `FlashCall` event carries and hands it to the host
  handler.
- **host → AS3** — `FlashRuntime::call` parses the `<invoke>`, marshals the args,
  and invokes the AS3 callback via `Player::call_internal_interface`, serializing
  the result back through this crate's codec.
- **Threading** — `ruffle_core::Player` is `!Send`, so it runs on **one
  dedicated thread** behind a command channel; `RuffleRuntime` holds only the
  channel `Sender`s (so it stays `Send` for the FFI boundary). This is the same
  shape `ruffle_desktop` uses.
- **Headless** — a farm/army host needs no GPU/audio/network: `PlayerBuilder::build()`
  falls back to the built-in `NullRenderer`/`NullAudioBackend`/`NullNavigatorBackend`,
  so the feature pulls in only `ruffle_core`.

### Proven end to end

`tests/ruffle_roundtrip.rs` loads a real AVM2 SWF (Ruffle's own
`external_interface` test asset, `tests/fixtures/external_interface.swf`), runs
the movie in the embedded player, and asserts **both directions**:

- host→AS3: `<invoke name="parrot">battleon</invoke>` → the AS3 `parrot()`
  callback echoes `battleon` back through the codec (the exact shape of
  `GetGameObject<string>("world.strMapName")`);
- AS3→host: the movie's own `ExternalInterface.call("ping")` reaches our
  provider and the registered host handler.

### What's left for live AQW

Only the pieces that need a live game session / `game.aq.com` egress, isolated
in `load_movie_from_url` / `inject_swf_same_domain`:

- load `Loader3.swf` **from https** (AQW calls `SharedObject.getLocal(secure: true)`;
  an http origin → `Error #1009`) — a blocking https GET feeds the same
  `from_swf_bytes` path the test exercises;
- inject `skua.swf` into the **same `ApplicationDomain`** so
  `ApplicationDomain.getDefinition("world")` resolves.

`Bridge::create()` already selects `RuffleRuntime` under the feature (falling
back to the mock if the remote game can't be fetched).

## License

`MIT OR Apache-2.0`, matching Ruffle, so it is safe to bundle in public releases.
