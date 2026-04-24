using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PublicVideoLauncher.Services;

public sealed class FfmpegService
{
    private const int WindowsPowerShellFallbackPathLength = 240;
    private const int PowerShellUnblockTimeoutMs = 5000;
    private const uint GenericRead = 0x80000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public static void PrepareFreshOutputPath(string? path, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                if (OperatingSystem.IsWindows())
                    DeleteZoneIdentifier(directory);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Failed to create output directory for " + path + ": " + ex.Message);
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                DeleteZoneIdentifier(path);
            }

            if (File.Exists(path))
                File.Delete(path);

            if (OperatingSystem.IsWindows())
            {
                DeleteZoneIdentifier(path);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Failed to prepare fresh output path " + path + ": " + ex.Message);
        }
    }

    private static void DeleteZoneIdentifier(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
            return;

        try { DeleteFile(path + ":Zone.Identifier"); } catch { }

        try
        {
            DeleteFile(BuildExtendedWindowsPath(path) + ":Zone.Identifier");
        }
        catch
        {
        }
    }

    public async Task<MediaProbeInfo> ProbeMediaAsync(
        string ffmpegPath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            throw new InvalidOperationException("ffprobe was not found рядом с ffmpeg или в PATH.");

        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=format_name:stream=codec_type,codec_name,profile,pix_fmt");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(inputPath);

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

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed for {inputPath}\n\nSTDERR:\n{stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        string? formatName = null;
        string? videoCodec = null;
        string? audioCodec = null;
        string? pixFmt = null;

        if (root.TryGetProperty("format", out var formatEl) &&
            formatEl.ValueKind == JsonValueKind.Object &&
            formatEl.TryGetProperty("format_name", out var formatNameEl))
        {
            formatName = formatNameEl.GetString();
        }

        if (root.TryGetProperty("streams", out var streamsEl) &&
            streamsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streamsEl.EnumerateArray())
            {
                if (stream.ValueKind != JsonValueKind.Object)
                    continue;

                var codecType = stream.TryGetProperty("codec_type", out var codecTypeEl)
                    ? codecTypeEl.GetString()
                    : null;

                if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase) && videoCodec is null)
                {
                    if (stream.TryGetProperty("codec_name", out var codecNameEl))
                        videoCodec = codecNameEl.GetString();

                    if (stream.TryGetProperty("pix_fmt", out var pixFmtEl))
                        pixFmt = pixFmtEl.GetString();
                }
                else if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase) && audioCodec is null)
                {
                    if (stream.TryGetProperty("codec_name", out var codecNameEl))
                        audioCodec = codecNameEl.GetString();
                }
            }
        }

        return new MediaProbeInfo
        {
            ContainerFormat = formatName,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            PixelFormat = pixFmt
        };
    }

    public async Task NormalizeForPlaybackAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var probe = await ProbeMediaAsync(ffmpegPath, inputPath, cancellationToken);
        log?.Invoke($"Probe: {probe}");

        if (probe.IsBrowserSafeDirectPlay)
        {
            log?.Invoke("Source already browser-safe. Remuxing to normalized MP4 cache.");
            await RemuxToFaststartMp4Async(ffmpegPath, inputPath, outputPath, cancellationToken);
            return;
        }

        if (probe.CanRemuxToMp4WithoutVideoTranscode)
        {
            var pixFmtSafe = string.Equals(probe.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase);

            if (pixFmtSafe)
            {
                log?.Invoke("Video stream is browser-safe. Remuxing video and normalizing audio to MP4.");
                await RemuxToFaststartMp4Async(ffmpegPath, inputPath, outputPath, cancellationToken);
                return;
            }
        }

        log?.Invoke("Full normalization required. Transcoding to H.264/AAC MP4.");
        await ConvertToPlaybackMp4Async(ffmpegPath, inputPath, outputPath, cancellationToken);
    }

    public Task RemuxToFaststartMp4Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
        => RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-fflags", "+discardcorrupt",
            "-err_detect", "ignore_err",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-map", "0:a:0?",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "160k",
            "-ac", "2",
            "-ar", "44100",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);

    public static bool TryUnblockFile(string? path, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return true;

            DeleteZoneIdentifier(path);

            var stillBlocked = HasZoneIdentifier(path);
            if (stillBlocked)
            {
                if (!TryUnblockFileWithPowerShell(path, log))
                {
                    DeleteZoneIdentifier(path);
                    stillBlocked = HasZoneIdentifier(path);
                    if (stillBlocked)
                        return false;
                }
                else
                {
                    stillBlocked = HasZoneIdentifier(path);
                }
            }

            if (stillBlocked)
                log?.Invoke("File is still blocked: " + path);

            return !stillBlocked;
        }
        catch (Exception ex)
        {
            log?.Invoke("Failed to unblock file " + path + ": " + ex.Message);
            return false;
        }
    }

    public static bool HasZoneIdentifier(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            var zoneIdentifierPath = path + ":Zone.Identifier";
            using var directHandle = CreateFile(zoneIdentifierPath, GenericRead, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (!directHandle.IsInvalid)
                return true;

            var extendedZoneIdentifierPath = BuildExtendedWindowsPath(path) + ":Zone.Identifier";
            using var extendedHandle = CreateFile(extendedZoneIdentifierPath, GenericRead, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            return !extendedHandle.IsInvalid;
        }
        catch
        {
            return false;
        }
    }

    public static void TryUnblockDirectoryTree(string? rootPath, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return;

        TryUnblockFile(rootPath, log);

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                TryUnblockFile(directory, log);

            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                TryUnblockFile(file, log);
        }
        catch (Exception ex)
        {
            log?.Invoke("Failed to unblock prepared folder " + rootPath + ": " + ex.Message);
        }
    }

    public static void CopyFileWithoutAlternateStreams(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsWindows())
                DeleteZoneIdentifier(directory);
        }

        PrepareFreshOutputPath(destinationPath);

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    internal static bool NeedsPowerShellUnblockFallback(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return Path.GetFullPath(path).Length >= WindowsPowerShellFallbackPathLength;
        }
        catch
        {
            return path.Length >= WindowsPowerShellFallbackPathLength;
        }
    }

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
                await NormalizeForPlaybackAsync(ffmpegPath, entry.FullPath, entry.PlaybackCachePath, log, cancellationToken);
            }
            else
            {
                log($"Using playable MP4 cache: {Path.GetFileName(entry.PlaybackCachePath)}");
            }

            if (!string.IsNullOrWhiteSpace(entry.TranslationCachePath))
            {
                var proxySourcePath = entry.PlaybackCachePath;
                var needsProxyRebuild = await NeedsTranslationProxyRebuildAsync(
                    ffmpegPath,
                    proxySourcePath,
                    entry.TranslationCachePath,
                    log,
                    cancellationToken);

                if (needsProxyRebuild)
                {
                    entry.CacheState = "Building translation proxy";
                    log($"Creating proxy MP4 for translation: {entry.FileName}");
                    await ConvertToProxyMp4Async(ffmpegPath, proxySourcePath, entry.TranslationCachePath, cancellationToken);
                }
                else
                {
                    log($"Using translation proxy cache: {Path.GetFileName(entry.TranslationCachePath)}");
                }
            }

            entry.CacheState = "Ready";
        }
    }

    public Task ConvertToPlaybackMp4Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
        => RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-fflags", "+discardcorrupt",
            "-err_detect", "ignore_err",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-profile:v", "main",
            "-level", "4.1",
            "-c:a", "aac",
            "-b:a", "160k",
            "-ac", "2",
            "-ar", "44100",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);

    public async Task ConvertToProxyMp4Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var durationSeconds = await ProbeDurationAsync(ffmpegPath, inputPath, cancellationToken);

        await RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-f", "lavfi",
            "-i", $"color=c=black:s=320x180:r=1:d={FormatFfmpegDurationSeconds(durationSeconds)}",
            "-map", "1:v:0",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "stillimage",
            "-pix_fmt", "yuv420p",
            "-r", "1",
            "-g", "1",
            "-c:a", "aac",
            "-b:a", "128k",
            "-ac", "2",
            "-ar", "44100",
            "-t", FormatFfmpegTime(durationSeconds),
            "-shortest",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);
    }

    public async Task ConvertToLowFpsPayloadMp4Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var durationSeconds = await ProbeDurationAsync(ffmpegPath, inputPath, cancellationToken);

        await RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-f", "lavfi",
            "-i", $"color=c=black:s=320x180:r=1:d={FormatFfmpegDurationSeconds(durationSeconds)}",
            "-map", "1:v:0",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "stillimage",
            "-pix_fmt", "yuv420p",
            "-r", "1",
            "-g", "1",
            "-c:a", "aac",
            "-b:a", "128k",
            "-ac", "2",
            "-ar", "44100",
            "-t", FormatFfmpegTime(durationSeconds),
            "-shortest",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);
    }

    public async Task MergeVideoWithExternalAudioAsync(
        string ffmpegPath,
        string videoPath,
        string translatedAudioPath,
        string outputPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Merging translated audio into video: {Path.GetFileName(videoPath)}");

        await RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-i", videoPath,
            "-i", translatedAudioPath,
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            "-movflags", "+faststart",
            outputPath,
        }, outputPath, cancellationToken);
    }

    public async Task<double> ProbeDurationAsync(string ffmpegPath, string inputPath, CancellationToken cancellationToken)
    {
        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        if (!string.IsNullOrWhiteSpace(ffprobePath) && File.Exists(ffprobePath))
        {
            var probed = await TryProbeDurationWithFfprobeAsync(ffprobePath, inputPath, cancellationToken);
            if (probed is double ffprobeDuration && ffprobeDuration > 0.1d)
                return ffprobeDuration;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);

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

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var match = Regex.Match(stderr, @"Duration:\s*(?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}(?:\.\d+)?)");
        if (!match.Success)
            throw new InvalidOperationException("Could not detect source duration from ffmpeg output.");

        var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
        return (hours * 3600d) + (minutes * 60d) + seconds;
    }

    public Task CreateSegmentMp4Async(
        string ffmpegPath,
        string inputPath,
        double startSeconds,
        double durationSeconds,
        string outputPath,
        CancellationToken cancellationToken)
        => RunFfmpegAsync(ffmpegPath, new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-ss", FormatFfmpegTime(startSeconds),
            "-i", inputPath,
            "-t", FormatFfmpegTime(durationSeconds),
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

    public async Task ConcatVideosAsync(
        string ffmpegPath,
        IReadOnlyList<string> inputPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (inputPaths.Count == 0)
            throw new InvalidOperationException("No segment files were provided for concatenation.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var concatFile = Path.Combine(Path.GetTempPath(), $"translation-studio-concat-{Guid.NewGuid():N}.txt");
        try
        {
            var lines = inputPaths.Select(path => $"file '{path.Replace("'", "'\\''")}'").ToArray();
            await File.WriteAllLinesAsync(concatFile, lines, Encoding.UTF8, cancellationToken);

            await RunFfmpegAsync(ffmpegPath, new[]
            {
                "-y",
                "-hide_banner",
                "-loglevel", "error",
                "-f", "concat",
                "-safe", "0",
                "-i", concatFile,
                "-c", "copy",
                "-movflags", "+faststart",
                outputPath,
            }, outputPath, cancellationToken);
        }
        finally
        {
            try { File.Delete(concatFile); } catch { }
        }
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> arguments, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        PrepareFreshOutputPath(outputPath);

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

        if (!TryUnblockFile(outputPath))
        {
            // Best-effort only: Windows Mark-of-the-Web on generated media must not abort Prepare.
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

    private async Task<bool> NeedsTranslationProxyRebuildAsync(
        string ffmpegPath,
        string sourcePath,
        string proxyPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (NeedsRebuild(sourcePath, proxyPath))
            return true;

        try
        {
            var sourceDuration = await ProbeDurationAsync(ffmpegPath, sourcePath, cancellationToken);
            var proxyDuration = await ProbeDurationAsync(ffmpegPath, proxyPath, cancellationToken);
            if (ProxyDurationMismatch(sourceDuration, proxyDuration))
            {
                log($"Proxy cache duration mismatch detected. Rebuilding proxy: source={sourceDuration:0.###}s proxy={proxyDuration:0.###}s");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            log($"Proxy cache validation failed. Rebuilding proxy: {ex.Message}");
            return true;
        }
    }

    private static string FormatFfmpegTime(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        var totalHours = (int)Math.Floor(time.TotalHours);
        return string.Create(CultureInfo.InvariantCulture, $"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    private static string FormatFfmpegDurationSeconds(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        return clamped.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static bool ProxyDurationMismatch(double sourceDuration, double proxyDuration)
    {
        if (sourceDuration <= 0.1d || proxyDuration <= 0.1d)
            return true;

        var tolerance = Math.Max(2d, sourceDuration * 0.01d);
        return Math.Abs(sourceDuration - proxyDuration) > tolerance;
    }

    private static string? ResolveFfprobePath(string ffmpegPath)
    {
        var directory = Path.GetDirectoryName(ffmpegPath);
        var local = string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, "ffprobe" + Path.GetExtension(ffmpegPath));

        if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
            return local;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var fileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }
        }

        return local;
    }

    private static bool TryUnblockFileWithPowerShell(string path, Action<string>? log)
    {
        try
        {
            var powerShellPath = GetWindowsPowerShellPath();
            var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);

            var psi = new ProcessStartInfo
            {
                FileName = powerShellPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("Unblock-File -LiteralPath '" + escapedPath + "'");

            using var process = new Process { StartInfo = psi };
            process.Start();

            if (!process.WaitForExit(PowerShellUnblockTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                log?.Invoke("PowerShell unblock timed out: " + path);
                return false;
            }

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                log?.Invoke("PowerShell unblock failed for " + path + ": " + error.Trim());
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke("PowerShell unblock failed for " + path + ": " + ex.Message);
            return false;
        }
    }

    private static string GetWindowsPowerShellPath()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var fullPath = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(fullPath) ? fullPath : "powershell.exe";
    }

    private static string BuildExtendedWindowsPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\",
            StringComparison.Ordinal))
        {
            return fullPath;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + fullPath.TrimStart('\\');
        }

        return @"\\?\" + fullPath;
    }

    private static async Task<double?> TryProbeDurationWithFfprobeAsync(string ffprobePath, string inputPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=duration:stream=codec_type,duration:stream_tags");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(inputPath);

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

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            return null;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            double? formatDuration = null;
            var videoDurations = new List<double>();
            var audioDurations = new List<double>();
            var tagDurations = new List<double>();

            if (root.TryGetProperty("format", out var formatElement) &&
                formatElement.ValueKind == JsonValueKind.Object &&
                formatElement.TryGetProperty("duration", out var formatDurationElement))
            {
                var parsedFormatDuration = ParseDurationElement(formatDurationElement);
                if (parsedFormatDuration is > 0.1d)
                    formatDuration = parsedFormatDuration.Value;
            }

            if (root.TryGetProperty("streams", out var streamsElement) &&
                streamsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsElement.EnumerateArray())
                {
                    if (stream.ValueKind != JsonValueKind.Object)
                        continue;

                    var codecType = stream.TryGetProperty("codec_type", out var codecTypeElement)
                        ? codecTypeElement.GetString()
                        : null;

                    if (stream.TryGetProperty("duration", out var streamDurationElement))
                    {
                        var streamDuration = ParseDurationElement(streamDurationElement);
                        if (streamDuration is > 0.1d)
                        {
                            if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                            {
                                videoDurations.Add(streamDuration.Value);
                            }
                            else if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                            {
                                audioDurations.Add(streamDuration.Value);
                            }
                            else
                            {
                                tagDurations.Add(streamDuration.Value);
                            }
                        }
                    }

                    if (stream.TryGetProperty("tags", out var tagsElement) &&
                        tagsElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var tag in tagsElement.EnumerateObject())
                        {
                            if (tag.Name.Contains("DURATION", StringComparison.OrdinalIgnoreCase))
                            {
                                var tagDuration = ParseDurationElement(tag.Value);
                                if (tagDuration is > 0.1d)
                                    tagDurations.Add(tagDuration.Value);
                            }
                        }
                    }
                }
            }

            return SelectBestDurationCandidate(formatDuration, videoDurations, audioDurations, tagDurations);
        }
        catch
        {
            return null;
        }
    }

    internal static double? SelectBestDurationCandidate(
        double? formatDuration,
        IReadOnlyList<double> videoDurations,
        IReadOnlyList<double> audioDurations,
        IReadOnlyList<double> tagDurations)
    {
        var normalizedFormat = NormalizeDuration(formatDuration);
        var normalizedVideo = SelectClusteredDuration(videoDurations);
        var normalizedAudio = SelectClusteredDuration(audioDurations);
        var normalizedTag = SelectClusteredDuration(tagDurations);

        if (normalizedVideo is > 0.1d)
        {
            var candidatesCloseToVideo = new List<double> { normalizedVideo.Value };

            if (AreDurationsClose(normalizedVideo.Value, normalizedFormat))
                candidatesCloseToVideo.Add(normalizedFormat!.Value);

            if (AreDurationsClose(normalizedVideo.Value, normalizedAudio))
                candidatesCloseToVideo.Add(normalizedAudio!.Value);

            if (AreDurationsClose(normalizedVideo.Value, normalizedTag))
                candidatesCloseToVideo.Add(normalizedTag!.Value);

            return candidatesCloseToVideo.Max();
        }

        if (normalizedFormat is > 0.1d)
        {
            if (AreDurationsClose(normalizedFormat.Value, normalizedAudio))
                return Math.Max(normalizedFormat.Value, normalizedAudio!.Value);

            if (AreDurationsClose(normalizedFormat.Value, normalizedTag))
                return Math.Max(normalizedFormat.Value, normalizedTag!.Value);

            return normalizedFormat.Value;
        }

        if (normalizedAudio is > 0.1d)
        {
            if (AreDurationsClose(normalizedAudio.Value, normalizedTag))
                return Math.Max(normalizedAudio.Value, normalizedTag!.Value);

            return normalizedAudio.Value;
        }

        return normalizedTag;
    }

    private static double? SelectClusteredDuration(IReadOnlyList<double> durations)
    {
        var normalized = durations
            .Select(x => NormalizeDuration(x))
            .Where(x => x is > 0.1d)
            .Select(x => x!.Value)
            .ToArray();

        if (normalized.Length == 0)
            return null;

        if (normalized.Length == 1)
            return normalized[0];

        var grouped = normalized
            .GroupBy(x => Math.Round(x, 1))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First();

        return grouped.Max();
    }

    private static double? NormalizeDuration(double? value)
    {
        if (value is not > 0.1d || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;

        return value.Value;
    }

    private static bool AreDurationsClose(double candidate, double? other)
    {
        if (other is not > 0.1d)
            return false;

        var max = Math.Max(candidate, other.Value);
        var tolerance = Math.Max(5d, max * 0.05d);
        return Math.Abs(candidate - other.Value) <= tolerance;
    }

    private static double? ParseDurationElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var numericValue) => numericValue,
            JsonValueKind.String => ParseDurationText(element.GetString()),
            _ => null
        };
    }

    private static double? ParseDurationText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            return seconds;
        }

        var match = Regex.Match(trimmed, @"^(?<hours>\d{1,3}):(?<minutes>\d{2}):(?<seconds>\d{2}(?:[\.,]\d+)?)$");
        if (!match.Success)
            return null;

        var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        var parsedSeconds = double.Parse(match.Groups["seconds"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (hours * 3600d) + (minutes * 60d) + parsedSeconds;
    }
}

public sealed class MediaProbeInfo
{
    public string? ContainerFormat { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public string? PixelFormat { get; init; }

    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoCodec);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec);

    public bool IsRealMp4Container =>
        !string.IsNullOrWhiteSpace(ContainerFormat) &&
        ContainerFormat.Contains("mp4", StringComparison.OrdinalIgnoreCase);

    public bool IsBrowserSafeDirectPlay =>
        IsRealMp4Container &&
        string.Equals(VideoCodec, "h264", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase) &&
        (
            string.IsNullOrWhiteSpace(AudioCodec) ||
            string.Equals(AudioCodec, "aac", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(AudioCodec, "mp3", StringComparison.OrdinalIgnoreCase)
        );

    public bool CanRemuxToMp4WithoutVideoTranscode =>
        string.Equals(VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
        => $"container={ContainerFormat ?? "?"}, video={VideoCodec ?? "?"}, audio={AudioCodec ?? "?"}, pix_fmt={PixelFormat ?? "?"}";
}
