//! macOS shared-texture target: an offscreen CGL context renders into an
//! IOSurface that Electron imports directly.
//!
//! Transcribes mpv_addon.cc:914-1033. The CGL entry points and pixel-format
//! attributes come from the `cgl` crate rather than being re-declared here --
//! `kCGLPFAAccelerated` is 73 and `kCGLPFAAllowOfflineRenderers` is 96, which
//! are indexed enum values, not the four-character codes their names suggest.
//! Guessing those would have been silently wrong.
//!
//! The IOSurface/CF declarations below are function and symbol *names*, so a
//! mistake is a link error on the macOS runner rather than corrupt memory. The
//! handful of genuine numeric constants are marked where a Mac run is the only
//! check that can confirm them.
#![allow(dead_code)]

use crate::ffi;
use crate::render;
use napi::bindgen_prelude::*;
use std::ffi::{c_int, c_void, CString};
use std::ptr;

const CF_NUMBER_SINT32: c_int = 5; // kCFNumberSInt32Type
const TEXTURE_RECTANGLE: u32 = 0x84F5;
const RGBA: u32 = 0x1908;
const BGRA: u32 = 0x80E1;
const UNSIGNED_INT_8_8_8_8_REV: u32 = 0x8367;
const TEXTURE_2D: u32 = 0x0DE1;
const TEXTURE_MIN_FILTER: u32 = 0x2800;
const TEXTURE_MAG_FILTER: u32 = 0x2801;
const TEXTURE_WRAP_S: u32 = 0x2802;
const TEXTURE_WRAP_T: u32 = 0x2803;
const LINEAR: u32 = 0x2601;
const CLAMP_TO_EDGE: u32 = 0x812F;
const FRAMEBUFFER: u32 = 0x8D40;
const COLOR_ATTACHMENT0: u32 = 0x8CE0;
const FRAMEBUFFER_COMPLETE: u32 = 0x8CD5;
const RGBA8: u32 = 0x8058;

/// Four-character code, spelled out so 'BGRA' is not a magic number.
const fn fourcc(a: u8, b: u8, c: u8, d: u8) -> i32 {
    ((a as i32) << 24) | ((b as i32) << 16) | ((c as i32) << 8) | d as i32
}

#[link(name = "IOSurface", kind = "framework")]
extern "C" {
    fn IOSurfaceCreate(properties: *const c_void) -> *mut c_void;
    fn IOSurfaceAlignProperty(key: *const c_void, size: usize) -> usize;
    static kIOSurfaceWidth: *const c_void;
    static kIOSurfaceHeight: *const c_void;
    static kIOSurfaceBytesPerElement: *const c_void;
    static kIOSurfaceBytesPerRow: *const c_void;
    static kIOSurfaceAllocSize: *const c_void;
    static kIOSurfacePixelFormat: *const c_void;
}

#[link(name = "CoreFoundation", kind = "framework")]
extern "C" {
    fn CFDictionaryCreateMutable(
        alloc: *const c_void,
        capacity: isize,
        key_callbacks: *const c_void,
        value_callbacks: *const c_void,
    ) -> *mut c_void;
    fn CFDictionarySetValue(dict: *mut c_void, key: *const c_void, value: *const c_void);
    fn CFNumberCreate(
        alloc: *const c_void,
        number_type: c_int,
        value_ptr: *const c_void,
    ) -> *mut c_void;
    fn CFRelease(cf: *const c_void);
    static kCFTypeDictionaryKeyCallBacks: c_void;
    static kCFTypeDictionaryValueCallBacks: c_void;
}

#[link(name = "OpenGL", kind = "framework")]
extern "C" {
    fn glGenTextures(n: i32, textures: *mut u32);
    fn glDeleteTextures(n: i32, textures: *const u32);
    fn glBindTexture(target: u32, texture: u32);
    fn glTexParameteri(target: u32, pname: u32, param: i32);
    fn glGenFramebuffers(n: i32, ids: *mut u32);
    fn glBindFramebuffer(target: u32, framebuffer: u32);
    fn glFramebufferTexture2D(
        target: u32,
        attachment: u32,
        textarget: u32,
        texture: u32,
        level: i32,
    );
    fn glCheckFramebufferStatus(target: u32) -> u32;
    fn glDeleteFramebuffers(n: i32, ids: *const u32);
    fn glFlush();
}

pub struct MacSharedTarget {
    ctx: *mut ffi::mpv_render_context,
    gl_ctx: *mut c_void,
    surface: *mut c_void,
    texture: u32,
    fbo: u32,
    width: i32,
    height: i32,
}

