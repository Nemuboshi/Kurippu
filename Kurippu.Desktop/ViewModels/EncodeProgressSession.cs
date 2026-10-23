namespace Kurippu.Desktop.ViewModels;

public sealed class EncodeProgressSession : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();

    public CancellationToken Token { get; }

    public EncodeProgressSession()
    {
        Token = cancellation.Token;
    }

    public void Cancel() => cancellation.Cancel();
    public void Dispose() => cancellation.Dispose();
}
