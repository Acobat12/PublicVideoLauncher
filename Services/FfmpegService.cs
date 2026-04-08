using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class FfmpegService
{
    public async Task EnsureCachesAsync(
        string ffmpegPath,
        IReadOnlyList<VideoEntry> entries,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectMp4)
            {
                entry.CacheState = "Not needed";
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.PlaybackCachePath))
                throw new InvalidOperationException($"Playback cache path is missing for {entry.FileName}");

            if (NeedsRebuild(entry.FullPath, entry.PlaybackCachePath))
            {
                entry.CacheState = "Converting playback";
                log($"Creating playable MP4: {entry.FileName}");
                await ConvertToPlaybackMp4Async(ffmpegPath, entry.FullPath, entry.PlaybackCachePath, cancellationToken);
            }
            else
            {
                log($"Using playable MP4 cache: {Path.GetFileName(entry.PlaybackCachePath)}");
            }

            if (!string.IsNullOrWhiteSpace(entry.TranslationCachePath))
            {
                if (NeedsRebuild(entry.FullPath, entry.TranslationCachePath))
                {
                    entry.CacheState = "Building translation proxy";
                    log($"Creating proxy MP4 for translation: {entry.FileName}");
                    await ConvertToProxyMp4Async(ffmpegPath, entry.FullPath, entry.TranslationCachePath, cancellationToken);
                }
                else
                {
                    log($"Using translation proxy cache: {Path.GetFileName(entry.TranslationCachePath)}");
                }
            }

            entry.CacheState = "Ready";
        }
    }

    public Task ConvertToPlaybackMp4Async(string ffmpegPath, string inputPath, string outputPath, CancellationToken cancellationToken)
        => RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "160k",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);

    public Task ConvertToProxyMp4Async(string ffmpegPath, string inputPath, string outputPath, CancellationToken cancellationToken)
        => RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-f", "lavfi",
            "-i", "color=c=black:s=320x180:r=1",
            "-map", "1:v:0",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "stillimage",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "128k",
            "-ac", "2",
            "-ar", "44100",
            "-shortest",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);

    private static async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> arguments, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"ffmpeg failed.\nOutput: {outputPath}\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");
        }
    }

    private static bool NeedsRebuild(string sourcePath, string outputPath)
    {
        if (!File.Exists(outputPath))
            return true;

        var source = new FileInfo(sourcePath);
        var output = new FileInfo(outputPath);

        return output.Length <= 0 || output.LastWriteTimeUtc < source.LastWriteTimeUtc;
    }
}
