using System.Collections.Generic;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class BatchSelectionHelperTests
{
    [Fact]
    public void SelectAll_SetsIncludeInBatchToTrue_ForEveryEntry()
    {
        var entries = new List<VideoEntry>
        {
            new() { IncludeInBatch = false },
            new() { IncludeInBatch = true },
            new() { IncludeInBatch = false }
        };

        BatchSelectionHelper.SelectAll(entries);

        Assert.All(entries, entry => Assert.True(entry.IncludeInBatch));
    }

    [Fact]
    public void UnselectAll_SetsIncludeInBatchToFalse_ForEveryEntry()
    {
        var entries = new List<VideoEntry>
        {
            new() { IncludeInBatch = true },
            new() { IncludeInBatch = true },
            new() { IncludeInBatch = false }
        };

        BatchSelectionHelper.UnselectAll(entries);

        Assert.All(entries, entry => Assert.False(entry.IncludeInBatch));
    }

    [Fact]
    public void InvertSelection_FlipsIncludeInBatch_ForEveryEntry()
    {
        var entries = new List<VideoEntry>
        {
            new() { IncludeInBatch = true },
            new() { IncludeInBatch = false },
            new() { IncludeInBatch = true }
        };

        BatchSelectionHelper.InvertSelection(entries);

        Assert.False(entries[0].IncludeInBatch);
        Assert.True(entries[1].IncludeInBatch);
        Assert.False(entries[2].IncludeInBatch);
    }

    [Fact]
    public void CountSelected_ReturnsOnlySelectedEntries()
    {
        var entries = new List<VideoEntry>
        {
            new() { IncludeInBatch = true },
            new() { IncludeInBatch = false },
            new() { IncludeInBatch = true }
        };

        var count = BatchSelectionHelper.CountSelected(entries);

        Assert.Equal(2, count);
    }
}
