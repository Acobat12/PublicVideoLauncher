using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class StudioModeQueueAdapterTests
{
    [Fact]
    public async Task PrepareEntryAsync_StoresPreparedJobByEntryId()
    {
        var entry = new VideoEntry { Id = "a", Title = "Video A" };
        var expectedJob = new TranslationStudioJob { JobId = "a", SourceVideoTitle = "Video A" };

        var adapter = new StudioModeQueueAdapter(
            [entry],
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.FromResult(new TranslationStudioJob { JobId = "a", SourceVideoTitle = "Video A" }),
            static (_, _) => Task.CompletedTask,
            static _ => { },
            static _ => { },
            static (_, _) => Task.CompletedTask);

        await adapter.PrepareEntryAsync(entry, CancellationToken.None);

        Assert.True(adapter.TryGetPreparedJob("a", out var preparedJob));
        Assert.NotNull(preparedJob);
        Assert.Equal(expectedJob.JobId, preparedJob!.JobId);
    }

    [Fact]
    public void RegisterCompletedOutputPath_AllowsRunnerLookup()
    {
        var adapter = new StudioModeQueueAdapter(
            [new VideoEntry { Id = "a", Title = "Video A" }],
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.FromResult(new TranslationStudioJob { JobId = "a" }),
            static (_, _) => Task.CompletedTask,
            static _ => { },
            static _ => { },
            static (_, _) => Task.CompletedTask);

        adapter.RegisterCompletedOutputPath("a", @"C:\out\a.studio.translated.mp4");

        Assert.True(adapter.TryGetCompletedOutputPath("a", out var outputPath));
        Assert.Equal(@"C:\out\a.studio.translated.mp4", outputPath);
    }
}
