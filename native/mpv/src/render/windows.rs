//! Windows shared-texture target: mpv renders through an OpenGL context that is
//! D3D11-interoperable, the result is copied into an NT-handle-shared texture,
//! and Electron imports that handle.
//!
//! Transcribes mpv_addon.cc:1035-1288 structure for structure, so the review is
//! a diff against the C++ rather than a re-derivation. Two deliberate changes:
//! the COM objects are `windows`-crate smart references (no manual `Release`,
//! which the C++ laddered by hand in `cleanup()`), and `load_mpv_proc` is gone --
//! `mpv_set_wakeup_callback` and `mpv_render_context_set_update_callback` are
//! resolved by the import library like every other mpv symbol, so a missing one
//! is a load-time error instead of a callback that never fires.
//!
//! Nothing here compiles on the Linux dev box. CI on `windows-latest` and the
//! partner's hardware run are the only checks this module has.
#![allow(dead_code)]

use crate::ffi;
use crate::player::mpv_error;
use crate::render;
use napi::bindgen_prelude::*;
use std::ffi::{c_int, c_uint, c_void, CString};
use std::mem::size_of;
use std::ptr;
use windows::core::{Interface, PCSTR};
use windows::Win32::Foundation::{CloseHandle, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::Graphics::Direct3D::{
    D3D_FEATURE_LEVEL, D3D_FEATURE_LEVEL_10_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_11_0,
    D3D_FEATURE_LEVEL_11_1,
};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D,
    D3D11_BIND_RENDER_TARGET, D3D11_BIND_SHADER_RESOURCE, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
    D3D11_RESOURCE_MISC_SHARED, D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX,
    D3D11_RESOURCE_MISC_SHARED_NTHANDLE, D3D11_TEXTURE2D_DESC, D3D11_USAGE_DEFAULT,
};
use windows::Win32::Graphics::Dxgi::Common::DXGI_FORMAT_B8G8R8A8_UNORM;
use windows::Win32::Graphics::Dxgi::{
    IDXGIKeyedMutex, IDXGIResource1, DXGI_SHARED_RESOURCE_READ, DXGI_SHARED_RESOURCE_WRITE,
};
use windows::Win32::Graphics::Gdi::{
    ChoosePixelFormat, GetDC, ReleaseDC, SetPixelFormat, HGDIOBJ, PFD_DOUBLEBUFFER,
    PFD_DRAW_TO_WINDOW, PFD_MAIN_PLANE, PFD_SUPPORT_OPENGL, PFD_TYPE_RGBA, PIXELFORMATDESCRIPTOR,
};
use windows::Win32::Graphics::OpenGL::{
    wglCreateContext, wglDeleteContext, wglGetProcAddress, wglMakeCurrent, HGLRC,
};
use windows::Win32::System::LibraryLoader::{GetModuleHandleA, GetProcAddress, LoadLibraryA};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExA, DefWindowProcA, DestroyWindow, RegisterClassExA, UnregisterClassA, CS_HREDRAW,
    CS_VREDRAW, HWND_MESSAGE, WNDCLASSEXA, WS_OVERLAPPEDWINDOW,
};

const TEXTURE_2D: u32 = 0x0DE1;
const TEXTURE_MIN_FILTER: u32 = 0x2800;
const TEXTURE_MAG_FILTER: u32 = 0x2801;
const TEXTURE_WRAP_S: u32 = 0x2802;
const TEXTURE_WRAP_T: u32 = 0x2803;
const LINEAR: i32 = 0x2601;
const CLAMP_TO_EDGE: i32 = 0x812F;
const FRAMEBUFFER: u32 = 0x8D40;
const COLOR_ATTACHMENT0: u32 = 0x8CE0;
const FRAMEBUFFER_COMPLETE: u32 = 0x8CD5;
const RGBA8: i32 = 0x8058;
/// `WGL_ACCESS_WRITE_DISCARD_NV`, and the only value valid for a
/// render-to-texture interop object.
const WGL_ACCESS_WRITE_DISCARD_NV: u32 = 0x2;

