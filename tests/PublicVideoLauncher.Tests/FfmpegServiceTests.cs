using PublicVideoLauncher.Services;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class FfmpegServiceTests
{
    [Fact]
    public void NeedsPowerShellUnblockFallback_ReturnsFalse_ForShortPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.False(FfmpegService.NeedsPowerShellUnblockFallback(@"C:\Temp\short.mp4"));
    }

    [Fact]
    public void NeedsPowerShellUnblockFallback_ReturnsTrue_ForLongPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var longPath = @"C:\Temp\" + new string('a', 260) + ".mp4";
        Assert.True(FfmpegService.NeedsPowerShellUnblockFallback(longPath));
    }

    [Fact]
    public void ProxyDurationMismatch_ReturnsTrue_WhenProxyIsTruncated()
    {
        Assert.True(FfmpegService.ProxyDurationMismatch(1618.1495, 819.0));
    }

    [Fact]
    public void ProxyDurationMismatch_ReturnsFalse_WhenDurationsAreClose()
    {
        Assert.False(FfmpegService.ProxyDurationMismatch(1618.1495, 1618.0));
    }

    [Fact]
    public void SelectBestDurationCandidate_PrefersVideoDuration_WhenFormatAndAudioAreBroken()
    {
        var selected = FfmpegService.SelectBestDurationCandidate(
            formatDuration: 225431.916553,
            videoDurations: [1618.118118],
            audioDurations: [225431.916553],
            tagDurations: []);

        Assert.NotNull(selected);
        Assert.InRange(selected!.Value, 1618.0, 1618.2);
    }

    [Fact]
    public void SelectBestDurationCandidate_UsesCloseDurations_WhenVideoAndAudioAgree()
    {
        var selected = FfmpegService.SelectBestDurationCandidate(
            formatDuration: 1618.2,
            videoDurations: [1618.1],
            audioDurations: [1618.3],
            tagDurations: [1618.2]);

        Assert.NotNull(selected);
        Assert.InRange(selected!.Value, 1618.2, 1618.3);
    }

    [Fact]
    public void SelectBestDurationCandidate_FallsBackToFormat_WhenVideoIsMissing()
    {
        var selected = FfmpegService.SelectBestDurationCandidate(
            formatDuration: 903.5,
            videoDurations: [],
            audioDurations: [],
            tagDurations: []);

        Assert.Equal(903.5, selected);
    }
}
