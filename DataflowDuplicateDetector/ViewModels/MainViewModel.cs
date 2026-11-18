using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataflowDuplicateDetector.Models;
using DataflowDuplicateDetector.Services;
using DataflowDuplicateDetector.Services.Interfaces;
using Microsoft.Win32;

namespace DataflowDuplicateDetector.ViewModels;

internal sealed partial class MainViewModel : ObservableObject
{
    private readonly Window _owner;
    private IReadOnlyList<DuplicateGroup> _results = [];
    private bool _canExport;

    public MainViewModel(Window owner)
    {
        _owner = owner;
        selectedMode = Modes[3];
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string folder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetDegree))]
    [NotifyPropertyChangedFor(nameof(CanSetCapacity))]
    private ModeOption selectedMode;

    [ObservableProperty] private string degree = Environment.ProcessorCount.ToString();
    [ObservableProperty] private string capacity = "100";
    [ObservableProperty] private string status = "フォルダーを選択してください。";
    [ObservableProperty] private IReadOnlyList<DuplicateGroupViewModel> groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanSetDegree))]
    [NotifyPropertyChangedFor(nameof(CanSetCapacity))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool isBusy;

    public IReadOnlyList<ModeOption> Modes { get; } =
    [
        new(ScanMode.Sequential, "逐次", "1 ファイルずつフル SHA-256 を計算します。"),
        new(ScanMode.Parallel, "Parallel.ForEachAsync", "指定した並列度で全ファイルをフルハッシュします。"),
        new(ScanMode.DataflowFull, "Dataflow：全件フルハッシュ", "有界パイプラインで全ファイルをフルハッシュします。"),
        new(ScanMode.DataflowQuick, "Dataflow：短縮ハッシュ", "サイズと先頭 1 MiB で候補を絞り、候補をフル SHA-256 で照合します。")
    ];

    public StatisticsViewModel Statistics { get; } = new();
    public bool CanConfigure => !IsBusy;
    public bool CanSetDegree => CanConfigure && SelectedMode.Mode != ScanMode.Sequential;
    public bool CanSetCapacity => CanConfigure && SelectedMode.IsDataflow;
    private bool CanStart() => CanConfigure && !string.IsNullOrWhiteSpace(Folder);
    private bool CanExport() => CanConfigure && _canExport;

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "走査するフォルダーを選択" };
        if (Directory.Exists(Folder))
            dialog.InitialDirectory = Folder;
        if (dialog.ShowDialog(_owner) == true)
            Folder = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanStart), IncludeCancelCommand = true)]
    private async Task StartAsync(CancellationToken token)
    {
        var mode = SelectedMode;
        var workers = 1;
        var capacity = 1;
        if (mode.Mode != ScanMode.Sequential && (!int.TryParse(Degree, out workers) || workers < 1))
        {
            Status = "並列度に正の整数を入力してください。";
            return;
        }
        if (mode.IsDataflow && (!int.TryParse(Capacity, out capacity) || capacity < 1))
        {
            Status = "容量に正の整数を入力してください。";
            return;
        }

        var folder = Folder.Trim();
        var hash = new Sha256HashService();
        IDuplicateFinder finder = mode.Mode switch
        {
            ScanMode.Sequential => new SequentialDuplicateFinder(hash),
            ScanMode.Parallel => new ParallelDuplicateFinder(hash, workers),
            _ => new DataflowDuplicateFinder(hash, workers, capacity, mode.Mode == ScanMode.DataflowQuick)
        };
        var counters = new PipelineStatistics();
        using var metrics = new MetricsService();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Statistics.Snapshot = metrics.Sample(counters);
        _canExport = false;
        IsBusy = true;
        Groups = [];
        _results = [];
        Status = "走査中…";
        Statistics.Snapshot = metrics.Sample(counters);
        timer.Start();
        try
        {
            _results = await Task.Run(() => finder.ExecuteAsync(folder, counters, token), token);
            token.ThrowIfCancellationRequested();
            timer.Stop();
            var sample = metrics.Sample(counters);
            Statistics.Snapshot = sample;
            Groups = _results.Select(group => new DuplicateGroupViewModel(group)).ToArray();
            _canExport = true;
            Status = $"完了：{_results.Count:N0} グループ";
            var average = sample.Elapsed.TotalSeconds > 0
                ? sample.Counts.FilesProcessed / sample.Elapsed.TotalSeconds : 0;
            MessageBox.Show(_owner,
                $"モード：{mode.Label}\n検出ファイル：{sample.Counts.FilesFound:N0}\n" +
                $"フルハッシュ：{sample.Counts.FilesHashed:N0}\n重複グループ：{_results.Count:N0}\n" +
                $"経過：{sample.Elapsed.TotalSeconds:N2} 秒\n平均処理速度：{average:N0} 件/秒",
                "実行サマリー", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Status = "キャンセルしました。";
        }
        catch (Exception ex)
        {
            Status = $"走査に失敗しました：{ex.Message}";
        }
        finally
        {
            timer.Stop();
            if (!_canExport)
                Statistics.Snapshot = metrics.Sample(counters);
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Filter = "CSV ファイル (*.csv)|*.csv", FileName = "duplicates.csv" };
        if (dialog.ShowDialog(_owner) != true)
            return;

        IsBusy = true;
        try
        {
            await Task.Run(() => CsvExporter.ExportAsync(dialog.FileName, _results));
            Status = $"保存しました：{dialog.FileName}";
        }
        catch (Exception ex) when (ScanFiles.IsFileError(ex))
        {
            Status = $"CSV の保存に失敗しました：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelScan() => StartCommand.Cancel();

    internal enum ScanMode { Sequential, Parallel, DataflowFull, DataflowQuick }

    internal sealed record ModeOption(ScanMode Mode, string Label, string Description)
    {
        public bool IsDataflow => Mode is ScanMode.DataflowFull or ScanMode.DataflowQuick;
    }
}
