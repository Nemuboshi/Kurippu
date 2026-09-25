// Consumed by `player.rs` (Task 6). Until then every entry point here is
// unreachable from the crate root, which is a dead-code warning, not a defect.
#![allow(dead_code)]

use crate::ffi;
use napi::bindgen_prelude::*;
use napi::threadsafe_function::{ThreadsafeFunction, ThreadsafeFunctionCallMode};
use std::ffi::c_void;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

/// The concrete TSFN shape produced by [`wakeup_tsfn`] below. Written out once
/// so the field types and the builder cannot drift apart.
type WakeupTsfn = ThreadsafeFunction<(), Unknown<'static>, (), Status, false, false, 1>;

fn build(slot: Function<'static>) -> Result<WakeupTsfn> {
    slot.build_threadsafe_function::<()>()
        .max_queue_size::<1>()
        .callee_handled::<false>()
        .weak::<false>()
        .build_callback::<(), _>(|_| Ok(()))
}

/// The only object reachable from libmpv's `extern "C"` callbacks.
///
/// mpv documents the wakeup callback as callable from any thread and does not
/// serialise it, and the render-update callback arrives from mpv's render
/// thread. Both therefore land on a hub that is shared across threads, which is
/// why the two TSFNs sit behind `Mutex`es in the one `Arc` that the mpv handle
/// and the render context both borrow.
///
/// This replaces the C++ design, which passed a raw `this` to libmpv and
/// guarded it with `alive_ : std::atomic<bool>` (`mpv_addon.cc:230,343-355`).
/// That guard cannot work: a callback firing after the object is freed reads
/// `alive_` out of memory that was already released. The flag is kept here
/// because it is cheap and avoids taking a lock during teardown, but
/// correctness comes from the ownership ordering in `player.rs`, not from it.
pub struct CallbackHub {
    alive: AtomicBool,
    wakeup: Mutex<Option<WakeupTsfn>>,
    render_update: Mutex<Option<WakeupTsfn>>,
}

impl CallbackHub {
    pub fn new() -> Arc<Self> {
        Arc::new(Self {
            alive: AtomicBool::new(true),
            wakeup: Mutex::new(None),
            render_update: Mutex::new(None),
        })
    }

    pub fn is_alive(&self) -> bool {
        self.alive.load(Ordering::Acquire)
    }

    fn install(slot: &Mutex<Option<WakeupTsfn>>, func: Option<Function<'static>>) -> Result<()> {
        // Passing no function clears the callback, matching the C++
        // `setEventCallback()` / `setUpdateCallback()` called with no argument.
        *slot.lock().unwrap() = func.map(build).transpose()?;
        Ok(())
    }

    pub fn set_wakeup(&self, func: Option<Function<'static>>) -> Result<()> {
        Self::install(&self.wakeup, func)
    }

    pub fn set_render_update(&self, func: Option<Function<'static>>) -> Result<()> {
        Self::install(&self.render_update, func)
    }

    /// Hands the hub to libmpv as the wakeup context.
    ///
    /// `Arc::as_ptr` borrows without incrementing, and the trampoline pairs its
    /// `Arc::from_raw` with `Arc::into_raw` so the refcount is left exactly
    /// where it was. Safety therefore rests on a single ordering, enforced by
    /// `MpvPlayer::teardown`: the mpv registration is cleared before the
    /// owning `Arc` can drop.
    pub fn install_mpv_wakeup(self: &Arc<Self>, handle: *mut ffi::mpv_handle) {
        let ctx = Arc::as_ptr(self) as *mut c_void;
        unsafe { ffi::mpv_set_wakeup_callback(handle, Some(wakeup_trampoline), ctx) };
    }

    pub fn install_render_update(self: &Arc<Self>, render_ctx: *mut ffi::mpv_render_context) {
        let ctx = Arc::as_ptr(self) as *mut c_void;
        unsafe {
            ffi::mpv_render_context_set_update_callback(render_ctx, Some(render_trampoline), ctx);
        };
    }

