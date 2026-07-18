//! Offscreen wgpu rendering for the visible game view (feature `ruffle-render`).
//!
//! Draws the movie to an offscreen `TextureTarget` with ruffle's wgpu backend
//! and reads it back as RGBA8 via `WgpuRenderBackend::capture_frame`, so the C#
//! side can blit those pixels into an Avalonia bitmap. Input (mouse/keyboard) is
//! forwarded down into the same `Player` via `handle_event`, so the game the user
//! sees is the same live `Player` the ExternalInterface bridge drives — one
//! process, bot and view sharing the exact AVM2 world.
//!
//! `Player` is `!Send`, so the owning `RuffleRuntime` keeps it on its dedicated
//! worker thread; this type is constructed and used only on that thread.

use std::any::Any;
use std::cell::RefCell;
use std::io::Read;
use std::sync::mpsc::{channel, Sender};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use ruffle_core::events::{MouseButton, PlayerEvent};
use ruffle_core::limits::ExecutionLimit;
use ruffle_core::tag_utils::SwfMovie;
use ruffle_core::{FloatDuration, Player, PlayerBuilder};

use futures::executor::LocalPool;

use ruffle_render_wgpu::backend::{
    create_wgpu_instance, request_adapter_and_device, WgpuRenderBackend,
};
use ruffle_render_wgpu::descriptors::Descriptors;
use ruffle_render_wgpu::target::TextureTarget;
use ruffle_render_wgpu::wgpu;

use crate::navigator::SkuaNavigator;
use crate::ruffle_runtime::{from_ruffle, to_ruffle, SharedHandler, SkuaExternalInterfaceProvider};
use crate::xml::{parse_invoke, serialize_response};

/// A movie rendered to an offscreen wgpu texture, with pixel readback + input,
/// AND the ExternalInterface bridge — so the SAME live player the user sees is
/// the one the bot drives (host→AS3 via `call`, AS3→host via the handler).
pub struct RenderPlayer {
    player: Arc<Mutex<Player>>,
    width: u32,
    height: u32,
    // Drives the navigator's fetch/socket futures. Ticked on every frame.
    // RefCell because `run_until_stalled` needs `&mut` while `RenderPlayer` is
    // shared behind `&self` on its owning thread (never crosses threads).
    executor: RefCell<LocalPool>,
    // AS3 -> host sink for skua.swf's ExternalInterface.call(...).
    handler: SharedHandler,
    // Wall-clock timestamp of the previous `tick`, so `update_timers(dt)` sees
    // real elapsed time (AQW's login/heartbeat/UI all run on `setInterval`).
    last_tick: RefCell<Option<Instant>>,
}

