namespace DataflowDuplicateDetector.Models;

public sealed class PipelineStatistics
{
    internal long Found, Finished, Hashed, Groups, Skipped, SkippedFolders, SendWaits, SendWaitTicks;
    internal Func<int>? ReadQueueLength;

    public ScanCounters Read() => new(
        Interlocked.Read(ref Found), Interlocked.Read(ref Finished), Interlocked.Read(ref Hashed),
        Interlocked.Read(ref Groups), Interlocked.Read(ref Skipped), Interlocked.Read(ref SkippedFolders),
        Volatile.Read(ref ReadQueueLength)?.Invoke(), Interlocked.Read(ref SendWaits),
        TimeSpan.FromTicks(Interlocked.Read(ref SendWaitTicks)));
}

public sealed record ScanCounters(long FilesFound, long FilesProcessed, long FilesHashed,
    long DuplicateGroups, long SkippedFiles, long SkippedFolders, int? QueueLength,
    long SendWaits, TimeSpan SendWaitTime);