type GenTextures = unsafe extern "system" fn(n: c_int, textures: *mut c_uint);
type DeleteTextures = unsafe extern "system" fn(n: c_int, textures: *const c_uint);
type BindTexture = unsafe extern "system" fn(target: u32, texture: c_uint);
type TexParameteri = unsafe extern "system" fn(target: u32, pname: u32, param: c_int);
type GenFramebuffers = unsafe extern "system" fn(n: c_int, ids: *mut c_uint);
type BindFramebuffer = unsafe extern "system" fn(target: u32, framebuffer: c_uint);
type FramebufferTexture2D = unsafe extern "system" fn(
    target: u32,
    attachment: u32,
    textarget: u32,
    texture: c_uint,
    level: c_int,
);
type CheckFramebufferStatus = unsafe extern "system" fn(target: u32) -> u32;
type DeleteFramebuffers = unsafe extern "system" fn(n: c_int, ids: *const c_uint);
/// GL 1.1 core, so opengl32.dll exports it directly rather than through the
/// extension loader.
type Flush = unsafe extern "system" fn();

type DxOpenDevice = unsafe extern "system" fn(share: *mut c_void) -> *mut c_void;
type DxCloseDevice = unsafe extern "system" fn(device: *mut c_void) -> i32;
type DxRegisterObject = unsafe extern "system" fn(
    device: *mut c_void,
    dxobject: *mut c_void,
    target: u32,
    format: u32,
    access: u32,
) -> *mut c_void;
type DxUnregisterObject =
    unsafe extern "system" fn(device: *mut c_void, object: *mut c_void) -> i32;
type DxLockObjects =
    unsafe extern "system" fn(device: *mut c_void, count: c_int, objects: *mut *mut c_void) -> i32;
type DxUnlockObjects =
    unsafe extern "system" fn(device: *mut c_void, count: c_int, objects: *mut *mut c_void) -> i32;

/// `wglGetProcAddress` returns `NULL` (not `INVALID_HANDLE`) on failure, and
/// values in `1..=3` mean "not an extension here", exactly as the C++ treated
/// them. Nothing in the versions we target returns those sentinels for the
/// functions asked for below.
unsafe fn gl_proc(name: &[u8]) -> *mut c_void {
    let mut buf = name.to_vec();
    buf.push(0);
    let ptr = wglGetProcAddress(PCSTR(buf.as_ptr()));
    if !ptr.is_null() {
        return ptr as *mut c_void;
    }
    // GL 1.1 core lives in opengl32.dll itself, not in the extension loader.
    let mut module = GetModuleHandleA(PCSTR(b"opengl32.dll\0".as_ptr()));
    if module.is_err() {
        if let Ok(loaded) = LoadLibraryA(PCSTR(b"opengl32.dll\0".as_ptr())) {
            module = Ok(loaded);
        }
    }
    match module {
        Ok(handle) => GetProcAddress(handle, PCSTR(buf.as_ptr()))
            .map(|p| p as *mut c_void)
            .unwrap_or(ptr::null_mut()),
        Err(_) => ptr::null_mut(),
    }
}

unsafe fn load<T: Copy>(name: &[u8]) -> Option<T> {
    let raw = gl_proc(name);
    if raw.is_null() {
        return None;
    }
    // SAFETY: `name` selects a specific entry point; T is that signature.
    Some(unsafe { std::mem::transmute_copy::<*mut c_void, T>(&raw) })
}

unsafe extern "system" fn hidden_wnd_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    unsafe { DefWindowProcA(hwnd, msg, wparam, lparam) }
}

struct Gl {
    gen_textures: GenTextures,
    delete_textures: DeleteTextures,
    bind_texture: BindTexture,
    tex_parameteri: TexParameteri,
    gen_framebuffers: GenFramebuffers,
    bind_framebuffer: BindFramebuffer,
    framebuffer_texture2d: FramebufferTexture2D,
    check_framebuffer_status: CheckFramebufferStatus,
    delete_framebuffers: DeleteFramebuffers,
    flush: Flush,
}

struct DxInterop {
    open: DxOpenDevice,
    close: DxCloseDevice,
    register_object: DxRegisterObject,
    unregister_object: DxUnregisterObject,
    lock_objects: DxLockObjects,
    unlock_objects: DxUnlockObjects,
}

