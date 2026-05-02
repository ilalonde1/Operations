#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Shared state surface so Worker and WatcherHostedService produce a single
// coherent ServiceHeartbeat row. Without this the 60s Worker heartbeat
// nulls out the 5-minute Watcher's gen four ticks out of five.
//
// All members are read+write from multiple threads; implementations use
// volatile/Interlocked rather than locks because writes are scalar.
internal interface IWatcherState
{
    int Generation { get; }

    bool IsAttached { get; }

    DateTimeOffset? LastEventAt { get; }

    void SetAttached(int generation);

    void SetDetached();

    void NoteEvent();
}

internal sealed class WatcherState : IWatcherState
{
    private int _generation;
    private int _isAttached; // 0/1 via Interlocked
    private long _lastEventTicks;

    public int Generation => Volatile.Read(ref _generation);

    public bool IsAttached => Volatile.Read(ref _isAttached) == 1;

    public DateTimeOffset? LastEventAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastEventTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void SetAttached(int generation)
    {
        Volatile.Write(ref _generation, generation);
        Volatile.Write(ref _isAttached, 1);
    }

    public void SetDetached()
    {
        Volatile.Write(ref _isAttached, 0);
    }

    public void NoteEvent()
    {
        Interlocked.Exchange(ref _lastEventTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
