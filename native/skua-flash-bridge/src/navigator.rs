//! A minimal `NavigatorBackend` so the in-app game view can actually LOAD the
//! game (feature `ruffle-render`).
//!
//! ruffle's headless build uses a null navigator that can't fetch anything, so
//! AQW would never get past its loader. ruffle_desktop's navigator pulls in the
//! whole reqwest+tokio stack and is wired to a winit event loop; instead this is
//! a small self-contained backend:
//!
//! * **HTTP** (`fetch`) — a blocking `ureq` GET/POST on a worker thread, handed
//!   back to ruffle's async loader via a `oneshot`. The body is read fully up
//!   front, so `body()`/`next_chunk()` just replay it.
//! * **Sockets** (`connect_socket`) — a `std::net::TcpStream` (the AQW game
//!   server is a raw XML socket), pumped both ways on threads and reported to
//!   AVM via `SocketAction`.
//! * **Futures** are driven by a `futures::executor::LocalPool` the render host
//!   ticks each frame (no tokio).

#![cfg(feature = "ruffle-render")]

use std::borrow::Cow;
use std::io::{Read, Write};
use std::net::TcpStream;
use std::time::Duration;

use async_channel::{Receiver, Sender};
use encoding_rs::Encoding;
use futures::channel::oneshot;
use futures::executor::LocalSpawner;
use futures::task::LocalSpawnExt;

use ruffle_core::backend::navigator::{
    ErrorResponse, NavigationMethod, NavigatorBackend, OwnedFuture, Request, SuccessResponse,
};
use indexmap::IndexMap;
use ruffle_core::loader::Error as LoaderError;
use ruffle_core::socket::{ConnectionState, SocketAction, SocketHandle};
use url::{ParseError, Url};

/// Append a line to `~/.config/Skua/vibeskua-game.log` (and stderr) so the live
/// game's network activity is diagnosable without a terminal. Best-effort.
/// Each line is prefixed with UTC seconds + PID: multiple processes (manager +
/// clients) share the file and runs were previously indistinguishable.
pub(crate) fn game_log(msg: &str) {
    let ts = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let pid = std::process::id();
    eprintln!("[skua-game {pid}] {msg}");
    if let Ok(home) = std::env::var("HOME") {
        let dir = std::path::Path::new(&home).join(".config").join("Skua");
        let _ = std::fs::create_dir_all(&dir);
        if let Ok(mut f) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(dir.join("vibeskua-game.log"))
        {
            use std::io::Write as _;
            let _ = writeln!(f, "[{ts} {pid}] {msg}");
        }
    }
}

/// Result of a completed blocking HTTP request.
struct HttpResult {
    url: String,
    status: u16,
    body: Vec<u8>,
}

pub struct SkuaNavigator {
    base_url: Url,
    spawner: LocalSpawner,
    upgrade_to_https: bool,
}

impl SkuaNavigator {
    pub fn new(base_url: Url, spawner: LocalSpawner, upgrade_to_https: bool) -> Self {
        Self {
            base_url,
            spawner,
            upgrade_to_https,
        }
    }
}

impl NavigatorBackend for SkuaNavigator {
    fn navigate_to_url(
        &self,
        _url: &str,
        _target: &str,
        _vars_method: Option<(NavigationMethod, IndexMap<String, String>)>,
    ) {
        // Opening external links in a browser isn't meaningful for the embedded
        // game view; ignore (AQW's gameplay doesn't rely on it).
    }

