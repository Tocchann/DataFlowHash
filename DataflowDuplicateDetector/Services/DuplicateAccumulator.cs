using System.Collections.Concurrent;
using DataflowDuplicateDetector.Models;

namespace DataflowDuplicateDetector.Services;

internal sealed class DuplicateAccumulator(PipelineStatistics statistics)
{
    private readonly ConcurrentDictionary<string, List<FileEntry>> _files = new(StringComparer.Ordinal);

    public void Add(HashResult result)
    {
        var files = _files.GetOrAdd(result.Hash, static _ => []);
        lock (files)
        {
            files.Add(result.File);
            if (files.Count == 2)
                Interlocked.Increment(ref statistics.Groups);
        }
        Interlocked.Increment(ref statistics.Finished);
    }

    public IReadOnlyList<DuplicateGroup> GetGroups() => _files
        .Where(pair => pair.Value.Count > 1)
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new DuplicateGroup(pair.Key,
            pair.Value.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray()))
        .ToArray();
}
