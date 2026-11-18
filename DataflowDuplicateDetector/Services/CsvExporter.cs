using System.Globalization;
using System.IO;
using System.Text;
using DataflowDuplicateDetector.Models;

namespace DataflowDuplicateDetector.Services;

public static class CsvExporter
{
    public static async Task ExportAsync(string path, IReadOnlyList<DuplicateGroup> groups)
    {
        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(true));
        await WriteAsync(writer, groups).ConfigureAwait(false);
    }

    public static async Task WriteAsync(TextWriter writer, IReadOnlyList<DuplicateGroup> groups)
    {
        await writer.WriteLineAsync("Hash,Count,Path").ConfigureAwait(false);
        foreach (var group in groups)
        {
            foreach (var file in group.Files)
                await writer.WriteLineAsync($"{Escape(group.Hash)},{group.Count.ToString(CultureInfo.InvariantCulture)},{Escape(file.Path)}")
                    .ConfigureAwait(false);
        }
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value
        : $"\"{value.Replace("\"", "\"\"")}\"";
}
