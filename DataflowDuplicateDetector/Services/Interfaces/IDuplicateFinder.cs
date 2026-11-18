using DataflowDuplicateDetector.Models;

namespace DataflowDuplicateDetector.Services.Interfaces;

public interface IDuplicateFinder
{
    Task<IReadOnlyList<DuplicateGroup>> ExecuteAsync(string folder, PipelineStatistics statistics,
        CancellationToken cancellationToken);
}
