using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services;

namespace DataflowDuplicateDetector.Dataflow;

internal sealed class QuickHashStage(Sha256HashService hashService, PipelineStatistics statistics)
{
    private readonly Dictionary<(long Length, string Hash), FileEntry?> _candidates = [];

    public async Task<HashResult?> ComputeAsync(FileEntry file, CancellationToken token)
    {
        try
        {
            return new(file, await hashService.ComputePrefixHashAsync(file.Path, token).ConfigureAwait(false));
        }
        catch (Exception ex) when (ScanFiles.IsFileError(ex))
        {
            ScanFiles.Skip(statistics);
            return null;
        }
    }

    // Called by a single consumer. A null value means the key has already matched.
    public IEnumerable<FileEntry> SelectCandidates(HashResult result)
    {
        var key = (result.File.Length, result.Hash);
        if (!_candidates.TryGetValue(key, out var first))
        {
            _candidates.Add(key, result.File);
            return [];
        }

        if (first is null)
            return [result.File];

        _candidates[key] = null;
        return [first, result.File];
    }

    public void FinishNonCandidates()
    {
        Interlocked.Add(ref statistics.Finished, _candidates.Values.LongCount(file => file is not null));
    }
}
