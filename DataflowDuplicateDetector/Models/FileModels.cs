namespace DataflowDuplicateDetector.Models;

public sealed record FileEntry(string Path, long Length, DateTime LastWriteTimeUtc);

public sealed record HashResult(FileEntry File, string Hash);

public sealed record DuplicateGroup(string Hash, IReadOnlyList<FileEntry> Files)
{
    public int Count => Files.Count;
}
