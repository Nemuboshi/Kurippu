// The libmpv C ABI. Struct/enum/function declarations live in
// mpv_generated.rs and are DERIVED from the installed headers -- see
// ffi/regenerate.sh for the seven transcription errors that motivated this.
// Everything in this file is hand-written, and is limited to names that carry
// no layout or discriminant risk.
#![allow(unused_imports, dead_code)] // re-exports are consumed by later tasks

#[allow(
    dead_code,
    non_camel_case_types,
    non_snake_case,
    non_upper_case_globals,
    clippy::all
)]
mod mpv_generated {
    include!("mpv_generated.rs");
}

pub use mpv_generated::*;

/// The union member names in the C header are spelled `mpv_node::u`, and the
/// generated type name is bindgen's internal one.
pub use mpv_generated::mpv_node__bindgen_ty_1 as mpv_node_union;

/// Short symbolic names for the property/node formats this addon switches on.
/// Matching on these is a const-path pattern against the generated `c_uint`
/// typedef, so the values stay the header's, not ours.
pub mod format {
    pub use super::mpv_generated::{
        mpv_format_MPV_FORMAT_BYTE_ARRAY as MPV_FORMAT_BYTE_ARRAY,
        mpv_format_MPV_FORMAT_DOUBLE as MPV_FORMAT_DOUBLE,
        mpv_format_MPV_FORMAT_FLAG as MPV_FORMAT_FLAG,
        mpv_format_MPV_FORMAT_INT64 as MPV_FORMAT_INT64,
        mpv_format_MPV_FORMAT_NODE as MPV_FORMAT_NODE,
        mpv_format_MPV_FORMAT_NODE_ARRAY as MPV_FORMAT_NODE_ARRAY,
        mpv_format_MPV_FORMAT_NODE_MAP as MPV_FORMAT_NODE_MAP,
        mpv_format_MPV_FORMAT_NONE as MPV_FORMAT_NONE,
        mpv_format_MPV_FORMAT_STRING as MPV_FORMAT_STRING,
    };
}

pub mod event {
    pub use super::mpv_generated::{
        mpv_event_id_MPV_EVENT_NONE as MPV_EVENT_NONE,
        mpv_event_id_MPV_EVENT_PROPERTY_CHANGE as MPV_EVENT_PROPERTY_CHANGE,
    };
}

pub mod render_param {
    pub use super::mpv_generated::{
        mpv_render_param_type_MPV_RENDER_PARAM_API_TYPE as MPV_RENDER_PARAM_API_TYPE,
        mpv_render_param_type_MPV_RENDER_PARAM_FLIP_Y as MPV_RENDER_PARAM_FLIP_Y,
        mpv_render_param_type_MPV_RENDER_PARAM_INVALID as MPV_RENDER_PARAM_INVALID,
        mpv_render_param_type_MPV_RENDER_PARAM_OPENGL_FBO as MPV_RENDER_PARAM_OPENGL_FBO,
        mpv_render_param_type_MPV_RENDER_PARAM_OPENGL_INIT_PARAMS as MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,
        mpv_render_param_type_MPV_RENDER_PARAM_SW_FORMAT as MPV_RENDER_PARAM_SW_FORMAT,
        mpv_render_param_type_MPV_RENDER_PARAM_SW_POINTER as MPV_RENDER_PARAM_SW_POINTER,
        mpv_render_param_type_MPV_RENDER_PARAM_SW_SIZE as MPV_RENDER_PARAM_SW_SIZE,
        mpv_render_param_type_MPV_RENDER_PARAM_SW_STRIDE as MPV_RENDER_PARAM_SW_STRIDE,
    };
}

pub mod error {
    pub use super::mpv_generated::{
        mpv_error_MPV_ERROR_COMMAND as MPV_ERROR_COMMAND,
        mpv_error_MPV_ERROR_PROPERTY_NOT_FOUND as MPV_ERROR_PROPERTY_NOT_FOUND,
        mpv_error_MPV_ERROR_SUCCESS as MPV_ERROR_SUCCESS,
    };
}

