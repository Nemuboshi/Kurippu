//! The `MpvPlayer` class: libmpv lifecycle, the fifteen methods the main
//! process calls, and the teardown order that keeps mpv's callbacks from
//! outliving the object they point at.

use crate::callbacks::CallbackHub;
use crate::ffi;
use crate::render::software::SoftwareTarget;
use crate::scale;
use crate::value;
use napi::bindgen_prelude::*;
use napi_derive::napi;
use std::ffi::{c_void, CString};
use std::os::raw::{c_char, c_int};
use std::sync::Arc;

/// Formats an mpv error code the way `throw_mpv_error` did
/// (`mpv_addon.cc:78-82`), so the strings reaching the console are unchanged.
pub fn mpv_error(action: &str, code: c_int) -> Error {
    Error::new(
        Status::GenericFailure,
        format!("{action} failed: {} ({code})", ffi::error_text(code)),
    )
}

#[derive(Clone, Copy)]
struct Handle(*mut ffi::mpv_handle);
#[derive(Clone, Copy)]
struct RenderCtx(*mut ffi::mpv_render_context);

// SAFETY: both are reached only through `&MpvPlayer` / `&mut MpvPlayer`, and
// napi gives a wrapped object to one thread at a time. libmpv handles are
// themselves thread-safe, which is what lets mpv's own threads call back in.
unsafe impl Send for Handle {}
unsafe impl Send for RenderCtx {}

#[napi(js_name = "MpvPlayer")]
pub struct MpvPlayer {
    // Order is the teardown contract, mirrored explicitly in `teardown`:
    // render context, then handle, then the hub both of them borrow.
    render_ctx: Option<RenderCtx>,
    handle: Option<Handle>,
    hub: Option<Arc<CallbackHub>>,
    software: Option<SoftwareTarget>,
    mode: String,
    deinterlace: bool,
    // Consumed by the shared-texture targets in Tasks 7 and 8.
    #[allow(dead_code)]
    timestamp: u64,
    next_observer: u64,
}

fn cstring(value: impl AsRef<str>) -> CString {
    CString::new(value.as_ref().to_owned()).unwrap_or_else(|_| CString::new("").unwrap())
}

impl MpvPlayer {
    fn handle(&self) -> Result<*mut ffi::mpv_handle> {
        self.handle
            .map(|h| h.0)
            .filter(|p| !p.is_null())
            .ok_or_else(|| Error::new(Status::GenericFailure, "player is destroyed"))
    }

    fn render_ctx(&self) -> Result<*mut ffi::mpv_render_context> {
        self.render_ctx
            .map(|c| c.0)
            .filter(|p| !p.is_null())
            .ok_or_else(|| Error::new(Status::GenericFailure, "render context is not initialized"))
    }

    fn command(&self, args: &[&str]) -> Result<c_int> {
        let owned: Vec<CString> = args.iter().copied().map(cstring).collect();
        let mut ptrs: Vec<*const c_char> = owned.iter().map(|c| c.as_ptr()).collect();
        ptrs.push(std::ptr::null());
        let code = unsafe { ffi::mpv_command(self.handle()?, ptrs.as_mut_ptr()) };
        Ok(code)
    }

    /// Detaches from libmpv, then stops answering callbacks.
    ///
    /// This is the ordering the C++ encoded as a hand-guarded ladder in
    /// `cleanup()` (mpv_addon.cc:853-912). Idempotent: `destroy()` may be
    /// called twice, and `Drop` runs it again after an explicit call.
    fn teardown(&mut self) {
        self.software = None;
        if let Some(hub) = self.hub.take() {
            if let Some(ctx) = self.render_ctx.take() {
                hub.clear_render_update(ctx.0);
                unsafe { ffi::mpv_render_context_free(ctx.0) };
            }
            if let Some(handle) = self.handle.take() {
                hub.clear_mpv_wakeup(handle.0);
                // Blocks until any in-flight callback has returned.
                unsafe { ffi::mpv_terminate_destroy(handle.0) };
            }
            hub.shutdown();
        }
        self.render_ctx = None;
        self.handle = None;
    }
}

