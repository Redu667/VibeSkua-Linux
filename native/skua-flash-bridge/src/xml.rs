//! Hand-rolled codec for the Flash ExternalInterface XML dialect.
//!
//! This is the wire format on Skua's two pipes (see CLAUDE.md, "The `IFlashUtil`
//! seam"):
//!
//!   Host -> AS3   `<invoke name="fn" returntype="xml"><arguments>..</arguments></invoke>`
//!   AS3  -> Host   a bare value element, e.g. `<string>battleon</string>`, or an
//!                  `<invoke>` element for a callback the SWF raises on the host.
//!
//! The grammar is small and closed, so a focused recursive-descent parser is far
//! less risk than wiring up (and fetching) a general XML crate — and it keeps the
//! shipped `.so` dependency-free. Every branch is covered by tests.

use crate::value::FlashValue;
use std::collections::BTreeMap;

/// A parse failure, with a human-readable reason and the byte offset reached.
#[derive(Debug, Clone, PartialEq)]
pub struct XmlError {
    pub reason: String,
    pub pos: usize,
}

impl std::fmt::Display for XmlError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "XML parse error at byte {}: {}", self.pos, self.reason)
    }
}

impl std::error::Error for XmlError {}

type Result<T> = std::result::Result<T, XmlError>;

// ---------------------------------------------------------------------------
// Serialisation
// ---------------------------------------------------------------------------

/// Serialise a host -> AS3 call: `<invoke name="..." returntype="xml">...</invoke>`.
pub fn serialize_invoke(name: &str, args: &[FlashValue]) -> String {
    let mut out = String::with_capacity(64);
    out.push_str("<invoke name=\"");
    escape_into(name, &mut out);
    out.push_str("\" returntype=\"xml\"><arguments>");
    for arg in args {
        serialize_value(arg, &mut out);
    }
    out.push_str("</arguments></invoke>");
    out
}

/// Serialise a single value element — the shape returned across the boundary.
pub fn serialize_response(value: &FlashValue) -> String {
    let mut out = String::with_capacity(32);
    serialize_value(value, &mut out);
    out
}

fn serialize_value(value: &FlashValue, out: &mut String) {
    match value {
        FlashValue::Null => out.push_str("<null/>"),
        FlashValue::Undefined => out.push_str("<undefined/>"),
        FlashValue::Bool(true) => out.push_str("<true/>"),
        FlashValue::Bool(false) => out.push_str("<false/>"),
        FlashValue::Number(n) => {
            out.push_str("<number>");
            out.push_str(&format_number(*n));
            out.push_str("</number>");
        }
        FlashValue::String(s) => {
            out.push_str("<string>");
            escape_into(s, out);
            out.push_str("</string>");
        }
        FlashValue::Array(items) => {
            out.push_str("<array>");
            for (i, item) in items.iter().enumerate() {
                out.push_str("<property id=\"");
                out.push_str(&i.to_string());
                out.push_str("\">");
                serialize_value(item, out);
                out.push_str("</property>");
            }
            out.push_str("</array>");
        }
        FlashValue::Object(map) => {
            out.push_str("<object>");
            for (key, item) in map {
                out.push_str("<property id=\"");
                escape_into(key, out);
                out.push_str("\">");
                serialize_value(item, out);
                out.push_str("</property>");
            }
            out.push_str("</object>");
        }
    }
}

/// Format a Flash `Number`. Integral finite values print without a decimal point
/// (matching Flash: `5`, not `5.0`); everything else uses Rust's shortest
/// round-trippable representation.
fn format_number(n: f64) -> String {
    if !n.is_finite() {
        return if n.is_nan() {
            "NaN".to_owned()
        } else if n > 0.0 {
            "Infinity".to_owned()
        } else {
            "-Infinity".to_owned()
        };
    }
    if n.fract() == 0.0 && n.abs() < 1e15 {
        format!("{}", n as i64)
    } else {
        format!("{}", n)
    }
}

fn escape_into(s: &str, out: &mut String) {
    for ch in s.chars() {
        match ch {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '"' => out.push_str("&quot;"),
            '\'' => out.push_str("&apos;"),
            _ => out.push(ch),
        }
    }
}

// ---------------------------------------------------------------------------
// Parsing
// ---------------------------------------------------------------------------

