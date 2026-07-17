//! `RuffleRuntime` — the real [`FlashRuntime`] backed by an embedded
//! `ruffle_core::Player`.
//!
//! ## Build & verification status
//!
//! Compiled **only** under `--features ruffle`, which pulls in `ruffle_core` (a
//! git-only dependency on github.com, not on crates.io), pinned in `Cargo.toml`
//! to the nightly this was verified against. It is excluded from the default
//! build and the offline CI job so the transport crate stays dependency-free
//! and verifiable with no network.
//!
//! This module compiles and links against the **real** `ruffle_core`, and the
//! integration test `tests/ruffle_roundtrip.rs` (also `--features ruffle`) drives
//! an embedded `Player` end to end against a real AVM2 SWF: **host → AS3** via
//! `call_internal_interface` round-trips a value back, and **AS3 → host** via
//! the SWF's `ExternalInterface.call(...)` reaches our provider's `call_method`.
//! Both directions go through this crate's ExternalInterface XML codec, exactly
//! as the Windows `FlashCall`/`CallFunction` path does.
//!
//! A headless host needs only `ruffle_core`: `PlayerBuilder::build()` falls back
//! to the built-in `NullRenderer`/`NullAudioBackend`/`NullNavigatorBackend`, so
//! no GPU/audio/network backends are required (Skua collapses the viewport to
//! 1x1 for farm/army mode anyway).
//!
//! Same-domain `skua.swf` injection (`inject_swf_same_domain` below) — the piece
//! that lets the bot DRIVE the game — is implemented via the patched `ruffle_core`
//! (`native/ruffle-skua-inject.patch`) and verified end to end by
//! `tests/ruffle_inject.rs` without the live game. The only path that still needs
//! a network-capable environment is loading AQW's remote `Loader3.swf` over https
//! (`load_movie_from_url`); the local-SWF path (`from_swf_bytes`) is the same code
//! above the navigator seam.
//!
//! ## Design (both directions route through this crate's XML codec)
//!
//! * **host → AS3** (`FlashRuntime::call`): parse the `<invoke>`, convert args to
//!   `external::Value`, invoke the AS3-registered callback via
//!   `Player::call_internal_interface`, serialize the result back to value XML.
//! * **AS3 → host** (`skua.swf` doing `ExternalInterface.call("debug", …)` etc.):
//!   Ruffle routes it to our provider's `call_method`, which re-encodes it as the
//!   same `<invoke>` XML the Windows `FlashCall` event carries and hands it to the
//!   stored host handler.

use std::sync::{Arc, Mutex};

use crate::runtime::FlashRuntime;
use crate::value::FlashValue;
use crate::xml::{parse_invoke, serialize_invoke, serialize_response};

use ruffle_core::context::UpdateContext;
use ruffle_core::external::{ExternalInterfaceProvider, Value as RuffleValue};
use ruffle_core::limits::ExecutionLimit;
use ruffle_core::tag_utils::SwfMovie;
use ruffle_core::{Player, PlayerBuilder};

/// Shared AS3 → host sink. `None` until the C# side registers a callback.
pub(crate) type SharedHandler = Arc<Mutex<Option<Box<dyn FnMut(&str) + Send>>>>;

// ---------------------------------------------------------------------------
// Value marshalling  (FlashValue <-> ruffle_core::external::Value)
//
// external::Value is exactly:
//   Undefined | Null | Bool(bool) | Number(f64) | String(String)
//     | Object(BTreeMap<String, Value>) | List(Vec<Value>)
// (verified against core/src/external.rs), so this is a 1:1 mapping.
// ---------------------------------------------------------------------------

pub(crate) fn to_ruffle(value: &FlashValue) -> RuffleValue {
    match value {
        FlashValue::Null => RuffleValue::Null,
        FlashValue::Undefined => RuffleValue::Undefined,
        FlashValue::Bool(b) => RuffleValue::Bool(*b),
        FlashValue::Number(n) => RuffleValue::Number(*n),
        FlashValue::String(s) => RuffleValue::String(s.clone()),
        FlashValue::Array(items) => RuffleValue::List(items.iter().map(to_ruffle).collect()),
        FlashValue::Object(map) => {
            RuffleValue::Object(map.iter().map(|(k, v)| (k.clone(), to_ruffle(v))).collect())
        }
    }
}

