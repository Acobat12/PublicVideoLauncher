using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class MainModeQueueAdapterTests
{
    [Fact]
    public void GetEntriesInDisplayOrder_ReturnsOriginalEntries()
    {
        var entries = new List<VideoEntry>
        {
            new() { Id = "b", Title = "Video B" },
            new() { Id = "a", Title = "Video A" }
        };

        var adapter = CreateAdapter(entries);

        Assert.Same(entries, adapter.GetEntriesInDisplayOrder());
    }

    [Fact]
    public void RegisterCompletedOutputPath_AllowsRunnerLookup()
    {
        var adapter = CreateAdapter([new VideoEntry { Id = "a", Title = "Video A" }]);

        adapter.RegisterCompletedOutputPath("a", @"C:\out\a.translated.mp4");

        Assert.True(adapter.TryGetCompletedOutputPath("a", out var outputPath));
        Assert.Equal(@"C:\out\a.translated.mp4", outputPath);
    }

    private static MainModeQueueAdapter CreateAdapter(IReadOnlyList<VideoEntry> entries)
    {
        return new MainModeQueueAdapter(
            entries,
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            static _ => { },
            static _ => { },
            static (_, _) => Task.CompletedTask);
    }
}
