using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Tests;

public sealed class TranslationStudioServiceTests
{
    [Fact]
    public void BuildPublicPlayerUrl_JobPlaylist_DoesNotIncludeSelectedSegment()
    {
        var service = new TranslationStudioService();
        var job = new TranslationStudioJob
        {
            PlaylistRelativeUrl = "/_translation-studio/video-one/playlist.json"
        };

        var url = service.BuildPublicPlayerUrl("https://example.com/", job);

        Assert.StartsWith(
            "https://example.com/_translation-studio/video-one/player.html?list=%2F_translation-studio%2Fvideo-one%2Fplaylist.json",
            url,
            StringComparison.Ordinal);
        Assert.DoesNotContain("&src=", url, StringComparison.Ordinal);
        Assert.Contains("&_ts=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPublicPlayerUrl_SelectedSegment_IncludesOnlyThatSegmentSrc()
    {
        var service = new TranslationStudioService();
        var job = new TranslationStudioJob
        {
            PlaylistRelativeUrl = "/_translation-studio/video-one/playlist.json"
        };
        var segment = new TranslationStudioSegment
        {
            PayloadRelativeUrl = "/_translation-studio/video-one/segments/segment-001/payload.mp4"
        };

        var url = service.BuildPublicPlayerUrl("https://example.com/", job, segment);

        Assert.Contains(
            "list=%2F_translation-studio%2Fvideo-one%2Fplaylist.json",
            url,
            StringComparison.Ordinal);
        Assert.Contains(
            "&src=%2F_translation-studio%2Fvideo-one%2Fsegments%2Fsegment-001%2Fpayload.mp4",
            url,
            StringComparison.Ordinal);
        Assert.Contains("&_ts=", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadJobAsync_NormalizesLegacyTranslationStudioPageUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-studio-tests-" + Path.GetRandomFileName());
        try
        {
            var sourceVideoId = "video-one";
            var jobFolder = TranslationStudioService.GetJobFolder(root, sourceVideoId);
            Directory.CreateDirectory(jobFolder);
            var job = new TranslationStudioJob
            {
                SchemaVersion = 1,
                JobId = sourceVideoId,
                WorkspaceRoot = root,
                JobFolder = jobFolder,
                SourceVideoId = sourceVideoId,
                SourceVideoTitle = "Video One",
                PlaylistRelativeUrl = "/_translation-studio/video-one/playlist.json",
                PlayerRelativeUrl = "/_translation-studio/video-one/translation-studio.html?list=%2F_translation-studio%2Fvideo-one%2Fplaylist.json&src=%2Fold.mp4",
                Segments = new List<TranslationStudioSegment>()
            };
            var json = JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.WriteAllTextAsync(
                TranslationStudioService.GetManifestPath(jobFolder),
                json,
                TestContext.Current.CancellationToken);

            var loaded = await new TranslationStudioService().LoadJobAsync(root, sourceVideoId, TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(
                "/_translation-studio/video-one/player.html?list=%2F_translation-studio%2Fvideo-one%2Fplaylist.json",
                loaded.PlayerRelativeUrl);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildAutomaticSegmentPlan_DoesNotSplit_WhenDurationIsBelowThreshold()
    {
        var plan = TranslationStudioService.BuildAutomaticSegmentPlan(
            totalDurationSeconds: 26 * 60 + 58,
            splitLongVideo: true,
            splitThresholdMinutes: 120,
            targetSegmentMinutes: 60);

        var segment = Assert.Single(plan);
        Assert.Equal(0d, segment.StartSeconds);
        Assert.Equal(26 * 60 + 58, segment.DurationSeconds, 3);
    }

    [Fact]
    public void BuildPreparationSignature_Changes_WhenSegmentPlanChanges()
    {
        var shortPlan = TranslationStudioService.BuildAutomaticSegmentPlan(
            totalDurationSeconds: 26 * 60 + 58,
            splitLongVideo: true,
            splitThresholdMinutes: 120,
            targetSegmentMinutes: 60);
        var longPlan = TranslationStudioService.BuildAutomaticSegmentPlan(
            totalDurationSeconds: 14 * 60 * 60 + 37 * 60 + 11,
            splitLongVideo: true,
            splitThresholdMinutes: 120,
            targetSegmentMinutes: 60);

        var shortSignature = TranslationStudioService.BuildPreparationSignature(
            @"C:\videos\lesson.avi",
            12345,
            45678,
            26 * 60 + 58,
            true,
            120,
            60,
            shortPlan);
        var longSignature = TranslationStudioService.BuildPreparationSignature(
            @"C:\videos\lesson.avi",
            12345,
            45678,
            14 * 60 * 60 + 37 * 60 + 11,
            true,
            120,
            60,
            longPlan);

        Assert.NotEqual(shortSignature, longSignature);
    }

    [Fact]
    public void ShouldResetPreparedAssets_ReturnsTrue_ForOlderSchemaOrChangedSignature()
    {
        var existingJob = new TranslationStudioJob
        {
            SchemaVersion = 6,
            PreparationSignature = "OLD"
        };

        Assert.True(TranslationStudioService.ShouldResetPreparedAssets(existingJob, "NEW"));
        Assert.False(TranslationStudioService.ShouldResetPreparedAssets(null, "NEW"));
    }
}
