namespace Kurippu.Desktop.Media;

public sealed class RenderFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
