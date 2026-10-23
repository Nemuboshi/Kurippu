using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop;

public partial class LogWindow : Window
{
    private readonly ActivityLog log;

    public LogWindow() : this(new ActivityLog()) { }

    public LogWindow(ActivityLog log)
    {
        InitializeComponent();
        this.log = log;
        DataContext = log;
    }

    public string DisplayedText => LogText.Text ?? "";

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrEmpty(LogText.SelectedText) ? LogText.Text : LogText.SelectedText;
        if (text is not null && Clipboard is not null) await Clipboard.SetTextAsync(text);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => LogText.SelectAll();
    private void OnClear(object? sender, RoutedEventArgs e) => log.Clear();
}
