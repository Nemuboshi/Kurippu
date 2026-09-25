// Shared-texture plumbing: `build_texture_info` and its helpers are consumed by
// the macOS (Task 7) and Windows (Task 8) targets, so they are unreachable on
// this platform until those land.
#![allow(dead_code)]

// Render paths. The software target builds and consumes a CPU buffer; the
// shared-texture targets hand an OS surface to electron.sharedTexture.
//
// Both publish through `build_texture_info` so the shape Electron validates
// exists once. In the C++ original it did not: `renderSharedTexture` carried a
// ~35-line duplicate of the whole object literal for macOS (mpv_addon.cc:663-697)
// and again for Windows (mpv_addon.cc:759-793).

pub mod software;

#[cfg(target_os = "macos")]
pub mod macos;
#[cfg(target_os = "windows")]
pub mod windows;

use napi::bindgen_prelude::*;

/// Colour metadata handed to `electron.sharedTexture`. The C++ literal at
/// mpv_addon.cc:681-685 and 777-781; order matters only in that it is the
/// order the object is enumerated in.
pub const COLOR_SPACE: [(&str, &str); 4] = [
    ("primaries", "bt709"),
    ("transfer", "srgb"),
    ("matrix", "rgb"),
    ("range", "full"),
];

/// Publishes a raw OS handle to JS as little/big-endian-neutral native bytes,
/// which is what `electron.sharedTexture` reinterprets. `usize` and the handle
/// types are the same width on every target this crate builds for.
pub fn pointer_bytes(value: usize) -> Vec<u8> {
    value.to_ne_bytes().to_vec()
}

/// The `Electron.SharedTextureImportTextureInfo` shape. `handle_key` is
/// `ioSurface` on macOS and `ntHandle` on Windows.
pub fn build_texture_info<'env>(
    env: &'env Env,
    id: String,
    width: i32,
    height: i32,
    timestamp: u64,
    handle_key: &str,
    handle_bytes: Vec<u8>,
) -> Result<Object<'env>> {
    let mut info = Object::new(env)?;
    info.set("id", id)?;
    info.set("pixelFormat", "bgra")?;
    info.set("codedSize", sized_rect(env, width, height)?)?;

    let mut rect = sized_rect(env, width, height)?;
    rect.set("x", 0)?;
    rect.set("y", 0)?;
    info.set("visibleRect", rect)?;
    info.set("contentRect", rect)?;

    // The C++ counter is a u64 handed over as a JS number; values this side of
    // 2^53 are exact, and a frame counter will not approach it.
    info.set("timestamp", timestamp as f64)?;

    let mut color_space = Object::new(env)?;
    for (key, value) in COLOR_SPACE {
        color_space.set(key, value)?;
    }
    info.set("colorSpace", color_space)?;

    let mut handle = Object::new(env)?;
    handle.set(handle_key, BufferSlice::copy_from(env, handle_bytes)?)?;
    info.set("handle", handle)?;
    Ok(info)
}

fn sized_rect<'env>(env: &'env Env, width: i32, height: i32) -> Result<Object<'env>> {
    let mut rect = Object::new(env)?;
    rect.set("width", width)?;
    rect.set("height", height)?;
    Ok(rect)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pointer_bytes_is_the_native_width_of_the_handle() {
        assert_eq!(pointer_bytes(0).len(), std::mem::size_of::<usize>());
        if std::mem::size_of::<usize>() == 8 {
            assert_eq!(pointer_bytes(0x0102_0304_0506_0708).len(), 8);
        }
    }

    #[test]
    fn color_space_keeps_the_cpp_pairs() {
        assert_eq!(COLOR_SPACE[0], ("primaries", "bt709"));
        assert_eq!(COLOR_SPACE[1], ("transfer", "srgb"));
        assert_eq!(COLOR_SPACE[2], ("matrix", "rgb"));
        assert_eq!(COLOR_SPACE[3], ("range", "full"));
    }
}
