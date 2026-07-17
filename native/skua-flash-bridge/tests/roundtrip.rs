//! Integration tests for the Flash bridge.
//!
//! These prove the two XML pipes and the C ABI end to end against the offline
//! mock — including Layer 3b's definition-of-done: reading `world.strMapName`
//! from "AS3" back into the host.

use std::collections::BTreeMap;
use std::ffi::{c_char, c_void, CStr, CString};
use std::sync::atomic::{AtomicBool, Ordering};

use skua_flash::value::FlashValue;
use skua_flash::xml::{parse_invoke, parse_value, serialize_invoke, serialize_response};
use skua_flash::MockRuntime;
use skua_flash::runtime::{to_json, FlashRuntime};

// --- codec: value round-trips -------------------------------------------------

#[test]
fn value_roundtrips_cover_every_variant() {
    let mut obj = BTreeMap::new();
    obj.insert("hp".to_string(), FlashValue::Number(1000.0));
    obj.insert("name".to_string(), FlashValue::string("Skua & <you>"));
    obj.insert("alive".to_string(), FlashValue::Bool(true));

    let cases = vec![
        FlashValue::Null,
        FlashValue::Undefined,
        FlashValue::Bool(true),
        FlashValue::Bool(false),
        FlashValue::Number(0.0),
        FlashValue::Number(42.0),
        FlashValue::Number(-17.5),
        FlashValue::string(""),
        FlashValue::string("battleon"),
        FlashValue::string("needs \"escaping\" & <b>bold</b> 'q'"),
        FlashValue::Array(vec![
            FlashValue::Number(1.0),
            FlashValue::string("two"),
            FlashValue::Bool(false),
            FlashValue::Null,
        ]),
        FlashValue::Object(obj),
    ];

    for case in cases {
        let xml = serialize_response(&case);
        let back = parse_value(&xml).expect("must parse");
        assert_eq!(case, back, "round-trip failed for {xml}");
    }
}

#[test]
fn integers_serialize_without_decimal_point() {
    assert_eq!(serialize_response(&FlashValue::Number(5.0)), "<number>5</number>");
    assert_eq!(serialize_response(&FlashValue::Number(5.5)), "<number>5.5</number>");
}

#[test]
fn invoke_serialization_matches_windows_flashutil() {
    // Must byte-match Skua.WPF FlashUtil.Call's request builder so the same
    // skua.swf answers identically on both platforms.
    let xml = serialize_invoke(
        "setGameObject",
        &[FlashValue::string("world.strMapName"), FlashValue::string("battleon")],
    );
    assert_eq!(
        xml,
        "<invoke name=\"setGameObject\" returntype=\"xml\">\
         <arguments><string>world.strMapName</string><string>battleon</string></arguments></invoke>"
    );
}

#[test]
fn parse_invoke_extracts_name_and_args() {
    let (name, args) = parse_invoke(
        "<invoke name=\"getGameObject\" returntype=\"xml\">\
         <arguments><string>world.strMapName</string></arguments></invoke>",
    )
    .unwrap();
    assert_eq!(name, "getGameObject");
    assert_eq!(args, vec![FlashValue::string("world.strMapName")]);
}

#[test]
fn parser_tolerates_whitespace_and_self_closing_containers() {
    assert_eq!(parse_value("  <null/>  ").unwrap(), FlashValue::Null);
    assert_eq!(parse_value("<array/>").unwrap(), FlashValue::Array(vec![]));
    assert_eq!(parse_value("<true></true>").unwrap(), FlashValue::Bool(true));
}

// --- the definition-of-done: read world.strMapName via the mock ---------------

#[test]
fn round_trips_world_strmapname_through_mock_runtime() {
    let mut rt = MockRuntime::new();
    rt.load_swf(b"fake-skua-swf-bytes").unwrap();

    // Exactly what Skua.Core's IFlashUtil.GetGameObject("world.strMapName") sends.
    let request = serialize_invoke("getGameObject", &[FlashValue::string("world.strMapName")]);
    let response = rt.call(&request);

    // Mock mirrors skua.swf: value JSON-encoded inside a <string>.
    let value = parse_value(&response).unwrap();
    let json = value.as_str().expect("response is a <string>");
    assert_eq!(json, "\"battleon\"");

    // And the C# side would JSON-deserialize that to the bare string.
    assert_eq!(json.trim_matches('"'), "battleon");
}

#[test]
fn nested_game_object_path_resolves() {
    let mut rt = MockRuntime::new();
    let request = serialize_invoke(
        "getGameObject",
        &[FlashValue::string("world.myAvatar.objData.intHP")],
    );
    let response = rt.call(&request);
    let json = parse_value(&response).unwrap();
    assert_eq!(json.as_str().unwrap(), "1000");
}

#[test]
fn missing_path_returns_json_null() {
    let mut rt = MockRuntime::new();
    let request = serialize_invoke("getGameObject", &[FlashValue::string("world.doesNotExist")]);
    let response = rt.call(&request);
    assert_eq!(parse_value(&response).unwrap().as_str().unwrap(), "null");
}