/// Parse a host <- AS3 value element (the return of a `CallFunction`).
pub fn parse_value(xml: &str) -> Result<FlashValue> {
    let mut p = Parser::new(xml);
    p.skip_ws();
    let v = p.read_value()?;
    p.skip_ws();
    if !p.at_end() {
        return Err(p.err("trailing content after value"));
    }
    Ok(v)
}

/// Parse an `<invoke>` element, returning `(function_name, arguments)`.
///
/// Used for AS3 -> host callbacks the SWF raises (the `FlashCall` event on
/// Windows). `returntype` is accepted and ignored.
pub fn parse_invoke(xml: &str) -> Result<(String, Vec<FlashValue>)> {
    let mut p = Parser::new(xml);
    p.skip_ws();
    let tag = p.read_open_tag()?;
    if tag.name != "invoke" {
        return Err(p.err(&format!("expected <invoke>, found <{}>", tag.name)));
    }
    let name = tag
        .attr("name")
        .ok_or_else(|| p.err("<invoke> is missing the name attribute"))?;

    let mut args = Vec::new();
    p.skip_ws();
    // <arguments> is optional in the wild; tolerate a bare <invoke/>, an
    // ARGUMENT-LESS `<invoke ...></invoke>` (what the C# host emits for
    // zero-arg calls like `isTrue`/`loadClient` — the exact wire format the
    // Windows ActiveX host accepts, so it is canon), or an invoke whose only
    // child is the arguments container. Rejecting the argument-less form made
    // every zero-arg host->AS3 call silently return null WITHOUT reaching the
    // player — which broke the loadClient boot handshake in production while
    // every test (all of which passed at least one argument) stayed green.
    if tag.self_closing {
        return Ok((name, args));
    }
    if p.peek_close_tag_is("invoke") {
        p.read_close_tag("invoke")?;
        return Ok((name, args));
    }
    let inner = p.read_open_tag()?;
    if inner.name != "arguments" {
        return Err(p.err(&format!(
            "expected <arguments>, found <{}>",
            inner.name
        )));
    }
    if !inner.self_closing {
        loop {
            p.skip_ws();
            if p.peek_close_tag_is("arguments") {
                p.read_close_tag("arguments")?;
                break;
            }
            args.push(p.read_value()?);
        }
    }
    p.skip_ws();
    p.read_close_tag("invoke")?;
    Ok((name, args))
}

struct OpenTag {
    name: String,
    attrs: Vec<(String, String)>,
    self_closing: bool,
}

impl OpenTag {
    fn attr(&self, key: &str) -> Option<String> {
        self.attrs
            .iter()
            .find(|(k, _)| k == key)
            .map(|(_, v)| v.clone())
    }
}

struct Parser<'a> {
    src: &'a [u8],
    pos: usize,
}

impl<'a> Parser<'a> {
    fn new(s: &'a str) -> Self {
        Parser {
            src: s.as_bytes(),
            pos: 0,
        }
    }

    fn err(&self, reason: &str) -> XmlError {
        XmlError {
            reason: reason.to_owned(),
            pos: self.pos,
        }
    }

    fn at_end(&self) -> bool {
        self.pos >= self.src.len()
    }

    fn peek(&self) -> Option<u8> {
        self.src.get(self.pos).copied()
    }

    fn skip_ws(&mut self) {
        while let Some(b) = self.peek() {
            if b == b' ' || b == b'\t' || b == b'\n' || b == b'\r' {
                self.pos += 1;
            } else {
                break;
            }
        }
    }

    fn expect(&mut self, b: u8) -> Result<()> {
        if self.peek() == Some(b) {
            self.pos += 1;
            Ok(())
        } else {
            Err(self.err(&format!("expected '{}'", b as char)))
        }
    }

    /// Read an opening (or self-closing) tag; leaves position just past `>`.
    fn read_open_tag(&mut self) -> Result<OpenTag> {
        self.expect(b'<')?;
        let name = self.read_name()?;
        let mut attrs = Vec::new();
        loop {
            self.skip_ws();
            match self.peek() {
                Some(b'/') => {
                    self.pos += 1;
                    self.expect(b'>')?;
                    return Ok(OpenTag {
                        name,
                        attrs,
                        self_closing: true,
                    });
                }
                Some(b'>') => {
                    self.pos += 1;
                    return Ok(OpenTag {
                        name,
                        attrs,
                        self_closing: false,
                    });
                }
                Some(_) => {
                    let key = self.read_name()?;
                    self.skip_ws();
                    self.expect(b'=')?;
                    self.skip_ws();
                    let val = self.read_attr_value()?;
                    attrs.push((key, val));
                }
                None => return Err(self.err("unterminated tag")),
            }
        }
    }

