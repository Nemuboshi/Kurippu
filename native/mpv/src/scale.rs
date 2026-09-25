// Consumed by `player.rs` (Task 6). Every rule in here is exercised by the
// tests below, but nothing is reachable from the crate root until then, which
// makes an unused item a warning rather than a defect.
#![allow(dead_code)]

// Argument rules for the addon's JS surface, kept free of napi so every one of
// them is unit-testable. These encode behaviour the renderer already depends
// on, including two places where the C++ is surprising; the comments say which.

use std::ffi::c_int;

#[derive(Debug, PartialEq, Eq)]
pub enum TrackError {
    UnknownKind,
}

/// Mirrors napi `Int32Value()`: non-finite input becomes 0, finite input
/// truncates toward zero. Mapping NaN to the default instead would silently
/// change what mpv is asked to render.
fn to_int32(v: f64) -> i32 {
    if v.is_finite() {
        v.trunc() as i32
    } else {
        0
    }
}

fn clamp_pair(w: Option<f64>, h: Option<f64>, max_w: i32, max_h: i32) -> (i32, i32) {
    // The C++ only reads the dimensions when *both* arguments are numbers, so
    // a single supplied argument leaves the whole pair at its default.
    let (Some(w), Some(h)) = (w, h) else {
        return (960, 540);
    };
    (to_int32(w).max(2).min(max_w), to_int32(h).max(2).min(max_h))
}

/// `renderFrame` ceiling: 1920x1080 (`mpv_addon.cc:586-587`).
pub fn clamp_software(w: Option<f64>, h: Option<f64>) -> (i32, i32) {
    clamp_pair(w, h, 1920, 1080)
}

/// `renderSharedTexture` ceiling: 3840x2160 (`mpv_addon.cc:633-634`).
pub fn clamp_shared(w: Option<f64>, h: Option<f64>) -> (i32, i32) {
    clamp_pair(w, h, 3840, 2160)
}

/// `std::to_string(double)` is printf `%f`: always six decimals, so 1.2345 is
/// passed to mpv as "1.234500". Plain Rust formatting yields "1.2345"; both
/// parse, but the six-decimal form keeps the emitted command identical.
pub fn seek_arg(seconds: f64) -> String {
    if seconds.is_finite() {
        format!("{seconds:.6}")
    } else {
        String::from("nan")
    }
}

pub fn track_property(kind: &str) -> Result<&'static str, TrackError> {
    match kind {
        "video" => Ok("vid"),
        "audio" => Ok("aid"),
        "subtitle" => Ok("sid"),
        _ => Err(TrackError::UnknownKind),
    }
}

/// `Ok(None)`-shaped result: `Some("no")` means "disable this track", `None`
/// means the id failed the integer test at `mpv_addon.cc:520-524`.
pub fn track_id_arg(id: Option<f64>) -> Option<String> {
    match id {
        None => Some(String::from("no")),
        Some(v)
            if v.is_finite() && v >= 0.0 && v.fract() == 0.0 && v <= 9_007_199_254_740_991.0 =>
        {
            Some(format!("{}", v as i64))
        }
        Some(_) => None,
    }
}

/// `vf-remove` legitimately reports these when the filter was never added.
/// Getting this wrong was a real bug in the original addon: it compared against
/// `MPV_ERROR_PROPERTY_UNSET`, a constant that does not exist in client.h
/// (commit 99ea49f).
pub fn vf_removal_is_ok(code: c_int) -> bool {
    code >= 0
        || code == crate::ffi::error::MPV_ERROR_PROPERTY_NOT_FOUND
        || code == crate::ffi::error::MPV_ERROR_COMMAND
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn software_defaults_and_clamps() {
        assert_eq!(clamp_software(None, None), (960, 540));
        assert_eq!(clamp_software(Some(0.0), Some(0.0)), (2, 2));
        assert_eq!(clamp_software(Some(-5.0), Some(1.0)), (2, 2));
        assert_eq!(clamp_software(Some(4000.0), Some(4000.0)), (1920, 1080));
        assert_eq!(clamp_software(Some(1280.7), Some(720.9)), (1280, 720));
        // NaN is a number, so the C++ reaches Int32Value() and JS ToInt32 turns
        // it into 0, which the max(2, .) floor lifts to 2 -- not the default.
        assert_eq!(clamp_software(Some(f64::NAN), Some(f64::NAN)), (2, 2));
        assert_eq!(clamp_software(Some(f64::INFINITY), Some(400.0)), (2, 400));
        // Only one dimension supplied: the C++ requires both to be numbers and
        // otherwise keeps the 960x540 default for both.
        assert_eq!(clamp_software(Some(640.0), None), (960, 540));
    }

    #[test]
    fn shared_defaults_and_clamps() {
        assert_eq!(clamp_shared(None, None), (960, 540));
        assert_eq!(clamp_shared(Some(0.0), Some(0.0)), (2, 2));
        assert_eq!(clamp_shared(Some(8000.0), Some(8000.0)), (3840, 2160));
        assert_eq!(clamp_shared(Some(1920.0), Some(1080.0)), (1920, 1080));
    }

    #[test]
    fn the_two_modes_disagree_on_purpose() {
        assert_eq!(clamp_software(Some(4000.0), Some(4000.0)), (1920, 1080));
        assert_eq!(clamp_shared(Some(4000.0), Some(4000.0)), (3840, 2160));
    }

    #[test]
    fn seek_matches_cpp_to_string() {
        assert_eq!(seek_arg(0.0), "0.000000");
        assert_eq!(seek_arg(1.2345), "1.234500");
        assert_eq!(seek_arg(-1.0), "-1.000000");
        assert_eq!(seek_arg(10.0), "10.000000");
    }

    #[test]
    fn track_kind_mapping() {
        assert_eq!(track_property("video").unwrap(), "vid");
        assert_eq!(track_property("audio").unwrap(), "aid");
        assert_eq!(track_property("subtitle").unwrap(), "sid");
        assert_eq!(track_property("caption"), Err(TrackError::UnknownKind));
    }

    #[test]
    fn track_id_rejects_non_integers() {
        assert_eq!(track_id_arg(None).as_deref(), Some("no"));
        assert_eq!(track_id_arg(Some(7.0)).as_deref(), Some("7"));
        assert_eq!(track_id_arg(Some(0.0)).as_deref(), Some("0"));
        assert_eq!(track_id_arg(Some(-1.0)), None);
        assert_eq!(track_id_arg(Some(1.5)), None);
        assert_eq!(track_id_arg(Some(f64::NAN)), None);
        assert_eq!(track_id_arg(Some(9007199254740992.0)), None);
        assert_eq!(
            track_id_arg(Some(9007199254740991.0)).as_deref(),
            Some("9007199254740991")
        );
    }

    #[test]
    fn vf_removal_tolerates_exactly_two_codes() {
        assert!(vf_removal_is_ok(0));
        assert!(vf_removal_is_ok(
            crate::ffi::error::MPV_ERROR_PROPERTY_NOT_FOUND
        ));
        assert!(vf_removal_is_ok(crate::ffi::error::MPV_ERROR_COMMAND));
        assert!(!vf_removal_is_ok(
            crate::ffi::mpv_error_MPV_ERROR_PROPERTY_UNAVAILABLE
        ));
        assert!(!vf_removal_is_ok(-999_999));
    }
}
