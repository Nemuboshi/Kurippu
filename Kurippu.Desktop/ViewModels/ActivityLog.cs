using System.ComponentModel;

namespace Kurippu.Desktop.ViewModels;

public sealed class ActivityLog : INotifyPropertyChanged
{
    public string Text { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AppendLine(string line)
    {
        Text += line + Environment.NewLine;
        PropertyChanged?.Invoke(this, new(nameof(Text)));
    }

    public void Clear()
    {
        Text = "";
        PropertyChanged?.Invoke(this, new(nameof(Text)));
    }
}