    pub fn clear_mpv_wakeup(&self, handle: *mut ffi::mpv_handle) {
        unsafe { ffi::mpv_set_wakeup_callback(handle, None, std::ptr::null_mut()) };
    }

    pub fn clear_render_update(&self, render_ctx: *mut ffi::mpv_render_context) {
        unsafe {
            ffi::mpv_render_context_set_update_callback(render_ctx, None, std::ptr::null_mut());
        };
    }

    /// Drop the JS callbacks before publishing death, never after.
    pub fn shutdown(&self) {
        *self.wakeup.lock().unwrap() = None;
        *self.render_update.lock().unwrap() = None;
        self.alive.store(false, Ordering::Release);
    }

    fn fire(&self, slot: &Mutex<Option<WakeupTsfn>>) {
        if !self.is_alive() {
            return;
        }
        if let Some(tsfn) = slot.lock().unwrap().as_ref() {
            // NonBlocking mirrors the C++ `ThreadSafeFunction::NonBlockingCall`:
            // an mpv thread must never stall behind the JS event loop. A full
            // queue is not an error here, because the signal coalesces -- the
            // JS side drains every event with `pollEvents()` when it runs.
            let _ = tsfn.call((), ThreadsafeFunctionCallMode::NonBlocking);
        }
    }
}

unsafe extern "C" fn wakeup_trampoline(ctx: *mut c_void) {
    let hub = unsafe { Arc::from_raw(ctx as *const CallbackHub) };
    hub.fire(&hub.wakeup);
    let _ = Arc::into_raw(hub);
}

unsafe extern "C" fn render_trampoline(ctx: *mut c_void) {
    let hub = unsafe { Arc::from_raw(ctx as *const CallbackHub) };
    hub.fire(&hub.render_update);
    let _ = Arc::into_raw(hub);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assert_send_sync<T: Send + Sync>() {}

    #[test]
    fn hub_is_shareable_across_threads() {
        assert_send_sync::<CallbackHub>();
        assert_send_sync::<Arc<CallbackHub>>();
    }

    /// The refcount discipline the trampolines depend on: borrowing the Arc to
    /// service a callback must not be what frees the hub, and must not leak a
    /// reference either. The address crosses the thread as a `usize` because a
    /// raw pointer is not `Send` -- the same hazard the production trampolines
    /// are written around.
    #[test]
    fn a_borrowed_hub_neither_leaks_nor_frees_the_last_reference() {
        let hub = CallbackHub::new();
        assert_eq!(Arc::strong_count(&hub), 1);
        let addr = Arc::as_ptr(&hub) as usize;
        std::thread::spawn(move || {
            let borrowed = unsafe { Arc::from_raw(addr as *const CallbackHub) };
            assert!(borrowed.is_alive());
            borrowed.fire(&borrowed.wakeup);
            let _ = Arc::into_raw(borrowed);
        })
        .join()
        .unwrap();
        assert_eq!(Arc::strong_count(&hub), 1, "a trampoline changed the count");
        assert!(hub.wakeup.lock().unwrap().is_none());
    }

    /// A late signal after teardown must be ignored rather than touch the
    /// slots it no longer owns.
    #[test]
    fn a_dead_hub_ignores_signals() {
        let hub = CallbackHub::new();
        hub.shutdown();
        hub.fire(&hub.wakeup);
        hub.fire(&hub.render_update);
        assert!(!hub.is_alive());
    }

    /// Clearing with `None` is the supported way to detach a listener, and it
    /// must be idempotent -- `mpv-service.ts` can call it during teardown after
    /// the hub already shut down.
    #[test]
    fn detaching_without_a_function_is_idempotent() {
        let hub = CallbackHub::new();
        hub.set_wakeup(None).unwrap();
        hub.set_wakeup(None).unwrap();
        hub.set_render_update(None).unwrap();
        assert!(hub.wakeup.lock().unwrap().is_none());
        assert!(hub.render_update.lock().unwrap().is_none());
    }
}
