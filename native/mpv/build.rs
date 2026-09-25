use std::env;
use std::path::PathBuf;

/// Resolves the libmpv link directory. Mirrors binding.gyp's contract exactly:
/// MPV_LIB_DIR override, then per-platform defaults. Keeping the same env-var
/// names means CI and contributor docs do not change with the language.
fn main() {
    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let lib_dir = match env::var("MPV_LIB_DIR") {
        Ok(dir) => PathBuf::from(dir),
        Err(_) => match target_os.as_str() {
            "macos" => {
                let arch = env::var("CARGO_CFG_TARGET_ARCH").unwrap_or_default();
                PathBuf::from(if arch == "aarch64" {
                    "/opt/homebrew/lib"
                } else {
                    "/usr/local/lib"
                })
            }
            "windows" => PathBuf::from(env::var("USERPROFILE").unwrap_or_default())
                .join("libmpv")
                .join("lib"),
            "linux" => {
                let arch = env::var("CARGO_CFG_TARGET_ARCH").unwrap_or_default();
                PathBuf::from(if arch == "x86_64" {
                    "/usr/lib/x86_64-linux-gnu"
                } else {
                    "/usr/lib"
                })
            }
            other => panic!("unsupported target_os: {other}"),
        },
    };
    println!("cargo:rustc-link-search=native={}", lib_dir.display());
    println!("cargo:rustc-link-lib=dylib=mpv");
    println!("cargo:rerun-if-env-changed=MPV_LIB_DIR");
    println!("cargo:rerun-if-changed=build.rs");
}
