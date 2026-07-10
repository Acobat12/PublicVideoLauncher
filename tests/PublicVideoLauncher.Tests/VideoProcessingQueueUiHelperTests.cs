using System;
using System.Collections.Generic;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class VideoProcessingQueueUiHelperTests
{
    [Fact]
    public void SelectOnlyRetryableEntries_SelectsFailedAndTimedOutOnly()
    {
        var entries = new List<VideoEntry>
        {
            new() { Id = "completed", IncludeInBatch = true },
            new() { Id = "failed", IncludeInBatch = false },
            new() { Id = "timed-out", IncludeInBatch = false },
            new() { Id = "skipped", IncludeInBatch = true },
            new() { Id = "canceled", IncludeInBatch = true }
        };

        var report = new VideoProcessingQueueReport
        {
            Items =
            [
                new() { EntryId = "completed", Status = VideoProcessingItemLifecycleStatus.Completed },
                new() { EntryId = "failed", Status = VideoProcessingItemLifecycleStatus.Failed },
                new() { EntryId = "timed-out", Status = VideoProcessingItemLifecycleStatus.TimedOut },
                new() { EntryId = "skipped", Status = VideoProcessingItemLifecycleStatus.Skipped },
                new() { EntryId = "canceled", Status = VideoProcessingItemLifecycleStatus.Canceled }
            ]
        };

        var selectedCount = VideoProcessingQueueRetryHelper.SelectOnlyRetryableEntries(entries, report);

        Assert.Equal(2, selectedCount);
        Assert.False(entries[0].IncludeInBatch);
        Assert.True(entries[1].IncludeInBatch);
        Assert.True(entries[2].IncludeInBatch);
        Assert.False(entries[3].IncludeInBatch);
        Assert.False(entries[4].IncludeInBatch);
    }

    [Fact]
    public void BuildProgressText_UsesSelectedItemsOnly()
    {
        var session = new VideoProcessingQueueSession
        {
            OrderedEntryIds = ["a", "b", "c", "d"],
            ActiveEntryId = "c",
            ItemsByEntryId = new Dictionary<string, VideoProcessingQueueItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = new() { EntryId = "a", WasSelected = true },
                ["b"] = new() { EntryId = "b", WasSelected = false },
                ["c"] = new() { EntryId = "c", WasSelected = true },
                ["d"] = new() { EntryId = "d", WasSelected = true }
            }
        };

        var progressText = VideoProcessingQueueProgressHelper.BuildProgressText(session);

        Assert.Equal("Processing 2 / 3", progressText);
    }

    [Fact]
    public void BuildProgressText_UsesCustomFormatter_WhenProvided()
    {
        var session = new VideoProcessingQueueSession
        {
            OrderedEntryIds = ["a", "b"],
            ActiveEntryId = "b",
            ItemsByEntryId = new Dictionary<string, VideoProcessingQueueItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = new() { EntryId = "a", WasSelected = true },
                ["b"] = new() { EntryId = "b", WasSelected = true }
            }
        };

        var progressText = VideoProcessingQueueProgressHelper.BuildProgressText(session, static (current, total) => $"Обработка {current} / {total}");

        Assert.Equal("Обработка 2 / 2", progressText);
    }

    [Fact]
    public void BuildProgressText_WithoutActiveEntry_ReturnsNull()
    {
        var session = new VideoProcessingQueueSession
        {
            OrderedEntryIds = ["a"],
            ItemsByEntryId = new Dictionary<string, VideoProcessingQueueItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = new() { EntryId = "a", WasSelected = true }
            }
        };

        var progressText = VideoProcessingQueueProgressHelper.BuildProgressText(session);

        Assert.Null(progressText);
    }
}
