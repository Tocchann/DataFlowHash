using System.Diagnostics;
using DataflowDuplicateDetector.Models;

namespace DataflowDuplicateDetector.Services;

internal sealed class MetricsService : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastCpu;
    private TimeSpan _lastTime;
    private long _lastProcessed;
    private double _lastCpuUsage, _lastThroughput;

    public MetricsService() => _lastCpu = _process.TotalProcessorTime;

    public MetricsSnapshot Sample(PipelineStatistics statistics)
    {
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var time = _clock.Elapsed;
        var counts = statistics.Read();
        var seconds = (time - _lastTime).TotalSeconds;
        if (seconds >= 0.05)
        {
            _lastCpuUsage = Math.Clamp((cpu - _lastCpu).TotalSeconds / seconds /
                Environment.ProcessorCount * 100, 0, 100);
            _lastThroughput = (counts.FilesProcessed - _lastProcessed) / seconds;
            _lastCpu = cpu;
            _lastTime = time;
            _lastProcessed = counts.FilesProcessed;
        }

        return new(counts, time, _lastThroughput, _lastCpuUsage, _process.WorkingSet64);
    }

    public void Dispose() => _process.Dispose();
}

public sealed record MetricsSnapshot(ScanCounters Counts, TimeSpan Elapsed,
    double Throughput, double CpuUsage, long MemoryUsageBytes);
