# Kurippu Avalonia Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Kurippu's Electron desktop application with a C# + Avalonia application for the four existing desktop targets, preserving its user-facing playback and encoding workflows.

**Architecture:** Avalonia views bind to focused view models. A UI-independent core produces encoding options and commands, a libmpv service owns native handles/events/frames, and a process service owns FFmpeg/optional yt-dlp. RID-specific native assets are staged and tested from published artifacts.

**Tech Stack:** .NET 10, C#, Avalonia desktop, libmpv C API, FFmpeg CLI, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-22-avalonia-migration-design.md`

## Global Constraints

- First release targets Windows x64, macOS x64, macOS arm64, and Linux x64; all four must start, play, encode, and pass package smoke checks.
- Preserve current source -> info -> encoder behavior and encoding semantics; do not redesign or remove functions during migration.
- Bundle FFmpeg and libmpv runtime assets per RID; keep yt-dlp an optional external tool.
- Software-frame playback is acceptable initially, subject to real usability checks; GPU zero-copy and Native AOT are not first-release requirements.
- Keep the Electron version operational until the Avalonia version passes the four-target release gate.
- The user requested uninterrupted implementation followed by consolidated verification; no intermediate approval gate.

## Review Focus

- Unicode/spaces/quotes in input and output paths: FFmpeg arguments and concat list are passed and escaped correctly (Task 2 tests).
- Missing yt-dlp or native libmpv: local-file work remains usable and errors are actionable (Tasks 2 and 3 tests).
- Cancellation during the first or second FFmpeg pass: child process and temporary outputs are cleaned (Task 2 tests).
- Rapid file changes/window close: mpv callbacks do not access freed handles or update disposed views (Task 3 tests).
- Track/crop geometry on anamorphic or letterboxed video: preview selection and exported parameters agree (Tasks 1 and 4 tests).

---

### Task 1: Core behavior and test parity

**Files:** Create `avalonia/Kurippu.Core/Kurippu.Core.csproj`, focused model/option/validation/FFmpeg command files; create `avalonia/Kurippu.Core.Tests/Kurippu.Core.Tests.csproj` and tests; read `src/engine/*.ts`, `src/renderer/encoder/store.svelte.ts`, `src/renderer/player/math.ts`, existing tests.

**Interfaces:** Produce `MediaInfo`, `EncodeOptions`, `EncodingPlan`, `OptionEngine.Derive(EncodeOptions, MediaInfo)`, `FfmpegArguments.Build(EncodingPlan)`, and crop/time helpers. No Avalonia or native library dependency.

- [ ] Port representative existing engine tests first, including defaults, codecs, sizing, subtitles, escapes, paths and two-pass argument sequences.
- [ ] Implement the focused C# core files with behavior matching those test fixtures; record intentional differences explicitly.
- [ ] Add a test for non-ASCII/spaced/quoted paths and a test for anamorphic letterbox/crop conversion.
- [ ] Run `dotnet test avalonia/Kurippu.Core.Tests/Kurippu.Core.Tests.csproj`; expect all tests PASS.
- [ ] Commit the core and tests when Git author identity is available.

### Task 2: Tool and workflow services

**Files:** Create `avalonia/Kurippu.Desktop/Services/ToolLocator.cs`, `ProcessRunner.cs`, `EncodeService.cs`, `SourceService.cs`, `TempFileStore.cs`; create service tests; read `src/main/process.ts`, `src/main/binaries.ts`, `src/renderer/encode/runner.svelte.ts`, `src/renderer/ytdlp.ts`.

**Interfaces:** Produce `IProcessRunner.RunAsync(executable, IReadOnlyList<string> args, IProgress<string>, CancellationToken)`, `EncodeService.EncodeAsync(EncodingPlan, ...)`, and `SourceService.ResolveOrDownloadAsync(...)`. Use argument lists, not a shell.

- [ ] Write tests with a fake runner for one/two pass, preview concatenation, cancellation, progress and temp cleanup; test missing optional yt-dlp.
- [ ] Implement executable resolution, process lifetime and output streaming; keep FFmpeg bundled and yt-dlp external.
- [ ] Implement the encode/download orchestration using Core plans; retain existing errors, test encode, output naming and open/show behavior.
- [ ] Run service tests on Windows and in CI on macOS/Linux; expect PASS.
- [ ] Commit services and tests when Git author identity is available.

### Task 3: Native media and software preview

**Files:** Create `avalonia/Kurippu.Desktop/Media/MpvNative.cs`, `MpvPlayer.cs`, `MpvVideoControl.cs`, `MediaProbe.cs`; create managed tests using a fake native boundary; read `native/mpv/src/mpv_addon.cc`, `src/main/mpv-service.ts`, `src/renderer/player/probe.ts`.

**Interfaces:** Produce `IMediaPlayer` with `OpenAsync`, `Play`, `Pause`, `Seek`, `SetVolume`, `SetTrack`, `SetDeinterlace`, `FrameReady`, `PropertyChanged`, `Dispose`; `MediaProbe.ProbeAsync(path)` returns Core `MediaInfo`.

- [ ] Write tests for state transitions, event dispatch, error reporting, reopen/dispose and callbacks after disposal.
- [ ] Implement minimal P/Invoke, safe native handle ownership, mpv event wakeups and software-frame rendering without leaking native pointers into views.
- [ ] Implement the Avalonia video control with frame throttling/owned pixel buffers and crop overlay coordination.
- [ ] From each target's published artifact, open, play, seek, resize, switch tracks and close sample files without leaked callbacks.
- [ ] Commit media implementation and tests when Git author identity is available.

### Task 4: Desktop UI parity

**Files:** Create `avalonia/Kurippu.Desktop/Kurippu.Desktop.csproj`, `App.axaml`, `MainWindow.axaml`, view models and focused source/player/options/encode views; read `src/renderer` and its tests.

**Interfaces:** UI depends on Core and service interfaces; no direct native calls, FFmpeg command assembly or shell commands in views.

- [ ] Write view-model tests for source navigation, option changes, track/crop selection, encode disabled states and error/cancel messages.
- [ ] Implement the source -> probe -> editor flow and user-visible controls: local/URL source, playback, effects/codecs, output/test/normal encode, logs/progress.
- [ ] Restore keyboard controls, resizing, dialogs, default output path and file/folder actions; keep existing workflows rather than pixel-perfect CSS.
- [ ] Run view-model tests and exercise representative UI paths in each desktop target.
- [ ] Commit UI and tests when Git author identity is available.

### Task 5: Distribution and release gate

**Files:** Create `avalonia/Kurippu.slnx`, RID asset staging/publish scripts in `avalonia/scripts`, and CI workflow; update root docs/scripts only at cutover, retaining the Electron workflow until proven.

**Interfaces:** `dotnet publish` per RID produces an app plus FFmpeg and libmpv dependency closure; CI uploads four separate artifacts.

- [ ] Add version-pinned asset retrieval/staging with per-RID runtime checks, including macOS install names/signing and Linux RPATH as needed.
- [ ] Add four-runner CI build/test/publish/package and post-publish smoke checks: binary found, libmpv loaded, FFmpeg short command, app launch.
- [ ] Compare current Electron and new releases on fixed samples: local/URL input, playback, crop/options, test/one/two-pass encode, preview concat, cancel/failure, output; record sizes without a size gate.
- [ ] Only after all four targets pass, switch release defaults and remove unused Electron/Node/N-API sources while preserving a rollback tag.
- [ ] Run final `dotnet test`, each target's publish/packaging job and installed-artifact smoke checks; expect PASS and no unapproved behavior regressions.
- [ ] Commit cutover when Git author identity is available.