pub struct WinSharedTarget {
    ctx: *mut ffi::mpv_render_context,
    hwnd: HWND,
    dc: Option<HDC>,
    gl_ctx: Option<HGLRC>,
    class_registered: bool,
    device: Option<ID3D11Device>,
    devctx: Option<ID3D11DeviceContext>,
    interop_device: *mut c_void,
    interop: Option<DxInterop>,
    gl: Option<Gl>,
    interop_texture: Option<ID3D11Texture2D>,
    export_texture: Option<ID3D11Texture2D>,
    keyed_mutex: Option<IDXGIKeyedMutex>,
    shared_handle: *mut c_void,
    interop_object: *mut c_void,
    gl_texture: c_uint,
    gl_fbo: c_uint,
    width: c_int,
    height: c_int,
    last_error: String,
}

// SAFETY: reached only through `&mut MpvPlayer`; the context is made current on
// the calling thread before every GL or interop operation.
unsafe impl Send for WinSharedTarget {}

fn hex(value: u32) -> String {
    format!("0x{value:x}")
}

impl WinSharedTarget {
    pub fn new(handle: *mut ffi::mpv_handle) -> Result<Self> {
        let mut this = Self {
            ctx: ptr::null_mut(),
            hwnd: HWND::default(),
            dc: None,
            gl_ctx: None,
            class_registered: false,
            device: None,
            devctx: None,
            interop_device: ptr::null_mut(),
            interop: None,
            gl: None,
            interop_texture: None,
            export_texture: None,
            keyed_mutex: None,
            shared_handle: ptr::null_mut(),
            interop_object: ptr::null_mut(),
            gl_texture: 0,
            gl_fbo: 0,
            width: 0,
            height: 0,
            last_error: String::new(),
        };
        this.init_gl()?;
        this.init_d3d()?;
        this.init_mpv(handle)?;
        Ok(this)
    }