#[napi]
impl MpvPlayer {
    #[napi(constructor)]
    pub fn new(options: Option<Object>) -> Result<Self> {
        let mode = options
            .as_ref()
            .and_then(|o| o.get::<String>("mode").ok().flatten())
            .unwrap_or_else(|| String::from("software"));

        let handle = unsafe { ffi::mpv_create() };
        if handle.is_null() {
            return Err(Error::new(Status::GenericFailure, "mpv_create failed"));
        }
        let handle = Handle(handle);

        let set = |key: &str, val: &str| {
            let (k, v) = (cstring(key), cstring(val));
            unsafe { ffi::mpv_set_option_string(handle.0, k.as_ptr(), v.as_ptr()) };
        };
        set("terminal", "no");
        set("msg-level", "all=warn");
        set("input-default-bindings", "no");
        set("audio-display", "no");
        set("sid", "no");
        set("pause", "yes");
        set("keep-open", "yes");
        let hwdec = std::env::var("MPV_HWDEC").unwrap_or_default();
        set(
            "hwdec",
            if !hwdec.is_empty() {
                hwdec.as_str()
            } else if cfg!(windows) {
                "no"
            } else {
                "auto-safe"
            },
        );
        set("sw-fast", "yes");
        set("vo", "libmpv");

        let hub = CallbackHub::new();
        hub.install_mpv_wakeup(handle.0);

        let ret = unsafe { ffi::mpv_initialize(handle.0) };
        if ret < 0 {
            unsafe { ffi::mpv_terminate_destroy(handle.0) };
            return Err(mpv_error("mpv_initialize", ret));
        }

        let mut player = Self {
            render_ctx: None,
            handle: Some(handle),
            hub: Some(hub),
            software: None,
            mode,
            deinterlace: false,
            timestamp: 0,
            next_observer: 1,
        };

        for (name, format) in [
            ("time-pos", ffi::format::MPV_FORMAT_DOUBLE),
            ("duration", ffi::format::MPV_FORMAT_DOUBLE),
            ("pause", ffi::format::MPV_FORMAT_FLAG),
            ("eof-reached", ffi::format::MPV_FORMAT_FLAG),
            ("width", ffi::format::MPV_FORMAT_INT64),
            ("height", ffi::format::MPV_FORMAT_INT64),
            ("video-codec", ffi::format::MPV_FORMAT_STRING),
            ("container-fps", ffi::format::MPV_FORMAT_DOUBLE),
        ] {
            let key = cstring(name);
            let id = player.next_observer;
            player.next_observer += 1;
            unsafe { ffi::mpv_observe_property(handle.0, id, key.as_ptr(), format) };
        }

        player.init_render_context()?;
        Ok(player)
    }

