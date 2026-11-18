using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using DataflowDuplicateDetector.Dataflow;
using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services.Interfaces;

namespace DataflowDuplicateDetector.Services;

public sealed class DataflowDuplicateFinder(IHashService hashService, int degree, int capacity,
    bool useQuickHash) : IDuplicateFinder
{
    public async Task<IReadOnlyList<DuplicateGroup>> ExecuteAsync(string folder, PipelineStatistics statistics,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degree);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = lifetime.Token;
        var groups = new DuplicateAccumulator(statistics);
        var ingress = new BufferBlock<string>(new() { BoundedCapacity = capacity, CancellationToken = token });
        var metadata = new TransformBlock<string, FileEntry?>(path => ScanFiles.ReadMetadata(path, statistics),
            Options(degree));
        var fullHash = new TransformBlock<FileEntry, HashResult?>(
            file => ScanFiles.HashAsync(file, hashService, statistics, token), Options(degree));
        var aggregate = new ActionBlock<HashResult>(groups.Add, Options(1));
        var links = new DataflowLinkOptions { PropagateCompletion = true };
        List<IDataflowBlock> blocks = [ingress, metadata, fullHash, aggregate];
        ingress.LinkTo(metadata, links);
        LinkNonNull(fullHash, aggregate, links);

        QuickHashStage? quick = null;
        if (useQuickHash)
        {
            quick = new(new Sha256HashService(), statistics);
            var prefix = new TransformBlock<FileEntry, HashResult?>(file => quick.ComputeAsync(file, token),
                Options(degree));
            var candidates = new TransformManyBlock<HashResult, FileEntry>(quick.SelectCandidates, Options(1));
            blocks.AddRange([prefix, candidates]);
            LinkNonNull(metadata, prefix, links);
            LinkNonNull(prefix, candidates, links);
            candidates.LinkTo(fullHash, links);
        }
        else
        {
            LinkNonNull(metadata, fullHash, links);
        }

        ExceptionDispatchInfo? failure = null;
        var monitors = Task.WhenAll(blocks.Select(block => MonitorAsync(block.Completion)));
        Volatile.Write(ref statistics.ReadQueueLength, () => ingress.Count);
        try
        {
            foreach (var path in ScanFiles.Enumerate(folder, statistics, token))
            {
                var accepted = ingress.SendAsync(path, token);
                var delayed = !accepted.IsCompleted;
                var start = delayed ? Stopwatch.GetTimestamp() : 0;
                if (delayed)
                    Interlocked.Increment(ref statistics.SendWaits);
                try
                {
                    if (!await accepted.ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("The Dataflow input declined a file.");
                    }
                }
                finally
                {
                    if (delayed)
                        Interlocked.Add(ref statistics.SendWaitTicks, Stopwatch.GetElapsedTime(start).Ticks);
                }
            }
            ingress.Complete();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !token.IsCancellationRequested)
                RecordFailure(ex);
            lifetime.Cancel();
        }
        finally
        {
            await monitors.ConfigureAwait(false);
            Volatile.Write(ref statistics.ReadQueueLength, static () => 0);
        }

        failure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        quick?.FinishNonCandidates();
        return groups.GetGroups();

        ExecutionDataflowBlockOptions Options(int workers) => new()
        {
            BoundedCapacity = capacity,
            MaxDegreeOfParallelism = Math.Min(workers, capacity),
            EnsureOrdered = false,
            CancellationToken = token
        };

        async Task MonitorAsync(Task completion)
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                lifetime.Cancel();
            }
        }

        void RecordFailure(Exception ex) =>
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
    }

    private static void LinkNonNull<T>(ISourceBlock<T?> source, ITargetBlock<T> target, DataflowLinkOptions options)
        where T : class
    {
        source.LinkTo(target!, options, static item => item is not null);
        source.LinkTo(DataflowBlock.NullTarget<T?>(), static item => item is null);
    }
}
