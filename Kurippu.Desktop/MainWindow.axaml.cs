using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kurippu.Core;
using Kurippu.Desktop.Media;
using Kurippu.Desktop.Services;
using Kurippu.Desktop.Theming;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop;

public partial class MainWindow : Window, IDisposable
{
    private static readonly string[] TextOptionControls =
    [
        nameof(ScaleWidthBox), nameof(ScaleHeightBox), nameof(SubtitlePathBox),
        nameof(AudioBitrateBox), nameof(AmplifyBox), nameof(FadeInBox), nameof(FadeOutBox),
        nameof(LimitBox), nameof(QualityBox), nameof(StartBox), nameof(EndBox), nameof(ThreadsBox)
    ];
    private static readonly string[] ComboOptionControls =
    [
        nameof(PresetBox), nameof(MultithreadingBox)
    ];
    private static readonly string[] TextFieldsToReset =
    [
        nameof(ScaleWidthBox), nameof(ScaleHeightBox), nameof(SubtitlePathBox),
        nameof(AudioBitrateBox), nameof(AmplifyBox), nameof(FadeInBox), nameof(FadeOutBox),
        nameof(LimitBox), nameof(QualityBox), nameof(StartBox), nameof(EndBox)
    ];
    private static readonly string[] SubtitleNoneItems = ["None"];

    private readonly MainWindowViewModel model = new();
    private readonly IProcessRunner process;
    private readonly string ffmpeg;
    private readonly Func<string?> defaultOutputDirectory;
    private readonly Func<Task<IMediaPlayer>> openPlayer;
    private readonly Action<string> openPath;
    private readonly Func<Action<EncodeUpdate>, IProgress<EncodeUpdate>> createEncodeProgress;
    private IMediaPlayer? player;
    private SourceService? sourceService;
    private SourceChoices? sourceChoices;
    private CancellationTokenSource? downloadCancellation;
    private string? downloadedSource;
    private int sourceGeneration;
    private EncodeProgressSession? encodeProgress;
    private readonly DetectionSession detection = new();
    private readonly ActivityLog activityLog = new();
    private LogWindow? logWindow;
    private bool updatingCrop;
    private bool initializingEditor;
    private bool syncingModes;
    private bool syncingAudioCodec;
    private bool updatingOutput;
    private bool autoOutputPath;
    private string? sourceSaveAs;
    private string? sourceTitle;
    private bool updatingSeek;
    private bool encoding;
    private bool muted;
    private string? lastOutput;
    private bool disposed;

    public MainWindow() : this(new ProcessRunner(),
        ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("Bundled FFmpeg not found"),
        DefaultOutputDirectory, () => Task.FromResult<IMediaPlayer>(new MpvPlayer()),
        OpenPath, callback => new Progress<EncodeUpdate>(callback))
    {
    }

    internal MainWindow(IProcessRunner process, string ffmpeg, Func<string?> defaultOutputDirectory)
        : this(process, ffmpeg, defaultOutputDirectory, () => Task.FromResult<IMediaPlayer>(new MpvPlayer()),
            OpenPath, callback => new Progress<EncodeUpdate>(callback))
    {
    }

    internal MainWindow(IProcessRunner process, string ffmpeg, Func<string?> defaultOutputDirectory,
        Func<Task<IMediaPlayer>> openPlayer)
        : this(process, ffmpeg, defaultOutputDirectory, openPlayer,
            OpenPath, callback => new Progress<EncodeUpdate>(callback))
    {
    }

    internal MainWindow(IProcessRunner process, string ffmpeg, Func<string?> defaultOutputDirectory,
        Func<Task<IMediaPlayer>> openPlayer, Action<string> openPath,
        Func<Action<EncodeUpdate>, IProgress<EncodeUpdate>> createEncodeProgress)
    {
        this.process = process;
        this.ffmpeg = ffmpeg;
        this.defaultOutputDirectory = defaultOutputDirectory;
        this.openPlayer = openPlayer;
        this.openPath = openPath;
        this.createEncodeProgress = createEncodeProgress;
        InitializeComponent();
        DataContext = model;
        ConfigureInputs();
        VideoSurface.TimeChanged += OnTimeChanged;
        CropOverlay.CropChanged += OnCropChanged;
        CropOverlay.Clicked += OnCropClicked;
        VideoSurface.RenderFailed += (_, args) => ShowError(args.Exception.Message);
        VideoSurface.RenderRecovered += (_, _) => StatusText.Text = model.Status;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
    }

    private void ConfigureInputs()
    {
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        CropOverlay.PointerWheelChanged += (_, args) =>
        {
            if (encoding || player is null) return;
            if (args.Delta.Y > 0) player.FrameStep(); else player.FrameBackStep();
            args.Handled = true;
        };
        foreach (var box in new[] { CropWidthBox, CropHeightBox, CropXBox, CropYBox })
            box.TextChanged += (_, _) => SyncCropFromFields();
        foreach (var box in FindControls<TextBox>(TextOptionControls))
            box.TextChanged += (_, _) => { if (player is not null && model.Editor is not null) UpdateOptions(); };
        foreach (var combo in FindControls<ComboBox>(ComboOptionControls))
            combo.SelectionChanged += (_, _) => { if (player is not null && model.Editor is not null) UpdateOptions(); };
        AudioCodecBox.SelectionChanged += (_, _) => OnAudioCodecChanged();
        SubtitlePathBox.TextChanged += (_, _) => UpdateSubtitleAvailability();
        foreach (var check in new[] { FixSarBox, BurnSubsBox })
            check.PropertyChanged += (_, args) =>
            {
                if (args.Property == ToggleButton.IsCheckedProperty && player is not null && model.Editor is not null)
                    UpdateOptions();
            };
        foreach (var check in new[] { LimitModeBox, CrfModeBox, TwoPassBox })
            check.PropertyChanged += (_, args) =>
            {
                if (args.Property == ToggleButton.IsCheckedProperty) OnModeChanged(check);
            };
        OutputBox.TextChanged += (_, _) => { if (!updatingOutput) autoOutputPath = false; };
    }

    private IEnumerable<T> FindControls<T>(IEnumerable<string> names)
        where T : Control =>
        names.Select(name => this.FindControl<T>(name) ??
            throw new InvalidOperationException($"Control '{name}' was not found"));

    private void OnCropChanged(object? sender, CropChangedEventArgs args)
    {
        var crop = args.Crop;
        updatingCrop = true;
        CropWidthBox.Text = crop?.Width.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "";
        CropHeightBox.Text = crop?.Height.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "";
        CropXBox.Text = crop?.X.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "";
        CropYBox.Text = crop?.Y.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "";
        updatingCrop = false;
        UpdateOptions();
    }

