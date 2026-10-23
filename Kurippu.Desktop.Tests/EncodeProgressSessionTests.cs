using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class EncodeProgressSessionTests
{
    [Fact]
    public void TokenCanBeReadByLateCallbackAfterSessionIsDisposed()
    {
        var session = new EncodeProgressSession();

        session.Dispose();

        Assert.False(session.Token.IsCancellationRequested);
    }

    [Fact]
    public void CancelRequestsTheCapturedToken()
    {
        using var session = new EncodeProgressSession();
        var token = session.Token;

        session.Cancel();

        Assert.True(token.IsCancellationRequested);
    }
}
