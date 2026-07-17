//! C ABI for the offscreen game view (feature `ruffle-render`).
//!
//! Handle-based, separate from the ExternalInterface bridge handle:
//!   void*   skua_flash_render_create(const char* url, uint32_t w, uint32_t h);
//!   int32_t skua_flash_render_capture(void* h, uint8_t* buf, size_t cap,
//!                                     uint32_t* out_w, uint32_t* out_h);
//!   void    skua_flash_render_mouse(void* h, uint32_t kind, double x, double y);
//!   void    skua_flash_render_text(void* h, uint32_t codepoint);
//!   void    skua_flash_render_destroy(void* h);
//!
//! Every entry point is `catch_unwind`-guarded — a panic must never cross FFI.

use crate::ffi::SkuaFlashCallback;
use crate::render::RenderHost;
use std::ffi::{c_char, c_void, CStr, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};

/// `skua_flash_render_create(url, width, height)` — fetch the root movie and
/// start a render worker. Returns an opaque handle, or null on failure.
///
/// # Safety
/// `url` must be a valid NUL-terminated UTF-8 C string.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_create(
    url: *const c_char,
    width: u32,
    height: u32,
) -> *mut c_void {
    if url.is_null() || width == 0 || height == 0 {
        return std::ptr::null_mut();
    }
    let url = match CStr::from_ptr(url).to_str() {
        Ok(s) => s.to_owned(),
        Err(_) => return std::ptr::null_mut(),
    };
    match catch_unwind(|| RenderHost::create(&url, width, height)) {
        Ok(Ok(host)) => Box::into_raw(Box::new(host)) as *mut c_void,
        Ok(Err(e)) => {
            eprintln!("[skua-render] create failed: {e}");
            std::ptr::null_mut()
        }
        Err(_) => std::ptr::null_mut(),
    }
}

/// `skua_flash_render_create_bytes(bytes, len, url, width, height)` — start a
/// render worker whose ROOT movie is the given SWF bytes, with `url` as its
/// nominal (https) origin. Used to boot `skua.swf` as the root movie so it can
/// load the game into itself (the Skua architecture). Returns an opaque handle,
/// or null on failure.
///
/// # Safety
/// `bytes` must point to `len` readable bytes; `url` must be a valid
/// NUL-terminated UTF-8 C string.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_create_bytes(
    bytes: *const u8,
    len: usize,
    url: *const c_char,
    width: u32,
    height: u32,
) -> *mut c_void {
    if bytes.is_null() || len == 0 || url.is_null() || width == 0 || height == 0 {
        return std::ptr::null_mut();
    }
    let swf = std::slice::from_raw_parts(bytes, len).to_vec();
    let url = match CStr::from_ptr(url).to_str() {
        Ok(s) => s.to_owned(),
        Err(_) => return std::ptr::null_mut(),
    };
    match catch_unwind(|| RenderHost::create_from_bytes(swf, &url, width, height)) {
        Ok(Ok(host)) => Box::into_raw(Box::new(host)) as *mut c_void,
        Ok(Err(e)) => {
            eprintln!("[skua-render] create_bytes failed: {e}");
            std::ptr::null_mut()
        }
        Err(_) => std::ptr::null_mut(),
    }
}

/// `skua_flash_render_capture(handle, buf, cap, out_w, out_h)` — advance one
/// frame and copy its RGBA8 pixels into `buf`. Returns the number of bytes
/// written (`w*h*4`), 0 if there is no frame yet, or negative on error /
/// insufficient capacity. `out_w`/`out_h` receive the frame dimensions.
///
/// # Safety
/// `handle` must be live; `buf` must point to `cap` writable bytes;
/// `out_w`/`out_h` must be valid writable pointers (or null).
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_capture(
    handle: *mut c_void,
    buf: *mut u8,
    cap: usize,
    out_w: *mut u32,
    out_h: *mut u32,
) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let host = &*(handle as *mut RenderHost);
    let result = catch_unwind(AssertUnwindSafe(|| host.capture()));
    let (w, h, pixels) = match result {
        Ok(Some(frame)) => frame,
        Ok(None) => return 0,
        Err(_) => return -2,
    };
    if !out_w.is_null() {
        *out_w = w;
    }
    if !out_h.is_null() {
        *out_h = h;
    }
    if buf.is_null() || cap < pixels.len() {
        return -3;
    }
    std::ptr::copy_nonoverlapping(pixels.as_ptr(), buf, pixels.len());
    pixels.len() as i32
}

