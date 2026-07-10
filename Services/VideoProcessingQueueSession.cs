using System;
using System.Collections.Generic;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public sealed class VideoProcessingQueueSession
{
    public string SessionId { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public IReadOnlyList<string> OrderedEntryIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, VideoProcessingQueueItem> ItemsByEntryId { get; init; }
        = new Dictionary<string, VideoProcessingQueueItem>(StringComparer.OrdinalIgnoreCase);
    public string? ActiveEntryId { get; set; }
    public bool StopRequested { get; set; }
    public DateTime StartedAtUtc { get; init; }
}
