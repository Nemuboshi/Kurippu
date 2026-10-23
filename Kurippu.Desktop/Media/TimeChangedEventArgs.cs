namespace Kurippu.Desktop.Media;

public sealed class TimeChangedEventArgs(double time) : EventArgs
{
    public double Time { get; } = time;
}
