{
  "targets": [
    {
      "target_name": "mpv_addon",
      "sources": ["src/mpv_addon.cc"],
      "include_dirs": [
        "<!@(node -p \"require('node-addon-api').include\")"
      ],
      "cflags_cc": ["-std=c++17"],
      "defines": ["NAPI_DISABLE_CPP_EXCEPTIONS"],
      "conditions": [
        ["OS=='mac'", {
          "include_dirs": [
            "<!(node -p \"process.env.MPV_INCLUDE_DIR || (process.arch === 'arm64' ? '/opt/homebrew/include' : '/usr/local/include')\")"
          ],
          "libraries": [
            "<!(node -p \"process.env.MPV_LIB || (process.arch === 'arm64' ? '/opt/homebrew/lib/libmpv.dylib' : '/usr/local/lib/libmpv.dylib')\")",
            "-framework OpenGL",
            "-framework IOSurface",
            "-framework CoreFoundation"
          ],
          "xcode_settings": {
            "MACOSX_DEPLOYMENT_TARGET": "12.0",
            "OTHER_CPLUSPLUSFLAGS": ["-std=c++17", "-fno-exceptions"]
          }
        }],
        ["OS=='win'", {
          "include_dirs": [
            "<!(node -p \"process.env.MPV_INCLUDE_DIR || require('path').join(require('os').homedir(), 'libmpv', 'include')\")"
          ],
          "libraries": [
            "<!(node -p \"process.env.MPV_LIB || require('path').join(require('os').homedir(), 'libmpv', 'lib', 'mpv.lib')\")",
            "d3d11.lib",
            "dxgi.lib",
            "opengl32.lib",
            "gdi32.lib",
            "user32.lib"
          ],
          "msvs_settings": {
            "VCCLCompilerTool": {
              "AdditionalOptions": ["/std:c++17"],
              "ExceptionHandling": 0
            }
          }
        }],
        ["OS=='linux'", {
          "include_dirs": [
            "<!(node -p \"process.env.MPV_INCLUDE_DIR || '/usr/include'\")"
          ],
          "libraries": [
            "<!(node -p \"process.env.MPV_LIB || (process.arch === 'x64' ? '/usr/lib/x86_64-linux-gnu/libmpv.so' : '/usr/lib/libmpv.so')\")",
            "-ldl",
            "-lpthread"
          ]
        }]
      ]
    }
  ]
}
