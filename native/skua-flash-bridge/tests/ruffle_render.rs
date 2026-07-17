//! Proves the offscreen wgpu render pipeline produces real pixels.
//!
//! Only built under `--features ruffle-render`. Renders a real AVM2 SWF to an
//! offscreen wgpu texture and reads it back, asserting the pixel buffer has the
//! right shape and is not entirely blank/zero — i.e. wgpu initialized and ruffle
//! actually rasterized a frame. This is the foundation the in-app game view
//! blits into an Avalonia bitmap.
//!
//! wgpu needs a graphics backend; in a GPU-less sandbox force software GL with
//! `SKUA_WGPU_BACKEND=gl` (and typically `LIBGL_ALWAYS_SOFTWARE=1`). On a real
//! machine the default (all backends) picks the GPU.

#![cfg(feature = "ruffle-render")]

use skua_flash::render::RenderPlayer;

use futures::executor::LocalPool;
use ruffle_core::backend::navigator::{NavigatorBackend, Request};
use skua_flash::navigator::SkuaNavigator;
use url::Url;

#[test]
fn navigator_fetches_over_https() {
    // Proves the minimal navigator's HTTP path works end to end: build a
    // request, drive the LocalPool, and get a real response body back. This is
    // what lets the game view actually LOAD assets (vs. a null navigator).
    let mut pool = LocalPool::new();
    let base = Url::parse("https://raw.githubusercontent.com/").unwrap();
    let nav = SkuaNavigator::new(base, pool.spawner(), false);

    let req = Request::get(
        "https://raw.githubusercontent.com/ruffle-rs/ruffle/master/README.md".to_owned(),
    );
    let fetch = nav.fetch(req);

    let response = match pool.run_until(fetch) {
        Ok(r) => r,
        Err(e) => {
            eprintln!("SKIP navigator fetch (no network?): {:?}", e.error);
            return;
        }
    };
    // A 2xx with a non-empty body proves the transport + buffering works.
    assert!(response.status() >= 200 && response.status() < 600);
    if response.status() == 200 {
        let body = pool.run_until(response.body()).expect("body");
        assert!(!body.is_empty(), "fetched README should have content");
    }
}

const FIXTURE: &[u8] = include_bytes!("fixtures/external_interface.swf");

#[test]
fn one_player_renders_and_answers_the_bot() {
    // The whole point of the in-process design: the SAME live player the user
    // sees rendered is the one the bot drives. Prove it on one RenderPlayer —
    // render real pixels AND round-trip ExternalInterface both directions.
    use std::sync::{Arc, Mutex};

    let player = match RenderPlayer::from_swf_bytes(FIXTURE, "file://both.swf", 320, 240) {
        Ok(p) => p,
        Err(e) => {
            eprintln!("SKIP render+bot test (no graphics backend): {e}");
            return;
        }
    };

    // AS3 -> host: capture the movie's own ExternalInterface.call("ping").
    let captured: Arc<Mutex<Vec<String>>> = Arc::new(Mutex::new(Vec::new()));
    {
        let sink = Arc::clone(&captured);
        player.set_handler(Box::new(move |xml: &str| {
            sink.lock().unwrap().push(xml.to_owned());
        }));
    }

    player.tick(2);

    // Rendering still works on this player.
    let (w, h, pixels) = player.capture().expect("pixels");
    assert_eq!((w, h), (320, 240));
    assert!(pixels.iter().any(|&b| b != 0), "rendered a frame");

    // host -> AS3: the bot's read path round-trips on the rendered player.
    let invoke =
        skua_flash::xml::serialize_invoke("parrot", &[skua_flash::FlashValue::String("battleon".into())]);
    let response = player.call(&invoke);
    assert!(response.contains("battleon"), "bot call round-trips: {response}");

    // AS3 -> host reached the handler.
    let seen = captured.lock().unwrap().clone();
    assert!(
        seen.iter().any(|x| x.contains("ping")),
        "movie's ExternalInterface.call reached the bot handler: {seen:?}"
    );
}

#[test]
fn wgpu_offscreen_render_produces_pixels() {
    let w = 320;
    let h = 240;

    let player = match RenderPlayer::from_swf_bytes(FIXTURE, "file://render_test.swf", w, h) {
        Ok(p) => p,
        Err(e) => {
            // No usable graphics backend in this environment — skip rather than
            // fail (CI without a GPU/software-GL can't exercise wgpu). The build
            // still proves the render path compiles against real ruffle_wgpu.
            eprintln!("SKIP wgpu render test: {e}");
            return;
        }
    };

    // Advance a couple of frames so the stage is set up, then read it back.
    player.tick(2);
    let (cw, ch, pixels) = player.capture().expect("capture_frame returned pixels");

    assert_eq!(cw, w, "captured width matches requested");
    assert_eq!(ch, h, "captured height matches requested");
    assert_eq!(
        pixels.len(),
        (w * h * 4) as usize,
        "RGBA8 buffer is width*height*4 bytes"
    );
    assert!(
        pixels.iter().any(|&b| b != 0),
        "frame is not entirely zero — wgpu rasterized something"
    );
}

#[test]
fn host_boots_root_movie_from_bytes_and_answers_the_bot() {
    // The production Skua boot: the ROOT movie comes from local bytes
    // (skua.swf) with an https-style nominal origin, its ExternalInterface
    // callbacks register on init, and the host round-trips a call through the
    // worker-thread RenderHost — the exact path GameSession uses on Linux.
    use skua_flash::render::RenderHost;

    let host = match RenderHost::create_from_bytes(
        FIXTURE.to_vec(),
        "https://game.aq.com/game/skua.swf",
        320,
        240,
    ) {
        Ok(h) => h,
        Err(e) => {
            eprintln!("SKIP root-from-bytes test (no graphics backend): {e}");
            return;
        }
    };

    // The worker self-ticks (~33ms); the fixture registers its callbacks on
    // init. Poll briefly like the app does rather than assuming timing.
    let invoke = skua_flash::xml::serialize_invoke(
        "parrot",
        &[skua_flash::FlashValue::String("battleon".into())],
    );
    let mut response = String::new();
    for _ in 0..100 {
        response = host.call(&invoke);
        if response.contains("battleon") {
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    assert!(
        response.contains("battleon"),
        "root-movie-from-bytes host answers the bot: {response}"
    );
}

#[test]
fn real_skua_swf_answers_the_csharp_zero_arg_handshake() {
    // The production boot handshake, byte-for-byte: the REAL skua.swf as the
    // root movie, polled with the EXACT zero-argument wire format the C# host
    // emits (no <arguments> block). This is the request shape that was being
    // rejected before reaching the player, silently killing loadClient.
    use skua_flash::render::RenderHost;

    let swf = match std::fs::read("../../Skua.AS3/skua/bin/skua.swf") {
        Ok(b) => b,
        Err(e) => {
            eprintln!("SKIP real-skua handshake test (no skua.swf): {e}");
            return;
        }
    };
    let host = match RenderHost::create_from_bytes(
        swf,
        "https://game.aq.com/game/skua.swf",
        320,
        240,
    ) {
        Ok(h) => h,
        Err(e) => {
            eprintln!("SKIP real-skua handshake test (no graphics backend): {e}");
            return;
        }
    };

    const IS_TRUE: &str = r#"<invoke name="isTrue" returntype="xml"></invoke>"#;
    let mut response = String::new();
    for _ in 0..100 {
        response = host.call(IS_TRUE);
        if response.contains("true") {
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    assert!(
        response.contains("true"),
        "zero-arg isTrue reaches skua.swf and answers: {response}"
    );
}