/// Render API type strings. These are `#define`s in render.h, so bindgen's
/// allowlist does not emit them; they are plain NUL-terminated literals.
pub const API_TYPE_OPENGL: &str = "opengl";
pub const API_TYPE_SW: &str = "sw";

/// mpv's SW renderer converts to the caller's buffer; the C++ addon requested
/// and documented "rgba", so the port must keep asking for exactly that.
pub const SW_FORMAT_RGBA: &str = "rgba";

/// `mpv_error_string` as a Rust string, preserving `mpv_error_text`'s two
/// special cases (`mpv_addon.cc:72-76`): non-negative codes are "ok", and a
/// null pointer from the library reads "unknown mpv error".
pub fn error_text(code: std::os::raw::c_int) -> String {
    if code >= 0 {
        return String::from("ok");
    }
    let raw = unsafe { mpv_error_string(code) };
    if raw.is_null() {
        String::from("unknown mpv error")
    } else {
        unsafe { std::ffi::CStr::from_ptr(raw) }
            .to_string_lossy()
            .into_owned()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;
    use std::ptr;

    /// Proves the generated declarations actually resolve against the real
    /// libmpv: a mis-named or mis-linked symbol fails here at load, and a wrong
    /// `mpv_node` layout would not be caught by compilation. The layout audit
    /// is bindgen's own `const _` block in mpv_generated.rs; this is the
    /// linkage and behaviour audit.
    #[test]
    fn a_real_mpv_handle_round_trips_through_the_generated_declarations() {
        unsafe {
            let handle = mpv_create();
            assert!(!handle.is_null(), "mpv_create returned null");
            assert_eq!(mpv_initialize(handle), 0, "mpv_initialize failed");

            let key = CString::new("volume").unwrap();
            let mut volume: f64 = 42.0;
            assert_eq!(
                mpv_set_property(
                    handle,
                    key.as_ptr(),
                    format::MPV_FORMAT_DOUBLE,
                    &mut volume as *mut _ as *mut _,
                ),
                0,
                "mpv_set_property(double) failed"
            );

            let mut read_back: f64 = 0.0;
            assert_eq!(
                mpv_get_property(
                    handle,
                    key.as_ptr(),
                    format::MPV_FORMAT_DOUBLE,
                    &mut read_back as *mut _ as *mut _,
                ),
                0,
                "mpv_get_property(double) failed"
            );
            assert_eq!(read_back, 42.0, "a double did not survive the ABI");

            let name = CString::new("duration").unwrap();
            assert!(
                !mpv_event_name(event::MPV_EVENT_PROPERTY_CHANGE).is_null(),
                "mpv_event_name is not resolvable"
            );
            let _ = name;
            mpv_terminate_destroy(handle);
        }
    }

    /// The error codes `setDeinterlace` tolerates are load-bearing: the C++
    /// compared against a constant that does not exist in client.h at all
    /// (commit 99ea49f). Pin the two real values.
    #[test]
    fn tolerated_error_codes_have_the_header_values() {
        assert_eq!(error::MPV_ERROR_PROPERTY_NOT_FOUND, -8);
        assert_eq!(error::MPV_ERROR_COMMAND, -12);
        assert_eq!(error::MPV_ERROR_SUCCESS, 0);
    }

    #[test]
    fn sw_render_params_are_the_header_values() {
        assert_eq!(render_param::MPV_RENDER_PARAM_SW_SIZE, 17);
        assert_eq!(render_param::MPV_RENDER_PARAM_SW_FORMAT, 18);
        assert_eq!(render_param::MPV_RENDER_PARAM_SW_STRIDE, 19);
        assert_eq!(render_param::MPV_RENDER_PARAM_SW_POINTER, 20);
        assert_eq!(event::MPV_EVENT_PROPERTY_CHANGE, 22);
    }
}