    fn init_render_context(&mut self) -> Result<()> {
        // Tasks 7 and 8 replace this branch with the macOS and Windows
        // targets. Everywhere else keeps the message the C++ produced.
        if self.mode == "shared-texture" {
            return Err(Error::new(
                Status::GenericFailure,
                "shared-texture mode is only implemented on macOS and Windows",
            ));
        }

        let mut ctx: *mut ffi::mpv_render_context = std::ptr::null_mut();
        let api = cstring(ffi::API_TYPE_SW);
        let mut params = [
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_API_TYPE,
                data: api.as_ptr() as *mut c_void,
            },
            ffi::mpv_render_param {
                type_: ffi::render_param::MPV_RENDER_PARAM_INVALID,
                data: std::ptr::null_mut(),
            },
        ];
        let ret = unsafe {
            ffi::mpv_render_context_create(&mut ctx, self.handle()?, params.as_mut_ptr())
        };
        if ret < 0 {
            return Err(mpv_error("mpv_render_context_create", ret));
        }
        if ctx.is_null() {
            return Err(Error::new(
                Status::GenericFailure,
                "mpv_render_context_create failed: ok (0)",
            ));
        }
        self.render_ctx = Some(RenderCtx(ctx));
        self.software = Some(SoftwareTarget::new(ctx));
        if let Some(hub) = self.hub.as_ref() {
            hub.install_render_update(ctx);
        }
        Ok(())
    }

    #[napi]
    pub fn open(&self, path: String) -> Result<()> {
        let ret = self.command(&["loadfile", &path, "replace"])?;
        if ret < 0 {
            return Err(mpv_error("loadfile", ret));
        }
        Ok(())
    }

    #[napi]
    pub fn play(&self) -> Result<()> {
        self.set_pause(0)
    }

    #[napi]
    pub fn pause(&self) -> Result<()> {
        self.set_pause(1)
    }

    fn set_pause(&self, flag: c_int) -> Result<()> {
        let key = cstring("pause");
        let ret = unsafe {
            ffi::mpv_set_property(
                self.handle()?,
                key.as_ptr(),
                ffi::format::MPV_FORMAT_FLAG,
                &raw const flag as *mut c_void,
            )
        };
        if ret < 0 {
            return Err(mpv_error(if flag == 0 { "play" } else { "pause" }, ret));
        }
        Ok(())
    }

    #[napi]
    pub fn stop(&self) -> Result<()> {
        let ret = self.command(&["stop"])?;
        if ret < 0 {
            return Err(mpv_error("stop", ret));
        }
        Ok(())
    }

    #[napi]
    pub fn seek(&self, seconds: f64) -> Result<()> {
        let arg = scale::seek_arg(seconds);
        let ret = self.command(&["seek", &arg, "absolute"])?;
        if ret < 0 {
            return Err(mpv_error("seek", ret));
        }
        Ok(())
    }

    #[napi(js_name = "setVolume")]
    pub fn set_volume(&self, value: f64) -> Result<()> {
        let volume = value.clamp(0.0, 100.0);
        let key = cstring("volume");
        let ret = unsafe {
            ffi::mpv_set_property(
                self.handle()?,
                key.as_ptr(),
                ffi::format::MPV_FORMAT_DOUBLE,
                &raw const volume as *mut c_void,
            )
        };
        if ret < 0 {
            return Err(mpv_error("setVolume", ret));
        }
        Ok(())
    }

    #[napi(js_name = "setTrack")]
    pub fn set_track(&self, kind: String, id: Option<f64>) -> Result<()> {
        let property = scale::track_property(&kind)
            .map_err(|_| Error::new(Status::InvalidArg, "Unknown track kind"))?;
        let value = scale::track_id_arg(id)
            .ok_or_else(|| Error::new(Status::InvalidArg, "track id must be an integer or null"))?;
        let ret = self.command(&["set", property, &value])?;
        if ret < 0 {
            return Err(mpv_error("set track", ret));
        }
        Ok(())
    }

    #[napi(js_name = "setDeinterlace")]
    pub fn set_deinterlace(&mut self, on: bool) -> Result<()> {
        if on == self.deinterlace {
            return Ok(());
        }
        if self.deinterlace {
            let removed = self.command(&["vf-remove", "yadif"])?;
            if !scale::vf_removal_is_ok(removed) {
                return Err(mpv_error("vf-remove", removed));
            }
        }
        if on {
            let added = self.command(&["vf-add", "yadif"])?;
            if added < 0 {
                return Err(mpv_error("vf-add", added));
            }
        }
        self.deinterlace = on;
        Ok(())
    }

    #[napi(js_name = "getProperty")]
    pub fn get_property<'env>(
        &self,
        env: &'env Env,
        name: String,
    ) -> Result<Option<Unknown<'env>>> {
        let handle = self.handle()?;
        let key = cstring(&name);
        Ok(match value::probe_strategy(&name) {
            value::Probe::AsNode => {
                let mut node =
                    unsafe { std::mem::MaybeUninit::<ffi::mpv_node>::zeroed().assume_init() };
                let ret = unsafe {
                    ffi::mpv_get_property(
                        handle,
                        key.as_ptr(),
                        ffi::format::MPV_FORMAT_NODE,
                        &mut node as *mut _ as *mut c_void,
                    )
                };
                if ret < 0 {
                    return Ok(None);
                }
                let converted = unsafe { value::node_to_js(env, &node) };
                unsafe { ffi::mpv_free_node_contents(&mut node) };
                converted?
            }
            value::Probe::DoubleThenInt64 => {
                let mut number: f64 = 0.0;
                if unsafe {
                    ffi::mpv_get_property(
                        handle,
                        key.as_ptr(),
                        ffi::format::MPV_FORMAT_DOUBLE,
                        &mut number as *mut _ as *mut c_void,
                    )
                } >= 0
                {
                    Some(number.into_unknown(env)?)
                } else {
                    let mut integer: i64 = 0;
                    if unsafe {
                        ffi::mpv_get_property(
                            handle,
                            key.as_ptr(),
                            ffi::format::MPV_FORMAT_INT64,
                            &mut integer as *mut _ as *mut c_void,
                        )
                    } >= 0
                    {
                        Some((integer as f64).into_unknown(env)?)
                    } else {
                        None
                    }
                }
            }
            value::Probe::StringThenNumeric => {
                // Tried first, and it succeeds for numbers too: this is why
                // getProperty("width") answers with a string.
                let raw = unsafe { ffi::mpv_get_property_string(handle, key.as_ptr()) };
                if !raw.is_null() {
                    let out = Some(cstring_to_unknown(env, raw)?);
                    unsafe { ffi::mpv_free(raw as *mut c_void) };
                    return Ok(out);
                }
                let mut number: f64 = 0.0;
                if unsafe {
                    ffi::mpv_get_property(
                        handle,
                        key.as_ptr(),
                        ffi::format::MPV_FORMAT_DOUBLE,
                        &mut number as *mut _ as *mut c_void,
                    )
                } >= 0
                {
                    Some(number.into_unknown(env)?)
                } else {
                    let mut flag: c_int = 0;
                    if unsafe {
                        ffi::mpv_get_property(
                            handle,
                            key.as_ptr(),
                            ffi::format::MPV_FORMAT_FLAG,
                            &mut flag as *mut _ as *mut c_void,
                        )
                    } >= 0
                    {
                        Some((flag != 0).into_unknown(env)?)
                    } else {
                        None
                    }
                }
            }
        })
    }

    #[napi(js_name = "pollEvents")]
    pub fn poll_events<'env>(&self, env: &'env Env) -> Result<Vec<Option<Unknown<'env>>>> {
        let handle = self.handle()?;
        let mut events: Vec<Option<Unknown>> = Vec::new();
        loop {
            let event = unsafe { ffi::mpv_wait_event(handle, 0.0) };
            if event.is_null() {
                break;
            }
            let event = unsafe { &*event };
            if event.event_id == ffi::event::MPV_EVENT_NONE {
                break;
            }
            let mut item = Object::new(env)?;
            item.set("id", event.event_id)?;
            let name_ptr = unsafe { ffi::mpv_event_name(event.event_id) };
            if !name_ptr.is_null() {
                item.set(
                    "type",
                    unsafe { std::ffi::CStr::from_ptr(name_ptr) }
                        .to_string_lossy()
                        .to_string(),
                )?;
            }
            if event.error < 0 {
                item.set("error", ffi::error_text(event.error))?;
            }
            if event.event_id == ffi::event::MPV_EVENT_PROPERTY_CHANGE && !event.data.is_null() {
                let prop = unsafe { &*(event.data as *const ffi::mpv_event_property) };
                let prop_name = if prop.name.is_null() {
                    String::new()
                } else {
                    unsafe { std::ffi::CStr::from_ptr(prop.name) }
                        .to_string_lossy()
                        .into_owned()
                };
                item.set("name", prop_name)?;
                let data = prop_property_data(env, prop)?;
                item.set("data", data)?;
            }
            events.push(Some(item.into_unknown(env)?));
        }
        Ok(events)
    }

    #[napi(js_name = "setUpdateCallback")]
    pub fn set_update_callback(&self, callback: Option<Function<'static>>) -> Result<()> {
        self.hub
            .as_ref()
            .ok_or_else(|| Error::new(Status::GenericFailure, "player is destroyed"))?
            .set_render_update(callback)
    }

    #[napi(js_name = "setEventCallback")]
    pub fn set_event_callback(&self, callback: Option<Function<'static>>) -> Result<()> {
        self.hub
            .as_ref()
            .ok_or_else(|| Error::new(Status::GenericFailure, "player is destroyed"))?
            .set_wakeup(callback)
    }

    #[napi(js_name = "renderFrame")]
    pub fn render_frame<'env>(
        &mut self,
        env: &'env Env,
        width: Option<f64>,
        height: Option<f64>,
    ) -> Result<Object<'env>> {
        if self.mode == "shared-texture" {
            return Err(Error::new(
                Status::GenericFailure,
                "renderFrame is unavailable in shared-texture mode",
            ));
        }
        let _ = self.render_ctx()?;
        let (width, height) = scale::clamp_software(width, height);
        let target = self.software.as_ref().ok_or_else(|| {
            Error::new(Status::GenericFailure, "render context is not initialized")
        })?;
        target.render(env, width, height)
    }

    #[napi(js_name = "renderSharedTexture")]
    pub fn render_shared_texture<'env>(
        &mut self,
        env: &'env Env,
        width: Option<f64>,
        height: Option<f64>,
    ) -> Result<Object<'env>> {
        if self.mode != "shared-texture" {
            return Err(Error::new(
                Status::GenericFailure,
                "renderSharedTexture is only available in shared-texture mode",
            ));
        }
        // Tasks 7 and 8 route this to the platform target once they exist; the
        // constructor already rejects shared-texture mode on other platforms,
        // so this arm is unreachable there.
        let _ = (env, width, height);
        Err(Error::new(
            Status::GenericFailure,
            "renderSharedTexture is only implemented on macOS and Windows",
        ))
    }

    #[napi]
    pub fn destroy(&mut self) {
        self.teardown();
    }
}