impl RenderPlayer {
    /// Build a player whose root movie is `bytes`, rendering offscreen at
    /// `width`x`height`. The wgpu backend is selectable via `SKUA_WGPU_BACKEND`
    /// (`gl` | `vulkan` | default = all) so software-GL sandboxes can force GL.
    pub fn from_swf_bytes(
        bytes: &[u8],
        url: &str,
        width: u32,
        height: u32,
    ) -> Result<Self, String> {
        // Deterministic backend preference. `Backends::all()` left the pick to
        // wgpu's HighPerformance roulette across whatever the machine exposes —
        // on a box with both a Vulkan and a GL path the chosen adapter could
        // differ run to run, and a tester's game rendered on some runs and came
        // up black on others with no record of why. Try Vulkan first (the path
        // all our rendering verification used), then GL, then anything; honor
        // SKUA_WGPU_BACKEND as an explicit override; and LOG the adapter that
        // actually won so the choice is never invisible again.
        let attempts: Vec<wgpu::Backends> = match std::env::var("SKUA_WGPU_BACKEND").ok().as_deref() {
            Some("gl") => vec![wgpu::Backends::GL],
            Some("vulkan") => vec![wgpu::Backends::VULKAN],
            Some("primary") => vec![wgpu::Backends::PRIMARY],
            _ => vec![wgpu::Backends::VULKAN, wgpu::Backends::GL, wgpu::Backends::all()],
        };

        let mut picked = None;
        let mut last_err = String::from("no graphics backend attempted");
        for backends in attempts {
            let instance = create_wgpu_instance(backends, wgpu::BackendOptions::default());
            match futures::executor::block_on(request_adapter_and_device(
                backends,
                &instance,
                None,
                wgpu::PowerPreference::HighPerformance,
            )) {
                Ok((adapter, device, queue)) => {
                    let info = adapter.get_info();
                    crate::navigator::game_log(&format!(
                        "wgpu adapter: {} ({:?}, driver: {} {})",
                        info.name, info.backend, info.driver, info.driver_info
                    ));
                    picked = Some((instance, adapter, device, queue));
                    break;
                }
                Err(e) => {
                    last_err = format!("wgpu init on {backends:?} failed: {e}");
                    crate::navigator::game_log(&last_err);
                }
            }
        }
        let Some((instance, adapter, device, queue)) = picked else {
            return Err(last_err);
        };

        let descriptors = Arc::new(Descriptors::new(instance, adapter, device, queue));
        let target = TextureTarget::new(&descriptors.device, (width, height))
            .map_err(|e| format!("TextureTarget::new failed: {e}"))?;
        let renderer = WgpuRenderBackend::new(descriptors, target)
            .map_err(|e| format!("WgpuRenderBackend::new failed: {e}"))?;

        let movie = SwfMovie::from_data(bytes, url.to_owned(), None, None)
            .map_err(|e| format!("SwfMovie::from_data failed: {e}"))?;

        // A real navigator so the movie can fetch its assets + reach the game
        // server. Futures it spawns are driven by `executor` each frame.
        let executor = LocalPool::new();
        let base_url = url::Url::parse(url)
            .map_err(|e| format!("base url parse failed: {e}"))?;
        let navigator = SkuaNavigator::new(base_url, executor.spawner(), false);

        // The ExternalInterface provider — same one the headless bot runtime
        // uses — so the rendered player is bot-addressable on the one Player.
        let handler: SharedHandler = Arc::new(Mutex::new(None));
        let provider = SkuaExternalInterfaceProvider {
            handler: Arc::clone(&handler),
        };

        let player = PlayerBuilder::new()
            .with_renderer(renderer)
            .with_navigator(navigator)
            .with_external_interface(Box::new(provider))
            .with_movie(movie)
            .with_viewport_dimensions(width, height, 1.0)
            .with_autoplay(true)
            .with_player_version(Some(9))
            .build();

        {
            let mut p = player.lock().map_err(|_| "player mutex poisoned".to_owned())?;
            let mut limit = ExecutionLimit::none();
            while !p.preload(&mut limit) {}
        }

        Ok(Self {
            player,
            width,
            height,
            executor: RefCell::new(executor),
            handler,
            last_tick: RefCell::new(None),
        })
    }

    /// Register the AS3 -> host sink (bot receives skua.swf's outbound calls).
    pub fn set_handler(&self, handler: Box<dyn FnMut(&str) + Send>) {
        // Wrap the sink so every AS3→host event's NAME lands in the game log —
        // whether skua.swf emitted 'requestLoadGame'/'loaded', and how often, was
        // previously unknowable from a user's machine.
        let mut inner = handler;
        let mut counts: std::collections::HashMap<String, u64> = std::collections::HashMap::new();
        let logged: Box<dyn FnMut(&str) + Send> = Box::new(move |xml: &str| {
            let name = xml
                .split_once("name=\"")
                .and_then(|(_, rest)| rest.split_once('"'))
                .map(|(n, _)| n)
                .unwrap_or("?");
            // Chatty events (every game packet) are sampled after the first few
            // so the log shows liveness without exploding in size.
            let n = counts.entry(name.to_owned()).or_insert(0);
            *n += 1;
            if *n <= 5 || *n % 200 == 0 {
                crate::navigator::game_log(&format!("EI event: {name} (#{n})"));
            }
            inner(xml);
        });
        if let Ok(mut guard) = self.handler.lock() {
            *guard = Some(logged);
        }
    }