/// `skua_flash_render_mouse(handle, kind, x, y)` — 0 = move, 1 = down, 2 = up.
///
/// # Safety
/// `handle` must be a live render handle.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_mouse(
    handle: *mut c_void,
    kind: u32,
    x: f64,
    y: f64,
) {
    if handle.is_null() {
        return;
    }
    let host = &*(handle as *mut RenderHost);
    let _ = catch_unwind(AssertUnwindSafe(|| host.mouse(kind as u8, x, y)));
}

/// `skua_flash_render_text(handle, codepoint)` — feed a typed character.
///
/// # Safety
/// `handle` must be a live render handle.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_text(handle: *mut c_void, codepoint: u32) {
    if handle.is_null() {
        return;
    }
    let host = &*(handle as *mut RenderHost);
    if let Some(c) = char::from_u32(codepoint) {
        let _ = catch_unwind(AssertUnwindSafe(|| host.text(c)));
    }
}

/// `skua_flash_render_call(handle, invoke_xml)` — host -> AS3 on the rendered
/// player (the bot's GetGameObject/CallGameFunction path). Returns a newly
/// allocated UTF-8 string the caller frees with `skua_flash_string_free`.
///
/// # Safety
/// `handle` must be live; `invoke_xml` a valid NUL-terminated C string.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_call(
    handle: *mut c_void,
    invoke_xml: *const c_char,
) -> *mut c_char {
    if handle.is_null() || invoke_xml.is_null() {
        return std::ptr::null_mut();
    }
    let host = &*(handle as *mut RenderHost);
    let input = match CStr::from_ptr(invoke_xml).to_str() {
        Ok(s) => s.to_owned(),
        Err(_) => return std::ptr::null_mut(),
    };
    let response = match catch_unwind(AssertUnwindSafe(|| host.call(&input))) {
        Ok(s) => s,
        Err(_) => "<null/>".to_owned(),
    };
    match CString::new(response) {
        Ok(c) => c.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}

/// `skua_flash_render_set_callback(handle, cb, user)` — register the AS3 -> host
/// sink (the bot's FlashCall event) on the rendered player.
///
/// # Safety
/// `handle` must be live; `user` is passed back verbatim to `cb`.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_set_callback(
    handle: *mut c_void,
    cb: SkuaFlashCallback,
    user: *mut c_void,
) {
    if handle.is_null() {
        return;
    }
    let host = &*(handle as *mut RenderHost);
    let user_addr = user as usize;
    let handler = move |xml: &str| {
        if let Ok(cstr) = CString::new(xml) {
            cb(user_addr as *mut c_void, cstr.as_ptr());
        }
    };
    let _ = catch_unwind(AssertUnwindSafe(|| host.set_handler(Box::new(handler))));
}

/// `skua_flash_render_load_swf(handle, bytes, len)` — inject skua.swf into the
/// live game player (same-domain), so the bot can drive the visible game.
/// Returns 0 on success, negative on failure.
///
/// # Safety
/// `handle` must be live; `bytes` must point to `len` readable bytes.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_load_swf(
    handle: *mut c_void,
    bytes: *const u8,
    len: usize,
) -> i32 {
    if handle.is_null() || bytes.is_null() || len == 0 {
        return -1;
    }
    let host = &*(handle as *mut RenderHost);
    let slice = std::slice::from_raw_parts(bytes, len).to_vec();
    match catch_unwind(AssertUnwindSafe(|| host.load_swf(slice))) {
        Ok(Ok(())) => 0,
        Ok(Err(e)) => {
            eprintln!("[skua-render] load_swf failed: {e}");
            -2
        }
        Err(_) => -3,
    }
}

/// `skua_flash_render_destroy(handle)` — stop the worker and free the host.
/// Safe to call with null.
///
/// # Safety
/// `handle` must be a pointer from `skua_flash_render_create`, not already freed.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_render_destroy(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        drop(Box::from_raw(handle as *mut RenderHost));
    }));
}
