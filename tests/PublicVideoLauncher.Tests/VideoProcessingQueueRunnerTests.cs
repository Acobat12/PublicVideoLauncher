using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class VideoProcessingQueueRunnerTests
{
    [Fact]
    public async Task RunAsync_UnselectedEntries_AreMarkedSkipped()
    {
        var adapter = new FakeAdapter(
            new VideoEntry { Id = "a", Title = "A", IncludeInBatch = false },
            new VideoEntry { Id = "b", Title = "B", IncludeInBatch = false });
        var runner = new VideoProcessingQueueRunner();

        var report = await runner.RunAsync(adapter, CancellationToken.None);

        Assert.Equal(2, report.Found);
        Assert.Equal(0, report.Selected);
        Assert.Equal(0, report.Processed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(2, report.Skipped);
        Assert.All(report.Items, item => Assert.Equal(VideoProcessingItemLifecycleStatus.Skipped, item.Status));
        Assert.Empty(adapter.SelectedEntryIds);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RunAsync_SelectedEntry_CompletesSuccessfully()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.True(await runner.NotifyTranslationStartedAsync("a", sessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("a", sessionId, CancellationToken.None));

        await WaitForStatusAsync(adapter, "a", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["a"] = @"C:\out\a.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("a", sessionId, @"C:\out\a.mp4", CancellationToken.None));

        var report = await runTask;
        var item = Assert.Single(report.Items);

        Assert.Equal(VideoProcessingItemLifecycleStatus.Completed, item.Status);
        Assert.True(item.Succeeded);
        Assert.Equal(1, report.Processed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(@"C:\out\a.mp4", item.OutputPath);
        Assert.Equal(new[] { "a" }, adapter.SelectedEntryIds);
        Assert.Equal(new[] { "a" }, adapter.PreparedEntryIds);
        Assert.Equal(new[] { "a" }, adapter.OpenedEntryIds);
    }

    [Fact]
    public async Task RunAsync_TranslationFailed_MarksItemFailed()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.True(await runner.NotifyTranslationFailedAsync("a", sessionId, "translation-failed", "Try again later", CancellationToken.None));

        var report = await runTask;
        var item = Assert.Single(report.Items);

        Assert.Equal(VideoProcessingItemLifecycleStatus.Failed, item.Status);
        Assert.Equal("translation-failed", item.ErrorCode);
        Assert.Equal("Try again later", item.ErrorMessage);
        Assert.Equal(1, report.Failed);
        Assert.Equal(0, report.Processed);
    }

    [Fact]
    public async Task RunAsync_Timeout_MarksItemTimedOut()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry)
        {
            TranslationStartTimeout = TimeSpan.FromMilliseconds(50)
        };
        var runner = new VideoProcessingQueueRunner();

        var report = await runner.RunAsync(adapter, CancellationToken.None);
        var item = Assert.Single(report.Items);

        Assert.Equal(VideoProcessingItemLifecycleStatus.TimedOut, item.Status);
        Assert.Equal("translation-start-timeout", item.ErrorCode);
        Assert.Equal("Translation did not start before timeout. Check that vot.user.js is installed and enabled, then verify that the callback can reach the app.", item.ErrorMessage);
        Assert.Equal(1, report.Failed);
        Assert.Equal(0, report.Processed);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RunAsync_TranslationReadyTimeout_UsesReadyTimeoutMessage()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry)
        {
            TranslationReadyTimeout = TimeSpan.FromMilliseconds(50)
        };
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.True(await runner.NotifyTranslationStartedAsync("a", sessionId, CancellationToken.None));

        var report = await runTask;
        var item = Assert.Single(report.Items);

        Assert.Equal(VideoProcessingItemLifecycleStatus.TimedOut, item.Status);
        Assert.Equal("translation-ready-timeout", item.ErrorCode);
        Assert.Equal("Translation started, but the result did not arrive before timeout.", item.ErrorMessage);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RunAsync_FinalizationTimeout_UsesFinalizationTimeoutMessage()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry)
        {
            FinalizationTimeout = TimeSpan.FromMilliseconds(50)
        };
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.True(await runner.NotifyTranslationStartedAsync("a", sessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("a", sessionId, CancellationToken.None));

        var report = await runTask;
        var item = Assert.Single(report.Items);

        Assert.Equal(VideoProcessingItemLifecycleStatus.TimedOut, item.Status);
        Assert.Equal("processing-finalization-timeout", item.ErrorCode);
        Assert.Equal("Translation was received, but finalization did not finish before timeout.", item.ErrorMessage);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RequestStopAsync_StopsBeforeNextSelectedItem()
    {
        var first = new VideoEntry { Id = "first", Title = "First", IncludeInBatch = true };
        var second = new VideoEntry { Id = "second", Title = "Second", IncludeInBatch = true };
        var adapter = new FakeAdapter(first, second);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "first");

        await runner.RequestStopAsync();
        Assert.True(await runner.NotifyTranslationStartedAsync("first", sessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("first", sessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "first", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["first"] = @"C:\out\first.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("first", sessionId, @"C:\out\first.mp4", CancellationToken.None));

        var report = await runTask;

        Assert.Equal(1, report.Processed);
        Assert.Equal(1, report.Canceled);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Completed, report.Items[0].Status);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Canceled, report.Items[1].Status);
        Assert.Equal("Not processed due to stop.", report.Items[1].Message);
        Assert.Equal(new[] { "first" }, adapter.SelectedEntryIds);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RunAsync_CancellationWhileActive_CancelsActiveAndPendingItems()
    {
        var first = new VideoEntry { Id = "first", Title = "First", IncludeInBatch = true };
        var second = new VideoEntry { Id = "second", Title = "Second", IncludeInBatch = true };
        var adapter = new FakeAdapter(first, second);
        var runner = new VideoProcessingQueueRunner();
        using var cancellationSource = new CancellationTokenSource();

        var runTask = runner.RunAsync(adapter, cancellationSource.Token);
        await WaitForActiveEntryAsync(runner, "first");

        cancellationSource.Cancel();

        var report = await runTask;

        Assert.Equal(0, report.Processed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(2, report.Canceled);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Canceled, report.Items[0].Status);
        Assert.Equal("Stopped during processing.", report.Items[0].Message);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Canceled, report.Items[1].Status);
        Assert.Equal("Not processed due to stop.", report.Items[1].Message);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task Notifications_AreRoutedByEntryId_NotByIndex()
    {
        var first = new VideoEntry { Id = "b", Title = "Second by Id", IncludeInBatch = true };
        var second = new VideoEntry { Id = "a", Title = "First by Id", IncludeInBatch = true };
        var adapter = new FakeAdapter(first, second);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var firstSessionId = await WaitForActiveEntryAsync(runner, "b");

        Assert.False(await runner.NotifyTranslationStartedAsync("a", firstSessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationStartedAsync("b", firstSessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("b", firstSessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "b", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["b"] = @"C:\out\b.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("b", firstSessionId, @"C:\out\b.mp4", CancellationToken.None));

        var secondSessionId = await WaitForActiveEntryAsync(runner, "a");
        Assert.True(await runner.NotifyTranslationStartedAsync("a", secondSessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("a", secondSessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "a", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["a"] = @"C:\out\a.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("a", secondSessionId, @"C:\out\a.mp4", CancellationToken.None));

        var report = await runTask;

        Assert.Equal(2, report.Processed);
        Assert.Equal(new[] { "b", "a" }, report.Items.Select(x => x.EntryId).ToArray());
    }

    [Fact]
    public async Task RunAsync_ReportCounters_AreCalculatedForMixedResults()
    {
        var completed = new VideoEntry { Id = "completed", Title = "Completed", IncludeInBatch = true };
        var failed = new VideoEntry { Id = "failed", Title = "Failed", IncludeInBatch = true };
        var skipped = new VideoEntry { Id = "skipped", Title = "Skipped", IncludeInBatch = false };
        var canceled = new VideoEntry { Id = "canceled", Title = "Canceled", IncludeInBatch = true };
        var adapter = new FakeAdapter(completed, failed, skipped, canceled);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);

        var completedSessionId = await WaitForActiveEntryAsync(runner, "completed");
        Assert.True(await runner.NotifyTranslationStartedAsync("completed", completedSessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("completed", completedSessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "completed", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["completed"] = @"C:\out\completed.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("completed", completedSessionId, @"C:\out\completed.mp4", CancellationToken.None));

        var failedSessionId = await WaitForActiveEntryAsync(runner, "failed");
        await runner.RequestStopAsync();
        Assert.True(await runner.NotifyTranslationFailedAsync("failed", failedSessionId, "translation-failed", "Try again later", CancellationToken.None));

        var report = await runTask;

        Assert.Equal(4, report.Found);
        Assert.Equal(3, report.Selected);
        Assert.Equal(1, report.Processed);
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(1, report.Canceled);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Completed, report.Items[0].Status);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Failed, report.Items[1].Status);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Skipped, report.Items[2].Status);
        Assert.Equal(VideoProcessingItemLifecycleStatus.Canceled, report.Items[3].Status);
        Assert.Equal("Not processed due to stop.", report.Items[3].Message);
        Assert.False(adapter.IsLocked);
    }

    [Fact]
    public async Task RunAsync_StaleSessionSignal_IsIgnored()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry);
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.False(await runner.NotifyTranslationStartedAsync("a", "stale-session", CancellationToken.None));
        Assert.True(await runner.NotifyTranslationStartedAsync("a", sessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("a", sessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "a", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["a"] = @"C:\out\a.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("a", sessionId, @"C:\out\a.mp4", CancellationToken.None));

        var report = await runTask;
        Assert.Equal(VideoProcessingItemLifecycleStatus.Completed, Assert.Single(report.Items).Status);
    }

    [Fact]
    public async Task RunAsync_KeepsQueueLockedUntilReportCompletes()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A", IncludeInBatch = true };
        var adapter = new FakeAdapter(entry)
        {
            BlockReportUntilReleased = true
        };
        var runner = new VideoProcessingQueueRunner();

        var runTask = runner.RunAsync(adapter, CancellationToken.None);
        var sessionId = await WaitForActiveEntryAsync(runner, "a");

        Assert.True(await runner.NotifyTranslationStartedAsync("a", sessionId, CancellationToken.None));
        Assert.True(await runner.NotifyTranslationReadyAsync("a", sessionId, CancellationToken.None));
        await WaitForStatusAsync(adapter, "a", VideoProcessingItemLifecycleStatus.FinalizingOutput);
        adapter.CompletedOutputs["a"] = @"C:\out\a.mp4";
        Assert.True(await runner.NotifyProcessingCompletedAsync("a", sessionId, @"C:\out\a.mp4", CancellationToken.None));

        await adapter.WaitForReportAsync();

        Assert.True(adapter.IsLocked);
        Assert.False(runTask.IsCompleted);

        adapter.ReleaseReport();
        var report = await runTask;

        Assert.Equal(1, report.Processed);
        Assert.False(adapter.IsLocked);
    }

    private static async Task<string> WaitForActiveEntryAsync(VideoProcessingQueueRunner runner, string entryId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var session = runner.ActiveSession;
            if (session is not null &&
                !string.IsNullOrWhiteSpace(session.SessionId) &&
                string.Equals(session.ActiveEntryId, entryId, StringComparison.OrdinalIgnoreCase))
            {
                return session.SessionId;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Active entry was not set to " + entryId);
    }

    private static async Task WaitForStatusAsync(FakeAdapter adapter, string entryId, VideoProcessingItemLifecycleStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (adapter.TryGetLastKnownStatus(entryId, out var current) && current == status)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException($"Status {status} was not observed for entry {entryId}.");
    }

    private sealed class FakeAdapter(params VideoEntry[] entries) : IVideoProcessingQueueAdapter
    {
        private readonly Dictionary<string, VideoProcessingItemLifecycleStatus> _lastStatuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource<bool> _reportStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseReport = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModeName => "Test";
        public TimeSpan TranslationStartTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan TranslationReadyTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan FinalizationTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public bool IsLocked { get; private set; }
        public bool BlockReportUntilReleased { get; init; }
        public IReadOnlyList<VideoEntry> Entries { get; } = entries;
        public List<string> SelectedEntryIds { get; } = [];
        public List<string> PreparedEntryIds { get; } = [];
        public List<string> OpenedEntryIds { get; } = [];
        public Dictionary<string, string?> CompletedOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public VideoProcessingQueueReport? LastReport { get; private set; }

        public IReadOnlyList<VideoEntry> GetEntriesInDisplayOrder() => Entries;

        public Task SelectEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        {
            SelectedEntryIds.Add(entry.Id);
            return Task.CompletedTask;
        }

        public Task PrepareEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        {
            PreparedEntryIds.Add(entry.Id);
            return Task.CompletedTask;
        }

        public Task OpenEntryAsync(VideoEntry entry, CancellationToken cancellationToken)
        {
            OpenedEntryIds.Add(entry.Id);
            return Task.CompletedTask;
        }

        public bool TryGetCompletedOutputPath(string entryId, out string? outputPath)
            => CompletedOutputs.TryGetValue(entryId, out outputPath);

        public void OnQueueLocked(bool locked) => IsLocked = locked;

        public void OnItemStateChanged(VideoProcessingQueueItem item)
            => _lastStatuses[item.EntryId] = item.Status;

        public async Task ShowReportAsync(VideoProcessingQueueReport report, CancellationToken cancellationToken)
        {
            LastReport = report;
            _reportStarted.TrySetResult(true);

            if (BlockReportUntilReleased)
                await _releaseReport.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForReportAsync() => _reportStarted.Task;

        public void ReleaseReport() => _releaseReport.TrySetResult(true);

        public bool TryGetLastKnownStatus(string entryId, out VideoProcessingItemLifecycleStatus status)
            => _lastStatuses.TryGetValue(entryId, out status);
    }
}
