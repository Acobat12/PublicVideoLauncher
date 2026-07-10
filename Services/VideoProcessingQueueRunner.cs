using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public sealed class VideoProcessingQueueRunner
{
    private const string NotSelectedMessage = "Not selected.";
    private const string NotProcessedDueToStopMessage = "Not processed due to stop.";
    private const string ActiveStopMessage = "Stopped during processing.";
    private const string FatalQueueErrorPendingMessage = "Not processed because the queue stopped after an unexpected error.";
    private const string TranslationStartTimeoutCode = "translation-start-timeout";
    private const string TranslationReadyTimeoutCode = "translation-ready-timeout";
    private const string FinalizationTimeoutCode = "processing-finalization-timeout";

    private readonly object _sync = new();
    private VideoProcessingQueueSession? _activeSession;
    private bool _isRunning;
    private string? _activeSessionId;
    private string? _activeEntryId;
    private RunnerPhase _currentPhase;
    private TaskCompletionSource<RunnerSignal>? _currentPhaseTcs;
    private List<RunnerSignal> _pendingSignals = new();

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _isRunning;
        }
    }

    public VideoProcessingQueueSession? ActiveSession
    {
        get
        {
            lock (_sync)
                return _activeSession;
        }
    }

    public async Task<VideoProcessingQueueReport> RunAsync(
        IVideoProcessingQueueAdapter adapter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var startedAtUtc = DateTime.UtcNow;
        VideoProcessingQueueSession session;

        lock (_sync)
        {
            if (_isRunning)
                throw new InvalidOperationException("A processing queue is already running.");

            _isRunning = true;
            session = CreateSession(adapter, startedAtUtc);
            _activeSession = session;
        }

        adapter.OnQueueLocked(true);

        try
        {
            foreach (var item in session.ItemsByEntryId.Values)
                adapter.OnItemStateChanged(item);

            for (var index = 0; index < session.OrderedEntryIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (session.StopRequested)
                {
                    MarkPendingItemsCanceled(session, index, adapter, NotProcessedDueToStopMessage);
                    break;
                }

                var item = session.ItemsByEntryId[session.OrderedEntryIds[index]];
                if (!item.WasSelected)
                    continue;

                await ProcessItemAsync(session, item, index, adapter, cancellationToken);

                if (session.StopRequested)
                {
                    MarkPendingItemsCanceled(session, index + 1, adapter, NotProcessedDueToStopMessage);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkCancellation(session, adapter, ActiveStopMessage, NotProcessedDueToStopMessage);
        }
        catch (Exception ex)
        {
            MarkUnexpectedQueueFailure(session, adapter, ex);
        }
        finally
        {
            ClearActiveState();
        }

        var report = BuildReport(session);
        try
        {
            await adapter.ShowReportAsync(report, CancellationToken.None);
            return report;
        }
        finally
        {
            adapter.OnQueueLocked(false);
        }
    }

    public Task RequestStopAsync()
    {
        lock (_sync)
        {
            if (_activeSession is not null)
                _activeSession.StopRequested = true;
        }

        return Task.CompletedTask;
    }

    public Task<bool> NotifyTranslationStartedAsync(string entryId, string? sessionId, CancellationToken cancellationToken)
        => NotifyAsync(entryId, sessionId, RunnerSignal.Started(), cancellationToken);

    public Task<bool> NotifyTranslationReadyAsync(string entryId, string? sessionId, CancellationToken cancellationToken)
        => NotifyAsync(entryId, sessionId, RunnerSignal.Ready(), cancellationToken);

    public Task<bool> NotifyTranslationFailedAsync(string entryId, string? sessionId, string errorCode, string errorMessage, CancellationToken cancellationToken)
        => NotifyAsync(entryId, sessionId, RunnerSignal.Failed(errorCode, errorMessage), cancellationToken);

    public Task<bool> NotifyProcessingCompletedAsync(string entryId, string? sessionId, string? outputPath, CancellationToken cancellationToken)
        => NotifyAsync(entryId, sessionId, RunnerSignal.Completed(outputPath), cancellationToken);

    public Task<bool> NotifyProcessingFailedAsync(string entryId, string? sessionId, string errorCode, string errorMessage, CancellationToken cancellationToken)
        => NotifyAsync(entryId, sessionId, RunnerSignal.Failed(errorCode, errorMessage), cancellationToken);

    private Task<bool> NotifyAsync(string entryId, string? sessionId, RunnerSignal signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_isRunning ||
                string.IsNullOrWhiteSpace(entryId) ||
                string.IsNullOrWhiteSpace(sessionId) ||
                !string.Equals(_activeSessionId, sessionId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_activeEntryId, entryId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(false);
            }

            _pendingSignals.Add(signal);
            TryDispatchPendingSignalsLocked();
            return Task.FromResult(true);
        }
    }

    private async Task ProcessItemAsync(
        VideoProcessingQueueSession session,
        VideoProcessingQueueItem item,
        int currentIndex,
        IVideoProcessingQueueAdapter adapter,
        CancellationToken cancellationToken)
    {
        try
        {
            if (item.Entry is null)
                throw new InvalidOperationException("Queue item is missing the source VideoEntry.");

            item.StartedAtUtc = DateTime.UtcNow;

            lock (_sync)
            {
                session.ActiveEntryId = item.EntryId;
                _activeSessionId = session.SessionId;
                _activeEntryId = item.EntryId;
                _pendingSignals = new List<RunnerSignal>();
                _currentPhase = RunnerPhase.None;
                _currentPhaseTcs = null;
            }

            await adapter.SelectEntryAsync(item.Entry, cancellationToken);

            SetItemState(item, VideoProcessingItemLifecycleStatus.Preparing, "Preparing item...", adapter);
            await adapter.PrepareEntryAsync(item.Entry, cancellationToken);

            SetItemState(item, VideoProcessingItemLifecycleStatus.WaitingForTranslationStart, "Waiting for translation to start...", adapter);
            BeginPhase(RunnerPhase.WaitingForTranslationStart);

            SetItemState(item, VideoProcessingItemLifecycleStatus.OpeningPlayer, "Opening player...", adapter);
            await adapter.OpenEntryAsync(item.Entry, cancellationToken);

            var startSignal = await WaitForSignalAsync(adapter.TranslationStartTimeout, cancellationToken);
            if (HandlePreFinalizationSignal(item, startSignal, BuildTranslationStartTimeoutMessage(), TranslationStartTimeoutCode, adapter))
                return;

            if (startSignal.Kind == RunnerSignalKind.Started)
            {
                item.TranslationStartedAtUtc = DateTime.UtcNow;
                SetItemState(item, VideoProcessingItemLifecycleStatus.WaitingForTranslationReady, "Waiting for translation result...", adapter);
                BeginPhase(RunnerPhase.WaitingForTranslationReady);

                var readySignal = await WaitForSignalAsync(adapter.TranslationReadyTimeout, cancellationToken);
                if (HandlePreFinalizationSignal(item, readySignal, BuildTranslationReadyTimeoutMessage(), TranslationReadyTimeoutCode, adapter))
                    return;
            }

            item.TranslationReadyAtUtc = DateTime.UtcNow;
            SetItemState(item, VideoProcessingItemLifecycleStatus.FinalizingOutput, "Finalizing output...", adapter);
            BeginPhase(RunnerPhase.FinalizingOutput);

            var finalSignal = await WaitForSignalAsync(adapter.FinalizationTimeout, cancellationToken);
            if (finalSignal.Kind == RunnerSignalKind.TimedOut)
            {
                MarkTimedOut(item, BuildFinalizationTimeoutMessage(), FinalizationTimeoutCode, adapter);
                return;
            }

            if (finalSignal.Kind == RunnerSignalKind.Failed)
            {
                MarkFailed(item, finalSignal.ErrorCode ?? "processing-failed", finalSignal.ErrorMessage ?? "Processing failed.", adapter);
                return;
            }

            if (!adapter.TryGetCompletedOutputPath(item.EntryId, out var completedOutputPath))
            {
                MarkFailed(item, "output-missing", "Processing completed, but no output path was confirmed.", adapter);
                return;
            }

            item.OutputPath = completedOutputPath ?? finalSignal.OutputPath;
            item.FinishedAtUtc = DateTime.UtcNow;
            SetItemState(item, VideoProcessingItemLifecycleStatus.Completed, "Completed.", adapter);

            lock (_sync)
            {
                if (session.StopRequested)
                    MarkPendingItemsCanceled(session, currentIndex + 1, adapter, NotProcessedDueToStopMessage);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkFailed(item, GetUnexpectedFailureCode(item.Status), ex.Message, adapter);
        }
    }

    private bool HandlePreFinalizationSignal(
        VideoProcessingQueueItem item,
        RunnerSignal signal,
        string timeoutMessage,
        string timeoutCode,
        IVideoProcessingQueueAdapter adapter)
    {
        switch (signal.Kind)
        {
            case RunnerSignalKind.TimedOut:
                MarkTimedOut(item, timeoutMessage, timeoutCode, adapter);
                return true;

            case RunnerSignalKind.Failed:
                MarkFailed(item, signal.ErrorCode ?? "translation-failed", signal.ErrorMessage ?? "Translation failed.", adapter);
                return true;

            default:
                return false;
        }
    }

    private void MarkPendingItemsCanceled(
        VideoProcessingQueueSession session,
        int startIndex,
        IVideoProcessingQueueAdapter adapter,
        string message)
    {
        for (var index = startIndex; index < session.OrderedEntryIds.Count; index++)
        {
            var item = session.ItemsByEntryId[session.OrderedEntryIds[index]];
            if (!item.WasSelected)
                continue;

            if (item.Status is not VideoProcessingItemLifecycleStatus.Pending)
                continue;

            item.StartedAtUtc ??= DateTime.UtcNow;
            item.FinishedAtUtc = DateTime.UtcNow;
            SetItemState(item, VideoProcessingItemLifecycleStatus.Canceled, message, adapter);
        }
    }

    private void MarkCancellation(
        VideoProcessingQueueSession session,
        IVideoProcessingQueueAdapter adapter,
        string activeItemMessage,
        string pendingItemMessage)
    {
        if (session.ActiveEntryId is not null &&
            session.ItemsByEntryId.TryGetValue(session.ActiveEntryId, out var activeItem) &&
            activeItem.Status is not VideoProcessingItemLifecycleStatus.Completed and
                not VideoProcessingItemLifecycleStatus.Failed and
                not VideoProcessingItemLifecycleStatus.TimedOut and
                not VideoProcessingItemLifecycleStatus.Canceled)
        {
            activeItem.StartedAtUtc ??= DateTime.UtcNow;
            activeItem.FinishedAtUtc = DateTime.UtcNow;
            SetItemState(activeItem, VideoProcessingItemLifecycleStatus.Canceled, activeItemMessage, adapter);
        }

        var activeIndex = 0;
        if (session.ActiveEntryId is not null)
        {
            for (var index = 0; index < session.OrderedEntryIds.Count; index++)
            {
                if (!string.Equals(session.OrderedEntryIds[index], session.ActiveEntryId, StringComparison.OrdinalIgnoreCase))
                    continue;

                activeIndex = index + 1;
                break;
            }
        }

        MarkPendingItemsCanceled(session, Math.Max(activeIndex, 0), adapter, pendingItemMessage);
    }

    private void MarkTimedOut(
        VideoProcessingQueueItem item,
        string message,
        string errorCode,
        IVideoProcessingQueueAdapter adapter)
    {
        item.ErrorCode = errorCode;
        item.ErrorMessage = message;
        item.FinishedAtUtc = DateTime.UtcNow;
        SetItemState(item, VideoProcessingItemLifecycleStatus.TimedOut, message, adapter);
    }

    private void MarkFailed(
        VideoProcessingQueueItem item,
        string errorCode,
        string errorMessage,
        IVideoProcessingQueueAdapter adapter)
    {
        item.ErrorCode = errorCode;
        item.ErrorMessage = errorMessage;
        item.FinishedAtUtc = DateTime.UtcNow;
        SetItemState(item, VideoProcessingItemLifecycleStatus.Failed, errorMessage, adapter);
    }

    private void MarkUnexpectedQueueFailure(
        VideoProcessingQueueSession session,
        IVideoProcessingQueueAdapter adapter,
        Exception exception)
    {
        if (session.ActiveEntryId is not null &&
            session.ItemsByEntryId.TryGetValue(session.ActiveEntryId, out var activeItem) &&
            activeItem.Status is not VideoProcessingItemLifecycleStatus.Completed and
                not VideoProcessingItemLifecycleStatus.Failed and
                not VideoProcessingItemLifecycleStatus.TimedOut and
                not VideoProcessingItemLifecycleStatus.Canceled)
        {
            MarkFailed(activeItem, GetUnexpectedFailureCode(activeItem.Status), exception.Message, adapter);
        }

        var startIndex = 0;
        if (session.ActiveEntryId is not null)
        {
            for (var index = 0; index < session.OrderedEntryIds.Count; index++)
            {
                if (!string.Equals(session.OrderedEntryIds[index], session.ActiveEntryId, StringComparison.OrdinalIgnoreCase))
                    continue;

                startIndex = index + 1;
                break;
            }
        }

        MarkPendingItemsCanceled(session, startIndex, adapter, FatalQueueErrorPendingMessage);
    }

    private void SetItemState(
        VideoProcessingQueueItem item,
        VideoProcessingItemLifecycleStatus status,
        string? message,
        IVideoProcessingQueueAdapter adapter)
    {
        item.Status = status;
        item.Message = message;
        adapter.OnItemStateChanged(item);
    }

    private void BeginPhase(RunnerPhase phase)
    {
        lock (_sync)
        {
            _currentPhase = phase;
            _currentPhaseTcs = new TaskCompletionSource<RunnerSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryDispatchPendingSignalsLocked();
        }
    }

    private async Task<RunnerSignal> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<RunnerSignal> signalTask;

        lock (_sync)
        {
            signalTask = _currentPhaseTcs?.Task
                ?? throw new InvalidOperationException("No active queue phase is waiting for signals.");
        }

        var delayTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(signalTask, delayTask);
        if (completedTask != signalTask)
        {
            await completedTask;
            return RunnerSignal.TimedOut();
        }

        var signal = await signalTask;

        lock (_sync)
        {
            _currentPhase = RunnerPhase.None;
            _currentPhaseTcs = null;
        }

        return signal;
    }

    private void TryDispatchPendingSignalsLocked()
    {
        if (_currentPhaseTcs is null || _currentPhaseTcs.Task.IsCompleted)
            return;

        for (var index = 0; index < _pendingSignals.Count; index++)
        {
            var signal = _pendingSignals[index];
            if (!IsRelevantForCurrentPhase(signal.Kind))
                continue;

            _pendingSignals.RemoveAt(index);
            _currentPhaseTcs.TrySetResult(signal);
            return;
        }
    }

    private bool IsRelevantForCurrentPhase(RunnerSignalKind kind)
        => _currentPhase switch
        {
            RunnerPhase.WaitingForTranslationStart => kind is RunnerSignalKind.Started or RunnerSignalKind.Ready or RunnerSignalKind.Failed,
            RunnerPhase.WaitingForTranslationReady => kind is RunnerSignalKind.Ready or RunnerSignalKind.Failed,
            RunnerPhase.FinalizingOutput => kind is RunnerSignalKind.Completed or RunnerSignalKind.Failed,
            _ => false
        };

    private VideoProcessingQueueSession CreateSession(IVideoProcessingQueueAdapter adapter, DateTime startedAtUtc)
    {
        var itemsByEntryId = new Dictionary<string, VideoProcessingQueueItem>(StringComparer.OrdinalIgnoreCase);
        var orderedEntryIds = new List<string>();

        foreach (var entry in adapter.GetEntriesInDisplayOrder())
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                throw new InvalidOperationException("Every VideoEntry must have a non-empty Id.");

            if (!itemsByEntryId.TryAdd(entry.Id, new VideoProcessingQueueItem
            {
                Entry = entry,
                EntryId = entry.Id,
                Title = entry.Title,
                WasSelected = entry.IncludeInBatch,
                Status = entry.IncludeInBatch ? VideoProcessingItemLifecycleStatus.Pending : VideoProcessingItemLifecycleStatus.Skipped,
                Message = entry.IncludeInBatch ? null : NotSelectedMessage,
                CreatedAtUtc = startedAtUtc,
                StartedAtUtc = entry.IncludeInBatch ? null : startedAtUtc,
                FinishedAtUtc = entry.IncludeInBatch ? null : startedAtUtc
            }))
            {
                throw new InvalidOperationException("Duplicate VideoEntry.Id detected: " + entry.Id);
            }

            orderedEntryIds.Add(entry.Id);
        }

        return new VideoProcessingQueueSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Mode = adapter.ModeName,
            OrderedEntryIds = orderedEntryIds,
            ItemsByEntryId = itemsByEntryId,
            StartedAtUtc = startedAtUtc
        };
    }

    private VideoProcessingQueueReport BuildReport(VideoProcessingQueueSession session)
    {
        var orderedItems = session.OrderedEntryIds
            .Select(id => session.ItemsByEntryId[id])
            .ToList();

        return new VideoProcessingQueueReport
        {
            Mode = session.Mode,
            Found = orderedItems.Count,
            Selected = orderedItems.Count(x => x.WasSelected),
            Processed = orderedItems.Count(x => x.Status == VideoProcessingItemLifecycleStatus.Completed),
            Failed = orderedItems.Count(x => x.Status is VideoProcessingItemLifecycleStatus.Failed or VideoProcessingItemLifecycleStatus.TimedOut),
            Skipped = orderedItems.Count(x => x.Status == VideoProcessingItemLifecycleStatus.Skipped),
            Canceled = orderedItems.Count(x => x.Status == VideoProcessingItemLifecycleStatus.Canceled),
            Items = orderedItems.Select(x => new VideoProcessingQueueItemResult
            {
                Mode = session.Mode,
                EntryId = x.EntryId,
                Title = x.Title,
                WasSelected = x.WasSelected,
                Status = x.Status,
                Succeeded = x.Status == VideoProcessingItemLifecycleStatus.Completed,
                Message = x.Message,
                OutputPath = x.OutputPath,
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage,
                SkipReason = x.Status == VideoProcessingItemLifecycleStatus.Skipped ? x.Message : null,
                StartedAtUtc = x.StartedAtUtc ?? x.CreatedAtUtc,
                FinishedAtUtc = x.FinishedAtUtc ?? x.CreatedAtUtc
            }).ToList(),
            StartedAtUtc = session.StartedAtUtc,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private void ClearActiveState()
    {
        lock (_sync)
        {
            _activeSessionId = null;
            _activeEntryId = null;
            _currentPhase = RunnerPhase.None;
            _currentPhaseTcs = null;
            _pendingSignals = new List<RunnerSignal>();
            _activeSession = null;
            _isRunning = false;
        }
    }

    private enum RunnerPhase
    {
        None,
        WaitingForTranslationStart,
        WaitingForTranslationReady,
        FinalizingOutput
    }

    private static string BuildTranslationStartTimeoutMessage()
        => "Translation did not start before timeout. Check that vot.user.js is installed and enabled, then verify that the callback can reach the app.";

    private static string BuildTranslationReadyTimeoutMessage()
        => "Translation started, but the result did not arrive before timeout.";

    private static string BuildFinalizationTimeoutMessage()
        => "Translation was received, but finalization did not finish before timeout.";

    private static string GetUnexpectedFailureCode(VideoProcessingItemLifecycleStatus status)
        => status switch
        {
            VideoProcessingItemLifecycleStatus.Preparing => "prepare-failed",
            VideoProcessingItemLifecycleStatus.OpeningPlayer => "open-player-failed",
            VideoProcessingItemLifecycleStatus.WaitingForTranslationStart => "translation-start-failed",
            VideoProcessingItemLifecycleStatus.WaitingForTranslationReady => "translation-ready-failed",
            VideoProcessingItemLifecycleStatus.FinalizingOutput => "processing-failed",
            _ => "processing-failed"
        };

    private enum RunnerSignalKind
    {
        Started,
        Ready,
        Failed,
        Completed,
        TimedOut
    }

    private sealed record RunnerSignal(
        RunnerSignalKind Kind,
        string? ErrorCode = null,
        string? ErrorMessage = null,
        string? OutputPath = null)
    {
        public static RunnerSignal Started() => new(RunnerSignalKind.Started);
        public static RunnerSignal Ready() => new(RunnerSignalKind.Ready);
        public static RunnerSignal Completed(string? outputPath) => new(RunnerSignalKind.Completed, OutputPath: outputPath);
        public static RunnerSignal Failed(string? errorCode, string? errorMessage) => new(RunnerSignalKind.Failed, errorCode, errorMessage);
        public static RunnerSignal TimedOut() => new(RunnerSignalKind.TimedOut);
    }
}