    fn fetch(&self, request: Request) -> OwnedFuture<Box<dyn SuccessResponse>, ErrorResponse> {
        let url = match self.resolve_url(request.url()) {
            Ok(u) => u.to_string(),
            Err(e) => {
                let err = ErrorResponse {
                    url: request.url().to_string(),
                    error: LoaderError::FetchError(e.to_string()),
                };
                return Box::pin(async move { Err(err) });
            }
        };

        // Debug-only escape hatch: SKUA_URL_REWRITE="<prefix>=><replacement>"
        // rewrites the PHYSICAL request URL (e.g. to a local fixture server)
        // while the movie — and everything origin-gated in ruffle — still sees
        // the original URL. Lets the skua.swf -> game boot chain be exercised in
        // sandboxes with no game.aq.com egress. No effect when the env is unset.
        let fetch_url = match std::env::var("SKUA_URL_REWRITE") {
            Ok(rule) => match rule.split_once("=>") {
                Some((from, to)) if url.starts_with(from) => {
                    let rewritten = format!("{to}{}", &url[from.len()..]);
                    game_log(&format!("rewrite {url} -> {rewritten}"));
                    rewritten
                }
                _ => url.clone(),
            },
            Err(_) => url.clone(),
        };

        let method = request.method();
        let body = request.body().clone();
        let headers: Vec<(String, String)> = request
            .headers()
            .iter()
            .map(|(k, v)| (k.clone(), v.clone()))
            .collect();

        // Run the blocking HTTP request off-thread; the async loader awaits the
        // oneshot. LocalPool has no reactor, so we must not block it.
        let (tx, rx) = oneshot::channel::<Result<HttpResult, String>>();
        let req_url = fetch_url;
        let logical_url = url.clone();
        std::thread::spawn(move || {
            let mut result = blocking_http(&req_url, method, body, &headers);
            match &result {
                Ok(r) => game_log(&format!("fetch {} -> {} ({} bytes)", req_url, r.status, r.body.len())),
                Err(e) => game_log(&format!("fetch {req_url} -> ERROR: {e}")),
            }
            // Report the LOGICAL url back to ruffle so the loaded content keeps
            // its original (game-domain) identity even when rewritten.
            if let Ok(r) = &mut result {
                r.url = logical_url;
            }
            let _ = tx.send(result);
        });

        Box::pin(async move {
            match rx.await {
                Ok(Ok(res)) => Ok(Box::new(SkuaResponse {
                    url: res.url,
                    status: res.status,
                    body: Some(res.body),
                }) as Box<dyn SuccessResponse>),
                Ok(Err(e)) => Err(ErrorResponse {
                    url,
                    error: LoaderError::FetchError(e),
                }),
                Err(_) => Err(ErrorResponse {
                    url,
                    error: LoaderError::FetchError("fetch worker dropped".to_owned()),
                }),
            }
        })
    }

    fn resolve_url(&self, url: &str) -> Result<Url, ParseError> {
        match self.base_url.join(url) {
            Ok(u) => Ok(self.pre_process_url(u)),
            Err(e) => Err(e),
        }
    }

    fn spawn_future(&mut self, future: OwnedFuture<(), LoaderError>) {
        let _ = self.spawner.spawn_local(async move {
            if let Err(e) = future.await {
                eprintln!("[skua-navigator] async task error: {e}");
            }
        });
    }

    fn pre_process_url(&self, mut url: Url) -> Url {
        if self.upgrade_to_https && url.scheme() == "http" {
            let _ = url.set_scheme("https");
        }
        url
    }

    fn connect_socket(
        &mut self,
        host: String,
        port: u16,
        timeout: Duration,
        handle: SocketHandle,
        receiver: Receiver<Vec<u8>>,
        sender: Sender<SocketAction>,
    ) {
        std::thread::spawn(move || {
            let addr = format!("{host}:{port}");
            game_log(&format!("socket: connecting to {addr} (timeout {timeout:?})"));
            let stream = match addr
                .to_socket_addrs_first()
                .and_then(|sa| TcpStream::connect_timeout(&sa, timeout))
            {
                Ok(s) => s,
                Err(e) => {
                    game_log(&format!("socket: connect to {addr} FAILED: {e}"));
                    let _ = sender.send_blocking(SocketAction::Connect(
                        handle,
                        ConnectionState::Failed,
                    ));
                    return;
                }
            };
            let _ = stream.set_nodelay(true);
            game_log(&format!("socket: connected to {addr}"));
            let _ = sender.send_blocking(SocketAction::Connect(handle, ConnectionState::Connected));

            // Reader: socket -> AVM.
            let read_stream = match stream.try_clone() {
                Ok(s) => s,
                Err(_) => {
                    let _ = sender.send_blocking(SocketAction::Close(handle));
                    return;
                }
            };
            let read_sender = sender.clone();
            let reader = std::thread::spawn(move || {
                let mut stream = read_stream;
                let mut buf = [0u8; 4096];
                let mut total = 0u64;
                let mut first = true;
                loop {
                    match stream.read(&mut buf) {
                        Ok(0) => {
                            game_log(&format!("socket: server closed (read {total} bytes)"));
                            break;
                        }
                        Err(e) => {
                            game_log(&format!("socket: read error after {total} bytes: {e}"));
                            break;
                        }
                        Ok(n) => {
                            total += n as u64;
                            if first {
                                first = false;
                                game_log(&format!("socket: first {n} bytes from server"));
                            }
                            if read_sender
                                .send_blocking(SocketAction::Data(handle, buf[..n].to_vec()))
                                .is_err()
                            {
                                break;
                            }
                        }
                    }
                }
                let _ = read_sender.send_blocking(SocketAction::Close(handle));
            });

            // Writer: AVM -> socket. Ends when the AVM side drops the sender.
            let mut write_stream = stream;
            let mut sent = 0u64;
            while let Ok(data) = receiver.recv_blocking() {
                if sent == 0 {
                    game_log(&format!("socket: first {} bytes to server", data.len()));
                }
                sent += data.len() as u64;
                if write_stream.write_all(&data).is_err() {
                    game_log("socket: write error");
                    break;
                }
            }
            game_log(&format!("socket: writer ended (sent {sent} bytes)"));
            let _ = write_stream.shutdown(std::net::Shutdown::Both);
            let _ = reader.join();
        });
    }
}

