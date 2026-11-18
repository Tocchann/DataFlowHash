using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services.Interfaces;

namespace DataflowDuplicateDetector.Services;

public sealed class ParallelDuplicateFinder(IHashService hashService, int degree) : IDuplicateFinder
{
    public async Task<IReadOnlyList<DuplicateGroup>> ExecuteAsync(string folder, PipelineStatistics statistics,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degree);
        var groups = new DuplicateAccumulator(statistics);
        await Parallel.ForEachAsync(ScanFiles.Enumerate(folder, statistics, cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellationToken },
            async (path, token) =>
            {
                if (ScanFiles.ReadMetadata(path, statistics) is { } file &&
                    await ScanFiles.HashAsync(file, hashService, statistics, token).ConfigureAwait(false) is { } result)
                    groups.Add(result);
            }).ConfigureAwait(false);
        return groups.GetGroups();
    }
}