impl Drop for MpvPlayer {
    fn drop(&mut self) {
        self.teardown();
    }
}

fn cstring_to_unknown<'env>(env: &'env Env, raw: *mut c_char) -> Result<Unknown<'env>> {
    let text = unsafe { std::ffi::CStr::from_ptr(raw) }
        .to_string_lossy()
        .into_owned();
    env.create_string_from_std(text)?.into_unknown(env)
}

/// Property-change payloads, transcribed from mpv_addon.cc:814-840.
fn prop_property_data<'env>(
    env: &'env Env,
    prop: &ffi::mpv_event_property,
) -> Result<Option<Unknown<'env>>> {
    if prop.data.is_null() {
        return Ok(None);
    }
    Ok(match prop.format {
        ffi::format::MPV_FORMAT_FLAG => {
            Some((unsafe { *(prop.data as *const c_int) } != 0).into_unknown(env)?)
        }
        ffi::format::MPV_FORMAT_INT64 => {
            Some((unsafe { *(prop.data as *const i64) } as f64).into_unknown(env)?)
        }
        ffi::format::MPV_FORMAT_DOUBLE => {
            Some((unsafe { *(prop.data as *const f64) }).into_unknown(env)?)
        }
        ffi::format::MPV_FORMAT_STRING => {
            let value = unsafe { *(prop.data as *const *const c_char) };
            let text = if value.is_null() {
                String::new()
            } else {
                unsafe { std::ffi::CStr::from_ptr(value) }
                    .to_string_lossy()
                    .into_owned()
            };
            Some(env.create_string_from_std(text)?.into_unknown(env)?)
        }
        _ => None,
    })
}
