using System;

namespace PublicVideoLauncher.Models;

public sealed class VideoProcessingQueueItem
{
    public VideoEntry? Entry { get; init; }
    public string EntryId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool WasSelected { get; init; }
    public VideoProcessingItemLifecycleStatus Status { get; set; } = VideoProcessingItemLifecycleStatus.Pending;
    public string? Message { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? TranslationStartedAtUtc { get; set; }
    public DateTime? TranslationReadyAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
