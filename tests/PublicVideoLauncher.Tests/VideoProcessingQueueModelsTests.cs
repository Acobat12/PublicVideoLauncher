using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class VideoProcessingQueueModelsTests
{
    [Fact]
    public void VideoEntry_IncludeInBatch_DefaultsToTrue()
    {
        var entry = new VideoEntry();

        Assert.True(entry.IncludeInBatch);
    }

    [Fact]
    public void VideoProcessingQueueItem_DefaultsAreSafe()
    {
        var item = new VideoProcessingQueueItem();

        Assert.Null(item.Entry);
        Assert.Equal(string.Empty, item.EntryId);
        Assert.Equal(string.Empty, item.Title);
        Assert.False(item.WasSelected);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Pending, item.Status);
        Assert.Null(item.Message);
        Assert.Null(item.OutputPath);
        Assert.Null(item.ErrorCode);
        Assert.Null(item.ErrorMessage);
        Assert.Equal(default, item.CreatedAtUtc);
        Assert.Null(item.StartedAtUtc);
        Assert.Null(item.TranslationStartedAtUtc);
        Assert.Null(item.TranslationReadyAtUtc);
        Assert.Null(item.FinishedAtUtc);
    }

    [Fact]
    public void VideoProcessingQueueItemResult_DefaultsAreSafe()
    {
        var result = new VideoProcessingQueueItemResult();

        Assert.Equal(string.Empty, result.Mode);
        Assert.Equal(string.Empty, result.EntryId);
        Assert.Equal(string.Empty, result.Title);
        Assert.False(result.WasSelected);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Pending, result.Status);
        Assert.False(result.Succeeded);
        Assert.Null(result.Message);
        Assert.Null(result.OutputPath);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.SkipReason);
        Assert.Equal(default, result.StartedAtUtc);
        Assert.Equal(default, result.FinishedAtUtc);
    }

    [Fact]
    public void VideoProcessingQueueReport_DefaultsAreSafe()
    {
        var report = new VideoProcessingQueueReport();

        Assert.Equal(string.Empty, report.Mode);
        Assert.Equal(0, report.Found);
        Assert.Equal(0, report.Selected);
        Assert.Equal(0, report.Processed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(0, report.Canceled);
        Assert.Empty(report.Items);
        Assert.Equal(default, report.StartedAtUtc);
        Assert.Equal(default, report.FinishedAtUtc);
    }

    [Fact]
    public void VideoProcessingQueueSession_DefaultsAreSafe()
    {
        var session = new VideoProcessingQueueSession();

        Assert.Equal(string.Empty, session.SessionId);
        Assert.Equal(string.Empty, session.Mode);
        Assert.Empty(session.OrderedEntryIds);
        Assert.Empty(session.ItemsByEntryId);
        Assert.Null(session.ActiveEntryId);
        Assert.False(session.StopRequested);
        Assert.Equal(default, session.StartedAtUtc);
    }
}
