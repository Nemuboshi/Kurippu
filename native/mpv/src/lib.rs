#![allow(clippy::unused_unit)]
mod callbacks;
mod ffi;
mod player;
mod render;
mod scale;
mod value;

use napi::bindgen_prelude::*;
use napi_derive::napi;

#[napi(js_name = "MpvPlayer")]
pub struct MpvPlayer {
    destroyed: bool,
}

#[napi]
impl MpvPlayer {
    #[napi(constructor)]
    pub fn new(options: Option<Unknown>) -> Result<Self> {
        let _ = options;
        Ok(Self { destroyed: false })
    }

    #[napi]
    pub fn destroy(&mut self) {
        self.destroyed = true;
    }
}