pub(crate) fn from_ruffle(value: &RuffleValue) -> FlashValue {
    match value {
        RuffleValue::Null => FlashValue::Null,
        RuffleValue::Undefined => FlashValue::Undefined,
        RuffleValue::Bool(b) => FlashValue::Bool(*b),
        RuffleValue::Number(n) => FlashValue::Number(*n),
        RuffleValue::String(s) => FlashValue::String(s.clone()),
        RuffleValue::List(items) => FlashValue::Array(items.iter().map(from_ruffle).collect()),
        RuffleValue::Object(map) => {
            FlashValue::Object(map.iter().map(|(k, v)| (k.clone(), from_ruffle(v))).collect())
        }
    }
}

// ---------------------------------------------------------------------------
// AS3 -> host: our ExternalInterfaceProvider.
//
// Trait (verified, core/src/external.rs):
//   fn call_method(&self, context: &mut UpdateContext<'_>, name: &str, args: &[Value]) -> Value;
//   fn on_callback_available(&self, name: &str);
//   fn get_id(&self) -> Option<String>;
// ---------------------------------------------------------------------------

pub(crate) struct SkuaExternalInterfaceProvider {
    pub(crate) handler: SharedHandler,
}

impl ExternalInterfaceProvider for SkuaExternalInterfaceProvider {
    fn call_method(
        &self,
        _context: &mut UpdateContext<'_>,
        name: &str,
        args: &[RuffleValue],
    ) -> RuffleValue {
        // Re-encode as the same <invoke> XML the Windows FlashCall event carries,
        // so the C# handler is byte-for-byte identical to the ActiveX path.
        let converted: Vec<FlashValue> = args.iter().map(from_ruffle).collect();
        let invoke_xml = serialize_invoke(name, &converted);

        if let Ok(mut guard) = self.handler.lock() {
            if let Some(h) = guard.as_mut() {
                h(&invoke_xml);
            }
        }
        // skua.swf's host-directed calls (debug, requestLoadGame, …) ignore the
        // return value.
        RuffleValue::Null
    }

    fn on_callback_available(&self, _name: &str) {
        // AS3 registered a callback; the host invokes callbacks lazily by name in
        // RuffleRuntime::call, so nothing to do here.
    }

    fn get_id(&self) -> Option<String> {
        None
    }
}

// ---------------------------------------------------------------------------
// The runtime.
//
// `ruffle_core::Player` is deliberately `!Send` (it holds a single-threaded
// gc-arena and `Rc`-based backends), but `FlashRuntime: Send` because the C#
// host may call the `.so` from any thread. We reconcile the two the same way
// `ruffle_desktop` does: the Player lives on ONE dedicated thread, and the
// runtime handle talks to it over a command channel. The handle holds only
// `Sender`s + a `JoinHandle` (all `Send`), so `RuffleRuntime: Send` holds.
// ---------------------------------------------------------------------------

use std::sync::mpsc::{channel, Sender};
use std::thread::JoinHandle;

/// Commands sent to the player thread. Each carries a reply channel where the
/// worker posts the result, so the calling side stays synchronous (matching the
/// blocking `IFlashUtil` contract).
enum Command {
    Call(String, Sender<String>),
    RunFrames(u32, Sender<()>),
    LoadSwf(Vec<u8>, Sender<Result<(), String>>),
    SetHandler(Box<dyn FnMut(&str) + Send>),
    Shutdown,
}

pub struct RuffleRuntime {
    tx: Sender<Command>,
    worker: Option<JoinHandle<()>>,
}

impl RuffleRuntime {
    /// Build a headless Player, load AQW's `Loader3.swf` over https, and register
    /// the Skua ExternalInterface provider.
    ///
    /// The game URL defaults to AQW's loader and can be overridden with the
    /// `SKUA_GAME_URL` environment variable. It MUST be `https` — AQW calls
    /// `SharedObject.getLocal(secure: true)`, which Ruffle refuses over `http`,
    /// after which `Game.onAddedToStage()` dereferences null → `Error #1009`
    /// (see CLAUDE.md).
    pub fn new() -> Result<Self, String> {
        let game_url = std::env::var("SKUA_GAME_URL")
            .unwrap_or_else(|_| "https://game.aq.com/game/gamefiles/Loader3.swf?ver=a".to_owned());
        if !game_url.starts_with("https://") {
            return Err(format!("SKUA_GAME_URL must be https (got {game_url})"));
        }

        let bytes = load_movie_from_url(&game_url)
            .map_err(|e| format!("failed to fetch {game_url}: {e}"))?;
        Self::from_swf_bytes(&bytes, &game_url)
    }