/// Resolve `host:port` to the first socket address (small helper to keep the
/// `connect_socket` body readable).
trait ToSocketAddrFirst {
    fn to_socket_addrs_first(&self) -> std::io::Result<std::net::SocketAddr>;
}
impl ToSocketAddrFirst for String {
    fn to_socket_addrs_first(&self) -> std::io::Result<std::net::SocketAddr> {
        use std::net::ToSocketAddrs;
        self.to_socket_addrs()?.next().ok_or_else(|| {
            std::io::Error::new(std::io::ErrorKind::AddrNotAvailable, "no address resolved")
        })
    }
}

fn blocking_http(
    url: &str,
    method: NavigationMethod,
    body: Option<(Vec<u8>, String)>,
    headers: &[(String, String)],
) -> Result<HttpResult, String> {
    let agent = ureq::AgentBuilder::new()
        .timeout(Duration::from_secs(30))
        .build();

    let mut req = match method {
        NavigationMethod::Get => agent.get(url),
        NavigationMethod::Post => agent.post(url),
    };
    // Browser-like defaults: ureq's own UA ("ureq/2.x") is exactly what
    // bot-filtering front-ends (Cloudflare et al.) reject, and game.aq.com's
    // API endpoints sit behind one. The game's Flash runtime always presented
    // a browser UA, so match that unless the movie set its own headers.
    if !headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("user-agent")) {
        req = req.set(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        );
    }
    if !headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("accept")) {
        req = req.set("Accept", "*/*");
    }
    if url.contains("aq.com") && !headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("referer")) {
        req = req.set("Referer", "https://game.aq.com/game/");
    }
    for (k, v) in headers {
        req = req.set(k, v);
    }

    let response = match body {
        Some((data, content_type)) if !data.is_empty() => {
            req.set("Content-Type", &content_type).send_bytes(&data)
        }
        _ => req.call(),
    };

    let response = match response {
        Ok(r) => r,
        // ureq returns Err for non-2xx; surface the status if it's an HTTP error
        // so ruffle can decide, otherwise a transport error.
        Err(ureq::Error::Status(code, r)) => {
            let final_url = r.get_url().to_owned();
            let mut buf = Vec::new();
            let _ = r.into_reader().read_to_end(&mut buf);
            return Ok(HttpResult {
                url: final_url,
                status: code,
                body: buf,
            });
        }
        Err(e) => return Err(e.to_string()),
    };

    let status = response.status();
    let final_url = response.get_url().to_owned();
    let mut buf = Vec::new();
    response
        .into_reader()
        .read_to_end(&mut buf)
        .map_err(|e| e.to_string())?;
    Ok(HttpResult {
        url: final_url,
        status,
        body: buf,
    })
}

/// A fully-buffered HTTP response handed to ruffle's loader.
struct SkuaResponse {
    url: String,
    status: u16,
    body: Option<Vec<u8>>,
}

impl SuccessResponse for SkuaResponse {
    fn url(&self) -> Cow<'_, str> {
        Cow::Borrowed(&self.url)
    }

    fn set_url(&mut self, url: String) {
        self.url = url;
    }

    fn body(self: Box<Self>) -> OwnedFuture<Vec<u8>, LoaderError> {
        let body = self.body.unwrap_or_default();
        Box::pin(async move { Ok(body) })
    }

    fn text_encoding(&self) -> Option<&'static Encoding> {
        None
    }

    fn status(&self) -> u16 {
        self.status
    }

    fn redirected(&self) -> bool {
        false
    }

    fn next_chunk(&mut self) -> OwnedFuture<Option<Vec<u8>>, LoaderError> {
        // Whole body is already buffered: yield it once, then None.
        let chunk = self.body.take();
        Box::pin(async move { Ok(chunk) })
    }

    fn expected_length(&self) -> Result<Option<u64>, LoaderError> {
        Ok(self.body.as_ref().map(|b| b.len() as u64))
    }
}
