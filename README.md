# Kurippu

Kurippu is a C# / Avalonia desktop video editor. The former Electron
implementation and its Node build pipeline have been removed. Windows x64,
macOS x64/arm64, and Linux x64 are the target platforms. Publishing for a RID
alone is not a release gate: playback, encoding, UI behavior, and native
dependency packaging still require validation on each target system.

The desktop workbench keeps the operating system title bar. It has an
aspect-fitted video preview and transport on the left, with Info, Picture,
Audio, and Export tabs on the right. Info contains source details and the
change-source action. Fragment start/end are visible beside their mark buttons
and use `[` / `]` shortcuts outside text fields. Subtitle burn is available
only after selecting an embedded track or an existing external file.
Opening a source leaves playback paused on its first frame. The timeline
highlights the whole source until fragment marks are set, then highlights only
the marked range. Playback remains available while encoding; test encode is
not exposed in the workbench. Advanced encoding options expand within Export.
The Detail log button in the status bar opens a separate, live-updating log
window. Canceling an encode resets its progress indicator.
The appearance selector in the bottom status bar follows the system by
default, or can select a cohesive Light or Dark theme. The workbench remains
side by side at its minimum 1180x720 window size; only the individual inspector
tabs and expanded URL source choices scroll vertically.

Build locally with .NET 10:

```sh
dotnet test Kurippu.slnx
dotnet run --project Kurippu.Desktop
dotnet msbuild Kurippu.Desktop/Kurippu.Desktop.csproj -restore -t:Package -p:Configuration=Release -p:RuntimeIdentifier=win-x64
publish/win-x64/Kurippu.Desktop.exe --smoke
```

Substitute `osx-x64`, `osx-arm64`, or `linux-x64` on matching runners. The
`Package` target publishes the desktop app and then runs the C#
`Kurippu.Packaging` tool, which verifies pinned FFmpeg downloads and collects
native libmpv dependencies from Homebrew or the Linux build runner. `Package`
requires network access; an ordinary `dotnet publish` does not download or
stage runtime assets. The lower-level two-step form is:

```sh
dotnet publish Kurippu.Desktop/Kurippu.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
dotnet run --project Kurippu.Packaging -- stage win-x64 publish/win-x64
```

`--smoke` creates a sample, opens/renders it with libmpv, and exercises
one-pass, two-pass, test, and preview-concat FFmpeg exports. `--ui-smoke`
starts then closes a window.
The CI workflow archives each RID separately, measures its size,
extracts it, then runs native, empty-window, and real-file UI smoke modes
(`--smoke`, `--ui-smoke`, `--ui-open-smoke`) from the extracted artifact. Release
staging discards native PDB debug symbols; keep separate symbols if needed.

Neither smoke substitutes for hands-on UI and playback performance checks.
The macOS/Linux native closure, signing/distribution policy, URL selection,
crop interaction, and parity against the documented workflows require
per-platform review before release. yt-dlp is intentionally optional and external.
In particular, the four-RID cross-publish on Windows does not validate native
macOS or Linux playback, DPI, keyboard navigation, or theme interaction;
those remain native-runner and hands-on release gates.

The project is licensed under GPL-3.0-only.
