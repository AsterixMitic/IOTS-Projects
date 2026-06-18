namespace StorageService.Services;

public sealed class StorageMetrics
{
    private long _receivedMessages;
    private long _persistedMessages;
    private long _failedMessages;
    private long _persistedBatches;
    private long _lastPersistDurationMs;

    public void RecordReceived() => Interlocked.Increment(ref _receivedMessages);

    public void RecordPersisted(int count, TimeSpan duration)
    {
        Interlocked.Add(ref _persistedMessages, count);
        Interlocked.Increment(ref _persistedBatches);
        Interlocked.Exchange(ref _lastPersistDurationMs, (long)duration.TotalMilliseconds);
    }

    public void RecordFailed() => Interlocked.Increment(ref _failedMessages);

    public object Snapshot() => new
    {
        receivedMessages = Interlocked.Read(ref _receivedMessages),
        persistedMessages = Interlocked.Read(ref _persistedMessages),
        failedMessages = Interlocked.Read(ref _failedMessages),
        persistedBatches = Interlocked.Read(ref _persistedBatches),
        lastPersistDurationMs = Interlocked.Read(ref _lastPersistDurationMs)
    };
}