    /// Spawn the player thread with `bytes` as its root movie and register the
    /// Skua ExternalInterface provider. This is the whole runtime above the
    /// navigator seam — the round-trip test drives it directly with a local SWF,
    /// and `new()` funnels the fetched game bytes through here. Blocks until the
    /// player has built + preloaded (or reports the build error).
    pub fn from_swf_bytes(bytes: &[u8], url: &str) -> Result<Self, String> {
        let bytes = bytes.to_vec();
        let url = url.to_owned();
        let (tx, rx) = channel::<Command>();
        let (ready_tx, ready_rx) = channel::<Result<(), String>>();

        let worker = std::thread::spawn(move || {
            // Everything Player-touching happens on THIS thread only.
            let handler: SharedHandler = Arc::new(Mutex::new(None));

            let built = (|| -> Result<Arc<Mutex<Player>>, String> {
                // SwfMovie::from_data(&data, url, loader_url, load_bytes_info)
                // (verified, core/common/src/tag_utils.rs).
                let movie = SwfMovie::from_data(&bytes, url, None, None)
                    .map_err(|e| format!("SwfMovie::from_data failed: {e}"))?;

                let provider = SkuaExternalInterfaceProvider {
                    handler: Arc::clone(&handler),
                };

                // Headless player: build() supplies Null renderer/audio/
                // navigator/ui, so only the movie + our provider are needed.
                // autoplay(true) is required for run_frame() to advance.
                let player = PlayerBuilder::new()
                    .with_movie(movie)
                    .with_autoplay(true)
                    .with_external_interface(Box::new(provider))
                    .build();

                // Preload fully (unbounded budget) so symbols + the root AVM2
                // script are ready before the first tick.
                {
                    let mut p = player.lock().map_err(|_| "player mutex poisoned".to_owned())?;
                    let mut limit = ExecutionLimit::none();
                    while !p.preload(&mut limit) {}
                }
                Ok(player)
            })();

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

            // Command loop — all Player access is serialized here.
            for cmd in rx {
                match cmd {
                    Command::Call(xml, reply) => {
                        let _ = reply.send(do_call(&player, &xml));
                    }
                    Command::RunFrames(n, done) => {
                        if let Ok(mut p) = player.lock() {
                            for _ in 0..n {
                                p.run_frame();
                            }
                        }
                        let _ = done.send(());
                    }
                    Command::LoadSwf(swf, reply) => {
                        let result = if swf.is_empty() {
                            Err("empty skua.swf payload".to_owned())
                        } else if let Ok(mut p) = player.lock() {
                            inject_swf_same_domain(&mut p, &swf).map(|()| { for _ in 0..4 { p.run_frame(); } })
                        } else {
                            Err("player mutex poisoned".to_owned())
                        };
                        let _ = reply.send(result);
                    }
                    Command::SetHandler(h) => {
                        if let Ok(mut guard) = handler.lock() {
                            *guard = Some(h);
                        }
                    }
                    Command::Shutdown => break,
                }
            }
        });

        match ready_rx.recv() {
            Ok(Ok(())) => Ok(RuffleRuntime {
                tx,
                worker: Some(worker),
            }),
            Ok(Err(e)) => Err(e),
            Err(_) => Err("ruffle player thread died during startup".to_owned()),
        }
    }

    /// Advance the movie by `frames` frames on the player thread. The root AVM2
    /// script (which calls `ExternalInterface.addCallback` / `ExternalInterface.call`)
    /// runs as frames execute, so set the AS3→host handler *before* ticking to
    /// capture the movie's outbound calls. Blocks until the frames have run.
    pub fn run_frames(&mut self, frames: u32) {
        let (done_tx, done_rx) = channel();
        if self.tx.send(Command::RunFrames(frames, done_tx)).is_ok() {
            let _ = done_rx.recv();
        }
    }
}