// SAFETY: reached only through `&mut MpvPlayer`, which napi hands to one thread
// at a time. The CGL context is made current on that thread before use.
unsafe impl Send for MacSharedTarget {}

/// mpv resolves GL entry points through this; the C++ used dlsym(RTLD_DEFAULT).
unsafe extern "C" fn get_proc_address(
    _ctx: *mut c_void,
    name: *const std::os::raw::c_char,
) -> *mut c_void {
    let c_name = std::ffi::CStr::from_ptr(name);
    libc::dlsym(libc::RTLD_DEFAULT, c_name.as_ptr())
}

impl MacSharedTarget {
    pub fn new(handle: *mut ffi::mpv_handle) -> Result<Self> {
        let mut pixel_format: *mut c_void = ptr::null_mut();
        let mut num_formats: i32 = 0;
        let attrs = [
            cgl::kCGLPFAAccelerated,
            cgl::kCGLPFAAllowOfflineRenderers,
            0,
        ];
        let err = unsafe {
            cgl::CGLChoosePixelFormat(attrs.as_ptr(), &mut pixel_format, &mut num_formats)
        };
        if err != cgl::kCGLNoError || pixel_format.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to initialize CGL context",
            ));
        }

        let mut gl_ctx: *mut c_void = ptr::null_mut();
        let err = unsafe { cgl::CGLCreateContext(pixel_format, ptr::null_mut(), &mut gl_ctx) };
        unsafe { cgl::CGLDestroyPixelFormat(pixel_format) };
        if err != cgl::kCGLNoError || gl_ctx.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to initialize CGL context",
            ));
        }
        unsafe { cgl::CGLSetCurrentContext(gl_ctx) };

        let api = CString::new(ffi::API_TYPE_OPENGL).unwrap();
        let mut gl_init = ffi::mpv_opengl_init_params {
            get_proc_address: Some(get_proc_address),
            get_proc_address_ctx: ptr::null_mut(),
        };
        let mut params = [
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_API_TYPE,
                data: api.as_ptr() as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,
                data: &mut gl_init as *mut _ as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_INVALID,
                data: ptr::null_mut(),
            },
        ];

        let mut ctx: *mut ffi::mpv_render_context = ptr::null_mut();
        let ret = unsafe { ffi::mpv_render_context_create(&mut ctx, handle, params.as_mut_ptr()) };
        if ret < 0 {
            unsafe { cgl::CGLDestroyContext(gl_ctx) };
            return Err(crate::player::mpv_error(
                "mpv_render_context_create(opengl)",
                ret,
            ));
        }

        Ok(Self {
            ctx,
            gl_ctx,
            surface: ptr::null_mut(),
            texture: 0,
            fbo: 0,
            width: 0,
            height: 0,
        })
    }

    pub fn render_ctx(&self) -> *mut ffi::mpv_render_context {
        self.ctx
    }

    fn destroy_target(&mut self) {
        if self.gl_ctx.is_null() {
            return;
        }
        unsafe { cgl::CGLSetCurrentContext(self.gl_ctx) };
        if self.fbo != 0 {
            unsafe { glDeleteFramebuffers(1, &self.fbo) };
            self.fbo = 0;
        }
        if self.texture != 0 {
            unsafe { glDeleteTextures(1, &self.texture) };
            self.texture = 0;
        }
        if !self.surface.is_null() {
            unsafe { CFRelease(self.surface) };
            self.surface = ptr::null_mut();
        }
        self.width = 0;
        self.height = 0;
    }

    /// Recreates the surface only when the requested size changed, matching the
    /// C++ early return at mpv_addon.cc:961.
    fn ensure_target(&mut self, width: i32, height: i32) -> Result<()> {
        if !self.surface.is_null() && self.width == width && self.height == height {
            return Ok(());
        }
        self.destroy_target();
        unsafe { cgl::CGLSetCurrentContext(self.gl_ctx) };

        let props = unsafe {
            CFDictionaryCreateMutable(
                ptr::null(),
                0,
                &raw const kCFTypeDictionaryKeyCallBacks,
                &raw const kCFTypeDictionaryValueCallBacks,
            )
        };
        if props.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to create IOSurface render target",
            ));
        }

        let set_number = |key: *const c_void, value: i32| {
            let number = unsafe {
                CFNumberCreate(
                    ptr::null(),
                    CF_NUMBER_SINT32,
                    &value as *const _ as *const c_void,
                )
            };
            unsafe { CFDictionarySetValue(props, key, number) };
            unsafe { CFRelease(number) };
        };

        let bytes_per_row = unsafe {
            IOSurfaceAlignProperty(&raw const kIOSurfaceBytesPerRow, (width * 4) as usize)
        } as i32;
        let alloc_size = unsafe {
            IOSurfaceAlignProperty(
                &raw const kIOSurfaceAllocSize,
                (bytes_per_row as usize) * height as usize,
            )
        } as i32;

        set_number(&raw const kIOSurfaceWidth, width);
        set_number(&raw const kIOSurfaceHeight, height);
        set_number(&raw const kIOSurfaceBytesPerElement, 4);
        set_number(&raw const kIOSurfaceBytesPerRow, bytes_per_row);
        set_number(&raw const kIOSurfaceAllocSize, alloc_size);
        set_number(
            &raw const kIOSurfacePixelFormat,
            fourcc(b'B', b'G', b'R', b'A'),
        );

        let surface = unsafe { IOSurfaceCreate(props) };
        unsafe { CFRelease(props) };
        if surface.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to create IOSurface render target",
            ));
        }

        unsafe {
            glGenTextures(1, &mut self.texture);
            glBindTexture(TEXTURE_RECTANGLE, self.texture);
            glTexParameteri(TEXTURE_RECTANGLE, TEXTURE_MIN_FILTER, LINEAR as i32);
            glTexParameteri(TEXTURE_RECTANGLE, TEXTURE_MAG_FILTER, LINEAR as i32);
            glTexParameteri(TEXTURE_RECTANGLE, TEXTURE_WRAP_S, CLAMP_TO_EDGE as i32);
            glTexParameteri(TEXTURE_RECTANGLE, TEXTURE_WRAP_T, CLAMP_TO_EDGE as i32);
        }

        let err = unsafe {
            cgl::CGLTexImageIOSurface2D(
                self.gl_ctx,
                TEXTURE_RECTANGLE,
                RGBA,
                width,
                height,
                BGRA,
                UNSIGNED_INT_8_8_8_8_REV,
                surface,
                0,
            )
        };
        if err != cgl::kCGLNoError {
            unsafe { CFRelease(surface) };
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to create IOSurface render target",
            ));
        }

        unsafe {
            glGenFramebuffers(1, &mut self.fbo);
            glBindFramebuffer(FRAMEBUFFER, self.fbo);
            glFramebufferTexture2D(
                FRAMEBUFFER,
                COLOR_ATTACHMENT0,
                TEXTURE_RECTANGLE,
                self.texture,
                0,
            );
        }
        if unsafe { glCheckFramebufferStatus(FRAMEBUFFER) } != FRAMEBUFFER_COMPLETE {
            unsafe { CFRelease(surface) };
            self.destroy_target();
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to create IOSurface render target",
            ));
        }

        self.surface = surface;
        self.width = width;
        self.height = height;
        Ok(())
    }

    pub fn render(
        &mut self,
        env: &Env,
        width: i32,
        height: i32,
        timestamp: u64,
    ) -> Result<Object<'_>> {
        self.ensure_target(width, height)?;
        unsafe { cgl::CGLSetCurrentContext(self.gl_ctx) };

        let mut fbo = ffi::mpv_opengl_fbo {
            fbo: self.fbo as c_int,
            w: width,
            h: height,
            internal_format: RGBA8 as c_int,
        };
        let mut flip_y: c_int = 1;
        let mut params = [
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_OPENGL_FBO,
                data: &mut fbo as *mut _ as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_FLIP_Y,
                data: &mut flip_y as *mut _ as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_INVALID,
                data: ptr::null_mut(),
            },
        ];

        unsafe { ffi::mpv_render_context_update(self.ctx) };
        let ret = unsafe { ffi::mpv_render_context_render(self.ctx, params.as_mut_ptr()) };
        unsafe { glFlush() };
        if ret < 0 {
            return Err(crate::player::mpv_error(
                "mpv_render_context_render(opengl)",
                ret,
            ));
        }

        render::build_texture_info(
            env,
            format!("{}", self.surface as usize),
            width,
            height,
            timestamp,
            "ioSurface",
            render::pointer_bytes(self.surface as usize),
        )
    }
}

impl Drop for MacSharedTarget {
    fn drop(&mut self) {
        self.destroy_target();
        if !self.gl_ctx.is_null() {
            unsafe { cgl::CGLDestroyContext(self.gl_ctx) };
            self.gl_ctx = ptr::null_mut();
        }
    }
}
