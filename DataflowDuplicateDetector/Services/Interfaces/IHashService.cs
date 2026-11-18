namespace DataflowDuplicateDetector.Services.Interfaces;

public interface IHashService
{
    Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken);
}
