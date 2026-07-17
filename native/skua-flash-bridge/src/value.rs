//! The value model that crosses the Flash ExternalInterface boundary.
//!
//! Flash's ExternalInterface serialises a small, closed set of ActionScript
//! value types to XML. Skua.Core already speaks exactly this dialect on the
//! Windows side (via `AxShockwaveFlash.CallFunction`), so matching it faithfully
//! here means the C# core needs **no changes** to talk to the Linux bridge.

use std::collections::BTreeMap;

/// A value as understood by Flash ExternalInterface.
///
/// `Object` uses a `BTreeMap` so property ordering is deterministic — important
/// only for stable test assertions; Flash itself does not promise key order.
#[derive(Debug, Clone, PartialEq)]
pub enum FlashValue {
    /// AS3 `null`, serialised `<null/>`.
    Null,
    /// AS3 `undefined`, serialised `<undefined/>`.
    Undefined,
    /// AS3 `Boolean`, serialised `<true/>` / `<false/>`.
    Bool(bool),
    /// AS3 `Number`/`int`/`uint`, serialised `<number>...</number>`.
    Number(f64),
    /// AS3 `String`, serialised `<string>...</string>` (contents XML-escaped).
    String(String),
    /// AS3 `Array`, serialised `<array><property id="0">..</property>..</array>`.
    Array(Vec<FlashValue>),
    /// AS3 `Object`, serialised `<object><property id="key">..</property>..</object>`.
    Object(BTreeMap<String, FlashValue>),
}

impl FlashValue {
    /// Convenience: build a string value from anything string-like.
    pub fn string(s: impl Into<String>) -> Self {
        FlashValue::String(s.into())
    }

    /// Borrow the inner string, if this is a `String`.
    pub fn as_str(&self) -> Option<&str> {
        match self {
            FlashValue::String(s) => Some(s),
            _ => None,
        }
    }

    /// Borrow the inner number, if this is a `Number`.
    pub fn as_number(&self) -> Option<f64> {
        match self {
            FlashValue::Number(n) => Some(*n),
            _ => None,
        }
    }

    /// Borrow the inner bool, if this is a `Bool`.
    pub fn as_bool(&self) -> Option<bool> {
        match self {
            FlashValue::Bool(b) => Some(*b),
            _ => None,
        }
    }
}

impl From<&str> for FlashValue {
    fn from(s: &str) -> Self {
        FlashValue::String(s.to_owned())
    }
}

impl From<String> for FlashValue {
    fn from(s: String) -> Self {
        FlashValue::String(s)
    }
}

impl From<bool> for FlashValue {
    fn from(b: bool) -> Self {
        FlashValue::Bool(b)
    }
}

impl From<f64> for FlashValue {
    fn from(n: f64) -> Self {
        FlashValue::Number(n)
    }
}

impl From<i64> for FlashValue {
    fn from(n: i64) -> Self {
        FlashValue::Number(n as f64)
    }
}
