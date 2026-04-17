using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
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
                    PlaybackUrl = "/.public-video-cache/sample.play.mp4",
                    TranslationUrl = "/.public-video-cache/sample.proxy.mp4",
                    OriginalUrl = "/Sample.mkv",
                    Extension = ".mkv"
                }
            };

            var service = new PlayerFilesService();
            await service.WriteAllAsync(root, entries, null, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(root, "playlist.json")));
            Assert.True(File.Exists(Path.Combine(root, "player.html")));
            Assert.True(File.Exists(Path.Combine(root, "favicon.svg")));

            var json = await File.ReadAllTextAsync(Path.Combine(root, "playlist.json"));
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement[0];
            Assert.Equal("/.public-video-cache/sample.play.mp4", item.GetProperty("src").GetString());
            Assert.Equal("/.public-video-cache/sample.proxy.mp4", item.GetProperty("translateSrc").GetString());
            Assert.Equal("/Sample.mkv", item.GetProperty("originalSrc").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
