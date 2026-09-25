// mpv_node -> JS conversion, and the property probe order.
//
// The converter needs a live napi `Env`, so it is the one part of this port
// with no unit test. Everything testable about it -- which shape a property
// name resolves to -- is separated into `probe_strategy` and covered there.
// The recursion transcribes `mpv_node_to_js` at mpv_addon.cc:84-130, and the
// arm order is load-bearing.
//
// napi 3.x deprecates the `Env::create_*` + `JsObject` construction style, so
// this uses `Object`, `Array` and `BufferSlice` with the `ToNapiValue::
// into_unknown` trait method. `compat-mode` turns out not to be required.
#![allow(dead_code)]

use crate::ffi;
use crate::ffi::format::*;
use napi::bindgen_prelude::*;
use std::ffi::CStr;

#[derive(Debug, PartialEq, Eq)]
pub enum Probe {
    AsNode,
    DoubleThenInt64,
    StringThenNumeric,
}

/// The C++ special-cases three name sets in a fixed order, and the fall-through
/// is not the neutral default it looks like: `mpv_get_property_string` succeeds
/// for numeric properties too, so `getProperty("width")` answers with the
/// string "1920". The renderer's probe path was written against that.
pub fn probe_strategy(name: &str) -> Probe {
    match name {
        "track-list" | "metadata" => Probe::AsNode,
        "duration" | "demuxer-bitrate" | "file-size" => Probe::DoubleThenInt64,
        _ => Probe::StringThenNumeric,
    }
}

fn cstr(raw: *const std::ffi::c_char) -> String {
    if raw.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(raw) }
            .to_string_lossy()
            .into_owned()
    }
}

/// `None` is the contract's JS `null`.
///
/// # Safety
/// `node` must be a valid `mpv_node` that stays alive for the call. The caller
/// owns `mpv_free_node_contents`, exactly as in the C++.
pub unsafe fn node_to_js<'env>(
    env: &'env Env,
    node: &ffi::mpv_node,
) -> Result<Option<Unknown<'env>>> {
    Ok(match node.format {
        MPV_FORMAT_STRING => {
            let raw = unsafe { node.u.string };
            if raw.is_null() {
                None
            } else {
                Some(env.create_string_from_std(cstr(raw))?.into_unknown(env)?)
            }
        }
        // A plain bool is ToNapiValue; Env::get_boolean only exists under the
        // compat-mode feature, which this crate deliberately does not enable.
        MPV_FORMAT_FLAG => Some((unsafe { node.u.flag } != 0).into_unknown(env)?),
        MPV_FORMAT_INT64 => Some(
            env.create_double(unsafe { node.u.int64 } as f64)?
                .into_unknown(env)?,
        ),
        MPV_FORMAT_DOUBLE => Some(
            env.create_double(unsafe { node.u.double_ })?
                .into_unknown(env)?,
        ),
        MPV_FORMAT_NODE_ARRAY => {
            let list = unsafe { node.u.list };
            let len = if list.is_null() {
                0
            } else {
                unsafe { (*list).num }.max(0) as usize
            };
            let mut items: Vec<Option<Unknown>> = Vec::with_capacity(len);
            if !list.is_null() {
                for i in 0..len {
                    let child = unsafe { *(*list).values.add(i) };
                    items.push(unsafe { node_to_js(env, &child) }?);
                }
            }
            Some(Array::from_vec(env, items)?.into_unknown(env)?)
        }
        MPV_FORMAT_NODE_MAP => {
            let list = unsafe { node.u.list };
            let mut obj = Object::new(env)?;
            if !list.is_null() {
                let len = unsafe { (*list).num }.max(0) as usize;
                for i in 0..len {
                    let key = unsafe { *(*list).keys.add(i) };
                    if key.is_null() {
                        continue;
                    }
                    let child = unsafe { *(*list).values.add(i) };
                    obj.set(cstr(key), unsafe { node_to_js(env, &child) }?)?;
                }
            }
            Some(obj.into_unknown(env)?)
        }
        MPV_FORMAT_BYTE_ARRAY => {
            let ba = unsafe { node.u.ba };
            let bytes = if ba.is_null() || unsafe { (*ba).data }.is_null() {
                Vec::new()
            } else {
                let size = unsafe { (*ba).size };
                unsafe { std::slice::from_raw_parts((*ba).data as *const u8, size) }.to_vec()
            };
            Some(BufferSlice::copy_from(env, bytes)?.into_unknown(env)?)
        }
        _ => None,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn node_first_for_tree_shapes() {
        assert_eq!(probe_strategy("track-list"), Probe::AsNode);
        assert_eq!(probe_strategy("metadata"), Probe::AsNode);
    }

    #[test]
    fn numeric_pair_for_the_three_sized_props() {
        assert_eq!(probe_strategy("duration"), Probe::DoubleThenInt64);
        assert_eq!(probe_strategy("demuxer-bitrate"), Probe::DoubleThenInt64);
        assert_eq!(probe_strategy("file-size"), Probe::DoubleThenInt64);
    }

    #[test]
    fn everything_else_falls_through_to_string_first() {
        // The string attempt succeeds for numerics too, so getProperty("width")
        // must keep returning the string "1920". Quirk preserved deliberately.
        assert_eq!(probe_strategy("width"), Probe::StringThenNumeric);
        assert_eq!(probe_strategy("pause"), Probe::StringThenNumeric);
        assert_eq!(probe_strategy("no-such-property"), Probe::StringThenNumeric);
        assert_eq!(probe_strategy(""), Probe::StringThenNumeric);
    }

    #[test]
    fn no_prefix_peeking_on_the_tree_shapes() {
        // "track-list/v1" must not be mistaken for the tree-shaped "track-list".
        assert_eq!(probe_strategy("track-list/v1"), Probe::StringThenNumeric);
        assert_eq!(probe_strategy("metadata/foo"), Probe::StringThenNumeric);
    }
}
