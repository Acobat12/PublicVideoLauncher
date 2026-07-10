using System;

namespace PublicVideoLauncher.Models;

public sealed class VideoProcessingQueueItemResult
{
    public string Mode { get; init; } = string.Empty;
    public string EntryId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool WasSelected { get; init; }
    public VideoProcessingItemLifecycleStatus Status { get; init; } = VideoProcessingItemLifecycleStatus.Pending;
    public bool Succeeded { get; init; }
    public string? Message { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SkipReason { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
}
