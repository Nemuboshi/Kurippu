using System.Collections.Concurrent;

namespace Kurippu.Desktop.Tests.TestSupport;

public sealed class ControlledProgress<T> : IProgress<T>
{
    private readonly ConcurrentQueue<(Action<T> Callback, T Value)> pending = new();
    private readonly TaskCompletionSource firstReport =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<T>? callback;
    private int reportCount;

    public Task Reported => firstReport.Task;
    public int PendingCount => pending.Count;
    public int ReportCount => Volatile.Read(ref reportCount);

    public void SetCallback(Action<T> value) => callback = value;

    public void Report(T value)
    {
        var target = callback ?? throw new InvalidOperationException("Progress callback has not been configured");
        pending.Enqueue((target, value));
        Interlocked.Increment(ref reportCount);
        firstReport.TrySetResult();
    }

    public bool TryDispatchNext()
    {
        if (!pending.TryDequeue(out var item)) return false;
        item.Callback(item.Value);
        return true;
    }

    public int DispatchAll()
    {
        var dispatched = 0;
        while (TryDispatchNext()) dispatched++;
        return dispatched;
    }
}
