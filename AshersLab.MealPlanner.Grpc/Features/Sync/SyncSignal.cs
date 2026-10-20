namespace AshersLab.MealPlanner.Grpc.Features.Sync;

/// <summary>
/// The newest seq the log has committed, and a way to wait for it to move - what the <c>Changes</c> stream
/// tells each open browser. One per process; the process is the only writer.
/// </summary>
public sealed class SyncSignal
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _moved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid Epoch { get; private set; }

    public long LatestSeq { get; private set; }

    /// <summary>At startup, from the database, before anything is served.</summary>
    public void Initialise(Guid epoch, long latestSeq)
    {
        Epoch     = epoch;
        LatestSeq = latestSeq;
    }

    /// <summary>After a push commits.</summary>
    public void Published(long latestSeq)
    {
        TaskCompletionSource moved;
        lock (_lock)
        {
            if (latestSeq <= LatestSeq)
                return;

            LatestSeq = latestSeq;
            moved     = _moved;
            _moved    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        moved.TrySetResult();
    }

    /// <summary>
    /// Returns when the seq passes <paramref name="seen"/>, after <paramref name="timeout"/>, or when cancelled -
    /// never throws. The caller re-reads <see cref="LatestSeq"/>.
    /// </summary>
    public async Task WaitAsync(long seen, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task moved;
        lock (_lock)
        {
            if (LatestSeq > seen)
                return;

            moved = _moved.Task;
        }

        await Task.WhenAny(moved, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
    }
}
