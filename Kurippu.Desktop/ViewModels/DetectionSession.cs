namespace Kurippu.Desktop.ViewModels;

public sealed class DetectionSession : IDisposable
{
    private CancellationTokenSource? current;
    public bool Active => current is not null;

    public CancellationToken Begin(Action synchronize)
    {
        if (Active) throw new InvalidOperationException("Detection already running");
        synchronize();
        current = new CancellationTokenSource();
        return current.Token;
    }

    public void CancelForEdit() => current?.Cancel();

    public void End(CancellationToken token)
    {
        if (current?.Token != token) return;
        current.Dispose();
        current = null;
    }

    public void Dispose()
    {
        current?.Cancel();
        current?.Dispose();
        current = null;
    }
}
