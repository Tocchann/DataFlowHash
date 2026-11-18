using System.IO;
using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services;
using DataflowDuplicateDetector.Services.Interfaces;
using Xunit;

namespace DataflowDuplicateDetector.Tests;

public sealed class DuplicateFinderTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("DataFlowHash-");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task AllModesAgreeAndQuickModeOnlyFullyHashesCandidates()
    {
        var sample = new byte[1024 * 1024 + 1];
        Write("a.bin", sample);
        Write("b.bin", sample);
        Directory.CreateDirectory(Path.Combine(_folder.FullName, "nested"));
        Write(Path.Combine("nested", "c.bin"), sample);
        sample[^1] = 1;
        Write("same-prefix.bin", sample);
        Write("different-prefix.bin", [2, .. sample.AsSpan(1)]);
        Write("different-size.bin", [1]);
        Write("empty-a.bin", []);
        Write("empty-b.bin", []);

        var hash = new Sha256HashService();
        IDuplicateFinder[] finders =
        [
            new SequentialDuplicateFinder(hash),
            new ParallelDuplicateFinder(hash, 4),
            new DataflowDuplicateFinder(hash, 4, 1, false),
            new DataflowDuplicateFinder(hash, 4, 1, true)
        ];
        string[]? baseline = null;
        foreach (var finder in finders)
        {
            var stats = new PipelineStatistics();
            var groups = await finder.ExecuteAsync(_folder.FullName, stats, default).WaitAsync(Timeout);
            Assert.Equal(new[] { 2, 3 }, groups.Select(group => group.Count).Order());
            var actual = groups.SelectMany(group => group.Files.Select(file => $"{group.Hash}:{file.Path}")).ToArray();
            baseline ??= actual;
            Assert.Equal(baseline, actual);
            var counters = stats.Read();
            Assert.Equal(8, counters.FilesFound);
            Assert.Equal(8, counters.FilesProcessed);
            Assert.Equal(ReferenceEquals(finder, finders[^1]) ? 6 : 8, counters.FilesHashed);
            Assert.Equal(2, counters.DuplicateGroups);
            Assert.Equal(0, counters.SkippedFiles);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataflowHandlesAnEmptyFolderAndReportsAMissingRoot(bool quick)
    {
        var finder = new DataflowDuplicateFinder(new Sha256HashService(), 4, 1, quick);
        var stats = new PipelineStatistics();
        Assert.Empty(await finder.ExecuteAsync(_folder.FullName, stats, default).WaitAsync(Timeout));
        Assert.Equal(0, stats.Read().FilesFound);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            finder.ExecuteAsync(Path.Combine(_folder.FullName, "missing"), new(), default).WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DownstreamFailureOrCancellationUnblocksAFullPipeline(bool quick, bool fail)
    {
        for (var index = 0; index < 32; index++)
            Write($"{index}.bin", [1, 2, 3]);

        var hash = new GatedHashService();
        using var cts = new CancellationTokenSource(Timeout);
        var stats = new PipelineStatistics();
        var finder = new DataflowDuplicateFinder(hash, 4, 1, quick);
        var running = finder.ExecuteAsync(_folder.FullName, stats, cts.Token);
        try
        {
            await hash.Entered.Task.WaitAsync(Timeout);
            while (stats.Read() is not { QueueLength: 1, SendWaits: > 0 })
                await Task.Delay(20, cts.Token);

            Assert.InRange(stats.Read().FilesFound, 2, 31);
            if (fail)
            {
                hash.Result.SetException(new InvalidOperationException("test hash failure"));
                var error = await Record.ExceptionAsync(() => running.WaitAsync(Timeout));
                Assert.Contains("test hash failure", error?.ToString());
            }
            else
            {
                await cts.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Timeout));
            }
            Assert.Equal(0, stats.Read().QueueLength);
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaselinesCancelInFlightHashing(bool parallel)
    {
        Write("cancel.bin", [1]);
        var hash = new GatedHashService();
        IDuplicateFinder finder = parallel ? new ParallelDuplicateFinder(hash, 2) : new SequentialDuplicateFinder(hash);
        using var cts = new CancellationTokenSource(Timeout);
        var running = finder.ExecuteAsync(_folder.FullName, new(), cts.Token);
        await hash.Entered.Task.WaitAsync(Timeout);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Timeout));
    }

    [Fact]
    public async Task CsvEscapesPathsAndWritesOneRowPerFile()
    {
        using var writer = new StringWriter();
        await CsvExporter.WriteAsync(writer,
            [new("ABC", [new("a,b", 0, default), new("c\"d", 0, default)])]);
        Assert.Equal($"Hash,Count,Path{writer.NewLine}ABC,2,\"a,b\"{writer.NewLine}" +
            $"ABC,2,\"c\"\"d\"{writer.NewLine}", writer.ToString());
    }

    private void Write(string path, byte[] bytes) => File.WriteAllBytes(Path.Combine(_folder.FullName, path), bytes);

    public void Dispose() => _folder.Delete(recursive: true);

    private sealed class GatedHashService : IHashService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return Result.Task.WaitAsync(cancellationToken);
        }
    }
}
