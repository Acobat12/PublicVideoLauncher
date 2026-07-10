using PublicVideoLauncher.Services;
using System.IO;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class VideoWorkspaceTests
{
    [Fact]
    public void NewSlug_NormalizesTextAndFallsBackToVideo()
    {
        Assert.Equal("hello-world-2026", VideoWorkspace.NewSlug("Hello, World! 2026"));
        Assert.Equal("video", VideoWorkspace.NewSlug("***"));
    }

    [Fact]
    public void EncodeUrlPath_EscapesSegmentsButKeepsSeparators()
    {
        Assert.Equal("folder/My%20Video%201.mkv", VideoWorkspace.EncodeUrlPath("folder/My Video 1.mkv"));
    }

    [Fact]
    public void Load_UsesProxyForTranslationWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvl-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Clip 1.mkv"), "data");
            var workspace = new VideoWorkspace();

            workspace.Load(root, proxyTranslationEnabled: true);

            var entry = Assert.Single(workspace.Entries);
            Assert.Equal("/Clip%201.mkv", entry.OriginalUrl);
            Assert.Equal("/.public-video-cache/clip-1-mkv.play.mp4", entry.PlaybackUrl);
            Assert.Equal("/.public-video-cache/clip-1-mkv.proxy.mp4", entry.TranslationUrl);
            Assert.Equal("Pending", entry.CacheState);
            Assert.True(entry.IncludeInBatch);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
