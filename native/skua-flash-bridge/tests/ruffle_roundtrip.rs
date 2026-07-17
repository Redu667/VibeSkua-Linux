//! End-to-end round-trip against a **real embedded `ruffle_core::Player`**.
//!
//! Only built under `--features ruffle` (which pulls in the git-only
//! `ruffle_core`, pinned in Cargo.toml). It drives the same AVM2 SWF Ruffle uses
//! for its own `external_interface` test through the Skua bridge, proving both
//! directions of the ExternalInterface seam that the entire Linux port rests on:
//!
//!   * **host → AS3**: `FlashRuntime::call` sends an `<invoke name="parrot">`
//!     through the XML codec into the live player; the AS3 `parrot()` callback
//!     echoes its argument, which comes back through the codec as a value.
//!     This is the exact shape of `IFlashUtil.Call` / `GetGameObject`.
//!   * **AS3 → host**: the SWF's own `ExternalInterface.call("ping")` (run as the
//!     movie ticks) is delivered to our `ExternalInterfaceProvider`, re-encoded
//!     as the `<invoke>` XML the Windows `FlashCall` event carries, and handed to
//!     the registered host handler.
//!
//! If this passes, `RuffleRuntime` is a working `IFlashUtil` backend on Linux;
//! all that remains for live AQW is loading the remote game SWF + injecting
//! `skua.swf` into the same ApplicationDomain (the navigator/domain seam), which
//! rides on this exact plumbing.

#![cfg(feature = "ruffle")]

use std::sync::{Arc, Mutex};

use skua_flash::ruffle_runtime::RuffleRuntime;
use skua_flash::xml::serialize_invoke;
use skua_flash::{FlashRuntime, FlashValue};

const FIXTURE: &[u8] = include_bytes!("fixtures/external_interface.swf");

#[test]
fn ruffle_external_interface_round_trips_both_directions() {
    let mut runtime = RuffleRuntime::from_swf_bytes(FIXTURE, "file://external_interface.swf")
        .expect("embed ruffle_core + load the AVM2 external_interface SWF");

    // Capture AS3 -> host calls the movie makes as it ticks.
    let captured: Arc<Mutex<Vec<String>>> = Arc::new(Mutex::new(Vec::new()));
    {
        let sink = Arc::clone(&captured);
        runtime.set_flash_call_handler(Box::new(move |invoke_xml: &str| {
            sink.lock().unwrap().push(invoke_xml.to_owned());
        }));
    }

    // Run the movie: the root AVM2 script registers "parrot"/"callWith"/… and
    // fires ExternalInterface.call("ping"), "non_existent", "reentry".
    runtime.run_frames(2);

    // --- AS3 -> host --------------------------------------------------------
    let seen = captured.lock().unwrap().clone();
    assert!(
        seen.iter().any(|xml| xml.contains("\"ping\"") || xml.contains("name=\"ping\"")),
        "expected the movie's ExternalInterface.call(\"ping\") to reach the host handler; captured: {seen:?}"
    );

    // --- host -> AS3 (round-trip a value) -----------------------------------
    // parrot() returns arguments[0], so sending "battleon" must echo it back —
    // the same round-trip as GetGameObject<string>("world.strMapName").
    let invoke = serialize_invoke("parrot", &[FlashValue::String("battleon".to_owned())]);
    let response = runtime.call(&invoke);
    assert!(
        response.contains("battleon"),
        "host->AS3 round-trip through real ruffle_core failed; response: {response}"
    );
}