    fn read_name(&mut self) -> Result<String> {
        let start = self.pos;
        while let Some(b) = self.peek() {
            if b.is_ascii_alphanumeric() || b == b'_' || b == b'-' || b == b':' {
                self.pos += 1;
            } else {
                break;
            }
        }
        if self.pos == start {
            return Err(self.err("expected an element/attribute name"));
        }
        Ok(String::from_utf8_lossy(&self.src[start..self.pos]).into_owned())
    }

    fn read_attr_value(&mut self) -> Result<String> {
        let quote = self.peek().ok_or_else(|| self.err("expected quote"))?;
        if quote != b'"' && quote != b'\'' {
            return Err(self.err("attribute value must be quoted"));
        }
        self.pos += 1;
        let start = self.pos;
        while let Some(b) = self.peek() {
            if b == quote {
                let raw = &self.src[start..self.pos];
                self.pos += 1;
                return Ok(unescape(&String::from_utf8_lossy(raw)));
            }
            self.pos += 1;
        }
        Err(self.err("unterminated attribute value"))
    }

    /// Read text content up to the next `<`, unescaping entities.
    fn read_text(&mut self) -> String {
        let start = self.pos;
        while let Some(b) = self.peek() {
            if b == b'<' {
                break;
            }
            self.pos += 1;
        }
        unescape(&String::from_utf8_lossy(&self.src[start..self.pos]))
    }

    fn read_close_tag(&mut self, name: &str) -> Result<()> {
        self.expect(b'<')?;
        self.expect(b'/')?;
        let got = self.read_name()?;
        if got != name {
            return Err(self.err(&format!("expected </{}>, found </{}>", name, got)));
        }
        self.skip_ws();
        self.expect(b'>')
    }

    /// Non-consuming check for `</name>` at the current position.
    fn peek_close_tag_is(&self, name: &str) -> bool {
        let rest = &self.src[self.pos..];
        if !rest.starts_with(b"</") {
            return false;
        }
        rest[2..].starts_with(name.as_bytes())
            && rest
                .get(2 + name.len())
                .map(|&b| b == b'>' || b == b' ' || b == b'\t')
                .unwrap_or(false)
    }

    /// Read one value element.
    fn read_value(&mut self) -> Result<FlashValue> {
        let tag = self.read_open_tag()?;
        match tag.name.as_str() {
            "null" => {
                self.finish_simple(&tag, "null")?;
                Ok(FlashValue::Null)
            }
            "undefined" => {
                self.finish_simple(&tag, "undefined")?;
                Ok(FlashValue::Undefined)
            }
            "true" => {
                self.finish_simple(&tag, "true")?;
                Ok(FlashValue::Bool(true))
            }
            "false" => {
                self.finish_simple(&tag, "false")?;
                Ok(FlashValue::Bool(false))
            }
            "string" => {
                if tag.self_closing {
                    return Ok(FlashValue::String(String::new()));
                }
                let text = self.read_text();
                self.read_close_tag("string")?;
                Ok(FlashValue::String(text))
            }
            "number" => {
                if tag.self_closing {
                    return Err(self.err("<number/> has no value"));
                }
                let text = self.read_text();
                self.read_close_tag("number")?;
                let n = parse_number(text.trim())
                    .ok_or_else(|| self.err("invalid number literal"))?;
                Ok(FlashValue::Number(n))
            }
            "array" => {
                if tag.self_closing {
                    return Ok(FlashValue::Array(Vec::new()));
                }
                let props = self.read_properties("array")?;
                // Order by the numeric id so sparse/out-of-order input still
                // yields the right positions.
                let mut indexed: Vec<(usize, FlashValue)> = props
                    .into_iter()
                    .map(|(id, v)| (id.parse::<usize>().unwrap_or(usize::MAX), v))
                    .collect();
                indexed.sort_by_key(|(i, _)| *i);
                Ok(FlashValue::Array(indexed.into_iter().map(|(_, v)| v).collect()))
            }
            "object" => {
                if tag.self_closing {
                    return Ok(FlashValue::Object(BTreeMap::new()));
                }
                let props = self.read_properties("object")?;
                let mut map = BTreeMap::new();
                for (id, v) in props {
                    map.insert(id, v);
                }
                Ok(FlashValue::Object(map))
            }
            other => Err(self.err(&format!("unknown value element <{}>", other))),
        }
    }