    /// host -> AS3: run a `<invoke>` against the live player and return the
    /// value XML — the bot's `GetGameObject`/`CallGameFunction` path, now hitting
    /// the very player the user is looking at.
    pub fn call(&self, invoke_xml: &str) -> String {
        let (name, args) = match parse_invoke(invoke_xml) {
            Ok(v) => v,
            Err(_) => return "<null/>".to_owned(),
        };
        // Log the non-chatty host→AS3 calls (lifecycle/actions, not the
        // per-frame read polls) so a run's story is reconstructable from disk.
        if !matches!(
            name.as_str(),
            "getGameObject" | "getGameObjectS" | "getGameObjectKey" | "getArrayObject"
                | "isNull" | "isTrue" | "selectArrayObjects" | "lnkCreate" | "lnkDestroy"
        ) {
            crate::navigator::game_log(&format!("EI call: {name}"));
        }
        let ruffle_args: Vec<_> = args.iter().map(to_ruffle).collect();
        let result = {
            let mut p = match self.player.lock() {
                Ok(p) => p,
                Err(_) => return "<null/>".to_owned(),
            };
            p.call_internal_interface(&name, ruffle_args)
        };
        let flash_result = from_ruffle(&result);
        // Opt-in diagnostics (SKUA_TRACE_GETOBJ=1): record read calls that came
        // back empty (null/undefined), WITH the requested path. This is what
        // pinpoints inventory/bank read failures — skua.swf's getGameObject
        // throwing #1009 in Ruffle returns null here, so a script sees an empty
        // inventory and re-does completed steps. Gated so normal runs aren't
        // spammed by the legitimate "does this path exist?" null checks.
        if matches!(
            flash_result,
            crate::value::Value::Null | crate::value::Value::Undefined
        ) && matches!(
            name.as_str(),
            "getGameObject" | "getGameObjectS" | "getGameObjectKey" | "getArrayObject" | "selectArrayObjects"
        ) && std::env::var("SKUA_TRACE_GETOBJ").is_ok()
        {
            let path = match args.first() {
                Some(crate::value::Value::String(s)) => s.clone(),
                other => format!("{other:?}"),
            };
            crate::navigator::game_log(&format!("read returned empty: {name}({path})"));
        }
        serialize_response(&flash_result)
    }

    pub fn player(&self) -> &Arc<Mutex<Player>> {
        &self.player
    }

    pub fn dimensions(&self) -> (u32, u32) {
        (self.width, self.height)
    }

    /// Inject `skua.swf` into THIS live player (the visible game), same-domain
    /// with the game's root movie, so the bot's `Call()` path drives the very
    /// game the user sees. Uses the patched `Player::inject_swf_same_domain`,
    /// then drives `preload` to completion so skua.swf's document-class
    /// constructor runs and registers its ExternalInterface callbacks (the
    /// byte-loader parses during `preload`, not `run_frame`).
    pub fn inject(&self, bytes: Vec<u8>) -> Result<(), String> {
        let mut p = self.player.lock().map_err(|_| "player mutex poisoned".to_owned())?;
        p.inject_swf_same_domain(bytes)?;
        let mut limit = ExecutionLimit::none();
        while !p.preload(&mut limit) {}
        Ok(())
    }

    /// Advance the movie by `frames` frames, draining the navigator's async
    /// work (asset fetches, socket I/O) around each frame so loads progress.
    ///
    /// Each frame we also pump the subsystems `Player::run_frame` alone does
    /// NOT touch — sockets, NetConnections and timers are only serviced by
    /// `Player::tick`, so calling `run_frame` directly (as this did before)
    /// left `SocketAction::Connect` unprocessed forever: the TCP socket to the
    /// AQW game server connected, but AS3's `connect` event never fired and the
    /// client sat on "Connecting to game server...". We service them explicitly
    /// here (mirroring `Player::tick`'s order) while keeping the deterministic
    /// "advance exactly `frames` frames" semantics the render/test paths rely on.
    pub fn tick(&self, frames: u32) {
        for _ in 0..frames {
            self.drive_futures();
            let dt = self.elapsed_since_last_tick();
            if let Ok(mut p) = self.player.lock() {
                p.update_sockets();
                p.update_net_connections();
                p.update_timers(dt);
                p.run_frame();
            }
        }
        self.drive_futures();
    }

