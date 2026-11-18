using System.IO;
using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services.Interfaces;

namespace DataflowDuplicateDetector.Services;

internal static class ScanFiles
{
    public static IEnumerable<string> Enumerate(string folder, PipelineStatistics statistics,
        CancellationToken token)
    {
        var root = Path.GetFullPath(folder);
        return Walk(root, true);

        IEnumerable<string> Walk(string directory, bool isRoot)
        {
            token.ThrowIfCancellationRequested();
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            IEnumerator<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options).GetEnumerator();
            }
            catch (Exception ex) when (!isRoot && IsFileError(ex))
            {
                Interlocked.Increment(ref statistics.SkippedFolders);
                yield break;
            }

            using var enumerator = entries;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileSystemInfo entry;
                try
                {
                    if (!entries.MoveNext())
                        break;
                    entry = entries.Current;
                }
                catch (Exception ex) when (!isRoot && IsFileError(ex))
                {
                    Interlocked.Increment(ref statistics.SkippedFolders);
                    break;
                }

                if (entry is DirectoryInfo)
                {
                    foreach (var path in Walk(entry.FullName, false))
                        yield return path;
                }
                else
                {
                    Interlocked.Increment(ref statistics.Found);
                    yield return entry.FullName;
                }
            }
        }
    }

    public static FileEntry? ReadMetadata(string path, PipelineStatistics statistics)
    {
        try
        {
            var info = new FileInfo(path);
            return new(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Skip(statistics);
            return null;
        }
    }

    public static async Task<HashResult?> HashAsync(FileEntry file, IHashService hashService,
        PipelineStatistics statistics, CancellationToken token)
    {
        try
        {
            var hash = await hashService.ComputeHashAsync(file.Path, token).ConfigureAwait(false);
            Interlocked.Increment(ref statistics.Hashed);
            return new(file, hash);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Skip(statistics);
            return null;
        }
    }

    public static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    public static void Skip(PipelineStatistics statistics)
    {
        Interlocked.Increment(ref statistics.Skipped);
        Interlocked.Increment(ref statistics.Finished);
    }
}
