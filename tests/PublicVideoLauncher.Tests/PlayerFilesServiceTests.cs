using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class PlayerFilesServiceTests
{
    [Fact]
    public async Task WriteAllAsync_WritesPlaylistAndPlayerAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-player-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var entries = new List<VideoEntry>
            {
                new()
                {
                    Id = "sample",
                    Title = "Sample",
                    VotHelperUrl = "/.public-video-vot/sample.mp4",
                    PlaybackUrl = "/.public-video-cache/sample.play.mp4",
                    TranslationUrl = "/.public-video-cache/sample.proxy.mp4",
                    OriginalUrl = "/Sample.mkv",
                    Extension = ".mkv"
                }
            };

            var service = new PlayerFilesService();
            await service.WriteAllAsync(root, entries, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(root, "playlist.json")));
            Assert.True(File.Exists(Path.Combine(root, "player.html")));
            Assert.True(File.Exists(Path.Combine(root, "favicon.svg")));

            var json = await File.ReadAllTextAsync(Path.Combine(root, "playlist.json"), TestContext.Current.CancellationToken);
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement[0];
            Assert.Equal("/.public-video-cache/sample.play.mp4", item.GetProperty("src").GetString());
            Assert.Equal("/.public-video-cache/sample.play.mp4", item.GetProperty("playbackSrc").GetString());
            Assert.Equal("/.public-video-vot/sample.mp4", item.GetProperty("votSrc").GetString());
            Assert.Equal("/.public-video-cache/sample.proxy.mp4", item.GetProperty("translateSrc").GetString());
            Assert.Equal("/Sample.mkv", item.GetProperty("originalSrc").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAllAsync_LocalizesPlayerPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-player-ru-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var entries = new List<VideoEntry>
            {
                new()
                {
                    Id = "sample",
                    Title = "Sample",
                    VotHelperUrl = "/.public-video-vot/sample.mp4",
                    PlaybackUrl = "/.public-video-cache/sample.play.mp4",
                    TranslationUrl = "/.public-video-cache/sample.proxy.mp4",
                    OriginalUrl = "/Sample.mkv",
                    Extension = ".mkv"
                }
            };

            var service = new PlayerFilesService();
            await service.WriteAllAsync(root, entries, null, AppLanguage.Russian, TestContext.Current.CancellationToken);

            var html = await File.ReadAllTextAsync(Path.Combine(root, "player.html"), TestContext.Current.CancellationToken);
            Assert.Contains("<html lang=\"ru\">", html);
            Assert.Contains("Локальный видеоплеер", html);
            Assert.Contains("Автоматически включать следующее", html);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WritePlayerPageAsync_EmbedsExtendedTranslationCallbackProtocol()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-player-callback-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var service = new PlayerFilesService();
            await service.WritePlayerPageAsync(
                root,
                "player.html",
                "/translation-ready",
                "Local Video Player",
                TestContext.Current.CancellationToken);

            var html = await File.ReadAllTextAsync(Path.Combine(root, "player.html"), TestContext.Current.CancellationToken);
            Assert.Contains("buildCallbackUrl(\"translation-started\")", html);
            Assert.Contains("buildCallbackUrl(\"translation-ready\")", html);
            Assert.Contains("buildCallbackUrl(\"translation-failed\")", html);
            Assert.Contains("function getQueueEntryId()", html);
            Assert.Contains("function getQueueSessionId()", html);
            Assert.Contains("entryId: getQueueEntryId()", html);
            Assert.Contains("sessionId: getQueueSessionId()", html);
            Assert.Contains("errorCode: errorCode || null", html);
            Assert.Contains("errorMessage: errorMessage || null", html);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
