using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Kurippu.Core;
using Kurippu.Desktop.Media;
using Kurippu.Desktop.Services;
using Kurippu.Desktop.Tests.TestSupport;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class EncodeLifecycleHeadlessTests
{
    // Scenario: Encoding completes while its final progress callback is still queued.
    [AvaloniaFact]
    public async Task EncodingCanFinishWithProgressStillQueuedWithoutLateCallbackCrash()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            Assert.True(await WaitUntilAsync(() => runner.Calls == 1));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(encodeTask.IsCompleted);

            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(progress.PendingCount > 0);

            progress.DispatchAll();
            Assert.True(encodeButton.IsEnabled);
            Assert.Equal(100, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The window closes while a progress callback is still queued.
    [AvaloniaFact]
    public async Task ClosingWhileProgressIsQueuedDropsLateUiWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            Assert.True(await WaitUntilAsync(() => runner.Calls == 1));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));

            window.Close();
            Dispatcher.UIThread.RunJobs();
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(progress.PendingCount > 0);
            progress.DispatchAll();
            Assert.False(window.IsVisible);
            Assert.Equal(0, encodeProgress.Value);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The process has exited but the UI has not finalized the encode task.
    [AvaloniaFact]
    public async Task ClosingAfterProgressIsQueuedFinishesWithoutDispatchingLateUiWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));

            runner.CompleteEncoding();
            await runner.Finished.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(progress.PendingCount > 0);
            var valueAfterClose = encodeProgress.Value;
            progress.DispatchAll();
            Assert.Equal(valueAfterClose, encodeProgress.Value);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Cancel is requested while progress callbacks are still queued.
    [AvaloniaFact]
    public async Task CancelWhileProgressIsQueuedDropsLateProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));

            window.CancelEncode();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            progress.DispatchAll();

            Assert.True(encodeButton.IsEnabled);
            Assert.Equal(0, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The encoder exits with a failure and the window must recover.
    [AvaloniaFact]
    public async Task EncodeFailureRestoresControlsAndReportsStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner(exitCode: 1);
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var status = window.FindControl<TextBlock>("StatusText")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            progress.DispatchAll();

            Assert.True(encodeButton.IsEnabled);
            Assert.Contains("FFmpeg pass 1 failed (1)", status.Text);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A failed encode still has queued progress that arrives later.
    [AvaloniaFact]
    public async Task EncodeFailureWithQueuedProgressDoesNotLetLateCallbackOverwriteError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner(exitCode: 1);
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;
            var status = window.FindControl<TextBlock>("StatusText")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(progress.PendingCount > 0);

            progress.DispatchAll();
            Assert.Contains("FFmpeg pass 1 failed (1)", status.Text);
            Assert.True(encodeProgress.Value < 100);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Cancel and window close happen back-to-back with queued progress.
    [AvaloniaFact]
    public async Task ClosingImmediatelyAfterCancelDoesNotDispatchLateProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));

            window.CancelEncode();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            var valueAfterClose = encodeProgress.Value;
            progress.DispatchAll();

            Assert.Equal(valueAfterClose, encodeProgress.Value);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Show folder is clicked before the output file has been created.
    [AvaloniaFact]
    public async Task ShowFolderOpensThePendingOutputDirectoryBeforeEncodingStarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var opened = new List<string>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>(), opened.Add);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var output = window.FindControl<TextBox>("OutputBox")!;
            Assert.True(File.Exists(output.Text) == false);
            Assert.Equal(Path.GetDirectoryName(output.Text), directory);

            var showFolder = window.FindControl<Button>("ShowOutputFolderButton")!;
            showFolder.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([directory], opened);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Show folder is clicked with no usable output path.
    [AvaloniaFact]
    public async Task ShowFolderReportsWhenNoOutputDirectoryIsAvailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var opened = new List<string>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>(), opened.Add);
            window.Show();
            var output = window.FindControl<TextBox>("OutputBox")!;
            var status = window.FindControl<TextBlock>("StatusText")!;
            output.Text = "";

            var showFolder = window.FindControl<Button>("ShowOutputFolderButton")!;
            showFolder.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Empty(opened);
            Assert.Equal("Choose an output folder first", status.Text);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The real Encode button runs the queued-progress lifecycle.
    [AvaloniaFact]
    public async Task EncodeButtonRunsTheFullQueuedProgressLifecycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            encodeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CompleteEncoding();
            await WaitUntilAsync(() => encodeButton.IsEnabled);

            Assert.True(progress.PendingCount > 0);
            progress.DispatchAll();
            Assert.Equal(100, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The real Cancel button runs the queued-progress lifecycle.
    [AvaloniaFact]
    public async Task CancelButtonRunsTheQueuedProgressLifecycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var cancelButton = window.FindControl<Button>("CancelEncodeButton")!;
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            encodeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            cancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => encodeButton.IsEnabled);
            progress.DispatchAll();

            Assert.Equal(0, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The window closes while the media player is still opening.
    [AvaloniaFact]
    public async Task ClosingWhileMediaIsOpeningDoesNotApplyLateMedia()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        FakeMediaPlayer? player = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            player = new FakeMediaPlayer(blockOpen: true);
            window = CreateWindow(runner, directory, progress, player: player);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            var model = (MainWindowViewModel)window.DataContext!;

            var openTask = window.OpenSourceAsync(source);
            await player.OpenStarted.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            player.CompleteOpen(source);
            await openTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(model.Editor);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Two-pass encoding keeps one progress session until both passes finish.
    [AvaloniaFact]
    public async Task TwoPassEncodingKeepsProgressLifecycleUntilBothPassesFinish()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var twoPass = window.FindControl<CheckBox>("TwoPassBox")!;
            var encodeButton = window.FindControl<Button>("EncodeButton")!;
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;
            twoPass.IsChecked = true;

            var encodeTask = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 2);
            Assert.False(encodeTask.IsCompleted);
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(progress.PendingCount > 0);
            progress.DispatchAll();

            Assert.True(encodeButton.IsEnabled);
            Assert.Equal(100, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: The real Progress<T> implementation drains through the UI dispatcher.
    [AvaloniaFact]
    public async Task RealProgressDispatcherDropsQueuedProgressAfterEncodingCompletes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new Progress<EncodeUpdate>());
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var encodeTask = window.StartEncodeAsync();
            await runner.FirstProgressReported.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(100, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A late callback from a finished encode is dispatched after a new encode starts.
    [AvaloniaFact]
    public async Task LateProgressFromPreviousEncodeCannotOverwriteTheNextEncode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var progress = new ControlledProgress<EncodeUpdate>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, progress);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var encodeProgress = window.FindControl<ProgressBar>("EncodeProgress")!;

            var firstEncode = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 1);
            await progress.Reported.WaitAsync(TimeSpan.FromSeconds(5));
            runner.CompleteFirstPass();
            await firstEncode.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(progress.PendingCount > 0);

            var secondEncode = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 2);
            await WaitUntilAsync(() => progress.ReportCount >= 2);
            Assert.Equal(0, encodeProgress.Value);

            Assert.True(progress.TryDispatchNext());
            Assert.Equal(0, encodeProgress.Value);

            runner.CompleteSecondPass();
            await secondEncode.WaitAsync(TimeSpan.FromSeconds(5));
            progress.DispatchAll();
            Assert.Equal(100, encodeProgress.Value);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Closing the window while crop detection is blocked must not apply its result.
    [AvaloniaFact]
    public async Task ClosingWhileCropDetectionIsRunningDoesNotApplyLateResult()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var runner = new ControlledDetectionRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>());
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var detectButton = window.FindControl<Button>("DetectCropButton")!;
            var crop = window.FindControl<CropOverlayControl>("CropOverlay")!;

            detectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            runner.Complete("lavfi.cropdetect.w=160\nlavfi.cropdetect.h=90\n" +
                "lavfi.cropdetect.x=80\nlavfi.cropdetect.y=45");
            await runner.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.Null(crop.Crop);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Editing an option cancels crop detection and drops its late result.
    [AvaloniaFact]
    public async Task EditingOptionsCancelsDetectionAndDropsLateCropResult()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var runner = new ControlledDetectionRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>());
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var detectButton = window.FindControl<Button>("DetectCropButton")!;
            var crop = window.FindControl<CropOverlayControl>("CropOverlay")!;
            var start = window.FindControl<TextBox>("StartBox")!;

            detectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
            start.Text = "1";
            runner.Complete("lavfi.cropdetect.w=160\nlavfi.cropdetect.h=90\n" +
                "lavfi.cropdetect.x=80\nlavfi.cropdetect.y=45");
            await runner.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.Null(crop.Crop);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A successful encode exposes its real output file to the Open output action.
    [AvaloniaFact]
    public async Task SuccessfulEncodeCanOpenItsOutputFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var opened = new List<string>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>(), opened.Add);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var output = window.FindControl<TextBox>("OutputBox")!;
            var outputPath = Assert.IsType<string>(output.Text);

            var encodeTask = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 1);
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(outputPath, []);

            window.FindControl<Button>("OpenOutputButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([outputPath], opened);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A successful encode exposes its real output directory to Show folder.
    [AvaloniaFact]
    public async Task SuccessfulEncodeCanOpenItsOutputFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var opened = new List<string>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>(), opened.Add);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var output = window.FindControl<TextBox>("OutputBox")!;
            var outputPath = Assert.IsType<string>(output.Text);

            var encodeTask = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 1);
            runner.CompleteEncoding();
            await encodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(outputPath, []);

            window.FindControl<Button>("ShowOutputFolderButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([directory], opened);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: Open output is clicked before any output file exists.
    [AvaloniaFact]
    public async Task OpenOutputWithoutExistingFileDoesNotCrash()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var opened = new List<string>();
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>(), opened.Add);
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);

            window.FindControl<Button>("OpenOutputButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Empty(opened);
            Assert.True(window.IsVisible);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A second Encode request is ignored while the first encode is still running.
    [AvaloniaFact]
    public async Task SecondEncodeClickIsIgnoredWhileFirstIsRunning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var runner = new ControlledProcessRunner();
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>());
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);

            var firstEncode = window.StartEncodeAsync();
            await WaitUntilAsync(() => runner.Calls == 1);
            var secondEncode = window.StartEncodeAsync();
            await secondEncode.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, runner.Calls);

            runner.CompleteEncoding();
            await firstEncode.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(window.FindControl<Button>("EncodeButton")!.IsEnabled);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // Scenario: A new detection can start after an edit cancels the previous run.
    [AvaloniaFact]
    public async Task DetectionCanStartAgainAfterAnEditCancelsTheFirstRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            var runner = new ControlledDetectionRunner(ignoreCancellation: true);
            window = CreateWindow(runner, directory, new ControlledProgress<EncodeUpdate>());
            window.Show();
            var source = Path.Combine(directory, "source.mp4");
            await File.WriteAllBytesAsync(source, []);
            await window.OpenSourceAsync(source);
            var detectButton = window.FindControl<Button>("DetectCropButton")!;
            var start = window.FindControl<TextBox>("StartBox")!;
            var crop = window.FindControl<CropOverlayControl>("CropOverlay")!;

            detectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => runner.Calls == 1);
            start.Text = "1";
            runner.Complete("lavfi.cropdetect.w=160\nlavfi.cropdetect.h=90\n" +
                "lavfi.cropdetect.x=80\nlavfi.cropdetect.y=45");
            await runner.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.True(await WaitUntilAsync(() => detectButton.IsEnabled));
            Assert.Null(crop.Crop);

            detectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.True(await WaitUntilAsync(() => runner.Calls == 2));
            Assert.True(detectButton.IsEnabled);
        }
        finally
        {
            if (window is not null)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MainWindow CreateWindow(IProcessRunner process, string directory,
        ControlledProgress<EncodeUpdate> progress, Action<string>? openPath = null,
        FakeMediaPlayer? player = null) =>
        new(process, "ffmpeg", () => directory, () => Task.FromResult<IMediaPlayer>(player ?? new FakeMediaPlayer()),
            openPath ?? (_ => { }), callback =>
            {
                progress.SetCallback(callback);
                return progress;
            });

    private static MainWindow CreateWindow(IProcessRunner process, string directory,
        IProgress<EncodeUpdate> progress) =>
        new(process, "ffmpeg", () => directory, () => Task.FromResult<IMediaPlayer>(new FakeMediaPlayer()),
            _ => { }, _ => progress);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10).ConfigureAwait(true);
        return condition();
    }
}
