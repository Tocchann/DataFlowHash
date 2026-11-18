using DataflowDuplicateDetector.Models;

namespace DataflowDuplicateDetector.ViewModels;

public sealed class DuplicateGroupViewModel(DuplicateGroup group)
{
    public string Hash => group.Hash;
    public int Count => group.Count;
    public string FirstFile => group.Files[0].Path;
    public IReadOnlyList<FileEntry> Files => group.Files;
}
