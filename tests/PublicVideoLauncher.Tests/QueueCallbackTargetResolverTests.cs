using System.Collections.Generic;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class QueueCallbackTargetResolverTests
{
    [Fact]
    public void ResolveQueueEntry_UsesEntryIdWhenAvailable()
    {
        var entries = new List<VideoEntry>
        {
            new() { Id = "one", Title = "One", PlaybackUrl = "/videos/one.mp4" },
            new() { Id = "two", Title = "Two", PlaybackUrl = "/videos/two.mp4" }
        };

        var entry = QueueCallbackTargetResolver.ResolveQueueEntry(entries, "two", "/videos/unknown.mp4");

        Assert.NotNull(entry);
        Assert.Equal("two", entry!.Id);
    }

    [Fact]
    public void ResolveQueueEntry_UnmatchedPayload_DoesNotFallBackToSelectedEntry()
    {
        var entries = new List<VideoEntry>
        {
            new() { Id = "selected", Title = "Selected", PlaybackUrl = "/videos/selected.mp4" }
        };

        var entry = QueueCallbackTargetResolver.ResolveQueueEntry(entries, null, "/videos/unknown.mp4");

        Assert.Null(entry);
    }
}