    /// Hidden message window + WGL context + extension pointers.
    fn init_gl(&mut self) -> Result<()> {
        let instance = GetModuleHandleA(PCSTR(ptr::null())).unwrap_or_default();
        let class_name: Vec<u8> = b"KurippuMpvHiddenGL\0".to_vec();
        let mut wc = WNDCLASSEXA::default();
        wc.cbSize = size_of::<WNDCLASSEXA>() as u32;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        wc.lpfnWndProc = Some(hidden_wnd_proc);
        wc.hInstance = instance;
        wc.lpszClassName = PCSTR(class_name.as_ptr());
        // RegisterClassExA fails with ERROR_CLASS_ALREADY_EXISTS if a second
        // player is built while the first is alive (mpv-service.ts swaps
        // pipelines by constructing before destroying). That is not an error
        // here; the C++ simply ignored the return value.
        let registered = unsafe { RegisterClassExA(&wc) };
        self.class_registered = registered != 0;

        self.hwnd = unsafe {
            CreateWindowExA(
                Default::default(),
                PCSTR(class_name.as_ptr()),
                PCSTR(ptr::null()),
                WS_OVERLAPPEDWINDOW,
                0,
                0,
                1,
                1,
                HWND_MESSAGE,
                None,
                instance,
                None,
            )
        }
        .map_err(|e| {
            Error::new(
                Status::GenericFailure,
                format!("CreateWindowExA failed: {e}"),
            )
        })?;

        self.dc = unsafe { GetDC(Some(self.hwnd)) }
            .ok_or_else(|| Error::new(Status::GenericFailure, "GetDC failed"))?
            .into();

        let mut pfd = PIXELFORMATDESCRIPTOR::default();
        pfd.nSize = size_of::<PIXELFORMATDESCRIPTOR>() as u32;
        pfd.nVersion = 1;
        pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        pfd.iPixelType = PFD_TYPE_RGBA;
        pfd.cColorBits = 32;
        pfd.cDepthBits = 24;
        pfd.iLayerType = PFD_MAIN_PLANE;

        let dc_for_pf = self.dc.unwrap();
        let format = unsafe { ChoosePixelFormat(Some(dc_for_pf), &pfd) };
        if format == 0 {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to initialize WGL/D3D11 interop context",
            ));
        }
        if unsafe { SetPixelFormat(Some(dc_for_pf), format, &pfd) }.is_err() {
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to initialize WGL/D3D11 interop context",
            ));
        }

        let gl_ctx = unsafe { wglCreateContext(Some(dc_for_pf)) }
            .map_err(|_| Error::new(Status::GenericFailure, "wglCreateContext failed"))?;
        if unsafe { wglMakeCurrent(Some(dc_for_pf), Some(gl_ctx)) }.is_err() {
            unsafe { wglDeleteContext(gl_ctx) };
            return Err(Error::new(
                Status::GenericFailure,
                "Failed to initialize WGL/D3D11 interop context",
            ));
        }
        self.gl_ctx = Some(gl_ctx);

        let gl = Gl {
            gen_textures: load(b"glGenTextures\0").ok_or_else(gl_fail("glGenTextures"))?,
            delete_textures: load(b"glDeleteTextures\0").ok_or_else(gl_fail("glDeleteTextures"))?,
            bind_texture: load(b"glBindTexture\0").ok_or_else(gl_fail("glBindTexture"))?,
            tex_parameteri: load(b"glTexParameteri\0").ok_or_else(gl_fail("glTexParameteri"))?,
            gen_framebuffers: load(b"glGenFramebuffers\0")
                .ok_or_else(gl_fail("glGenFramebuffers"))?,
            bind_framebuffer: load(b"glBindFramebuffer\0")
                .ok_or_else(gl_fail("glBindFramebuffer"))?,
            framebuffer_texture2d: load(b"glFramebufferTexture2D\0")
                .ok_or_else(gl_fail("glFramebufferTexture2D"))?,
            check_framebuffer_status: load(b"glCheckFramebufferStatus\0")
                .ok_or_else(gl_fail("glCheckFramebufferStatus"))?,
            delete_framebuffers: load(b"glDeleteFramebuffers\0")
                .ok_or_else(gl_fail("glDeleteFramebuffers"))?,
            flush: load(b"glFlush\0").ok_or_else(gl_fail("glFlush"))?,
        };
        let interop = DxInterop {
            open: load(b"wglDXOpenDeviceNV\0").ok_or_else(gl_fail("wglDXOpenDeviceNV"))?,
            close: load(b"wglDXCloseDeviceNV\0").ok_or_else(gl_fail("wglDXCloseDeviceNV"))?,
            register_object: load(b"wglDXRegisterObjectNV\0")
                .ok_or_else(gl_fail("wglDXRegisterObjectNV"))?,
            unregister_object: load(b"wglDXUnregisterObjectNV\0")
                .ok_or_else(gl_fail("wglDXUnregisterObjectNV"))?,
            lock_objects: load(b"wglDXLockObjectsNV\0")
                .ok_or_else(gl_fail("wglDXLockObjectsNV"))?,
            unlock_objects: load(b"wglDXUnlockObjectsNV\0")
                .ok_or_else(gl_fail("wglDXUnlockObjectsNV"))?,
        };
        self.gl = Some(gl);
        self.interop = Some(interop);
        Ok(())
    }

    /// Hardware device, WARP fallback, then open the WGL interop device on it.
    fn init_d3d(&mut self) -> Result<()> {
        let levels = [
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL_10_0,
        ];
        let mut device: Option<ID3D11Device> = None;
        let mut context: Option<ID3D11DeviceContext> = None;
        let mut level = D3D_FEATURE_LEVEL_11_0;

        for hardware in [true, false] {
            let driver = if hardware {
                windows::Win32::Graphics::Direct3D11::D3D_DRIVER_TYPE_HARDWARE
            } else {
                windows::Win32::Graphics::Direct3D11::D3D_DRIVER_TYPE_WARP
            };
            let result = unsafe {
                D3D11CreateDevice(
                    None,
                    driver,
                    None,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    Some(&levels),
                    windows::Win32::Graphics::Direct3D11::D3D11_SDK_VERSION,
                    Some(&mut device),
                    Some(&mut level),
                    Some(&mut context),
                )
            };
            if result.is_ok() && device.is_some() {
                break;
            }
            if !hardware {
                return Err(Error::new(
                    Status::GenericFailure,
                    "Failed to create a D3D11 device",
                ));
            }
        }

        let device = device
            .ok_or_else(|| Error::new(Status::GenericFailure, "Failed to create a D3D11 device"))?;
        self.devctx = context;

        let interop = self.interop.as_ref().ok_or_else(|| {
            Error::new(Status::GenericFailure, "WGL interop entry points missing")
        })?;
        let raw: *mut c_void = device.as_raw();
        self.interop_device = unsafe { (interop.open)(raw) };
        if self.interop_device.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "wglDXOpenDeviceNV failed: this GPU lacks WGL_NV_DX_interop",
            ));
        }
        self.device = Some(device);
        Ok(())
    }

    fn init_mpv(&mut self, handle: *mut ffi::mpv_handle) -> Result<()> {
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
        let ret =
            unsafe { ffi::mpv_render_context_create(&mut self.ctx, handle, params.as_mut_ptr()) };
        if ret < 0 {
            return Err(mpv_error("mpv_render_context_create(opengl)", ret));
        }
        Ok(())
    }

    pub fn render_ctx(&self) -> *mut ffi::mpv_render_context {
        self.ctx
    }

    fn make_current(&self) -> Result<()> {
        let (Some(dc), Some(gl_ctx)) = (self.dc, self.gl_ctx) else {
            return Err(Error::new(
                Status::GenericFailure,
                "WGL context is not initialized",
            ));
        };
        if unsafe { wglMakeCurrent(Some(dc), Some(gl_ctx)) }.is_err() {
            return Err(Error::new(Status::GenericFailure, "wglMakeCurrent failed"));
        }
        Ok(())
    }

    /// Drops the two textures and the FBO. COM references release themselves,
    /// which is the whole reason this reads shorter than `destroy_dx_shared_target`.
    fn destroy_target(&mut self) {
        if self.gl_ctx.is_some() && self.dc.is_some() {
            let _ = self.make_current();
            if let Some(gl) = self.gl.as_ref() {
                if !self.interop_object.is_null() && self.interop_device != ptr::null_mut() {
                    unsafe {
                        (self.interop.as_ref().unwrap().unregister_object)(
                            self.interop_device,
                            self.interop_object,
                        )
                    };
                    self.interop_object = ptr::null_mut();
                }
                if self.gl_fbo != 0 {
                    unsafe { (gl.delete_framebuffers)(1, &self.gl_fbo) };
                    self.gl_fbo = 0;
                }
                if self.gl_texture != 0 {
                    unsafe { (gl.delete_textures)(1, &self.gl_texture) };
                    self.gl_texture = 0;
                }
            }
        }
        self.interop_texture = None;
        self.export_texture = None;
        self.keyed_mutex = None;
        if self.shared_handle != ptr::null_mut() {
            unsafe { CloseHandle(windows::Win32::Foundation::HANDLE(self.shared_handle)) };
            self.shared_handle = ptr::null_mut();
        }
        self.width = 0;
        self.height = 0;
    }

    /// Builds the interop texture pair for `width` x `height`, reusing the
    /// existing pair when the size is unchanged (mpv_addon.cc:1185).
    fn ensure_target(&mut self, width: c_int, height: c_int) -> Result<()> {
        if self.export_texture.is_some()
            && self.interop_texture.is_some()
            && self.width == width
            && self.height == height
        {
            return Ok(());
        }
        self.destroy_target();
        self.last_error.clear();
        self.make_current()?;

        let device = self
            .device
            .as_ref()
            .ok_or_else(|| Error::new(Status::GenericFailure, "D3D11 device is not initialized"))?;

        let desc = D3D11_TEXTURE2D_DESC {
            Width: width as u32,
            Height: height as u32,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: windows::Win32::Graphics::Dxgi::Common::DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_DEFAULT,
            BindFlags: (D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE).bits(),
            CPUAccessFlags: Default::default(),
            MiscFlags: D3D11_RESOURCE_MISC_SHARED.bits(),
        };

        let interop_texture = unsafe { device.CreateTexture2D(&desc) }.map_err(|e| {
            self.last_error = format!("CreateTexture2D(interop) failed: {}", hex(e.0 as u32));
            Error::new(Status::GenericFailure, self.last_error.clone())
        })?;

        let export_desc = D3D11_TEXTURE2D_DESC {
            MiscFlags: (D3D11_RESOURCE_MISC_SHARED_NTHANDLE
                | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX)
                .bits(),
            ..desc
        };
        let export_texture = match unsafe { device.CreateTexture2D(&export_desc) } {
            Ok(t) => t,
            Err(e) => {
                self.last_error = format!("CreateTexture2D(export) failed: {}", hex(e.0 as u32));
                return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
            }
        };

        let keyed_mutex: IDXGIKeyedMutex = match export_texture.query_interface::<IDXGIKeyedMutex>()
        {
            Ok(m) => m,
            Err(e) => {
                self.last_error = format!(
                    "QueryInterface(IDXGIKeyedMutex) failed: {}",
                    hex(e.0 as u32)
                );
                return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
            }
        };

        let dxgi_resource: IDXGIResource1 = match export_texture.query_interface::<IDXGIResource1>()
        {
            Ok(r) => r,
            Err(e) => {
                self.last_error =
                    format!("QueryInterface(IDXGIResource1) failed: {}", hex(e.0 as u32));
                return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
            }
        };

        let shared = match unsafe {
            dxgi_resource.CreateSharedHandle(
                None,
                (DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE).bits(),
                None,
            )
        } {
            Ok(h) => h,
            Err(e) => {
                self.last_error = format!("CreateSharedHandle failed: {}", hex(e.0 as u32));
                self.destroy_target();
                return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
            }
        };

        let gl = self.gl.as_ref().unwrap();
        unsafe {
            (gl.gen_textures)(1, &mut self.gl_texture as *mut c_uint);
            (gl.bind_texture)(TEXTURE_2D, self.gl_texture);
            (gl.tex_parameteri)(TEXTURE_2D, TEXTURE_MIN_FILTER, LINEAR);
            (gl.tex_parameteri)(TEXTURE_2D, TEXTURE_MAG_FILTER, LINEAR);
            (gl.tex_parameteri)(TEXTURE_2D, TEXTURE_WRAP_S, CLAMP_TO_EDGE);
            (gl.tex_parameteri)(TEXTURE_2D, TEXTURE_WRAP_T, CLAMP_TO_EDGE);
        }

        let interop = self.interop.as_ref().unwrap();
        let interop_raw: *mut c_void = interop_texture.as_raw();
        self.interop_object = unsafe {
            (interop.register_object)(
                self.interop_device,
                interop_raw,
                TEXTURE_2D,
                0, // GL_TEXTURE_2D target implies the format
                WGL_ACCESS_WRITE_DISCARD_NV,
            )
        };
        if self.interop_object.is_null() {
            self.last_error = format!(
                "wglDXRegisterObjectNV failed: {}",
                hex(unsafe { windows::Win32::Foundation::GetLastError().0 } as u32)
            );
            self.destroy_target();
            return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
        }

        let mut objects = [self.interop_object];
        if unsafe { (interop.lock_objects)(self.interop_device, 1, objects.as_mut_ptr()) } == 0 {
            self.last_error = "wglDXLockObjectsNV failed".to_string();
            self.destroy_target();
            return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
        }
        unsafe {
            (gl.gen_framebuffers)(1, &mut self.gl_fbo as *mut c_uint);
            (gl.bind_framebuffer)(FRAMEBUFFER, self.gl_fbo);
            (gl.framebuffer_texture2d)(
                FRAMEBUFFER,
                COLOR_ATTACHMENT0,
                TEXTURE_2D,
                self.gl_texture,
                0,
            );
        }
        let complete =
            unsafe { (gl.check_framebuffer_status)(FRAMEBUFFER) } == FRAMEBUFFER_COMPLETE;
        unsafe { (interop.unlock_objects)(self.interop_device, 1, objects.as_mut_ptr()) };
        if !complete {
            self.last_error =
                "OpenGL framebuffer for WGL/D3D11 interop texture is incomplete".to_string();
            self.destroy_target();
            return Err(Error::new(Status::GenericFailure, self.last_error.clone()));
        }

        self.interop_texture = Some(interop_texture);
        self.export_texture = Some(export_texture);
        self.keyed_mutex = Some(keyed_mutex);
        self.shared_handle = shared.0 as *mut c_void;
        self.width = width;
        self.height = height;
        Ok(())
    }

    pub fn render<'env>(
        &mut self,
        env: &'env Env,
        width: c_int,
        height: c_int,
        timestamp: u64,
    ) -> Result<Object<'env>> {
        self.ensure_target(width, height)?;
        self.make_current()?;

        let interop = self.interop.as_ref().unwrap();
        let gl = self.gl.as_ref().unwrap();
        let mut objects = [self.interop_object];
        if unsafe { (interop.lock_objects)(self.interop_device, 1, objects.as_mut_ptr()) } == 0 {
            return Err(Error::new(
                Status::GenericFailure,
                "wglDXLockObjectsNV failed",
            ));
        }
        unsafe { (gl.bind_framebuffer)(FRAMEBUFFER, self.gl_fbo) };

        let mut fbo = ffi::mpv_opengl_fbo {
            fbo: self.gl_fbo as c_int,
            w: width,
            h: height,
            internal_format: RGBA8,
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
        unsafe { (gl.flush)() };
        let unlocked =
            unsafe { (interop.unlock_objects)(self.interop_device, 1, objects.as_mut_ptr()) } != 0;

        // The C++ reported a render failure and an unlock failure separately,
        // and never skipped the unlock to do so. Keep both.
        if ret < 0 {
            return Err(mpv_error("mpv_render_context_render(opengl)", ret));
        }
        if !unlocked {
            return Err(Error::new(
                Status::GenericFailure,
                "wglDXUnlockObjectsNV failed",
            ));
        }

        let mutex = self
            .keyed_mutex
            .as_ref()
            .ok_or_else(|| Error::new(Status::GenericFailure, "keyed mutex is not initialized"))?;
        if unsafe { mutex.AcquireSync(0, 1000) }.is_err() {
            return Err(Error::new(
                Status::GenericFailure,
                "IDXGIKeyedMutex::AcquireSync failed",
            ));
        }
        let (Some(interop_texture), Some(export_texture)) =
            (self.interop_texture.as_ref(), self.export_texture.as_ref())
        else {
            unsafe { mutex.ReleaseSync(0) };
            return Err(Error::new(
                Status::GenericFailure,
                "interop textures are not initialized",
            ));
        };
        if let Some(context) = self.devctx.as_ref() {
            unsafe { context.CopyResource(export_texture, interop_texture) };
            context.Flush();
        }
        if unsafe { mutex.ReleaseSync(0) }.is_err() {
            return Err(Error::new(
                Status::GenericFailure,
                "IDXGIKeyedMutex::ReleaseSync failed",
            ));
        }

        render::build_texture_info(
            env,
            format!("{}", self.shared_handle as usize),
            width,
            height,
            timestamp,
            "ntHandle",
            render::pointer_bytes(self.shared_handle as usize),
        )
    }
}

