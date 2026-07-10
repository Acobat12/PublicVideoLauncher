using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class MainModeQueueAdapter : IVideoProcessingQueueAdapter
{
    private readonly IReadOnlyList<VideoEntry> _entries;
    private readonly Func<VideoEntry, CancellationToken, Task> _selectEntryAsync;
    private readonly Func<VideoEntry, CancellationToken, Task> _prepareEntryAsync;
    private readonly Func<VideoEntry, CancellationToken, Task> _openEntryAsync;
    private readonly Action<bool> _onQueueLocked;
    private readonly Action<VideoProcessingQueueItem> _onItemStateChanged;
    private readonly Func<VideoProcessingQueueReport, CancellationToken, Task> _showReportAsync;
    private readonly object _outputSync = new();
    private readonly Dictionary<string, string?> _completedOutputs = new(StringComparer.OrdinalIgnoreCase);

    public MainModeQueueAdapter(
        IReadOnlyList<VideoEntry> entries,
        Func<VideoEntry, CancellationToken, Task> selectEntryAsync,
        Func<VideoEntry, CancellationToken, Task> prepareEntryAsync,
        Func<VideoEntry, CancellationToken, Task> openEntryAsync,
        Action<bool> onQueueLocked,
        Action<VideoProcessingQueueItem> onItemStateChanged,
        Func<VideoProcessingQueueReport, CancellationToken, Task> showReportAsync)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _selectEntryAsync = selectEntryAsync ?? throw new ArgumentNullException(nameof(selectEntryAsync));
        _prepareEntryAsync = prepareEntryAsync ?? throw new ArgumentNullException(nameof(prepareEntryAsync));
        _openEntryAsync = openEntryAsync ?? throw new ArgumentNullException(nameof(openEntryAsync));
        _onQueueLocked = onQueueLocked ?? throw new ArgumentNullException(nameof(onQueueLocked));
        _onItemStateChanged = onItemStateChanged ?? throw new ArgumentNullException(nameof(onItemStateChanged));
        _showReportAsync = showReportAsync ?? throw new ArgumentNullException(nameof(showReportAsync));
    }

    public string ModeName => "Main mode";
    public TimeSpan TranslationStartTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan TranslationReadyTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan FinalizationTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public IReadOnlyList<VideoEntry> GetEntriesInDisplayOrder() => _entries;

    public Task SelectEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        => _selectEntryAsync(entry, cancellationToken);

    public Task PrepareEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        => _prepareEntryAsync(entry, cancellationToken);

    public Task OpenEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        => _openEntryAsync(entry, cancellationToken);

    public bool TryGetCompletedOutputPath(string entryId, out string? outputPath)
    {
        lock (_outputSync)
            return _completedOutputs.TryGetValue(entryId, out outputPath);
    }

    public void RegisterCompletedOutputPath(string entryId, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return;

        lock (_outputSync)
            _completedOutputs[entryId] = outputPath;
    }

    public void OnQueueLocked(bool locked) => _onQueueLocked(locked);

    public void OnItemStateChanged(VideoProcessingQueueItem item)
        => _onItemStateChanged(item);

    public Task ShowReportAsync(VideoProcessingQueueReport report, CancellationToken cancellationToken)
        => _showReportAsync(report, cancellationToken);
}
