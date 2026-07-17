//! The runtime seam: what a Flash player must provide, and an offline mock.
//!
//! The bridge is written against the [`FlashRuntime`] trait, with two
//! implementations: [`MockRuntime`] (offline AQW `world` stand-in, always built)
//! and `RuffleRuntime` (embeds a real `ruffle_core::Player`, behind the optional
//! `ruffle` feature since `ruffle_core` is a github-only git dependency that
//! needs a nightly toolchain). Both directions of the ExternalInterface seam are
//! proven against the real engine by `tests/ruffle_roundtrip.rs`.
//!
//! See README.md, "The real Ruffle runtime".

use crate::value::FlashValue;
use crate::xml::{parse_invoke, serialize_response};
use std::collections::BTreeMap;

/// A Flash player the bridge can drive. Exactly the "two XML pipes" from
/// CLAUDE.md, plus SWF loading.
pub trait FlashRuntime: Send {
    /// Load `skua.swf` bytes into the player, into the same `ApplicationDomain`
    /// as the already-running game so `ApplicationDomain.getDefinition("world")`
    /// resolves. (For the real runtime this is the Ruffle-specific step.)
    fn load_swf(&mut self, bytes: &[u8]) -> std::result::Result<(), String>;

    /// Host -> AS3. Takes an `<invoke>` XML string, returns the value XML the
    /// SWF produced. Never panics; on error it returns `<null/>`.
    fn call(&mut self, invoke_xml: &str) -> String;

    /// Register the AS3 -> host sink. The runtime calls this handler with an
    /// `<invoke>` XML string whenever the SWF raises a `FlashCall`-style event.
    fn set_flash_call_handler(&mut self, handler: Box<dyn FnMut(&str) + Send>);
}

/// An in-memory stand-in for AQW's Flash object graph.
///
/// Faithful to Skua's contract at the transport level: it answers the same
/// `getGameObject` / `setGameObject` invokes the real `skua.swf` answers, and
/// returns values JSON-encoded inside a `<string>` element exactly as `skua.swf`
/// does (via `JSON.stringify`). That is enough to round-trip real Skua.Core
/// helpers such as `GetGameObject<string>("world.strMapName")` end to end.
pub struct MockRuntime {
    world: FlashValue,
    loaded: bool,
    handler: Option<Box<dyn FnMut(&str) + Send>>,
}

impl Default for MockRuntime {
    fn default() -> Self {
        Self::new()
    }
}

impl MockRuntime {
    /// Build a mock seeded with a small but representative slice of AQW's
    /// `world` object.
    pub fn new() -> Self {
        MockRuntime {
            world: default_world(),
            loaded: false,
            handler: None,
        }
    }

    /// Test/demo helper: simulate the SWF raising an AS3 -> host event, so the
    /// callback direction of the bridge can be exercised without Ruffle.
    pub fn emit_flash_call(&mut self, function: &str, args: &[FlashValue]) {
        if let Some(h) = self.handler.as_mut() {
            let xml = crate::xml::serialize_invoke(function, args);
            h(&xml);
        }
    }

    fn navigate(&self, path: &str) -> Option<&FlashValue> {
        let mut current = &self.world;
        // "world" is the root; skip a leading "world" segment if present.
        for (i, seg) in path.split('.').enumerate() {
            if i == 0 && seg == "world" {
                continue;
            }
            current = match current {
                FlashValue::Object(map) => map.get(seg)?,
                FlashValue::Array(items) => {
                    let idx: usize = seg.parse().ok()?;
                    items.get(idx)?
                }
                _ => return None,
            };
        }
        Some(current)
    }
}

impl FlashRuntime for MockRuntime {
    fn load_swf(&mut self, bytes: &[u8]) -> std::result::Result<(), String> {
        if bytes.is_empty() {
            return Err("empty SWF payload".to_owned());
        }
        self.loaded = true;
        Ok(())
    }

