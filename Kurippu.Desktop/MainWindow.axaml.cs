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

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel model = new();
    private readonly IProcessRunner process = new ProcessRunner();
    private MpvPlayer? player;
    private SourceService? sourceService;
    private SourceChoices? sourceChoices;
    private CancellationTokenSource? downloadCancellation;
    private string? downloadedSource;
    private int sourceGeneration;
    private CancellationTokenSource? encodeCancellation;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        VideoSurface.TimeChanged += OnTimeChanged;
        CropOverlay.CropChanged += crop =>
        {
            updatingCrop = true;
            CropWidthBox.Text = crop?.Width.ToString() ?? "";
            CropHeightBox.Text = crop?.Height.ToString() ?? "";
            CropXBox.Text = crop?.X.ToString() ?? "";
            CropYBox.Text = crop?.Y.ToString() ?? "";
            updatingCrop = false;
            UpdateOptions();
        };
        CropOverlay.Clicked += () => { if (!encoding) OnPlayPause(null, null!); };
        CropOverlay.PointerWheelChanged += (_, args) =>
        {
            if (encoding || player is null) return;
            if (args.Delta.Y > 0) player.FrameStep(); else player.FrameBackStep();
            args.Handled = true;
        };
        foreach (var box in new[] { CropWidthBox, CropHeightBox, CropXBox, CropYBox })
            box.TextChanged += (_, _) => SyncCropFromFields();
        foreach (var box in new[] { ScaleWidthBox, ScaleHeightBox, SubtitlePathBox, AudioBitrateBox,
            AmplifyBox, FadeInBox, FadeOutBox, LimitBox, QualityBox, StartBox, EndBox, ThreadsBox })
            box.TextChanged += (_, _) => { if (player is not null && model.Editor is not null) UpdateOptions(); };
        foreach (var combo in new[] { PresetBox, MultithreadingBox })
            combo.SelectionChanged += (_, _) => { if (player is not null && model.Editor is not null) UpdateOptions(); };
        AudioCodecBox.SelectionChanged += (_, _) => OnAudioCodecChanged();
        SubtitlePathBox.TextChanged += (_, _) => UpdateSubtitleAvailability();
        foreach (var check in new[] { FixSarBox, BurnSubsBox })
            check.PropertyChanged += (_, args) => {
                if (args.Property == ToggleButton.IsCheckedProperty && player is not null && model.Editor is not null)
                    UpdateOptions();
            };
        foreach (var check in new[] { LimitModeBox, CrfModeBox, TwoPassBox })
            check.PropertyChanged += (_, args) => {
                if (args.Property == ToggleButton.IsCheckedProperty) OnModeChanged(check);
            };
        OutputBox.TextChanged += (_, _) => { if (!updatingOutput) autoOutputPath = false; };
        VideoSurface.RenderFailed += error => ShowError(error.Message);
        VideoSurface.RenderRecovered += () => StatusText.Text = model.Status;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += (_, _) => {
            sourceGeneration++;
            downloadCancellation?.Cancel(); encodeCancellation?.Cancel(); detection.Dispose();
            logWindow?.Close();
            VideoSurface.Dispose(); player?.Dispose(); sourceService?.Dispose();
        };
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Video") { Patterns = ["*.mkv", "*.mp4", "*.webm", "*.mov", "*.avi", "*.ts", "*.wmv"] }, FilePickerFileTypes.All]
        });
        if (files.Count > 0) await OpenSourceAsync(files[0].Path.LocalPath);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (encoding) return;
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (path is not null && File.Exists(path)) await OpenSourceAsync(path);
    }

    private async void OnOpenUrl(object? sender, RoutedEventArgs e)
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
            sourceChoices = await sourceService.ResolveAsync(uri.AbsoluteUri, cancellation.Token);
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

    private async void OnDownloadUrl(object? sender, RoutedEventArgs e)
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
                choice.Subtitles[UrlSubtitleBox.SelectedIndex].Id, progress, cancellation.Token);
            FormatPanel.IsVisible = false;
            sourceTitle = choice.Title;
            sourceSaveAs = choice.SaveAs;
            await OpenSourceAsync(path, preserveSourceMetadata: true);
            if (player is not null && model.Editor?.Media.Path == path) downloadedSource = path;
            else sourceService.Release(path);
        }
        catch (OperationCanceledException) { StatusText.Text = "Download cancelled"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (downloadCancellation == cancellation) { cancellation.Dispose(); downloadCancellation = null; CancelDownloadButton.IsVisible = false; } }
    }

    private void OnCancelDownload(object? sender, RoutedEventArgs e) => downloadCancellation?.Cancel();

    private async Task OpenSourceAsync(string path, bool preserveSourceMetadata = false)
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
            if (downloadedSource is { } previous && previous != path)
            {
                sourceService?.Release(previous);
                downloadedSource = null;
            }
            player = new MpvPlayer();
            var media = await player.OpenAsync(path);
            if (generation != sourceGeneration) return;
            initializingEditor = true;
            model.SetMedia(media);
            VideoSurface.Player = player;
            SeekSlider.Maximum = Math.Max(1, media.Duration);
            SeekSlider.Value = 0;
            muted = false;
            MuteButton.Content = "Mute";
            MediaDetails.Text = $"Path: {media.Path}\nTitle: {SourceTitle.DisplayTitle(media.Title, sourceTitle)}\n" +
                $"Duration: {PlaybackTimeline.ShowTime(media.Duration)}\n" +
                $"Bitrate: {(media.Bitrate > 0 ? $"{media.Bitrate / 1000:0} Kbps" : "unknown")}\n" +
                $"Size: {(media.FileSize > 0 ? $"{media.FileSize / 1048576d:0.00} MiB" : "unknown")}\n" +
                $"Format: {media.Format}\n" +
                string.Join("\n", media.VideoTracks.Select((t, i) =>
                    $"Video #{i}: {t.Codec}, {t.Width}x{t.Height}, SAR {t.Sar:0.###}, {t.Fps:0.###} fps")) + "\n" +
                string.Join("\n", media.AudioTracks.Select((t, i) =>
                    $"Audio #{i}: {t.Codec}, {t.Channels} channels, {t.SampleRate} Hz, {t.Language}")) + "\n" +
                string.Join("\n", media.SubtitleTracks.Select((t, i) =>
                    $"Subtitle #{i}: {t.Codec}, {t.Language}, {t.Title}"));
            VideoTrackBox.ItemsSource = media.VideoTracks.Select((t, i) => $"{i}: {t.Codec} {t.Width}x{t.Height}").ToArray();
            VideoTrackBox.SelectedIndex = 0;
            AudioTrackBox.ItemsSource = media.AudioTracks.Select((t, i) => $"{i}: {t.Codec} {t.Language}").ToArray();
            AudioTrackBox.SelectedIndex = media.AudioTracks.Count > 0 ? 0 : -1;
            SubtitleTrackBox.ItemsSource = new[] { "None" }.Concat(media.SubtitleTracks.Select((t, i) => $"{i}: {t.Codec} {t.Language}")).ToArray();
            SubtitleTrackBox.SelectedIndex = 0;
            UpdateSubtitleAvailability();
            HasAudioBox.IsChecked = media.AudioTracks.Count > 0;
            CropOverlay.SourceWidth = media.Width;
            CropOverlay.SourceHeight = media.Height;
            CropOverlay.Sar = media.Sar;
            VideoStage.SetSource(media.Width, media.Height, media.Sar);
            CropOverlay.SetCrop(null);
            updatingCrop = true;
            CropWidthBox.Text = CropHeightBox.Text = CropXBox.Text = CropYBox.Text = "";
            updatingCrop = false;
            foreach (var box in new[] { ScaleWidthBox, ScaleHeightBox, SubtitlePathBox, AudioBitrateBox,
                AmplifyBox, FadeInBox, FadeOutBox, LimitBox, QualityBox, StartBox, EndBox }) box.Text = "";
            ThreadsBox.Text = "8";
            DeinterlaceBox.IsChecked = BurnSubsBox.IsChecked = LimitModeBox.IsChecked = TwoPassBox.IsChecked = false;
            PreviewPathBox.Text = PreviewTimeBox.Text = "";
            VideoCodecBox.SelectedIndex = 0;
            AudioCodecBox.SelectedIndex = 0;
            MultithreadingBox.SelectedIndex = 0;
            PresetBox.SelectedIndex = 4;
            CrfModeBox.IsChecked = true;
            FixSarBox.IsChecked = media.Sar != 1;
            RawArgumentsBox.Text = model.Editor!.RawArguments;
            initializingEditor = false;
            UpdateOptions();
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
            player.Pause();
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

    internal async Task RunOpenSmokeAsync()
    {
        var ffmpeg = ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("FFmpeg not found");
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var video = Path.Combine(directory, "source.mp4");
            var args = new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i",
                "testsrc=size=320x180:rate=25", "-t", "3", "-c:v", "mpeg4", video };
            if (await process.RunAsync(ffmpeg, args, null, CancellationToken.None) != 0)
                throw new InvalidOperationException("UI fixture encode failed");
            await OpenSourceAsync(video);
            if (model.Editor is null || !EditorPanel.IsVisible || MediaDetails.Text?.Contains("320x180") != true)
                throw new InvalidOperationException($"UI open failed: page={model.Page}, status={StatusText.Text}, details={MediaDetails.Text}");
            if (EditorTabs.Items.Count != 4)
                throw new InvalidOperationException("Inspector must have Info, Picture, Audio and Export tabs");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!VideoSurface.HasFrame && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
            if (!VideoSurface.HasFrame)
                throw new InvalidOperationException($"UI surface missing: window={Bounds}, editor={EditorPanel.Bounds}, parent={((Control)VideoSurface.Parent!).Bounds}, bounds={VideoSurface.Bounds}, attached={TopLevel.GetTopLevel(VideoSurface) is not null}, status={StatusText.Text}");
            if (Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1 ||
                EditorPanel.Bounds.Width > Bounds.Width - 32 + 1)
                throw new InvalidOperationException($"Desktop panel alignment or horizontal overflow: preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}, editor={EditorPanel.Bounds}, window={Bounds}");
            EditorTabs.SelectedIndex = 3;
            AdvancedSettings.IsExpanded = true;
            await Task.Delay(100);
            var exportScroll = (ScrollViewer)((TabItem)EditorTabs.Items[3]!).Content!;
            if (Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1 ||
                exportScroll.Extent.Width > exportScroll.Viewport.Width + 1)
                throw new InvalidOperationException($"Expanded export misaligns panels or scrolls horizontally: preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}, exportExtent={exportScroll.Extent}, exportViewport={exportScroll.Viewport}");
            AdvancedSettings.IsExpanded = false;
            EditorTabs.SelectedIndex = 1;
            OnOpenLog(null, null!);
            if (logWindow is not { IsVisible: true })
                throw new InvalidOperationException("Detail log did not open in its own window");
            activityLog.AppendLine("Live log update");
            await Task.Delay(100);
            if (!logWindow.DisplayedText.Contains("Live log update"))
                throw new InvalidOperationException("Detail log window did not receive live output");
            ThemeBox.SelectedIndex = 2;
            if (logWindow.RequestedThemeVariant != ThemeMode.Resolve("Dark"))
                throw new InvalidOperationException("Detail log window did not follow the selected theme");
            ThemeBox.SelectedIndex = 0;
            logWindow.Close();
            OnOpenLog(null, null!);
            if (logWindow is not { IsVisible: true } || !logWindow.DisplayedText.Contains("Live log update"))
                throw new InvalidOperationException("Reopened detail log lost output");
            var logRight = LogButton.TranslatePoint(new Point(LogButton.Bounds.Width, 0), this)?.X;
            var themeLeft = ThemeBox.TranslatePoint(default, this)?.X;
            if (logRight is null || themeLeft is null || logRight > themeLeft)
                throw new InvalidOperationException("Detail log button is not left of the theme selector");
            logWindow.Close();
            activityLog.Clear();
            if (player?.Property("pause") != "yes")
                throw new InvalidOperationException("Opening a source started playback");
            if (SeekSlider.HighlightStart != 0 || Math.Abs(SeekSlider.HighlightEnd - model.Editor.Media.Duration) > .001)
                throw new InvalidOperationException("Unmarked timeline is not fully highlighted");
            if (BurnSubsBox.IsEnabled)
                throw new InvalidOperationException("Subtitle burn enabled without a subtitle source");
            SeekSlider.Value = 0.5;
            OnMarkStart(null, null!);
            SeekSlider.Value = 2;
            OnMarkEnd(null, null!);
            if (StartBox.Text != "00:00.500" || EndBox.Text != "00:02.000" ||
                !StartBox.IsEffectivelyVisible || !EndBox.IsEffectivelyVisible)
                throw new InvalidOperationException($"Fragment marks not visible next to transport: {StartBox.Text} - {EndBox.Text}");
            if (Math.Abs(SeekSlider.HighlightStart - .5) > .001 || Math.Abs(SeekSlider.HighlightEnd - 2) > .001)
                throw new InvalidOperationException($"Timeline highlight does not follow fragment marks: {SeekSlider.HighlightStart}-{SeekSlider.HighlightEnd}; marks={StartBox.Text}-{EndBox.Text}; duration={model.Editor.Media.Duration}");
            StartBox.Text = "00:00.250";
            await Task.Delay(100);
            if (Math.Abs(SeekSlider.HighlightStart - .25) > .001)
                throw new InvalidOperationException("Editing a fragment time did not update the timeline");
            SeekSlider.Value = 1;
            PlayButton.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.OemOpenBrackets });
            if (StartBox.Text != "00:01.000")
                throw new InvalidOperationException("[ shortcut failed to mark start");
            SeekSlider.Value = 2;
            PlayButton.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.OemCloseBrackets });
            if (EndBox.Text != "00:02.000")
                throw new InvalidOperationException("] shortcut failed to mark end");
            SetEncodingControls(false);
            if (!PlayButton.IsEnabled) throw new InvalidOperationException("Playback disabled during encoding");
            OnPlayPause(null, null!);
            if (player?.Property("pause") != "no")
                throw new InvalidOperationException("Playback cannot start during encoding");
            OnPlayPause(null, null!);
            if (player?.Property("pause") != "yes")
                throw new InvalidOperationException("Playback cannot pause during encoding");
            SetEncodingControls(true);
            using (var cancellation = new CancellationTokenSource())
            {
                encodeCancellation = cancellation;
                EncodeProgress.Value = 43;
                OnCancelEncode(null, null!);
                if (!cancellation.IsCancellationRequested || EncodeProgress.Value != 0)
                    throw new InvalidOperationException($"Cancel did not reset encoding progress: {EncodeProgress.Value}");
                ApplyEncodeUpdate(new EncodeUpdate("late progress", 70), cancellation.Token);
                if (EncodeProgress.Value != 0)
                    throw new InvalidOperationException("Queued progress reappeared after cancel");
                encodeCancellation = null;
            }
            if (this.FindControl<Button>("TestEncodeButton") is not null)
                throw new InvalidOperationException("Test encode is still exposed in the workbench");
            CropWidthBox.Text = "160";
            CropHeightBox.Text = "90";
            await Task.Delay(100);
            var cropBefore = CropOverlay.Crop;
            if (cropBefore is null) throw new InvalidOperationException($"Crop setup failed: {ValidationText.Text}; fields={CropWidthBox.Text}/{CropHeightBox.Text}; options={model.Editor?.Options.CropWidth}/{model.Editor?.Options.CropHeight}; plan={model.Editor?.VisibleCrop}; start={StartBox.Text}; end={EndBox.Text}");
            var subtitles = Path.Combine(directory, "external.srt");
            await File.WriteAllTextAsync(subtitles, "1\n00:00:00,000 --> 00:00:01,000\nHello");
            SubtitlePathBox.Text = subtitles;
            await Task.Delay(100);
            if (!BurnSubsBox.IsEnabled) throw new InvalidOperationException("External subtitles did not enable burning");
            BurnSubsBox.IsChecked = true;
            if (CropOverlay.Crop != cropBefore) throw new InvalidOperationException("Subtitle option removed existing crop");
            SubtitlePathBox.Text = "";
            await Task.Delay(100);
            if (BurnSubsBox.IsEnabled || BurnSubsBox.IsChecked == true || CropOverlay.Crop != cropBefore)
                throw new InvalidOperationException("Unavailable subtitles left invalid burn or erased crop");
            CropWidthBox.Text = CropHeightBox.Text = "";
            StartBox.Text = EndBox.Text = "";
            if (Math.Abs(VideoSurface.Bounds.Width / VideoSurface.Bounds.Height - 16d / 9) > 0.02 ||
                Math.Abs(VideoSurface.Bounds.Width - CropOverlay.Bounds.Width) > 1 ||
                Math.Abs(VideoSurface.Bounds.Height - CropOverlay.Bounds.Height) > 1 ||
                InspectorPane.Bounds.Width < 300)
                throw new InvalidOperationException($"Workbench geometry invalid: video={VideoSurface.Bounds}, crop={CropOverlay.Bounds}, inspector={InspectorPane.Bounds}");
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
            if (lightBackground == darkBackground)
                throw new InvalidOperationException("Workbench background did not follow theme selection");
            ThemeBox.SelectedIndex = 0;
            Exception? sizeError = null;
            VideoSurface.RenderFailed += error => sizeError = error;
            VideoSurface.Width = 9000;
            var sizeDeadline = DateTime.UtcNow.AddSeconds(3);
            while (sizeError is null && DateTime.UtcNow < sizeDeadline) await Task.Delay(50);
            if (sizeError is not ArgumentOutOfRangeException)
                throw new InvalidOperationException($"Oversized preview did not report its limit: {sizeError}");
            var recoveredFrames = 0;
            VideoSurface.TimeChanged += _ => recoveredFrames++;
            VideoSurface.Width = double.NaN;
            var recoveryDeadline = DateTime.UtcNow.AddSeconds(3);
            while (recoveredFrames == 0 && DateTime.UtcNow < recoveryDeadline) await Task.Delay(50);
            if (recoveredFrames == 0) throw new InvalidOperationException("Preview did not resume after resizing");
            Width = MinWidth;
            Height = MinHeight;
            EditorTabs.SelectedIndex = 3;
            await Task.Delay(200);
            if (InspectorPane.GetValue(Grid.ColumnProperty) != 1 ||
                Math.Abs(PreviewPane.Bounds.Height - InspectorPane.Bounds.Height) > 1)
                throw new InvalidOperationException($"Minimum window must keep equal-height side-by-side panels: preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}");
            if (InspectorPane.Bounds.Height < 300)
                throw new InvalidOperationException($"Inspector too short at minimum window: {InspectorPane.Bounds}");
            if (EditorPanel.Bounds.Width > Bounds.Width - 32 + 1)
                throw new InvalidOperationException($"Minimum workbench overflows horizontally: editor={EditorPanel.Bounds}, window={Bounds}");
            AdvancedSettings.IsExpanded = true;
            RawArgumentsBox.BringIntoView();
            await Task.Delay(100);
            if (InspectorPane.Bounds.Width < 300 || PreviewPane.Bounds.Width < 400 ||
                StatusText.Bounds.Width <= 0 || EncodeButton.Bounds.Width <= 0 ||
                RawArgumentsBox.Bounds.Width <= 0 || LogButton.Bounds.Width <= 0)
                throw new InvalidOperationException($"Narrow workbench inaccessible: preview={PreviewPane.Bounds}, inspector={InspectorPane.Bounds}, export={EncodeButton.Bounds}");
            var progressRight = EncodeProgress.TranslatePoint(new Point(EncodeProgress.Bounds.Width, 0), this)?.X;
            if (progressRight is null || progressRight > Bounds.Width)
                throw new InvalidOperationException($"Encode controls clipped at minimum width: right={progressRight}, window={Bounds.Width}");
            EditorPanel.IsVisible = false;
            SourceScroll.IsVisible = true;
            FormatPanel.IsVisible = true;
            CancelDownloadButton.IsVisible = true;
            await Task.Delay(100);
            CancelDownloadButton.BringIntoView();
            await Task.Delay(100);
            var cancelBottom = CancelDownloadButton.TranslatePoint(new Point(0, CancelDownloadButton.Bounds.Height), this)?.Y;
            if (cancelBottom is null || cancelBottom > Bounds.Height - StatusText.Bounds.Height)
                throw new InvalidOperationException($"URL actions clipped at minimum window: bottom={cancelBottom}");
        }
        finally { Directory.Delete(directory, recursive: true); }
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

    private void OnTimeChanged(double time)
    {
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
        if (player.Property("pause") == "yes" && model.Editor is { } editor &&
            PlaybackTimeline.ResumeTarget(SeekSlider.Value, editor.Media.Duration, LoopBox.IsChecked == true,
                StartBox.Text ?? "", EndBox.Text ?? "") is { } target) player.Seek(target);
        if (player.Property("pause") == "yes") player.Play(); else player.Pause();
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
            if (mark == "start") OnMarkStart(sender, e);
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

    private async void OnSubtitleFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Subtitle file", AllowMultiple = false });
        if (files.Count > 0) SubtitlePathBox.Text = files[0].Path.LocalPath;
    }
    private async void OnOutputFile(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save output",
            SuggestedFileName = Path.GetFileName(OutputBox.Text) });
        if (file is not null) OutputBox.Text = file.Path.LocalPath;
    }
    private async void OnPreviewFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Preview image",
            AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png"] }] });
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

    private async void OnDetectCrop(object? sender, RoutedEventArgs e) => await DetectAsync(false, sender as Button);
    private async void OnDetectInterlace(object? sender, RoutedEventArgs e) => await DetectAsync(true, sender as Button);

    private async Task DetectAsync(bool interlace, Button? button)
    {
        if (model.Editor is not { } editor || detection.Active) return;
        var generation = sourceGeneration;
        var token = detection.Begin(UpdateOptions);
        if (button is not null) button.IsEnabled = false;
        try
        {
            var ffmpeg = ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("Bundled FFmpeg not found");
            var detector = new DetectionService(process, ffmpeg);
            var start = editor.Plan.Options?.Start ?? 0;
            var track = editor.Options.VideoTrack;
            if (interlace)
            {
                var detected = await detector.DetectInterlaceAsync(editor.Media.Path, start, track, token);
                if (!token.IsCancellationRequested && generation == sourceGeneration && detected)
                { DeinterlaceBox.IsChecked = true; UpdateOptions(); }
            }
            else
            {
                var crop = await detector.DetectCropAsync(editor.Media.Path, start, track, token);
                if (token.IsCancellationRequested || generation != sourceGeneration || crop is null) return;
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
        editor.Refresh();
        RefreshOutputPath();
        CropOverlay.SetCrop(editor.VisibleCrop);
        if (RawArgumentsBox.Text != editor.RawArguments) RawArgumentsBox.Text = editor.RawArguments;
        ValidationText.Text = string.Join("; ", editor.Errors.Select(error => $"{error.Field}: {error.Message}"));
        player?.SetDeinterlace(o.Deinterlace);
        if (editor.Media.VideoTracks.Count > o.VideoTrack) player?.SetTrack("vid", editor.Media.VideoTracks[o.VideoTrack].Id);
        if (o.HasAudio && editor.Media.AudioTracks.Count > o.AudioTrack) player?.SetTrack("aid", editor.Media.AudioTracks[o.AudioTrack].Id);
        else player?.SetTrack("aid", null);
        player?.SetTrack("sid", o.SubtitleTrack is { } sub ? editor.Media.SubtitleTracks[sub].Id : null);
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
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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

    private async void OnEncode(object? sender, RoutedEventArgs e) => await StartEncodeAsync();

    private async Task StartEncodeAsync()
    {
        if (encoding || model.Editor is not { } editor) return;
        try
        {
            if (BurnSubsBox.IsChecked == true &&
                !SubtitleAvailability.CanBurn(editor.Media, SubtitleTrackBox.SelectedIndex, SubtitlePathBox.Text))
            {
                ShowError("Selected subtitle source is no longer available");
                return;
            }
            UpdateOptions();
            if (!editor.CanEncode) return;
            if (string.IsNullOrWhiteSpace(editor.RawArguments)) throw new FormatException("FFmpeg arguments are empty");
            var rawArguments = FfmpegArguments.ParseRaw(editor.RawArguments);
            var ffmpeg = ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("Bundled FFmpeg not found");
            var output = OutputBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(output) || !Directory.Exists(Path.GetDirectoryName(output)))
                throw new InvalidOperationException("Choose a writable output folder");
            encoding = true;
            lastOutput = null;
            SetEncodingControls(false);
            encodeCancellation = new CancellationTokenSource();
            var cancellation = encodeCancellation;
            EncodeProgress.Value = 0;
            var progress = new Progress<EncodeUpdate>(update => ApplyEncodeUpdate(update, cancellation.Token));
            var service = new EncodeService(process, ffmpeg);
            PreviewSource? preview = null;
            if (!string.IsNullOrWhiteSpace(PreviewPathBox.Text))
            {
                if (!File.Exists(PreviewPathBox.Text)) throw new FileNotFoundException("Preview source not found");
                var time = string.IsNullOrWhiteSpace(PreviewTimeBox.Text) ? (double?)null :
                    OptionEngine.ParseTime(PreviewTimeBox.Text);
                preview = new(PreviewPathBox.Text, time, editor.Plan.Options!.FinalWidth,
                    editor.Plan.Options.FinalHeight, editor.Media.VideoTracks[editor.Options.VideoTrack].Sar,
                    editor.Media.VideoTracks[editor.Options.VideoTrack].Fps is > 0 ?
                        editor.Media.VideoTracks[editor.Options.VideoTrack].Fps : 25);
            }
            await service.EncodeAsync(editor.Plan.Options!, output,
                TitleBox.Text ?? "", editor.Options.TwoPass, false, progress, encodeCancellation.Token, preview, rawArguments);
            encodeCancellation.Token.ThrowIfCancellationRequested();
            EncodeProgress.Value = 100;
            lastOutput = output;
            RefreshOutputPath();
        }
        catch (OperationCanceledException) { activityLog.AppendLine("Cancelled"); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            if (encodeCancellation?.IsCancellationRequested == true) EncodeProgress.Value = 0;
            encoding = false;
            SetEncodingControls(true);
            encodeCancellation?.Dispose(); encodeCancellation = null;
        }
    }

    private void ApplyEncodeUpdate(EncodeUpdate update, CancellationToken token)
    {
        if (token.IsCancellationRequested || encodeCancellation is null || encodeCancellation.Token != token) return;
        activityLog.AppendLine(update.Line);
        EncodeProgress.Value = update.Percent;
    }

    private void SetEncodingControls(bool enabled)
    {
        CropOverlay.IsEnabled = enabled;
        EncodeButton.IsEnabled = enabled;
        ChangeSourceButton.IsEnabled = enabled;
    }

    private void OnCancelEncode(object? sender, RoutedEventArgs e)
    {
        if (encodeCancellation is null) return;
        encodeCancellation.Cancel();
        EncodeProgress.Value = 0;
    }
    private void OnOpenOutput(object? sender, RoutedEventArgs e)
    {
        var output = lastOutput;
        if (output is null || !File.Exists(output)) return;
        Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
    }
    private void OnShowOutputFolder(object? sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(lastOutput);
        if (directory is not null && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void ShowError(string message)
    {
        model.Status = message;
        StatusText.Text = message;
        ValidationText.Text = message;
        activityLog.AppendLine(message);
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            RequestedThemeVariant = ThemeMode.Resolve((combo.SelectedItem as ComboBoxItem)?.Content?.ToString());
            if (logWindow is { } window) window.RequestedThemeVariant = RequestedThemeVariant;
        }
    }

}
