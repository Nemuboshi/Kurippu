//! Software render target: mpv converts a frame into a CPU buffer that JS
//! paints onto a canvas.

use crate::ffi;
use napi::bindgen_prelude::*;
use std::ffi::c_void;

pub struct SoftwareTarget {
    ctx: *mut ffi::mpv_render_context,
}

// SAFETY: the render context is only ever touched from the JS thread, by way of
// `MpvPlayer`, which napi hands to one thread at a time.
unsafe impl Send for SoftwareTarget {}

impl SoftwareTarget {
    pub fn new(ctx: *mut ffi::mpv_render_context) -> Self {
        Self { ctx }
    }

    /// Renders one frame and hands the pixel buffer to JS without copying it.
    ///
    /// The C++ allocated a `std::vector` and then did `Napi::Buffer::Copy` into
    /// the JS value (`mpv_addon.cc:589,612`), so a 1920x1080 frame was copied
    /// twice per frame. Here mpv writes straight into a `Vec` that
    /// `BufferSlice::from_data` then adopts.
    pub fn render<'env>(&self, env: &'env Env, width: i32, height: i32) -> Result<Object<'env>> {
        // client.h specifies size_t for the stride. The C++ passed an `int*`,
        // so mpv read the four bytes past a 4-byte local as the high half of
        // the stride and worked only when that stack memory happened to be zero.
        let mut stride: usize = (width * 4) as usize;
        let mut size = [width, height];
        let mut rgba: Vec<u8> = vec![0u8; (width as usize) * (height as usize) * 4];
        let format = std::ffi::CString::new(ffi::SW_FORMAT_RGBA).expect("no interior nul");

        let pointer = rgba.as_mut_ptr() as *mut c_void;
        let mut params = [
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_SW_SIZE,
                data: &mut size as *mut _ as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_SW_FORMAT,
                data: format.as_ptr() as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_SW_STRIDE,
                data: &mut stride as *mut _ as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_SW_POINTER,
                data: pointer,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_INVALID,
                data: std::ptr::null_mut(),
            },
        ];

        unsafe { ffi::mpv_render_context_update(self.ctx) };
        let ret = unsafe { ffi::mpv_render_context_render(self.ctx, params.as_mut_ptr()) };
        if ret < 0 {
            return Err(super::super::player::mpv_error(
                "mpv_render_context_render",
                ret,
            ));
        }

        let mut frame = Object::new(env)?;
        frame.set("width", width)?;
        frame.set("height", height)?;
        frame.set("rgba", BufferSlice::from_data(env, rgba)?)?;
        Ok(frame)
    }
}
