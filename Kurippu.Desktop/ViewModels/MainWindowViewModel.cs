using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kurippu.Core;

namespace Kurippu.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string page = "source";
    private string status = "Open or drop a video to begin";
    private EditorViewModel? editor;

    public string Page { get => page; private set { page = value; Changed(); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public EditorViewModel? Editor { get => editor; private set { editor = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void BeginProbe() { Page = "info"; Status = "Gathering video info..."; }
    public void SetMedia(MediaInfo media)
    {
        Editor = new(media);
        Page = "encoder";
        Status = media.Title ?? Path.GetFileName(media.Path);
    }
    public void ChangeSource()
    {
        Editor = null;
        Page = "source";
        Status = "Open or drop a video to begin";
    }
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
