using System.Collections.Generic;
using System.Linq;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public static class BatchSelectionHelper
{
    public static void SelectAll(IEnumerable<VideoEntry> entries)
    {
        foreach (var entry in entries)
            entry.IncludeInBatch = true;
    }

    public static void UnselectAll(IEnumerable<VideoEntry> entries)
    {
        foreach (var entry in entries)
            entry.IncludeInBatch = false;
    }

    public static void InvertSelection(IEnumerable<VideoEntry> entries)
    {
        foreach (var entry in entries)
            entry.IncludeInBatch = !entry.IncludeInBatch;
    }

    public static int CountSelected(IEnumerable<VideoEntry> entries)
        => entries.Count(x => x.IncludeInBatch);
}