#[test]
fn to_json_encodes_object_graph() {
    let mut obj = BTreeMap::new();
    obj.insert("a".to_string(), FlashValue::Number(1.0));
    obj.insert("b".to_string(), FlashValue::string("x\"y"));
    let json = to_json(&FlashValue::Object(obj));
    assert_eq!(json, "{\"a\":1,\"b\":\"x\\\"y\"}");
}

// --- the C ABI, exercised exactly as C# will ---------------------------------

use skua_flash::ffi::{
    skua_flash_abi_version, skua_flash_call, skua_flash_create, skua_flash_destroy,
    skua_flash_load_swf, skua_flash_set_callback, skua_flash_string_free, ABI_VERSION,
};

#[test]
fn ffi_abi_version_is_stable() {
    assert_eq!(skua_flash_abi_version(), ABI_VERSION);
}

#[test]
fn ffi_call_path_round_trips() {
    unsafe {
        let h = skua_flash_create();
        assert!(!h.is_null());

        let swf = b"fake-swf";
        assert_eq!(skua_flash_load_swf(h, swf.as_ptr(), swf.len()), 0);

        let req = CString::new(serialize_invoke(
            "getGameObject",
            &[FlashValue::string("world.strAreaName")],
        ))
        .unwrap();
        let resp_ptr = skua_flash_call(h, req.as_ptr());
        assert!(!resp_ptr.is_null());

        let resp = CStr::from_ptr(resp_ptr).to_str().unwrap().to_owned();
        skua_flash_string_free(resp_ptr);

        let value = parse_value(&resp).unwrap();
        assert_eq!(value.as_str().unwrap(), "\"Battleon\"");

        skua_flash_destroy(h);
    }
}

static CALLBACK_FIRED: AtomicBool = AtomicBool::new(false);

extern "C" fn on_flash_call(_user: *mut c_void, invoke_xml: *const c_char) {
    let xml = unsafe { CStr::from_ptr(invoke_xml).to_str().unwrap() };
    // The AS3 -> host direction delivers an <invoke> we can parse just like a request.
    let (name, _args) = parse_invoke(xml).unwrap();
    if name == "testEvent" {
        CALLBACK_FIRED.store(true, Ordering::SeqCst);
    }
}

#[test]
fn ffi_callback_path_delivers_as3_to_host_events() {
    unsafe {
        let h = skua_flash_create();
        skua_flash_set_callback(h, on_flash_call, std::ptr::null_mut());

        // Trigger the mock's test hook, which re-raises an AS3 -> host event.
        let req = CString::new(serialize_invoke(
            "__skua_emit_test_event__",
            &[FlashValue::string("hello")],
        ))
        .unwrap();
        let resp = skua_flash_call(h, req.as_ptr());
        skua_flash_string_free(resp);

        assert!(CALLBACK_FIRED.load(Ordering::SeqCst), "callback did not fire");
        skua_flash_destroy(h);
    }
}

#[test]
fn ffi_handles_null_and_garbage_safely() {
    unsafe {
        // Null handle must not crash.
        assert!(skua_flash_call(std::ptr::null_mut(), std::ptr::null()).is_null());
        skua_flash_destroy(std::ptr::null_mut());
        skua_flash_string_free(std::ptr::null_mut());

        // Garbage invoke XML yields <null/>, never a panic across the boundary.
        let h = skua_flash_create();
        let junk = CString::new("not <valid xml").unwrap();
        let resp_ptr = skua_flash_call(h, junk.as_ptr());
        let resp = CStr::from_ptr(resp_ptr).to_str().unwrap().to_owned();
        skua_flash_string_free(resp_ptr);
        assert_eq!(resp, "<null/>");
        skua_flash_destroy(h);
    }
}

#[test]
fn parse_invoke_accepts_zero_arg_invokes_without_arguments_block() {
    // The EXACT wire format the C# host emits for zero-argument calls
    // (`isTrue`, `loadClient`): a non-self-closing <invoke> with NO <arguments>
    // child. The Windows ActiveX host accepts this, so it is canon. Rejecting
    // it made every zero-arg host->AS3 call return null without reaching the
    // player — killing the loadClient boot handshake in production.
    let (name, args) = parse_invoke(r#"<invoke name="isTrue" returntype="xml"></invoke>"#).unwrap();
    assert_eq!(name, "isTrue");
    assert!(args.is_empty());

    let (name, args) = parse_invoke(r#"<invoke name="loadClient" returntype="xml"></invoke>"#).unwrap();
    assert_eq!(name, "loadClient");
    assert!(args.is_empty());

    // Self-closing and empty-<arguments> forms keep working.
    let (name, args) = parse_invoke(r#"<invoke name="a" returntype="xml"/>"#).unwrap();
    assert_eq!(name, "a");
    assert!(args.is_empty());
    let (name, args) =
        parse_invoke(r#"<invoke name="b" returntype="xml"><arguments></arguments></invoke>"#).unwrap();
    assert_eq!(name, "b");
    assert!(args.is_empty());
}
