using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public interface IVideoProcessingQueueAdapter
{
    string ModeName { get; }
    TimeSpan TranslationStartTimeout { get; }
    TimeSpan TranslationReadyTimeout { get; }
    TimeSpan FinalizationTimeout { get; }

    IReadOnlyList<VideoEntry> GetEntriesInDisplayOrder();

    Task SelectEntryAsync(VideoEntry entry, CancellationToken cancellationToken);
    Task PrepareEntryAsync(VideoEntry entry, CancellationToken cancellationToken);
    Task OpenEntryAsync(VideoEntry entry, CancellationToken cancellationToken);

    bool TryGetCompletedOutputPath(string entryId, out string? outputPath);

    void OnQueueLocked(bool locked);
    void OnItemStateChanged(VideoProcessingQueueItem item);
    Task ShowReportAsync(VideoProcessingQueueReport report, CancellationToken cancellationToken);
}
