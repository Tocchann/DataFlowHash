using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services.Interfaces;

namespace DataflowDuplicateDetector.Services;

public sealed class SequentialDuplicateFinder(IHashService hashService) : IDuplicateFinder
{
    public async Task<IReadOnlyList<DuplicateGroup>> ExecuteAsync(string folder, PipelineStatistics statistics,
        CancellationToken cancellationToken)
    {
        var groups = new DuplicateAccumulator(statistics);
        foreach (var path in ScanFiles.Enumerate(folder, statistics, cancellationToken))
        {
            if (ScanFiles.ReadMetadata(path, statistics) is { } file &&
                await ScanFiles.HashAsync(file, hashService, statistics, cancellationToken).ConfigureAwait(false)
                    is { } result)
                groups.Add(result);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return groups.GetGroups();
    }
}
