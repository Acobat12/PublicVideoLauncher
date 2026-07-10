using System.Linq;

namespace PublicVideoLauncher.Services;

public static class VideoProcessingQueueProgressHelper
{
    public static string? BuildProgressText(
        VideoProcessingQueueSession? session,
        Func<int, int, string>? formatter = null)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.ActiveEntryId))
            return null;

        var selectedEntryIds = session.OrderedEntryIds
            .Where(id => session.ItemsByEntryId.TryGetValue(id, out var item) && item.WasSelected)
            .ToList();

        if (selectedEntryIds.Count == 0)
            return null;

        var currentIndex = selectedEntryIds.FindIndex(id => string.Equals(id, session.ActiveEntryId, System.StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return null;

        var current = currentIndex + 1;
        var total = selectedEntryIds.Count;
        return formatter is null
            ? $"Processing {current} / {total}"
            : formatter(current, total);
    }
}