    private void OnCropClicked(object? sender, EventArgs args)
    {
        if (!encoding) OnPlayPause(null, null!);
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        sourceGeneration++;
        downloadCancellation?.Cancel();
        downloadCancellation?.Dispose();
        downloadCancellation = null;
        encodeProgress?.Cancel();
        encodeProgress?.Dispose();
        encodeProgress = null;
        detection.Dispose();
        logWindow?.Close();
        VideoSurface.Dispose();
        player?.Dispose();
        player = null;
        sourceService?.Dispose();
        sourceService = null;
        GC.SuppressFinalize(this);
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(OpenFileAsync);

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Video") { Patterns = ["*.mkv", "*.mp4", "*.webm", "*.mov", "*.avi", "*.ts", "*.wmv"] }, FilePickerFileTypes.All]
        }).ConfigureAwait(true);
        if (files.Count > 0) await OpenSourceAsync(files[0].Path.LocalPath).ConfigureAwait(true);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (encoding) return;
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (path is not null && File.Exists(path)) _ = RunUiOperationAsync(() => OpenSourceAsync(path));
    }

    private void OnOpenUrl(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(ResolveUrlAsync);

    private async Task ResolveUrlAsync()
    {
        var url = UrlBox.Text?.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        { ShowError("Enter a valid video URL"); return; }
        var tool = ToolLocator.Default.YtDlp();
        if (tool is null) { ShowError("yt-dlp not found; install it to open URLs"); return; }
        sourceService ??= new SourceService(process, tool, ToolLocator.Default.Ffmpeg());
        downloadCancellation?.Cancel();
        downloadCancellation = new CancellationTokenSource();
        var cancellation = downloadCancellation;
        try
        {
            CancelDownloadButton.IsVisible = true;
            StatusText.Text = "Resolving URL...";
            sourceChoices = await sourceService.ResolveAsync(uri.AbsoluteUri, cancellation.Token)
                .ConfigureAwait(true);
            UrlTitleText.Text = sourceChoices.Title;
            UrlVideoBox.ItemsSource = sourceChoices.Videos.Select(f => f.Label).ToArray();
            UrlAudioBox.ItemsSource = sourceChoices.Audios.Select(f => f.Label).ToArray();
            UrlSubtitleBox.ItemsSource = sourceChoices.Subtitles.Select(f => f.Label).ToArray();
            UrlVideoBox.SelectedIndex = 0;
            UrlAudioBox.SelectedIndex = 0;
            UrlSubtitleBox.SelectedIndex = sourceChoices.DefaultSubtitleIndex;
            FormatPanel.IsVisible = true;
            StatusText.Text = "Choose formats and download";
        }
        catch (OperationCanceledException) { StatusText.Text = "URL resolution cancelled"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (downloadCancellation == cancellation) { cancellation.Dispose(); downloadCancellation = null; CancelDownloadButton.IsVisible = false; } }
    }

    private void OnDownloadUrl(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(DownloadUrlAsync);

    private async Task DownloadUrlAsync()
    {
        if (sourceChoices is not { } choice || sourceService is null || downloadCancellation is not null) return;
        downloadCancellation = new CancellationTokenSource();
        var cancellation = downloadCancellation;
        CancelDownloadButton.IsVisible = true;
        try
        {
            StatusText.Text = "Downloading source...";
            var progress = new Progress<string>(line => StatusText.Text = line);
            var path = await sourceService.DownloadAsync(choice.Url,
                choice.Videos[UrlVideoBox.SelectedIndex].Id,
                choice.Audios[UrlAudioBox.SelectedIndex].Id,
                choice.Subtitles[UrlSubtitleBox.SelectedIndex].Id, progress,
                cancellation.Token).ConfigureAwait(true);
            FormatPanel.IsVisible = false;
            sourceTitle = choice.Title;
            sourceSaveAs = choice.SaveAs;
            await OpenSourceAsync(path, preserveSourceMetadata: true).ConfigureAwait(true);
            if (player is not null && string.Equals(model.Editor?.Media.Path, path, StringComparison.Ordinal)) downloadedSource = path;
            else sourceService.Release(path);
        }
        catch (OperationCanceledException) { StatusText.Text = "Download cancelled"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (downloadCancellation == cancellation) { cancellation.Dispose(); downloadCancellation = null; CancelDownloadButton.IsVisible = false; } }
    }

    private void OnCancelDownload(object? sender, RoutedEventArgs e) => downloadCancellation?.Cancel();

    internal async Task OpenSourceAsync(string path, bool preserveSourceMetadata = false)
    {
        var generation = ++sourceGeneration;
        detection.CancelForEdit();
        if (!preserveSourceMetadata) { sourceSaveAs = null; sourceTitle = null; }
        model.BeginProbe();
        SourceScroll.IsVisible = false;
        InfoText.IsVisible = true;
        EditorPanel.IsVisible = false;
        StatusText.Text = model.Status;
        try
        {
            VideoSurface.Player = null;
            player?.Dispose();
            if (downloadedSource is { } previous && !string.Equals(previous, path, StringComparison.Ordinal))
            {
                sourceService?.Release(previous);
                downloadedSource = null;
            }
            player = await openPlayer().ConfigureAwait(true);
            var media = await player.OpenAsync(path).ConfigureAwait(true);
            if (generation != sourceGeneration) return;
            initializingEditor = true;
            PopulateEditor(media);
            initializingEditor = false;
            UpdateOptions();
            ShowEditor(media, path);
        }
        catch (Exception ex)
        {
            initializingEditor = false;
            if (generation != sourceGeneration) return;
            player?.Dispose(); player = null;
            model.ChangeSource();
            InfoText.IsVisible = false;
            SourceScroll.IsVisible = true;
            ShowError(ex.Message);
        }
    }

    private void PopulateEditor(MediaInfo media)
    {
        model.SetMedia(media);
        VideoSurface.Player = player;
        SeekSlider.Maximum = Math.Max(1, media.Duration);
        SeekSlider.Value = 0;
        muted = false;
        MuteButton.Content = "Mute";
        MediaDetails.Text = FormatMediaDetails(media);
        VideoTrackBox.ItemsSource = media.VideoTracks
            .Select((track, index) => $"{index}: {track.Codec} {track.Width}x{track.Height}")
            .ToArray();
        VideoTrackBox.SelectedIndex = 0;
        AudioTrackBox.ItemsSource = media.AudioTracks
            .Select((track, index) => $"{index}: {track.Codec} {track.Language}")
            .ToArray();
        AudioTrackBox.SelectedIndex = media.AudioTracks.Count > 0 ? 0 : -1;
        SubtitleTrackBox.ItemsSource = SubtitleNoneItems.Concat(media.SubtitleTracks
            .Select((track, index) => $"{index}: {track.Codec} {track.Language}")).ToArray();
        SubtitleTrackBox.SelectedIndex = 0;
        UpdateSubtitleAvailability();
        HasAudioBox.IsChecked = media.AudioTracks.Count > 0;
        SetCropSource(media.Width, media.Height, media.Sar);
        ResetEditorFields(media.Sar);
    }

    private string FormatMediaDetails(MediaInfo media) =>
        $"Path: {media.Path}\nTitle: {SourceTitle.DisplayTitle(media.Title, sourceTitle)}\n" +
        $"Duration: {PlaybackTimeline.ShowTime(media.Duration)}\n" +
        $"Bitrate: {(media.Bitrate > 0 ? $"{media.Bitrate / 1000:0} Kbps" : "unknown")}\n" +
        $"Size: {(media.FileSize > 0 ? $"{media.FileSize / 1048576d:0.00} MiB" : "unknown")}\n" +
        $"Format: {media.Format}\n" +
        string.Join("\n", media.VideoTracks.Select((track, index) =>
            $"Video #{index}: {track.Codec}, {track.Width}x{track.Height}, SAR {track.Sar:0.###}, {track.Fps:0.###} fps")) +
        "\n" +
        string.Join("\n", media.AudioTracks.Select((track, index) =>
            $"Audio #{index}: {track.Codec}, {track.Channels} channels, {track.SampleRate} Hz, {track.Language}")) +
        "\n" +
        string.Join("\n", media.SubtitleTracks.Select((track, index) =>
            $"Subtitle #{index}: {track.Codec}, {track.Language}, {track.Title}"));

    private void SetCropSource(int width, int height, double sar)
    {
        CropOverlay.SourceWidth = width;
        CropOverlay.SourceHeight = height;
        CropOverlay.Sar = sar;
        VideoStage.SetSource(width, height, sar);
        CropOverlay.SetCrop(null);
    }

    private void ResetEditorFields(double sar)
    {
        updatingCrop = true;
        CropWidthBox.Text = CropHeightBox.Text = CropXBox.Text = CropYBox.Text = "";
        updatingCrop = false;
        foreach (var box in FindControls<TextBox>(TextFieldsToReset))
            box.Text = "";
        ThreadsBox.Text = "8";
        DeinterlaceBox.IsChecked = BurnSubsBox.IsChecked = LimitModeBox.IsChecked =
            TwoPassBox.IsChecked = false;
        PreviewPathBox.Text = PreviewTimeBox.Text = "";
        VideoCodecBox.SelectedIndex = 0;
        AudioCodecBox.SelectedIndex = 0;
        MultithreadingBox.SelectedIndex = 0;
        PresetBox.SelectedIndex = 4;
        CrfModeBox.IsChecked = true;
        FixSarBox.IsChecked = sar != 1;
        RawArgumentsBox.Text = model.Editor!.RawArguments;
    }

    private void ShowEditor(MediaInfo media, string path)
    {
        autoOutputPath = true;
        RefreshOutputPath();
        Title = sourceTitle ?? media.Title ?? Path.GetFileName(path);
        TitleBox.Text = Title;
        lastOutput = null;
        StatusText.Text = model.Status;
        InfoText.IsVisible = false;
        EditorPanel.IsVisible = true;
        EditorTabs.SelectedIndex = 1;
        ChangeSourceButton.IsVisible = true;
        player?.Pause();
    }

    internal async Task RunOpenSmokeAsync()
    {
        var ffmpegPath = ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("FFmpeg not found");
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var video = Path.Combine(directory, "source.mp4");
            await CreateSmokeVideoAsync(ffmpegPath, video).ConfigureAwait(true);
            await OpenSourceAsync(video).ConfigureAwait(true);
            await VerifyInitialEditorAsync().ConfigureAwait(true);
            await VerifyLogWindowAsync().ConfigureAwait(true);
            await VerifyTimelineAndPlaybackAsync().ConfigureAwait(true);
            await VerifyEncodingProgressAsync().ConfigureAwait(true);
            await VerifyCropAndSubtitlesAsync(directory).ConfigureAwait(true);
            VerifyWorkbenchGeometry();
            await VerifyThemesAndResizeAsync().ConfigureAwait(true);
            await VerifyMinimumLayoutAsync().ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private async Task CreateSmokeVideoAsync(string executable, string output)
    {
        var args = new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i",
            "testsrc=size=320x180:rate=25", "-t", "3", "-c:v", "mpeg4", output };
        if (await process.RunAsync(executable, args, null, CancellationToken.None)
                .ConfigureAwait(true) != 0)
            throw new InvalidOperationException("UI fixture encode failed");
    }

    private async Task VerifyInitialEditorAsync()
    {
        if (model.Editor is null || !EditorPanel.IsVisible ||
            MediaDetails.Text?.Contains("320x180") != true)
            throw new InvalidOperationException(
                $"UI open failed: page={model.Page}, status={StatusText.Text}, " +
                $"details={MediaDetails.Text}");
        if (EditorTabs.Items.Count != 4)
            throw new InvalidOperationException(
                "Inspector must have Info, Picture, Audio and Export tabs");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!VideoSurface.HasFrame && DateTime.UtcNow < deadline)
            await Task.Delay(100).ConfigureAwait(true);
        if (!VideoSurface.HasFrame)
            throw new InvalidOperationException(
                $"UI surface missing: window={Bounds}, editor={EditorPanel.Bounds}, " +
                $"parent={((Control)VideoSurface.Parent!).Bounds}, bounds={VideoSurface.Bounds}, " +
                $"attached={TopLevel.GetTopLevel(VideoSurface) is not null}, " +
                $"status={StatusText.Text}");
        if (Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1 ||
            EditorPanel.Bounds.Width > Bounds.Width - 32 + 1)
            throw new InvalidOperationException(
                $"Desktop panel alignment or horizontal overflow: preview={PreviewPane.Bounds}, " +
                $"inspector={InspectorPane.Bounds}, editor={EditorPanel.Bounds}, window={Bounds}");
    }

    private async Task VerifyLogWindowAsync()
    {
        EditorTabs.SelectedIndex = 3;
        AdvancedSettings.IsExpanded = true;
        await Task.Delay(100).ConfigureAwait(true);
        var exportScroll = (ScrollViewer)((TabItem)EditorTabs.Items[3]!).Content!;
        if (Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1 ||
            exportScroll.Extent.Width > exportScroll.Viewport.Width + 1)
            throw new InvalidOperationException(
                $"Expanded export misaligns panels or scrolls horizontally: " +
                $"preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}, " +
                $"exportExtent={exportScroll.Extent}, exportViewport={exportScroll.Viewport}");
        AdvancedSettings.IsExpanded = false;
        EditorTabs.SelectedIndex = 1;
        OnOpenLog(null, null!);
        if (logWindow is not { IsVisible: true })
            throw new InvalidOperationException("Detail log did not open in its own window");
        activityLog.AppendLine("Live log update");
        await Task.Delay(100).ConfigureAwait(true);
        if (!logWindow.DisplayedText.Contains("Live log update"))
            throw new InvalidOperationException("Detail log window did not receive live output");
        ThemeBox.SelectedIndex = 2;
        if (logWindow.RequestedThemeVariant != ThemeMode.Resolve("Dark"))
            throw new InvalidOperationException("Detail log window did not follow the selected theme");
        ThemeBox.SelectedIndex = 0;
        logWindow.Close();
        OnOpenLog(null, null!);
        if (logWindow is not { IsVisible: true } ||
            !logWindow.DisplayedText.Contains("Live log update"))
            throw new InvalidOperationException("Reopened detail log lost output");
        var logRight = LogButton.TranslatePoint(new Point(LogButton.Bounds.Width, 0), this)?.X;
        var themeLeft = ThemeBox.TranslatePoint(default, this)?.X;
        if (logRight is null || themeLeft is null || logRight > themeLeft)
            throw new InvalidOperationException(
                "Detail log button is not left of the theme selector");
        logWindow.Close();
        activityLog.Clear();
    }

    private async Task VerifyTimelineAndPlaybackAsync()
    {
        if (!string.Equals(player?.GetProperty("pause"), "yes", StringComparison.Ordinal))
            throw new InvalidOperationException("Opening a source started playback");
        if (SeekSlider.HighlightStart != 0 || model.Editor is not { } editor ||
            Math.Abs(SeekSlider.HighlightEnd - editor.Media.Duration) > .001)
            throw new InvalidOperationException("Unmarked timeline is not fully highlighted");
        if (BurnSubsBox.IsEnabled)
            throw new InvalidOperationException("Subtitle burn enabled without a subtitle source");
        SeekSlider.Value = 0.5;
        OnMarkStart(null, null!);
        SeekSlider.Value = 2;
        OnMarkEnd(null, null!);
        if (!string.Equals(StartBox.Text, "00:00.500", StringComparison.Ordinal) ||
            !string.Equals(EndBox.Text, "00:02.000", StringComparison.Ordinal) ||
            !StartBox.IsEffectivelyVisible || !EndBox.IsEffectivelyVisible)
            throw new InvalidOperationException(
                $"Fragment marks not visible next to transport: {StartBox.Text} - {EndBox.Text}");
        if (Math.Abs(SeekSlider.HighlightStart - .5) > .001 ||
            Math.Abs(SeekSlider.HighlightEnd - 2) > .001)
            throw new InvalidOperationException(
                $"Timeline highlight does not follow fragment marks: " +
                $"{SeekSlider.HighlightStart}-{SeekSlider.HighlightEnd}; " +
                $"marks={StartBox.Text}-{EndBox.Text}; duration={model.Editor.Media.Duration}");
        StartBox.Text = "00:00.250";
        await Task.Delay(100).ConfigureAwait(true);
        if (Math.Abs(SeekSlider.HighlightStart - .25) > .001)
            throw new InvalidOperationException(
                "Editing a fragment time did not update the timeline");
        SeekSlider.Value = 1;
        PlayButton.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = Key.OemOpenBrackets
        });
        if (!string.Equals(StartBox.Text, "00:01.000", StringComparison.Ordinal))
            throw new InvalidOperationException("[ shortcut failed to mark start");
        SeekSlider.Value = 2;
        PlayButton.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = Key.OemCloseBrackets
        });
        if (!string.Equals(EndBox.Text, "00:02.000", StringComparison.Ordinal))
            throw new InvalidOperationException("] shortcut failed to mark end");
        SetEncodingControls(false);
        if (!PlayButton.IsEnabled)
            throw new InvalidOperationException("Playback disabled during encoding");
        OnPlayPause(null, null!);
        if (!string.Equals(player?.GetProperty("pause"), "no", StringComparison.Ordinal))
            throw new InvalidOperationException("Playback cannot start during encoding");
        OnPlayPause(null, null!);
        if (!string.Equals(player?.GetProperty("pause"), "yes", StringComparison.Ordinal))
            throw new InvalidOperationException("Playback cannot pause during encoding");
        SetEncodingControls(true);
    }

    private void VerifyEncodingProgress()
    {
        using var progress = new EncodeProgressSession();
        encodeProgress = progress;
        EncodeProgress.Value = 43;
        OnCancelEncode(null, null!);
        if (!progress.Token.IsCancellationRequested || EncodeProgress.Value != 0)
            throw new InvalidOperationException(
                $"Cancel did not reset encoding progress: {EncodeProgress.Value}");
        ApplyEncodeUpdate(new EncodeUpdate("late progress", 70), progress.Token);
        if (EncodeProgress.Value != 0)
            throw new InvalidOperationException("Queued progress reappeared after cancel");
        encodeProgress = null;
        if (this.FindControl<Button>("TestEncodeButton") is not null)
            throw new InvalidOperationException("Test encode is still exposed in the workbench");
    }

    private async Task VerifyEncodingProgressAsync()
    {
        VerifyEncodingProgress();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task VerifyCropAndSubtitlesAsync(string directory)
    {
        CropWidthBox.Text = "160";
        CropHeightBox.Text = "90";
        await Task.Delay(100).ConfigureAwait(true);
        var cropBefore = CropOverlay.Crop;
        if (cropBefore is null)
            throw new InvalidOperationException(
                $"Crop setup failed: {ValidationText.Text}; " +
                $"fields={CropWidthBox.Text}/{CropHeightBox.Text}; " +
                $"options={model.Editor?.Options.CropWidth}/{model.Editor?.Options.CropHeight}; " +
                $"plan={model.Editor?.VisibleCrop}; start={StartBox.Text}; end={EndBox.Text}");
        var subtitles = Path.Combine(directory, "external.srt");
        await File.WriteAllTextAsync(subtitles,
            "1\n00:00:00,000 --> 00:00:01,000\nHello").ConfigureAwait(true);
        SubtitlePathBox.Text = subtitles;
        await Task.Delay(100).ConfigureAwait(true);
        if (!BurnSubsBox.IsEnabled)
            throw new InvalidOperationException("External subtitles did not enable burning");
        BurnSubsBox.IsChecked = true;
        if (CropOverlay.Crop != cropBefore)
            throw new InvalidOperationException("Subtitle option removed existing crop");
        SubtitlePathBox.Text = "";
        await Task.Delay(100).ConfigureAwait(true);
        if (BurnSubsBox.IsEnabled || BurnSubsBox.IsChecked == true ||
            CropOverlay.Crop != cropBefore)
            throw new InvalidOperationException(
                "Unavailable subtitles left invalid burn or erased crop");
        CropWidthBox.Text = CropHeightBox.Text = "";
        StartBox.Text = EndBox.Text = "";
    }

    private void VerifyWorkbenchGeometry()
    {
        if (Math.Abs(VideoSurface.Bounds.Width / VideoSurface.Bounds.Height - 16d / 9) > 0.02 ||
            Math.Abs(VideoSurface.Bounds.Width - CropOverlay.Bounds.Width) > 1 ||
            Math.Abs(VideoSurface.Bounds.Height - CropOverlay.Bounds.Height) > 1 ||
            InspectorPane.Bounds.Width < 300)
            throw new InvalidOperationException(
                $"Workbench geometry invalid: video={VideoSurface.Bounds}, " +
                $"crop={CropOverlay.Bounds}, inspector={InspectorPane.Bounds}");
    }

    private async Task VerifyThemesAndResizeAsync()
    {
        foreach (var mode in new[] { "Light", "Dark", "System" })
        {
            ThemeBox.SelectedIndex = mode switch { "Light" => 1, "Dark" => 2, _ => 0 };
            if (RequestedThemeVariant != ThemeMode.Resolve(mode))
                throw new InvalidOperationException($"Theme selection failed: {mode}");
        }
        ThemeBox.SelectedIndex = 1;
        var lightBackground = Background?.ToString();
        ThemeBox.SelectedIndex = 2;
        var darkBackground = Background?.ToString();
        if (string.Equals(lightBackground, darkBackground, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Workbench background did not follow theme selection");
        ThemeBox.SelectedIndex = 0;
        Exception? sizeError = null;
        VideoSurface.RenderFailed += (_, args) => sizeError = args.Exception;
        VideoSurface.Width = 9000;
        var sizeDeadline = DateTime.UtcNow.AddSeconds(3);
        while (sizeError is null && DateTime.UtcNow < sizeDeadline)
            await Task.Delay(50).ConfigureAwait(true);
        if (sizeError is not ArgumentOutOfRangeException)
            throw new InvalidOperationException(
                $"Oversized preview did not report its limit: {sizeError}");
        var recoveredFrames = 0;
        VideoSurface.TimeChanged += (_, _) => recoveredFrames++;
        VideoSurface.Width = double.NaN;
        var recoveryDeadline = DateTime.UtcNow.AddSeconds(3);
        while (recoveredFrames == 0 && DateTime.UtcNow < recoveryDeadline)
            await Task.Delay(50).ConfigureAwait(true);
        if (recoveredFrames == 0)
            throw new InvalidOperationException("Preview did not resume after resizing");
    }

    private async Task VerifyMinimumLayoutAsync()
    {
        Width = MinWidth;
        Height = MinHeight;
        EditorTabs.SelectedIndex = 3;
        await Task.Delay(200).ConfigureAwait(true);
        if (InspectorPane.GetValue(Grid.ColumnProperty) != 1 ||
            Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1)
            throw new InvalidOperationException(
                $"Minimum window must keep equal-height side-by-side panels: " +
                $"preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}");
        if (InspectorPane.Bounds.Height < 300)
            throw new InvalidOperationException(
                $"Inspector too short at minimum window: {InspectorPane.Bounds}");
        if (EditorPanel.Bounds.Width > Bounds.Width - 32 + 1)
            throw new InvalidOperationException(
                $"Minimum workbench overflows horizontally: editor={EditorPanel.Bounds}, " +
                $"window={Bounds}");
        AdvancedSettings.IsExpanded = true;
        RawArgumentsBox.BringIntoView();
        await Task.Delay(100).ConfigureAwait(true);
        if (InspectorPane.Bounds.Width < 300 || PreviewPane.Bounds.Width < 400 ||
            StatusText.Bounds.Width <= 0 || EncodeButton.Bounds.Width <= 0 ||
            RawArgumentsBox.Bounds.Width <= 0 || LogButton.Bounds.Width <= 0)
            throw new InvalidOperationException(
                $"Narrow workbench inaccessible: preview={PreviewPane.Bounds}, " +
                $"inspector={InspectorPane.Bounds}, export={EncodeButton.Bounds}");
        var progressRight = EncodeProgress.TranslatePoint(
            new Point(EncodeProgress.Bounds.Width, 0), this)?.X;
        if (progressRight is null || progressRight > Bounds.Width)
            throw new InvalidOperationException(
                $"Encode controls clipped at minimum width: right={progressRight}, " +
                $"window={Bounds.Width}");
        EditorPanel.IsVisible = false;
        SourceScroll.IsVisible = true;
        FormatPanel.IsVisible = true;
        CancelDownloadButton.IsVisible = true;
        await Task.Delay(100).ConfigureAwait(true);
        CancelDownloadButton.BringIntoView();
        await Task.Delay(100).ConfigureAwait(true);
        var cancelBottom = CancelDownloadButton.TranslatePoint(
            new Point(0, CancelDownloadButton.Bounds.Height), this)?.Y;
        if (cancelBottom is null || cancelBottom > Bounds.Height - StatusText.Bounds.Height)
            throw new InvalidOperationException(
                $"URL actions clipped at minimum window: bottom={cancelBottom}");
    }

    private void OnChangeSource(object? sender, RoutedEventArgs e)
    {
        if (encoding) return;
        sourceGeneration++;
        detection.CancelForEdit();
        VideoSurface.Player = null;
        player?.Dispose(); player = null;
        if (downloadedSource is { } previous) { sourceService?.Release(previous); downloadedSource = null; }
        model.ChangeSource();
        SourceScroll.IsVisible = true;
        EditorPanel.IsVisible = false;
        ChangeSourceButton.IsVisible = false;
        StatusText.Text = model.Status;
        Title = "Kurippu";
        lastOutput = null;
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs args)
    {
        var time = args.Time;
        if (model.Editor is not { } editor) return;
        updatingSeek = true;
        SeekSlider.Value = Math.Clamp(time, 0, SeekSlider.Maximum);
        updatingSeek = false;
        ClockText.Text = $"{PlaybackTimeline.ShowTime(time)} / {PlaybackTimeline.ShowTime(editor.Media.Duration)}";
        if (!TimeBox.IsFocused) TimeBox.Text = PlaybackTimeline.ShowTime(time);
        if (LoopBox.IsChecked == true && PlaybackTimeline.LoopTarget(time,
            StartBox.Text ?? "", EndBox.Text ?? "", editor.Media.Duration) is { } target) player?.Seek(target);
    }

    private void OnSeekChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RangeTimeline.ValueProperty && !updatingSeek && player is not null)
            player.Seek(SeekSlider.Value);
    }

    private void OnVolumeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty && player is not null)
        {
            player.SetVolume(VolumeSlider.Value);
            if (VolumeSlider.Value > 0 && muted) SetMuted(false);
        }
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (player is null) return;
        if (string.Equals(player.GetProperty("pause"), "yes", StringComparison.Ordinal) && model.Editor is { } editor &&
            PlaybackTimeline.ResumeTarget(SeekSlider.Value, editor.Media.Duration, LoopBox.IsChecked == true,
                StartBox.Text ?? "", EndBox.Text ?? "") is { } target) player.Seek(target);
        if (string.Equals(player.GetProperty("pause"), "yes", StringComparison.Ordinal)) player.Play(); else player.Pause();
    }
    private void OnFrameStep(object? sender, RoutedEventArgs e) => player?.FrameStep();
    private void OnFrameBackStep(object? sender, RoutedEventArgs e) => player?.FrameBackStep();
    private void OnMarkStart(object? sender, RoutedEventArgs e)
    {
        var marks = PlaybackTimeline.MarkStart(SeekSlider.Value, StartBox.Text ?? "", EndBox.Text ?? "");
        StartBox.Text = marks.Start; EndBox.Text = marks.End;
        RefreshTimelineHighlight();
    }
    private void OnMarkEnd(object? sender, RoutedEventArgs e)
    {
        var marks = PlaybackTimeline.MarkEnd(SeekSlider.Value, StartBox.Text ?? "", EndBox.Text ?? "",
            model.Editor?.Media.Duration ?? 0);
        StartBox.Text = marks.Start; EndBox.Text = marks.End;
        RefreshTimelineHighlight();
    }
    private void SetMuted(bool value)
    {
        muted = value;
        player?.SetMuted(value);
        MuteButton.Content = value ? "Unmute" : "Mute";
    }
    private void OnMute(object? sender, RoutedEventArgs e) => SetMuted(!muted);
    private void OnTimeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (e.Key == Key.Enter)
        {
            try { player?.Seek(Math.Clamp(OptionEngine.ParseTime(TimeBox.Text ?? ""), 0, model.Editor?.Media.Duration ?? 0)); }
            catch (FormatException) { ShowError("Invalid playback time"); }
        }
        PlayButton.Focus();
        e.Handled = true;
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (player is null || e.Source is TextBox) return;
        if (PlaybackShortcuts.MarkFor(e.Key) is { } mark)
        {
            if (string.Equals(mark, "start", StringComparison.Ordinal)) OnMarkStart(sender, e);
            else OnMarkEnd(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Handled || e.Source is Button or ComboBox or CheckBox or RangeTimeline) return;
        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter: OnPlayPause(sender, e); break;
            case Key.Left: player.Seek(Math.Max(0, SeekSlider.Value - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 5))); break;
            case Key.Right: player.Seek(SeekSlider.Value + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 5)); break;
            case Key.Up: VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5); break;
            case Key.Down: VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5); break;
            case Key.OemPeriod: player.FrameStep(); break;
            case Key.OemComma: player.FrameBackStep(); break;
            case Key.M: OnMute(sender, e); break;
            case Key.T: TimeBox.Focus(); TimeBox.SelectAll(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnSubtitleFile(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(SelectSubtitleFileAsync);

    private async Task SelectSubtitleFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Subtitle file",
            AllowMultiple = false
        }).ConfigureAwait(true);
        if (files.Count > 0) SubtitlePathBox.Text = files[0].Path.LocalPath;
    }

    private void OnOutputFile(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(SelectOutputFileAsync);

    private async Task SelectOutputFileAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save output",
            SuggestedFileName = Path.GetFileName(OutputBox.Text)
        }).ConfigureAwait(true);
        if (file is not null) OutputBox.Text = file.Path.LocalPath;
    }

    private void OnPreviewFile(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(SelectPreviewFileAsync);

    private async Task SelectPreviewFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Preview image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png"] }]
        }).ConfigureAwait(true);
        if (files.Count > 0) { PreviewPathBox.Text = files[0].Path.LocalPath; PreviewTimeBox.Text = ""; }
    }

    private void OnPreviewCurrentFrame(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is not { } editor) return;
        PreviewPathBox.Text = editor.Media.Path;
        PreviewTimeBox.Text = SeekSlider.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OnClearPreview(object? sender, RoutedEventArgs e) => PreviewPathBox.Text = PreviewTimeBox.Text = "";

    private void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (logWindow is { } existing) { existing.Activate(); return; }
        var window = new LogWindow(activityLog) { RequestedThemeVariant = RequestedThemeVariant };
        logWindow = window;
        window.Closed += (_, _) => { if (logWindow == window) logWindow = null; };
        window.Show(this);
    }

    private void SyncCropFromFields()
    {
        if (updatingCrop || initializingEditor || model.Editor is not { } editor) return;
        UpdateOptions();
    }

    private void OnClearCrop(object? sender, RoutedEventArgs e)
    {
        ClearCrop();
    }

    private void ClearCrop()
    {
        updatingCrop = true;
        CropWidthBox.Text = CropHeightBox.Text = CropXBox.Text = CropYBox.Text = "";
        updatingCrop = false;
        CropOverlay.SetCrop(null);
        UpdateOptions();
    }

    private void OnDetectCrop(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(() => DetectAsync(false, sender as Button));

    private void OnDetectInterlace(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(() => DetectAsync(true, sender as Button));

    private async Task DetectAsync(bool interlace, Button? button)
    {
        if (model.Editor is not { } editor || detection.Active) return;
        var generation = sourceGeneration;
        var token = detection.Begin(UpdateOptions);
        if (button is not null) button.IsEnabled = false;
        try
        {
            var detector = new DetectionService(process, ffmpeg);
            var start = editor.Plan.Options?.Start ?? 0;
            var track = editor.Options.VideoTrack;
            if (interlace)
            {
                var detected = await detector.DetectInterlaceAsync(editor.Media.Path, start, track, token)
                    .ConfigureAwait(true);
                if (!token.IsCancellationRequested && generation == sourceGeneration &&
                    detection.Token == token && detected)
                { DeinterlaceBox.IsChecked = true; UpdateOptions(); }
            }
            else
            {
                var crop = await detector.DetectCropAsync(editor.Media.Path, start, track, token)
                    .ConfigureAwait(true);
                if (token.IsCancellationRequested || generation != sourceGeneration || crop is null ||
                    detection.Token != token) return;
                if (FfmpegDetection.IsFullFrame(crop, editor.Media, track)) ClearCrop();
                else CropOverlay.ApplyDetectedCrop(crop);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == sourceGeneration) ShowError(ex.Message); }
        finally
        {
            detection.End(token);
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void UpdateOptions()
    {
        if (initializingEditor || model.Editor is not { } editor) return;
        detection.CancelForEdit();
        var o = editor.Options;
        ApplyVideoOptions(editor, o);
        ApplyOutputOptions(editor, o);
        ApplyEditorState(editor);
        RefreshOutputPath();
        ApplyEditorPresentation(editor, o);
    }

    private void ApplyVideoOptions(EditorViewModel editor, OptionInputs o)
    {
        o.CropWidth = CropWidthBox.Text ?? ""; o.CropHeight = CropHeightBox.Text ?? "";
        o.CropX = CropXBox.Text ?? ""; o.CropY = CropYBox.Text ?? "";
        o.ScaleWidth = ScaleWidthBox.Text ?? ""; o.ScaleHeight = ScaleHeightBox.Text ?? "";
        o.FixSar = FixSarBox.IsChecked == true; o.Deinterlace = DeinterlaceBox.IsChecked == true;
        o.VideoTrack = Math.Max(0, VideoTrackBox.SelectedIndex);
        if (editor.Media.VideoTracks.Count > o.VideoTrack)
        {
            var track = editor.Media.VideoTracks[o.VideoTrack];
            CropOverlay.SourceWidth = track.Width > 0 ? track.Width : editor.Media.Width;
            CropOverlay.SourceHeight = track.Height > 0 ? track.Height : editor.Media.Height;
            CropOverlay.Sar = track.Sar > 0 ? track.Sar : editor.Media.Sar;
            VideoStage.SetSource(CropOverlay.SourceWidth, CropOverlay.SourceHeight, CropOverlay.Sar);
        }
    }

    private void ApplyOutputOptions(EditorViewModel editor, OptionInputs o)
    {
        o.AudioTrack = Math.Max(0, AudioTrackBox.SelectedIndex);
        o.HasAudio = HasAudioBox.IsChecked == true;
        o.SubtitleTrack = SubtitleTrackBox.SelectedIndex > 0 ? SubtitleTrackBox.SelectedIndex - 1 : null;
        o.SubtitlePath = SubtitlePathBox.Text ?? ""; o.BurnSubtitles = BurnSubsBox.IsChecked == true;
        o.AudioCodec = (AudioCodecBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "aac";
        o.AudioBitrate = AudioBitrateBox.Text ?? ""; o.Amplify = AmplifyBox.Text ?? "";
        o.FadeIn = FadeInBox.Text ?? ""; o.FadeOut = FadeOutBox.Text ?? "";
        o.VideoCodec = (VideoCodecBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "x264";
        o.Preset = (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "medium";
        o.Limit = LimitBox.Text ?? ""; o.Quality = QualityBox.Text ?? "";
        o.LimitMode = LimitModeBox.IsChecked == true; o.CrfMode = CrfModeBox.IsChecked == true;
        o.Start = StartBox.Text ?? ""; o.End = EndBox.Text ?? "";
        RefreshTimelineHighlight();
        o.TwoPass = TwoPassBox.IsChecked == true;
        o.Threads = ThreadsBox.Text ?? "";
        o.Multithreading = (MultithreadingBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "row-mt";
        editor.RawArguments = RawArgumentsBox.Text ?? "";
    }

    private static void ApplyEditorState(EditorViewModel editor)
    {
        editor.Refresh();
    }

    private void ApplyEditorPresentation(EditorViewModel editor, OptionInputs o)
    {
        CropOverlay.SetCrop(editor.VisibleCrop);
        if (!string.Equals(RawArgumentsBox.Text, editor.RawArguments, StringComparison.Ordinal)) RawArgumentsBox.Text = editor.RawArguments;
        ValidationText.Text = string.Join("; ", editor.Errors.Select(error => $"{error.Field}: {error.Message}"));
        player?.SetDeinterlace(o.Deinterlace);
        if (editor.Media.VideoTracks.Count > o.VideoTrack)
            player?.SetTrack(TrackKind.Video, editor.Media.VideoTracks[o.VideoTrack].Id);
        if (o.HasAudio && editor.Media.AudioTracks.Count > o.AudioTrack)
            player?.SetTrack(TrackKind.Audio, editor.Media.AudioTracks[o.AudioTrack].Id);
        else player?.SetTrack(TrackKind.Audio, null);
        player?.SetTrack(TrackKind.Subtitle,
            o.SubtitleTrack is { } sub ? editor.Media.SubtitleTracks[sub].Id : null);
    }

    private void RefreshTimelineHighlight()
    {
        if (model.Editor is not { } editor) return;
        var highlight = PlaybackTimeline.HighlightBounds(editor.Media.Duration, StartBox.Text ?? "", EndBox.Text ?? "");
        SeekSlider.HighlightStart = highlight.Start;
        SeekSlider.HighlightEnd = highlight.End;
    }

    private void RefreshOutputPath()
    {
        if (!autoOutputPath || model.Editor is not { } editor) return;
        var directory = defaultOutputDirectory();
        if (string.IsNullOrWhiteSpace(directory)) return;
        var next = OutputPaths.NextAvailable(directory, sourceSaveAs ?? editor.Media.Path,
            editor.Container, File.Exists);
        updatingOutput = true;
        OutputBox.Text = next;
        updatingOutput = false;
    }

    private void OnModeChanged(CheckBox changed)
    {
        if (initializingEditor || syncingModes || model.Editor is not { } editor || player is null) return;
        UpdateOptions();
        syncingModes = true;
        if (changed == CrfModeBox) editor.SetCrfMode(changed.IsChecked == true);
        else if (changed == LimitModeBox) editor.SetLimitMode(changed.IsChecked == true);
        else editor.SetTwoPass(changed.IsChecked == true);
        CrfModeBox.IsChecked = editor.Options.CrfMode;
        LimitModeBox.IsChecked = editor.Options.LimitMode;
        TwoPassBox.IsChecked = editor.Options.TwoPass;
        LimitBox.Text = editor.Options.Limit;
        syncingModes = false;
        UpdateOptions();
    }

    private void OnAudioCodecChanged()
    {
        if (initializingEditor || syncingAudioCodec || model.Editor is not { } editor || player is null) return;
        syncingAudioCodec = true;
        var selected = (AudioCodecBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "aac";
        editor.SelectAudioCodec(selected);
        var index = editor.Options.AudioCodec switch { "opus" => 1, "vorbis" => 2, "copy" => 3, _ => 0 };
        AudioCodecBox.SelectedIndex = index;
        AudioBitrateBox.Text = editor.Options.AudioBitrate;
        syncingAudioCodec = false;
        UpdateOptions();
    }

    private void OnEditorOptionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender == VideoCodecBox && VideoCodecBox.SelectedIndex == 0 && AudioCodecBox.SelectedIndex is 1 or 2)
            AudioCodecBox.SelectedIndex = 0;
        if (sender == VideoTrackBox && model.Editor is { } editor &&
            VideoTrackBox.SelectedIndex is var index && index >= 0 && index < editor.Media.VideoTracks.Count)
            FixSarBox.IsChecked = editor.Media.VideoTracks[index].Sar != 1;
        if (sender == SubtitleTrackBox) UpdateSubtitleAvailability();
        if (!initializingEditor && player is not null && model.Editor is not null) UpdateOptions();
    }

    private void UpdateSubtitleAvailability()
    {
        var available = model.Editor is { } editor &&
            SubtitleAvailability.CanBurn(editor.Media, SubtitleTrackBox.SelectedIndex, SubtitlePathBox.Text);
        BurnSubsBox.IsEnabled = available;
        ToolTip.SetTip(BurnSubsBox, available ? "Burn selected subtitles into the video" :
            "Select an embedded subtitle track or an existing external subtitle file first");
        if (!available && BurnSubsBox.IsChecked == true) BurnSubsBox.IsChecked = false;
    }

    private void OnEncode(object? sender, RoutedEventArgs e) =>
        _ = RunUiOperationAsync(StartEncodeAsync);

    internal async Task StartEncodeAsync()
    {
        if (encoding || model.Editor is not { } editor) return;
        EncodeProgressSession? progressSession = null;
        try
        {
            if (!TryPrepareEncode(editor, out var rawArguments, out var output)) return;
            encoding = true;
            lastOutput = null;
            SetEncodingControls(false);
            progressSession = new EncodeProgressSession();
            encodeProgress = progressSession;
            EncodeProgress.Value = 0;
            var token = progressSession.Token;
            var progress = createEncodeProgress(update => ApplyEncodeUpdate(update, token));
            var service = new EncodeService(process, ffmpeg);
            var preview = CreateEncodePreview(editor);
            await service.EncodeAsync(editor.Plan.Options!, output,
                TitleBox.Text ?? "", editor.Options.TwoPass, false, progress, token, preview,
                rawArguments).ConfigureAwait(true);
            CompleteEncode(output, token);
        }
        catch (OperationCanceledException) { activityLog.AppendLine("Cancelled"); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            if (progressSession?.Token.IsCancellationRequested == true) EncodeProgress.Value = 0;
            encoding = false;
            SetEncodingControls(true);
            progressSession?.Dispose();
            if (encodeProgress == progressSession) encodeProgress = null;
        }
    }

    private bool TryPrepareEncode(EditorViewModel editor, out string[] rawArguments, out string output)
    {
        rawArguments = [];
        output = "";
        if (BurnSubsBox.IsChecked == true &&
            !SubtitleAvailability.CanBurn(editor.Media, SubtitleTrackBox.SelectedIndex, SubtitlePathBox.Text))
        {
            ShowError("Selected subtitle source is no longer available");
            return false;
        }
        UpdateOptions();
        if (!editor.CanEncode) return false;
        if (string.IsNullOrWhiteSpace(editor.RawArguments)) throw new FormatException("FFmpeg arguments are empty");
        rawArguments = FfmpegArguments.ParseRaw(editor.RawArguments);
        output = OutputBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(output) || !Directory.Exists(Path.GetDirectoryName(output)))
            throw new InvalidOperationException("Choose a writable output folder");
        return true;
    }

    private PreviewSource? CreateEncodePreview(EditorViewModel editor)
    {
        if (string.IsNullOrWhiteSpace(PreviewPathBox.Text)) return null;
        if (!File.Exists(PreviewPathBox.Text)) throw new FileNotFoundException("Preview source not found");
        var time = string.IsNullOrWhiteSpace(PreviewTimeBox.Text) ? (double?)null :
            OptionEngine.ParseTime(PreviewTimeBox.Text);
        var track = editor.Media.VideoTracks[editor.Options.VideoTrack];
        return new(PreviewPathBox.Text, time, editor.Plan.Options!.FinalWidth,
            editor.Plan.Options.FinalHeight, track.Sar, track.Fps is > 0 ? track.Fps : 25);
    }

    private void CompleteEncode(string output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EncodeProgress.Value = 100;
        lastOutput = output;
        RefreshOutputPath();
    }

    private void ApplyEncodeUpdate(EncodeUpdate update, CancellationToken token)
    {
        if (token.IsCancellationRequested || encodeProgress?.Token != token) return;
        activityLog.AppendLine(update.Line);
        EncodeProgress.Value = update.Percent;
    }

    private void SetEncodingControls(bool enabled)
    {
        CropOverlay.IsEnabled = enabled;
        EncodeButton.IsEnabled = enabled;
        ChangeSourceButton.IsEnabled = enabled;
    }

    internal void CancelEncode()
    {
        if (encodeProgress is null) return;
        encodeProgress.Cancel();
        EncodeProgress.Value = 0;
    }

    private void OnCancelEncode(object? sender, RoutedEventArgs e)
    {
        CancelEncode();
    }
    private void OnOpenOutput(object? sender, RoutedEventArgs e)
    {
        var output = lastOutput;
        if (output is null || !File.Exists(output)) return;
        openPath(output);
    }
    private void OnShowOutputFolder(object? sender, RoutedEventArgs e)
    {
        var directory = OutputPaths.ExistingDirectory(lastOutput ?? OutputBox.Text, Directory.Exists);
        if (directory is not null)
            openPath(directory);
        else ShowError("Choose an output folder first");
    }

    private void ShowError(string message)
    {
        model.Status = message;
        StatusText.Text = message;
        ValidationText.Text = message;
        activityLog.AppendLine(message);
    }

    private static string? DefaultOutputDirectory()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) return directory;
        directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
    }

    private static void OpenPath(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            RequestedThemeVariant = ThemeMode.Resolve((combo.SelectedItem as ComboBoxItem)?.Content?.ToString());
            if (logWindow is { } window) window.RequestedThemeVariant = RequestedThemeVariant;
        }
    }

    private async Task RunUiOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

}
