using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using DataflowDuplicateDetector.Services.Interfaces;

namespace DataflowDuplicateDetector.Services;

public sealed class Sha256HashService : IHashService
{
    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = Open(filePath);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    public async Task<string> ComputePrefixHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = Open(filePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int bufferSize = 64 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var remaining = 1024 * 1024;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, bufferSize)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
}
