using System;
using System.Collections.Generic;

namespace PublicVideoLauncher.Models;

public sealed class VideoProcessingQueueReport
{
    public string Mode { get; init; } = string.Empty;
    public int Found { get; init; }
    public int Selected { get; init; }
    public int Processed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Canceled { get; init; }
    public IReadOnlyList<VideoProcessingQueueItemResult> Items { get; init; } = Array.Empty<VideoProcessingQueueItemResult>();
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
}