    /// Real time elapsed since the previous `tick`, as a `FloatDuration` for
    /// `update_timers`. The first call reports one frame's worth so timers that
    /// are due immediately still fire; subsequent calls report wall-clock delta.
    fn elapsed_since_last_tick(&self) -> FloatDuration {
        let now = Instant::now();
        let dt = match self.last_tick.borrow_mut().replace(now) {
            Some(prev) => FloatDuration::from_std(now.duration_since(prev)),
            None => FloatDuration::from_millis(16.0),
        };
        // Guard against a zero/negative delta (two ticks in the same instant),
        // which would starve timers; give them at least a sliver of progress.
        if dt.as_millis() <= 0.0 {
            FloatDuration::from_millis(1.0)
        } else {
            dt
        }
    }

    /// Pump the navigator's spawned futures to completion-or-stall.
    fn drive_futures(&self) {
        if let Ok(mut ex) = self.executor.try_borrow_mut() {
            ex.run_until_stalled();
        }
    }

    /// Render the current frame and read it back as RGBA8 (`w*h*4` bytes,
    /// row-major, top-left origin). Returns `(width, height, pixels)`.
    pub fn capture(&self) -> Option<(u32, u32, Vec<u8>)> {
        let mut p = self.player.lock().ok()?;
        p.render();
        let renderer =
            <dyn Any>::downcast_mut::<WgpuRenderBackend<TextureTarget>>(p.renderer_mut())?;
        let img = renderer.capture_frame()?;
        let (w, h) = (img.width(), img.height());
        Some((w, h, img.into_raw()))
    }

    pub fn mouse_move(&self, x: f64, y: f64) {
        self.event(PlayerEvent::MouseMove { x, y });
    }

    pub fn mouse_down(&self, x: f64, y: f64) {
        self.event(PlayerEvent::MouseDown {
            x,
            y,
            button: MouseButton::Left,
            index: None,
        });
    }

    pub fn mouse_up(&self, x: f64, y: f64) {
        self.event(PlayerEvent::MouseUp {
            x,
            y,
            button: MouseButton::Left,
        });
    }

    /// Feed a typed character into the movie (text fields, login form, …).
    pub fn text_input(&self, codepoint: char) {
        self.event(PlayerEvent::TextInput { codepoint });
    }

    fn event(&self, e: PlayerEvent) {
        if let Ok(mut p) = self.player.lock() {
            p.handle_event(e);
        }
    }
}

// ---------------------------------------------------------------------------
// RenderHost: owns the (!Send) RenderPlayer on a dedicated worker thread and
// exposes a Send handle (command channel) so the C# side can drive it from any
// thread — the same shape RuffleRuntime uses for the headless player.
// ---------------------------------------------------------------------------

enum RenderCommand {
    /// Advance one frame and read it back as RGBA8.
    Capture(Sender<Option<(u32, u32, Vec<u8>)>>),
    /// kind: 0 = move, 1 = down, 2 = up.
    Mouse(u8, f64, f64),
    Text(char),
    /// host -> AS3 (bot's GetGameObject/CallGameFunction).
    Call(String, Sender<String>),
    /// Register the AS3 -> host handler (bot's FlashCall sink).
    SetHandler(Box<dyn FnMut(&str) + Send>),
    /// Inject skua.swf into the live game player (same-domain).
    LoadSwf(Vec<u8>, Sender<Result<(), String>>),
    Shutdown,
}

pub struct RenderHost {
    tx: Sender<RenderCommand>,
    worker: Option<JoinHandle<()>>,
    width: u32,
    height: u32,
}

impl RenderHost {
    /// Fetch the root movie at `url` and start a render worker at `width`x`height`.
    /// Blocks until the player is built + preloaded (or reports the error).
    pub fn create(url: &str, width: u32, height: u32) -> Result<Self, String> {
        let url = url.to_owned();
        Self::spawn(width, height, move || {
            let bytes = fetch_bytes(&url)?;
            RenderPlayer::from_swf_bytes(&bytes, &url, width, height)
        })
    }

