//! Diagnostic: boot the REAL skua.swf as the root movie (the production path)
//! and print everything observable — AS3->host ExternalInterface traffic
//! (skua.swf's `debug`/`requestLoadGame`), the isTrue readiness probe, the
//! loadClient response, and ruffle's own tracing (AVM2 errors land there).
//!
//! Run:  SKUA_WGPU_BACKEND=gl LIBGL_ALWAYS_SOFTWARE=1 RUST_LOG=warn,ruffle_core=info \
//!       cargo +nightly run --example boot_skua --features ruffle-render -- ../../Skua.AS3/skua/bin/skua.swf

use skua_flash::render::RenderHost;

fn main() {
    // Install a stderr subscriber ONLY when RUST_LOG is set; otherwise rely on
    // the library's own install_ruffle_log (the production path), so this
    // example validates the shipped diagnostics end to end.
    if std::env::var("RUST_LOG").is_ok() {
        tracing_subscriber::fmt()
            .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
            .with_writer(std::io::stderr)
            .init();
    }

    let path = std::env::args().nth(1).expect("usage: boot_skua <skua.swf>");
    let bytes = std::fs::read(&path).expect("read swf");
    eprintln!("== booting {path} ({} bytes) as ROOT movie ==", bytes.len());

    let host = RenderHost::create_from_bytes(bytes, "https://game.aq.com/game/skua.swf", 960, 580)
        .expect("create_from_bytes");

    host.set_handler(Box::new(|xml: &str| {
        println!("[AS3->host] {xml}");
    }));

    // Readiness probe, then the Windows handshake.
    let is_true = skua_flash::xml::serialize_invoke("isTrue", &[]);
    let mut ready = false;
    for i in 0..100 {
        let r = host.call(&is_true);
        if r.contains("true") {
            println!("[probe] isTrue answered after {}ms: {r}", i * 100);
            ready = true;
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    if !ready {
        println!("[probe] isTrue NEVER answered — skua.swf callbacks did not register");
        return;
    }

    let load = skua_flash::xml::serialize_invoke("loadClient", &[]);
    println!("[host->AS3] loadClient -> {}", host.call(&load));

    // Let it run; watch for debug traffic / errors / pixels.
    for s in 0..20 {
        std::thread::sleep(std::time::Duration::from_secs(1));
        if let Some((w, h, px)) = host.capture() {
            let lit = px.chunks(4).filter(|c| c[0] as u16 + c[1] as u16 + c[2] as u16 > 30).count();
            println!("[{s:02}s] frame {w}x{h}, non-black px: {lit}");
        }
    }
}
