//! C ABI for `libskua_flash.so` — the surface Skua's Linux `IFlashUtil`
//! `[DllImport]`s. Handle-based, UTF-8 in/out, caller-frees for returned
//! strings. No Rust type crosses the boundary.
//!
//! ABI (see include/skua_flash.h):
//!   uint32_t skua_flash_abi_version(void);
//!   void*    skua_flash_create(void);
//!   void     skua_flash_set_callback(void* h, SkuaFlashCallback cb, void* user);
//!   int32_t  skua_flash_load_swf(void* h, const uint8_t* bytes, size_t len);
//!   char*    skua_flash_call(void* h, const char* invoke_xml);   // free with skua_flash_string_free
//!   void     skua_flash_string_free(char* s);
//!   void     skua_flash_destroy(void* h);
//!
//! Every entry point is `catch_unwind`-guarded: a Rust panic must never unwind
//! across the FFI boundary (that is undefined behaviour), so panics are trapped
//! and turned into a safe return value.

use crate::runtime::{FlashRuntime, MockRuntime};
use std::ffi::{c_char, c_void, CStr, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};

/// Bump on any breaking ABI change. The C# side checks this on load.
pub const ABI_VERSION: u32 = 1;

/// AS3 -> host callback: `(user_data, invoke_xml_utf8)`. The `invoke_xml`
/// pointer is valid only for the duration of the call; copy out synchronously.
pub type SkuaFlashCallback = extern "C" fn(*mut c_void, *const c_char);

/// The owned bridge object behind the opaque handle C# holds.
pub struct Bridge {
    runtime: Box<dyn FlashRuntime>,
}

impl Bridge {
    #[cfg(not(feature = "ruffle"))]
    fn new() -> Self {
        // Default build: the offline mock (no ruffle_core dependency).
        Bridge {
            runtime: Box::new(MockRuntime::new()),
        }
    }

    #[cfg(feature = "ruffle")]
    fn new() -> Self {
        // `--features ruffle`: the real ruffle_core-backed runtime. If it fails
        // to initialise (e.g. the game movie cannot be fetched), fall back to
        // the mock so skua_flash_create() never returns null unexpectedly; the
        // error surfaces on the first call instead.
        match crate::ruffle_runtime::RuffleRuntime::new() {
            Ok(rt) => Bridge { runtime: Box::new(rt) },
            Err(_) => Bridge { runtime: Box::new(MockRuntime::new()) },
        }
    }
}

/// `skua_flash_abi_version()` — returns the ABI version this `.so` implements.
#[no_mangle]
pub extern "C" fn skua_flash_abi_version() -> u32 {
    ABI_VERSION
}

/// `skua_flash_create()` — allocate a bridge; returns an opaque handle or null
/// on failure.
#[no_mangle]
pub extern "C" fn skua_flash_create() -> *mut c_void {
    match catch_unwind(|| Box::into_raw(Box::new(Bridge::new())) as *mut c_void) {
        Ok(ptr) => ptr,
        Err(_) => std::ptr::null_mut(),
    }
}

/// `skua_flash_destroy(handle)` — free a bridge created by `skua_flash_create`.
/// Safe to call with null.
///
/// # Safety
/// `handle` must be a pointer previously returned by `skua_flash_create` and not
/// already destroyed.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_destroy(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        drop(Box::from_raw(handle as *mut Bridge));
    }));
}

/// `skua_flash_set_callback(handle, cb, user)` — register the AS3 -> host sink.
///
/// # Safety
/// `handle` must be a live bridge handle. `user` is passed back verbatim to `cb`
/// and must remain valid for as long as callbacks may fire.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_set_callback(
    handle: *mut c_void,
    cb: SkuaFlashCallback,
    user: *mut c_void,
) {
    if handle.is_null() {
        return;
    }
    let bridge = &mut *(handle as *mut Bridge);
    // Raw C pointer isn't Send; move it across as an integer and rebuild it in
    // the closure. Function pointers are Send/Copy already.
    let user_addr = user as usize;
    let handler = move |xml: &str| {
        if let Ok(cstr) = CString::new(xml) {
            cb(user_addr as *mut c_void, cstr.as_ptr());
        }
        // xml containing an interior NUL is dropped rather than truncated.
    };
    let _ = catch_unwind(AssertUnwindSafe(|| {
        bridge.runtime.set_flash_call_handler(Box::new(handler));
    }));
}

/// `skua_flash_load_swf(handle, bytes, len)` — load `skua.swf`. Returns 0 on
/// success, negative on error.
///
/// # Safety
/// `handle` must be live; `bytes` must point to `len` readable bytes (or be null
/// with `len == 0`).
#[no_mangle]
pub unsafe extern "C" fn skua_flash_load_swf(
    handle: *mut c_void,
    bytes: *const u8,
    len: usize,
) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let bridge = &mut *(handle as *mut Bridge);
    let slice: &[u8] = if bytes.is_null() || len == 0 {
        &[]
    } else {
        std::slice::from_raw_parts(bytes, len)
    };
    match catch_unwind(AssertUnwindSafe(|| bridge.runtime.load_swf(slice))) {
        Ok(Ok(())) => 0,
        Ok(Err(_)) => -2,
        Err(_) => -3,
    }
}

/// `skua_flash_call(handle, invoke_xml)` — host -> AS3. Returns a newly
/// allocated, NUL-terminated UTF-8 string that the caller MUST free with
/// `skua_flash_string_free`. Returns null only if `handle`/`invoke_xml` is null
/// or on an internal panic.
///
/// # Safety
/// `handle` must be live; `invoke_xml` must be a valid NUL-terminated C string.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_call(
    handle: *mut c_void,
    invoke_xml: *const c_char,
) -> *mut c_char {
    if handle.is_null() || invoke_xml.is_null() {
        return std::ptr::null_mut();
    }
    let bridge = &mut *(handle as *mut Bridge);
    let input = match CStr::from_ptr(invoke_xml).to_str() {
        Ok(s) => s.to_owned(),
        Err(_) => return std::ptr::null_mut(),
    };
    let result = catch_unwind(AssertUnwindSafe(|| bridge.runtime.call(&input)));
    let response = match result {
        Ok(s) => s,
        Err(_) => "<null/>".to_owned(),
    };
    match CString::new(response) {
        Ok(c) => c.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}

/// `skua_flash_string_free(s)` — free a string returned by `skua_flash_call`.
/// Safe to call with null.
///
/// # Safety
/// `s` must be a pointer previously returned by `skua_flash_call` (or null) and
/// not already freed.
#[no_mangle]
pub unsafe extern "C" fn skua_flash_string_free(s: *mut c_char) {
    if s.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        drop(CString::from_raw(s));
    }));
}