    /// Start a render worker whose ROOT movie comes from local `bytes` (with
    /// `url` as its nominal origin — use an https URL so origin-gated APIs like
    /// `SharedObject.getLocal(secure)` behave as they do on the real site).
    ///
    /// This is the Skua architecture: `skua.swf` is the root movie (its
    /// ExternalInterface callbacks register on init), and the host then calls
    /// `loadClient` so skua.swf loads the live game INTO itself — giving its API
    /// the `game` reference every bot call resolves through. Loading the game as
    /// root and injecting skua.swf beside it leaves that reference null and the
    /// bot inert, which is why this exists.
    pub fn create_from_bytes(
        bytes: Vec<u8>,
        url: &str,
        width: u32,
        height: u32,
    ) -> Result<Self, String> {
        if bytes.is_empty() {
            return Err("empty root movie payload".to_owned());
        }
        let url = url.to_owned();
        Self::spawn(width, height, move || {
            RenderPlayer::from_swf_bytes(&bytes, &url, width, height)
        })
    }

    /// Shared worker startup: build the player via `build` on a dedicated thread
    /// (ruffle's `Player` is `!Send`), then run the command/tick loop.
    fn spawn(
        width: u32,
        height: u32,
        build: impl FnOnce() -> Result<RenderPlayer, String> + Send + 'static,
    ) -> Result<Self, String> {
        install_ruffle_log();
        let (tx, rx) = channel::<RenderCommand>();
        let (ready_tx, ready_rx) = channel::<Result<(), String>>();

        let worker = std::thread::spawn(move || {
            let built = build();

            let player = match built {
                Ok(p) => {
                    let _ = ready_tx.send(Ok(()));
                    p
                }
                Err(e) => {
                    let _ = ready_tx.send(Err(e));
                    return;
                }
            };

            // Self-driving tick loop: the game (and therefore the bot) advances
            // at ~30fps HERE, decoupled from pixel capture. So a client window
            // that isn't being watched can stop requesting captures (zero wgpu
            // render/readback cost) while its bot keeps running full speed — the
            // key to running an army without every background window paying to
            // render. `Capture` now only reads back the latest frame; it does not
            // tick. Commands are still handled promptly via `recv_timeout`.
            let frame = Duration::from_millis(33);
            let mut next_tick = Instant::now() + frame;
            loop {
                let timeout = next_tick.saturating_duration_since(Instant::now());
                match rx.recv_timeout(timeout) {
                    Ok(RenderCommand::Capture(reply)) => {
                        let _ = reply.send(player.capture());
                    }
                    Ok(RenderCommand::Mouse(kind, x, y)) => match kind {
                        1 => player.mouse_down(x, y),
                        2 => player.mouse_up(x, y),
                        _ => player.mouse_move(x, y),
                    },
                    Ok(RenderCommand::Text(c)) => player.text_input(c),
                    Ok(RenderCommand::Call(xml, reply)) => {
                        let _ = reply.send(player.call(&xml));
                    }
                    Ok(RenderCommand::SetHandler(h)) => player.set_handler(h),
                    Ok(RenderCommand::LoadSwf(swf, reply)) => {
                        let result = if swf.is_empty() {
                            Err("empty skua.swf payload".to_owned())
                        } else {
                            player.inject(swf)
                        };
                        let _ = reply.send(result);
                    }
                    Ok(RenderCommand::Shutdown) => break,
                    Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {}
                    Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => break,
                }

                // Advance the game whenever a frame is due (keeps the bot running
                // even while commands flow). Reset on drift so we don't spiral.
                if Instant::now() >= next_tick {
                    player.tick(1);
                    next_tick += frame;
                    if next_tick < Instant::now() {
                        next_tick = Instant::now() + frame;
                    }
                }
            }
        });

        match ready_rx.recv() {
            Ok(Ok(())) => Ok(Self {
                tx,
                worker: Some(worker),
                width,
                height,
            }),
            Ok(Err(e)) => Err(e),
            Err(_) => Err("render worker died during startup".to_owned()),
        }
    }

    pub fn dimensions(&self) -> (u32, u32) {
        (self.width, self.height)
    }