impl Drop for RuffleRuntime {
    fn drop(&mut self) {
        let _ = self.tx.send(Command::Shutdown);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

/// host → AS3: parse the `<invoke>`, marshal args, call the AS3-registered
/// callback via `Player::call_internal_interface`, and serialize the result back
/// to value XML. Runs on the player thread. Verified (core/src/player.rs):
///   pub fn call_internal_interface(
///       &mut self, name: &str, args: impl IntoIterator<Item = Value>) -> Value
fn do_call(player: &Arc<Mutex<Player>>, invoke_xml: &str) -> String {
    let (name, args) = match parse_invoke(invoke_xml) {
        Ok(v) => v,
        Err(_) => return "<null/>".to_owned(),
    };
    let ruffle_args: Vec<RuffleValue> = args.iter().map(to_ruffle).collect();

    let result = {
        let mut p = match player.lock() {
            Ok(p) => p,
            Err(_) => return "<null/>".to_owned(),
        };
        p.call_internal_interface(&name, ruffle_args)
    };

    serialize_response(&from_ruffle(&result))
}

impl FlashRuntime for RuffleRuntime {
    fn load_swf(&mut self, bytes: &[u8]) -> Result<(), String> {
        let (reply_tx, reply_rx) = channel();
        self.tx
            .send(Command::LoadSwf(bytes.to_vec(), reply_tx))
            .map_err(|_| "ruffle player thread gone".to_owned())?;
        reply_rx
            .recv()
            .map_err(|_| "ruffle player thread gone".to_owned())?
    }

    fn call(&mut self, invoke_xml: &str) -> String {
        let (reply_tx, reply_rx) = channel();
        if self
            .tx
            .send(Command::Call(invoke_xml.to_owned(), reply_tx))
            .is_err()
        {
            return "<null/>".to_owned();
        }
        reply_rx.recv().unwrap_or_else(|_| "<null/>".to_owned())
    }

    fn set_flash_call_handler(&mut self, handler: Box<dyn FnMut(&str) + Send>) {
        let _ = self.tx.send(Command::SetHandler(handler));
    }
}

// ---------------------------------------------------------------------------
// Backend wiring — the version-sensitive glue. Kept in small, clearly-labelled
// functions so reconciling against a pinned Ruffle nightly is localised.
// ---------------------------------------------------------------------------

/// NAVIGATOR: fetch the SWF at `url` over https and return its bytes, which
/// `from_swf_bytes` then parses via `SwfMovie::from_data`. A headless
/// `ruffle_core` build has no HTTP navigator wired in (build() uses the
/// `NullNavigatorBackend`), so this uses a plain blocking https GET — the only
/// network the host needs for the *root* movie. AQW must be served over https
/// (see `new`). game.aq.com is egress-blocked in the current CI/dev container,
/// so this path is exercised only where that host is reachable; the local-SWF
/// path (`from_swf_bytes`) is identical above this seam.
fn load_movie_from_url(url: &str) -> Result<Vec<u8>, String> {
    // Kept dependency-light: shell out to curl rather than pull an async HTTP
    // stack into the headless build. Swappable for a real navigator later.
    let out = std::process::Command::new("curl")
        .args(["-fsSL", url])
        .output()
        .map_err(|e| format!("spawning curl failed: {e}"))?;
    if !out.status.success() {
        return Err(format!(
            "curl exited {}: {}",
            out.status,
            String::from_utf8_lossy(&out.stderr)
        ));
    }
    Ok(out.stdout)
}

/// AS DOMAIN: load `bytes` (skua.swf) into the running player in the game's
/// ApplicationDomain so Skua can resolve `world`, `mcLogin`, … via
/// `ApplicationDomain.getDefinition`. This is the mechanism the bot needs to
/// *drive* the game (skua.swf registers the `ExternalInterface` callbacks —
/// `getGameObject`, `callGameFunction`, `sendPacket`, … — that our `Call()`
/// path invokes; those callbacks reach into the game via `getDefinition`).
///
/// ## How it works (and why it lives in a ruffle patch)
///
/// This mirrors AS3's `Loader.loadBytes`: build a `flash.display.Loader` +
/// `LoaderInfoObject`, resolve the game's root-movie `Avm2Domain`, and call
/// `LoadManager::load_movie_into_clip_bytes` with `MovieLoaderVMData::Avm2`.
/// `load_movie_into_clip_bytes` is `pub`, but every type it needs
/// (`LoaderInfoObject`, `LoaderDisplay`, `Avm2Domain`, …) lives in `ruffle_core`'s
/// **private** `avm2`/`display_object` modules, unreachable from this crate — so
/// the logic is added *inside* ruffle as one `pub Player::inject_swf_same_domain`
/// via `native/ruffle-skua-inject.patch` (applied by `prepare-ruffle-fork.sh`).
///
/// Verified end to end without the live game by `tests/ruffle_inject.rs`: an
/// injected SWF's document-class constructor runs inside the game's domain and
/// its `ExternalInterface.call` reaches the host handler — exactly what skua.swf
/// does. (Note: the injected movie's document class must extend `Sprite`/
/// `MovieClip`, which real skua.swf does, or ruffle's root-link check rejects it.)
fn inject_swf_same_domain(player: &mut Player, bytes: &[u8]) -> Result<(), String> {
    player.inject_swf_same_domain(bytes.to_vec())
}
