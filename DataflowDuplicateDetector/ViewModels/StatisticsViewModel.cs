using CommunityToolkit.Mvvm.ComponentModel;
using DataflowDuplicateDetector.Services;

namespace DataflowDuplicateDetector.ViewModels;

public sealed partial class StatisticsViewModel : ObservableObject
{
    [ObservableProperty] private MetricsSnapshot? snapshot;

    public string FilesFound => $"{Snapshot?.Counts.FilesFound ?? 0:N0}";
    public string FilesHashed => $"{Snapshot?.Counts.FilesHashed ?? 0:N0}";
    public string DuplicateGroups => $"{Snapshot?.Counts.DuplicateGroups ?? 0:N0}";
    public string Skipped => $"{Snapshot?.Counts.SkippedFiles ?? 0:N0} / {Snapshot?.Counts.SkippedFolders ?? 0:N0}";
    public string Throughput => $"{Snapshot?.Throughput ?? 0:N0} 件/秒";
    public string CpuUsage => $"{Snapshot?.CpuUsage ?? 0:F1}%";
    public string MemoryUsage => $"{(Snapshot?.MemoryUsageBytes ?? 0) / 1048576d:N1} MiB";
    public string Elapsed => $"{Snapshot?.Elapsed ?? TimeSpan.Zero:g}";
    public string QueueLength => Snapshot?.Counts.QueueLength is int count ? $"{count:N0}" : "—";
    public string Backpressure => Snapshot?.Counts.QueueLength is not null
        ? $"{Snapshot.Counts.SendWaits:N0} 回 / {Snapshot.Counts.SendWaitTime.TotalMilliseconds:N0} ms"
        : "—";

    partial void OnSnapshotChanged(MetricsSnapshot? value)
    {
        OnPropertyChanged(nameof(FilesFound));
        OnPropertyChanged(nameof(FilesHashed));
        OnPropertyChanged(nameof(DuplicateGroups));
        OnPropertyChanged(nameof(Skipped));
        OnPropertyChanged(nameof(Throughput));
        OnPropertyChanged(nameof(CpuUsage));
        OnPropertyChanged(nameof(MemoryUsage));
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(QueueLength));
        OnPropertyChanged(nameof(Backpressure));
    }
}