    /// Advance one frame and return its RGBA8 pixels (blocks on the worker).
    pub fn capture(&self) -> Option<(u32, u32, Vec<u8>)> {
        let (tx, rx) = channel();
        self.tx.send(RenderCommand::Capture(tx)).ok()?;
        rx.recv().ok().flatten()
    }

    pub fn mouse(&self, kind: u8, x: f64, y: f64) {
        let _ = self.tx.send(RenderCommand::Mouse(kind, x, y));
    }

    pub fn text(&self, codepoint: char) {
        let _ = self.tx.send(RenderCommand::Text(codepoint));
    }

    /// host -> AS3 on the rendered player (the bot's read/call path).
    pub fn call(&self, invoke_xml: &str) -> String {
        let (tx, rx) = channel();
        if self
            .tx
            .send(RenderCommand::Call(invoke_xml.to_owned(), tx))
            .is_err()
        {
            return "<null/>".to_owned();
        }
        rx.recv().unwrap_or_else(|_| "<null/>".to_owned())
    }

    /// Register the AS3 -> host handler (the bot's FlashCall event sink).
    pub fn set_handler(&self, handler: Box<dyn FnMut(&str) + Send>) {
        let _ = self.tx.send(RenderCommand::SetHandler(handler));
    }

    /// Inject skua.swf into the live game player (blocks on the worker).
    pub fn load_swf(&self, bytes: Vec<u8>) -> Result<(), String> {
        let (tx, rx) = channel();
        if self.tx.send(RenderCommand::LoadSwf(bytes, tx)).is_err() {
            return Err("render worker gone".to_owned());
        }
        rx.recv().unwrap_or_else(|_| Err("render worker dropped reply".to_owned()))
    }
}

impl Drop for RenderHost {
    fn drop(&mut self) {
        let _ = self.tx.send(RenderCommand::Shutdown);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

/// Install a `tracing` subscriber (once per process) that appends WARN+ events
/// to `~/.config/Skua/vibeskua-ruffle.log`. Ruffle reports every AVM2 error,
/// loader failure, and stub through `tracing`; without a subscriber they are
/// silently dropped, which made in-game failures (a black screen after an
/// uncaught AVM2 error, a failed asset fetch) undiagnosable in the shipped app.
/// `RUST_LOG` overrides the default `warn` filter. Best-effort: if another
/// subscriber is already installed (tests, the boot_skua example), keep it.
fn install_ruffle_log() {
    static ONCE: std::sync::Once = std::sync::Once::new();
    ONCE.call_once(|| {
        // Per-launch path published by the C# host (SessionLog.Init); falls
        // back to the legacy single file when hosted without it.
        let path = match std::env::var("SKUA_RUFFLE_LOG") {
            Ok(p) if !p.is_empty() => std::path::PathBuf::from(p),
            _ => {
                let Ok(home) = std::env::var("HOME") else { return };
                let dir = std::path::Path::new(&home).join(".config").join("Skua");
                let _ = std::fs::create_dir_all(&dir);
                dir.join("vibeskua-ruffle.log")
            }
        };
        if let Some(parent) = path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        let Ok(file) = std::fs::OpenOptions::new().create(true).append(true).open(&path) else {
            return;
        };
        let filter = tracing_subscriber::EnvFilter::try_from_default_env()
            .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("warn"));
        let _ = tracing_subscriber::fmt()
            .with_env_filter(filter)
            .with_writer(Mutex::new(file))
            .with_ansi(false)
            .try_init();
        eprintln!("[skua-render] ruffle log -> {}", path.display());
    });
}

/// Blocking fetch of the root movie bytes over https (the game loader).
fn fetch_bytes(url: &str) -> Result<Vec<u8>, String> {
    let resp = ureq::AgentBuilder::new()
        .timeout(Duration::from_secs(30))
        .build()
        .get(url)
        .call()
        .map_err(|e| format!("fetch {url} failed: {e}"))?;
    let mut buf = Vec::new();
    resp.into_reader()
        .read_to_end(&mut buf)
        .map_err(|e| e.to_string())?;
    Ok(buf)
}
