//! Proves `skua.swf` **same-domain injection** end to end against a real embedded
//! `ruffle_core::Player` — the mechanism that lets the bot DRIVE the game.
//!
//! Requires the patched ruffle_core (`native/prepare-ruffle-fork.sh` +
//! Cargo `[patch]`), which adds `Player::inject_swf_same_domain`. The probe SWF's
//! document class (`extends MovieClip`) calls `ExternalInterface.call("foo")` in
//! its constructor — exactly how skua.swf registers callbacks on load. If, after
//! injecting it into the running player, that call reaches the host handler, then
//! the injected SWF's code is executing inside the game's ApplicationDomain and
//! the bot bridge is live.

#![cfg(feature = "ruffle")]

use std::sync::{Arc, Mutex};

use skua_flash::ruffle_runtime::RuffleRuntime;
use skua_flash::FlashRuntime;

// A real AVM2 SWF whose document class (extends MovieClip) fires
// ExternalInterface.call("foo") from its constructor. Used as both the root
// ("game") movie and the injected ("skua.swf") movie: its document class extends
// Sprite, so the injected root links correctly (a class that extends Object would
// hit ruffle's "must inherit from Sprite" root-link check).
const SWF: &[u8] = include_bytes!("fixtures/skua_inject_probe.swf");

fn foo_count(sink: &Arc<Mutex<Vec<String>>>) -> usize {
    sink.lock().unwrap().iter().filter(|x| x.contains("\"foo\"")).count()
}

#[test]
fn injected_swf_constructor_runs_and_reaches_the_host() {
    let mut runtime = RuffleRuntime::from_swf_bytes(SWF, "file://game.swf")
        .expect("embed ruffle_core + load the root movie");

    let captured: Arc<Mutex<Vec<String>>> = Arc::new(Mutex::new(Vec::new()));
    {
        let sink = Arc::clone(&captured);
        runtime.set_flash_call_handler(Box::new(move |xml: &str| {
            sink.lock().unwrap().push(xml.to_owned());
        }));
    }

    // Root ("game") runs and fires foo once from its own constructor.
    runtime.run_frames(3);
    let before = foo_count(&captured);

    // Inject a second SWF into the same player, in the root movie's domain.
    runtime
        .load_swf(SWF)
        .expect("inject skua.swf (needs the patched ruffle_core)");
    runtime.run_frames(20);
    let after = foo_count(&captured);

    // The injected movie's document-class constructor ran and its
    // ExternalInterface.call reached the host — a SECOND live movie executing in
    // the one player the bot talks to.
    assert!(
        after > before,
        "injected SWF's document-class constructor did not reach the host \
         (foo before={before}, after={after})"
    );
}