    fn call(&mut self, invoke_xml: &str) -> String {
        let (name, args) = match parse_invoke(invoke_xml) {
            Ok(v) => v,
            Err(_) => return "<null/>".to_owned(),
        };

        match name.as_str() {
            // Test-only hook: re-raise the call as an AS3 -> host FlashCall so
            // the callback direction can be exercised through the C ABI (where
            // emit_flash_call is not directly reachable). Never emitted by real
            // Skua, so it is inert in production.
            "__skua_emit_test_event__" => {
                let fwd: Vec<FlashValue> = args.clone();
                self.emit_flash_call("testEvent", &fwd);
                "<true/>".to_owned()
            }
            // Skua's primary read path. skua.swf's getGameObject is typed
            // `:String` and returns `JSON.stringify(obj)`, so Flash serialises
            // the result as a <string> element. Mirror that exactly.
            "getGameObject" | "getGameObjectS" | "getGameObjectKey" => {
                let path = args.first().and_then(|a| a.as_str()).unwrap_or("");
                let found = self.navigate(path).cloned().unwrap_or(FlashValue::Null);
                serialize_response(&FlashValue::String(to_json(&found)))
            }
            // setGameObject is `:void` in skua.swf; ExternalInterface reports an
            // undefined return, which Skua.Core discards.
            "setGameObject" | "setGameObjectKey" => "<undefined/>".to_owned(),
            // Arbitrary game function invocation — returns JSON null here.
            "callGameFunction" | "callGameFunction0" => {
                serialize_response(&FlashValue::String("null".to_owned()))
            }
            // isNull is `:String` returning "true"/"false" (note: NOT <true/>),
            // so Skua's Call<bool> can Convert.ChangeType the inner text.
            "isNull" => {
                let path = args.first().and_then(|a| a.as_str()).unwrap_or("");
                let is_null = matches!(self.navigate(path), None | Some(FlashValue::Null));
                serialize_response(&FlashValue::String(is_null.to_string()))
            }
            "isTrue" => serialize_response(&FlashValue::String("true".to_owned())),
            // An unregistered ExternalInterface callback resolves to null.
            _ => "<null/>".to_owned(),
        }
    }

    fn set_flash_call_handler(&mut self, handler: Box<dyn FnMut(&str) + Send>) {
        self.handler = Some(handler);
    }
}

fn default_world() -> FlashValue {
    let mut obj_data = BTreeMap::new();
    obj_data.insert("strUsername".to_string(), FlashValue::string("SkuaTester"));
    obj_data.insert("intHP".to_string(), FlashValue::Number(1000.0));
    obj_data.insert("intHPMax".to_string(), FlashValue::Number(1000.0));
    obj_data.insert("intLevel".to_string(), FlashValue::Number(100.0));

    let mut my_avatar = BTreeMap::new();
    my_avatar.insert("objData".to_string(), FlashValue::Object(obj_data));

    let mut world = BTreeMap::new();
    world.insert("strMapName".to_string(), FlashValue::string("battleon"));
    world.insert("strAreaName".to_string(), FlashValue::string("Battleon"));
    world.insert("curRoom".to_string(), FlashValue::Number(1.0));
    world.insert("isMonsterAvailable".to_string(), FlashValue::Bool(true));
    world.insert("myAvatar".to_string(), FlashValue::Object(my_avatar));

    FlashValue::Object(world)
}

/// Minimal JSON encoder for a [`FlashValue`], matching how `skua.swf` returns
/// game objects (`JSON.stringify`). No external crate needed.
pub fn to_json(value: &FlashValue) -> String {
    let mut out = String::new();
    json_into(value, &mut out);
    out
}

fn json_into(value: &FlashValue, out: &mut String) {
    match value {
        FlashValue::Null | FlashValue::Undefined => out.push_str("null"),
        FlashValue::Bool(true) => out.push_str("true"),
        FlashValue::Bool(false) => out.push_str("false"),
        FlashValue::Number(n) => {
            if n.is_finite() {
                if n.fract() == 0.0 && n.abs() < 1e15 {
                    out.push_str(&format!("{}", *n as i64));
                } else {
                    out.push_str(&format!("{}", n));
                }
            } else {
                // JSON has no NaN/Infinity; JS JSON.stringify emits null.
                out.push_str("null");
            }
        }
        FlashValue::String(s) => json_string_into(s, out),
        FlashValue::Array(items) => {
            out.push('[');
            for (i, item) in items.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                json_into(item, out);
            }
            out.push(']');
        }
        FlashValue::Object(map) => {
            out.push('{');
            for (i, (k, v)) in map.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                json_string_into(k, out);
                out.push(':');
                json_into(v, out);
            }
            out.push('}');
        }
    }
}

fn json_string_into(s: &str, out: &mut String) {
    out.push('"');
    for ch in s.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out.push('"');
}
