using PublicVideoLauncher.Services;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class SmokePipelineTests
{
    [Fact]
    public async Task ScanToPlaylistAndPlayerFiles_WorksForMixedVideoSet()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-smoke-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Clip One.mkv"), "fake-video");
            File.WriteAllText(Path.Combine(root, "Clip Two.mp4"), "fake-video");
            File.WriteAllText(Path.Combine(root, "ignore.txt"), "not-a-video");

            var workspace = new VideoWorkspace();
            workspace.Load(root, proxyTranslationEnabled: true);

            Assert.Equal(2, workspace.Entries.Count);
            Assert.Contains(workspace.Entries, x => x.FileName == "Clip One.mkv" && x.PlaybackUrl.EndsWith("clip-one-mkv.play.mp4") && x.TranslationUrl.EndsWith("clip-one-mkv.proxy.mp4"));
            Assert.Contains(workspace.Entries, x => x.FileName == "Clip Two.mp4" && x.PlaybackUrl == "/.public-video-cache/clip-two-mp4.play.mp4" && x.TranslationUrl == "/.public-video-cache/clip-two-mp4.proxy.mp4");
            Assert.Contains(workspace.Entries, x => x.FileName == "Clip One.mkv" && x.VotHelperUrl == "/.public-video-vot/clip-one-mkv.mp4");
            Assert.Contains(workspace.Entries, x => x.FileName == "Clip Two.mp4" && x.VotHelperUrl == "/.public-video-vot/clip-two-mp4.mp4");

            var service = new PlayerFilesService();
            await service.WriteAllAsync(root, workspace.Entries, null, TestContext.Current.CancellationToken);

            var playlistPath = Path.Combine(root, "playlist.json");
            var playerPath = Path.Combine(root, "player.html");
            var faviconPath = Path.Combine(root, "favicon.svg");

            Assert.True(File.Exists(playlistPath));
            Assert.True(File.Exists(playerPath));
            Assert.True(File.Exists(faviconPath));

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(playlistPath, TestContext.Current.CancellationToken));
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("/.public-video-cache/clip-one-mkv.play.mp4", doc.RootElement[0].GetProperty("src").GetString());
            Assert.Equal("/.public-video-cache/clip-one-mkv.play.mp4", doc.RootElement[0].GetProperty("playbackSrc").GetString());
            Assert.Equal("/.public-video-vot/clip-one-mkv.mp4", doc.RootElement[0].GetProperty("votSrc").GetString());
            Assert.Contains(
                "const playlistUrl = params.get(\"list\") || \"/playlist.json\";",
                await File.ReadAllTextAsync(playerPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
