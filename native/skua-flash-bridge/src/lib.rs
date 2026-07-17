//! # skua-flash-bridge
//!
//! Layer 3b of the VibeSkua native-Linux port: the bridge that lets
//! `Skua.Core` (C#) drive a Flash runtime on Linux with no Wine and no VM.
//!
//! On Windows, `Skua.Core` reaches Flash through `IFlashUtil`, whose sole
//! implementation talks to the `AxShockwaveFlash` ActiveX control. CLAUDE.md
//! establishes that the entire contract collapses to **two XML pipes**:
//!
//! * host -> AS3 : `CallFunction("<invoke .../>")` returning a value XML string
//! * AS3 -> host : the `FlashCall` event, also XML
//!
//! plus loading `skua.swf` into the player. This crate implements exactly that
//! seam as a C-ABI shared library (`libskua_flash.so`) that Skua's Linux
//! `IFlashUtil` `[DllImport]`s into its own process — same single-process shape
//! as Windows, no IPC.
//!
//! ## What is complete and tested here
//! * [`value`] — the ExternalInterface value model.
//! * [`xml`]   — a faithful, dependency-free ExternalInterface XML codec.
//! * [`ffi`]   — the C ABI (`skua_flash_*`).
//! * [`runtime::MockRuntime`] — an offline AQW `world` stand-in so the whole
//!   pipe, including the `world.strMapName` round-trip that is Layer 3b's
//!   definition-of-done, is exercised without the game or the emulator.
//!
//! * `runtime::RuffleRuntime` (feature `ruffle`) — embeds a real
//!   `ruffle_core::Player` and backs the two pipes with a real
//!   `ExternalInterfaceProvider`. `tests/ruffle_roundtrip.rs` round-trips both
//!   directions against a real AVM2 SWF. Only loading the remote AQW game +
//!   injecting `skua.swf` into its `ApplicationDomain` awaits game.aq.com
//!   egress. See README.md, "The real Ruffle runtime".

pub mod ffi;
pub mod runtime;
pub mod value;
pub mod xml;

// The real ruffle_core-backed runtime. Compiled only under `--features ruffle`
// (github-only dependency); excluded from the default build and CI. See the
// module docs and README.md.
#[cfg(feature = "ruffle")]
pub mod ruffle_runtime;

// Offscreen wgpu rendering for the visible game view. Compiled only under
// `--features ruffle-render` (pulls in the wgpu backend). See src/render.rs.
#[cfg(feature = "ruffle-render")]
pub mod render;

// Minimal HTTP + socket navigator so the game view can load the live game.
#[cfg(feature = "ruffle-render")]
pub mod navigator;

// C ABI for the game view (skua_flash_render_*).
#[cfg(feature = "ruffle-render")]
pub mod render_ffi;

pub use ffi::ABI_VERSION;
pub use runtime::{FlashRuntime, MockRuntime};
pub use value::FlashValue;