fn gl_fail(name: &str) -> impl FnOnce() -> Error {
    let name = name.to_string();
    move || {
        Error::new(
            Status::GenericFailure,
            format!("Failed to initialize WGL/D3D11 interop context: {name} unavailable"),
        )
    }
}

/// mpv resolves GL entry points through this callback.
unsafe extern "C" fn get_proc_address(
    _ctx: *mut c_void,
    name: *const std::os::raw::c_char,
) -> *mut c_void {
    let c_name = unsafe { std::ffi::CStr::from_ptr(name) };
    let mut bytes = c_name.to_bytes().to_vec();
    bytes.push(0);
    let raw = unsafe { wglGetProcAddress(PCSTR(bytes.as_ptr())) };
    if !raw.is_null() && (raw as isize) > 3 {
        return raw as *mut c_void;
    }
    if let Ok(module) = unsafe { GetModuleHandleA(PCSTR(b"opengl32.dll\0".as_ptr())) } {
        if let Some(proc) = unsafe { GetProcAddress(module, PCSTR(bytes.as_ptr())) } {
            return proc as *mut c_void;
        }
    }
    ptr::null_mut()
}

impl Drop for WinSharedTarget {
    fn drop(&mut self) {
        self.destroy_target();
        if self.interop_device != ptr::null_mut() {
            if let Some(interop) = self.interop.as_ref() {
                unsafe { (interop.close)(self.interop_device) };
            }
            self.interop_device = ptr::null_mut();
        }
        if let (Some(dc), Some(gl_ctx)) = (self.dc, self.gl_ctx) {
            unsafe { wglMakeCurrent(None, None) };
            let _ = unsafe { wglDeleteContext(gl_ctx) };
            unsafe { ReleaseDC(Some(self.hwnd), Some(dc)) };
        }
        if !self.hwnd.is_invalid() {
            unsafe { DestroyWindow(self.hwnd) };
        }
        if self.class_registered {
            let instance = GetModuleHandleA(PCSTR(ptr::null())).unwrap_or_default();
            unsafe { UnregisterClassA(PCSTR(b"KurippuMpvHiddenGL\0".as_ptr()), Some(instance)) };
        }
        // device and devctx drop their own COM references here, in this order:
        // context before device, as D3D11 requires.
        self.devctx = None;
        self.device = None;
    }
}
