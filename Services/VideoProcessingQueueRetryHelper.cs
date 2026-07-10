using System.Collections.Generic;
using System.Linq;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public static class VideoProcessingQueueRetryHelper
{
    public static int SelectOnlyRetryableEntries(IEnumerable<VideoEntry> entries, VideoProcessingQueueReport report)
    {
        var retryableEntryIds = report.Items
            .Where(x => x.Status is VideoProcessingItemLifecycleStatus.Failed or VideoProcessingItemLifecycleStatus.TimedOut)
            .Select(x => x.EntryId)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var selectedCount = 0;
        foreach (var entry in entries)
        {
            entry.IncludeInBatch = retryableEntryIds.Contains(entry.Id);
            if (entry.IncludeInBatch)
                selectedCount++;
        }

        return selectedCount;
    }
}