    /// For `<true/>`, `<null/>` etc. — accept the self-closing form or an empty
    /// `<true></true>` pair.
    fn finish_simple(&mut self, tag: &OpenTag, name: &str) -> Result<()> {
        if tag.self_closing {
            Ok(())
        } else {
            self.skip_ws();
            self.read_close_tag(name)
        }
    }

    /// Read `<property id="...">value</property>*` until the container closes.
    fn read_properties(&mut self, container: &str) -> Result<Vec<(String, FlashValue)>> {
        let mut props = Vec::new();
        loop {
            self.skip_ws();
            if self.peek_close_tag_is(container) {
                self.read_close_tag(container)?;
                break;
            }
            let ptag = self.read_open_tag()?;
            if ptag.name != "property" {
                return Err(self.err(&format!(
                    "expected <property>, found <{}>",
                    ptag.name
                )));
            }
            let id = ptag
                .attr("id")
                .ok_or_else(|| self.err("<property> is missing the id attribute"))?;
            if ptag.self_closing {
                // <property id="x"/> — treat as undefined.
                props.push((id, FlashValue::Undefined));
                continue;
            }
            self.skip_ws();
            let value = self.read_value()?;
            self.skip_ws();
            self.read_close_tag("property")?;
            props.push((id, value));
        }
        Ok(props)
    }
}

/// Parse a `<number>` body. Flash uses JS number syntax; `NaN`/`Infinity` too.
fn parse_number(s: &str) -> Option<f64> {
    match s {
        "NaN" => Some(f64::NAN),
        "Infinity" => Some(f64::INFINITY),
        "-Infinity" => Some(f64::NEG_INFINITY),
        _ => s.parse::<f64>().ok(),
    }
}

/// Reverse XML entity escaping. Handles the five named entities plus decimal and
/// hex numeric character references. Unknown/malformed references are left as-is.
fn unescape(s: &str) -> String {
    if !s.contains('&') {
        return s.to_owned();
    }
    let mut out = String::with_capacity(s.len());
    let bytes = s.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] != b'&' {
            // Copy one UTF-8 char starting at i.
            let ch_len = utf8_len(bytes[i]);
            out.push_str(&s[i..(i + ch_len).min(s.len())]);
            i += ch_len;
            continue;
        }
        // Find the terminating ';' within a small window.
        if let Some(semi_rel) = s[i..].find(';') {
            let entity = &s[i + 1..i + semi_rel];
            match entity {
                "amp" => out.push('&'),
                "lt" => out.push('<'),
                "gt" => out.push('>'),
                "quot" => out.push('"'),
                "apos" => out.push('\''),
                _ if entity.starts_with("#x") || entity.starts_with("#X") => {
                    if let Ok(cp) = u32::from_str_radix(&entity[2..], 16) {
                        if let Some(c) = char::from_u32(cp) {
                            out.push(c);
                        }
                    } else {
                        out.push_str(&s[i..=i + semi_rel]);
                    }
                }
                _ if entity.starts_with('#') => {
                    if let Ok(cp) = entity[1..].parse::<u32>() {
                        if let Some(c) = char::from_u32(cp) {
                            out.push(c);
                        }
                    } else {
                        out.push_str(&s[i..=i + semi_rel]);
                    }
                }
                _ => {
                    // Unknown entity — leave verbatim.
                    out.push_str(&s[i..=i + semi_rel]);
                }
            }
            i += semi_rel + 1;
        } else {
            // Lone '&' with no ';' — copy literally.
            out.push('&');
            i += 1;
        }
    }
    out
}

fn utf8_len(first: u8) -> usize {
    if first < 0x80 {
        1
    } else if first >> 5 == 0b110 {
        2
    } else if first >> 4 == 0b1110 {
        3
    } else if first >> 3 == 0b11110 {
        4
    } else {
        1
    }
}
