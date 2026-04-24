using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class TranslationStudioService
{
    private const int CurrentSchemaVersion = 9;
    private const string PreparationPipelineVersion = "studio-prepare-v4-mainwindow-cache-pipeline";
    private readonly PlayerFilesService _playerFilesService = new();
    private readonly FfmpegService _ffmpegService = new();

    public async Task<TranslationStudioJob> PrepareJobAsync(
        string workspaceRoot,
        TranslationStudioPrepareRequest request,
        string ffmpegPath,
        string? translationCallbackUrl,
        AppLanguage language,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("Workspace root is empty.", nameof(workspaceRoot));

        var sourceEntry = request.SourceEntry ?? throw new ArgumentNullException(nameof(request.SourceEntry));
        var studioRoot = GetStudioRootFolder(workspaceRoot);
        var jobFolder = Path.Combine(studioRoot, sourceEntry.Id);
        Directory.CreateDirectory(jobFolder);
        var finalOutputPath = Path.Combine(
            workspaceRoot,
            "merged_output",
            Path.GetFileNameWithoutExtension(sourceEntry.FileName) + ".studio.translated.mp4");

        var sourceDurationSeconds = await _ffmpegService.ProbeDurationAsync(ffmpegPath, sourceEntry.FullPath, cancellationToken);
        var segmentPlan = BuildAutomaticSegmentPlan(
            sourceDurationSeconds,
            request.SplitLongVideo,
            request.SplitIfLongerThanMinutes,
            request.TargetSegmentMinutes);
        var shouldSplit = segmentPlan.Count > 1;

        log($"Detected source duration: {TimeSpan.FromSeconds(Math.Max(0d, sourceDurationSeconds)):hh\\:mm\\:ss}");
        log(shouldSplit
            ? $"Segmentation enabled: {segmentPlan.Count} segment(s) planned."
            : "Segmentation not needed for this video.");

        var sourceFileInfo = new FileInfo(sourceEntry.FullPath);
        var preparationSignature = BuildPreparationSignature(
            sourceEntry.FullPath,
            sourceFileInfo.Length,
            sourceFileInfo.LastWriteTimeUtc.Ticks,
            sourceDurationSeconds,
            request.SplitLongVideo,
            request.SplitIfLongerThanMinutes,
            request.TargetSegmentMinutes,
            segmentPlan);

        var existingJob = await LoadJobAsync(workspaceRoot, sourceEntry.Id, cancellationToken);
        if (ShouldResetPreparedAssets(existingJob, preparationSignature))
        {
            log(existingJob is null
                ? "Preparing new Translation Studio job files."
                : "Detected outdated Translation Studio prepared files. Rebuilding payload and segment assets.");
            ResetPreparedAssets(jobFolder, finalOutputPath, log);
        }

        var segments = shouldSplit
            ? await PrepareSegmentedJobAsync(workspaceRoot, sourceEntry, jobFolder, ffmpegPath, segmentPlan, log, cancellationToken)
            : await PrepareSingleSegmentJobAsync(workspaceRoot, sourceEntry, jobFolder, ffmpegPath, sourceDurationSeconds, log, cancellationToken);

        var playlistRelativeUrl = BuildRelativeUrl(workspaceRoot, Path.Combine(jobFolder, "playlist.json"));
        var playerPageRelativeUrl = BuildRelativeUrl(workspaceRoot, Path.Combine(jobFolder, "player.html"));
        var playerRelativeUrl = $"{playerPageRelativeUrl}?list={Uri.EscapeDataString(playlistRelativeUrl)}";

        var entries = segments.Select(segment => new VideoEntry
        {
            Id = segment.SegmentId,
            Title = segment.Title,
            FileName = Path.GetFileName(segment.PayloadPath),
            FullPath = segment.PayloadPath,
            Extension = ".mp4",
            OriginalUrl = segment.PayloadRelativeUrl,
            PlaybackUrl = segment.PayloadRelativeUrl,
            TranslationUrl = segment.PayloadRelativeUrl,
            VotHelperUrl = segment.PayloadRelativeUrl,
            ProxyModeEnabled = false,
            CacheState = "Ready"
        }).ToList();

        await _playerFilesService.WriteAllAsync(jobFolder, entries, translationCallbackUrl, language, cancellationToken);
        UnblockPreparedJobFolder(jobFolder, log);
        var firstSegment = segments[0];
        var job = new TranslationStudioJob
        {
            SchemaVersion = CurrentSchemaVersion,
            PreparationSignature = preparationSignature,
            JobId = sourceEntry.Id,
            WorkspaceRoot = workspaceRoot,
            JobFolder = jobFolder,
            SourceVideoId = sourceEntry.Id,
            SourceVideoTitle = sourceEntry.Title,
            SourceVideoFileName = sourceEntry.FileName,
            SourceVideoPath = sourceEntry.FullPath,
            SourceDurationSeconds = sourceDurationSeconds,
            AutoMergeOnCallback = request.AutoMergeOnCallback,
            IsSegmented = shouldSplit,
            SplitIfLongerThanMinutes = request.SplitIfLongerThanMinutes,
            TargetSegmentMinutes = request.TargetSegmentMinutes,
            PayloadPath = shouldSplit ? Path.Combine(jobFolder, "segments") : firstSegment.PayloadPath,
            PayloadRelativeUrl = firstSegment.PayloadRelativeUrl,
            PlaylistRelativeUrl = playlistRelativeUrl,
            PlayerRelativeUrl = playerRelativeUrl,
            TranslatedAudioPath = firstSegment.TranslatedAudioPath,
            MergedOutputPath = firstSegment.MergedOutputPath,
            FinalOutputPath = finalOutputPath,
            PreparedAtUtc = DateTime.UtcNow,
            Segments = segments
        };

        await SaveManifestAsync(job, cancellationToken);
        UnblockPreparedJobFolder(jobFolder, log);
        return job;
    }

    public async Task SaveManifestAsync(TranslationStudioJob job, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(job.JobFolder);
        var manifestPath = GetManifestPath(job.JobFolder);
        var json = JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
    }

    public string BuildPublicPlayerUrl(string publicBaseUrl, TranslationStudioJob job)
    {
        return BuildPublicPlayerUrl(publicBaseUrl, job, null);
    }

    public string BuildPublicPlayerUrl(string publicBaseUrl, TranslationStudioJob job, TranslationStudioSegment? segment)
    {
        var playerPath = BuildPlayerRelativeUrl(job, segment);
        return publicBaseUrl.TrimEnd('/') + AppendCacheBuster(playerPath);
    }

    public async Task<TranslationStudioJob?> LoadJobAsync(string workspaceRoot, string sourceVideoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(sourceVideoId))
            return null;

        var manifestPath = GetManifestPath(workspaceRoot, sourceVideoId);
        if (!File.Exists(manifestPath))
            return null;

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var job = JsonSerializer.Deserialize<TranslationStudioJob>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (job is null)
            return null;

        var refreshRecommended = job.SchemaVersion < CurrentSchemaVersion || string.IsNullOrWhiteSpace(job.PreparationSignature);
        job.Segments ??= new List<TranslationStudioSegment>();
        job.PlayerRelativeUrl = NormalizePlayerRelativeUrl(job);
        NormalizeSingleSegmentPaths(job);
        UnblockGeneratedFiles(job);
        job.SchemaVersion = CurrentSchemaVersion;
        job.PreparationRefreshRecommended = refreshRecommended;
        return job;
    }

    public static string GetStudioRootFolder(string workspaceRoot) => Path.Combine(workspaceRoot, ".public-video-cache", "translation-studio");

    public static string GetJobFolder(string workspaceRoot, string sourceVideoId) => Path.Combine(GetStudioRootFolder(workspaceRoot), sourceVideoId);

    public static string GetManifestPath(string workspaceRoot, string sourceVideoId) => GetManifestPath(GetJobFolder(workspaceRoot, sourceVideoId));

    public static string GetManifestPath(string jobFolder) => Path.Combine(jobFolder, "job-manifest.json");

    private static string BuildPlayerRelativeUrl(TranslationStudioJob job, TranslationStudioSegment? segment)
    {
        var playlistRelativeUrl = job.PlaylistRelativeUrl;
        var playerRelativeUrl = "/" + string.Join("/", job.PlaylistRelativeUrl.TrimStart('/').Split('/').SkipLast(1)) + "/player.html";
        var url = $"{playerRelativeUrl}?list={Uri.EscapeDataString(playlistRelativeUrl)}";
        if (segment is not null && !string.IsNullOrWhiteSpace(segment.PayloadRelativeUrl))
            url += "&src=" + Uri.EscapeDataString(segment.PayloadRelativeUrl);

        return url;
    }

    private static string NormalizePlayerRelativeUrl(TranslationStudioJob job)
    {
        if (string.IsNullOrWhiteSpace(job.PlayerRelativeUrl) ||
            job.PlayerRelativeUrl.Contains("translation-studio.html", StringComparison.OrdinalIgnoreCase) ||
            job.PlayerRelativeUrl.Contains("&src=", StringComparison.OrdinalIgnoreCase) ||
            job.PlayerRelativeUrl.Contains("?src=", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPlayerRelativeUrl(job, null);
        }

        return job.PlayerRelativeUrl;
    }

    private static string AppendCacheBuster(string url)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + "_ts=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private async Task<List<TranslationStudioSegment>> PrepareSingleSegmentJobAsync(
        string workspaceRoot,
        VideoEntry sourceEntry,
        string jobFolder,
        string ffmpegPath,
        double durationSeconds,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var sourceMp4Path = Path.Combine(jobFolder, "source.mp4");
        var payloadPath = Path.Combine(jobFolder, "payload.mp4");

        if (NeedsRebuild(sourceEntry.FullPath, sourceMp4Path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceMp4Path)!);
            log($"Creating Translation Studio playable source with the same pipeline as MainWindow: {sourceEntry.FileName}");
            await _ffmpegService.NormalizeForPlaybackAsync(
                ffmpegPath,
                sourceEntry.FullPath,
                sourceMp4Path,
                log,
                cancellationToken);
            TryUnblockPreparedMedia(sourceMp4Path, log);
        }
        else
        {
            log($"Using existing Translation Studio source mp4: {Path.GetFileName(sourceMp4Path)}");
            TryUnblockPreparedMedia(sourceMp4Path, log);
        }

        if (NeedsRebuild(sourceMp4Path, payloadPath))
        {
            log($"Creating Translation Studio payload with the same proxy pipeline as MainWindow: {sourceEntry.FileName}");
            await _ffmpegService.ConvertToProxyMp4Async(
                ffmpegPath,
                sourceMp4Path,
                payloadPath,
                cancellationToken);
            TryUnblockPreparedMedia(payloadPath, log);
        }
        else
        {
            log($"Using existing Translation Studio payload: {Path.GetFileName(payloadPath)}");
            TryUnblockPreparedMedia(payloadPath, log);
        }

        return
        [
            new TranslationStudioSegment
            {
                JobId = sourceEntry.Id,
                SegmentId = sourceEntry.Id + "-segment-001",
                Index = 1,
                TotalSegments = 1,
                Title = sourceEntry.Title,
                StartSeconds = 0,
                DurationSeconds = durationSeconds,
                SourceSegmentPath = sourceMp4Path,
                PayloadPath = payloadPath,
                PayloadRelativeUrl = BuildRelativeUrl(workspaceRoot, payloadPath),
                TranslatedAudioPath = Path.Combine(jobFolder, "translated_audio", sourceEntry.Id + ".translated.mp3"),
                MergedOutputPath = Path.Combine(jobFolder, "merged", "segment-001.translated.mp4")
            }
        ];
    }

    private async Task<List<TranslationStudioSegment>> PrepareSegmentedJobAsync(
        string workspaceRoot,
        VideoEntry sourceEntry,
        string jobFolder,
        string ffmpegPath,
        IReadOnlyList<(double StartSeconds, double DurationSeconds)> segmentPlan,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var segmentsRoot = Path.Combine(jobFolder, "segments");
        Directory.CreateDirectory(segmentsRoot);

        var totalSegments = segmentPlan.Count;
        var segments = new List<TranslationStudioSegment>(totalSegments);

        for (var index = 0; index < segmentPlan.Count; index++)
        {
            var (startSeconds, durationSeconds) = segmentPlan[index];
            var segmentNumber = index + 1;
            var segmentId = $"{sourceEntry.Id}-segment-{segmentNumber:000}";
            var segmentFolder = Path.Combine(segmentsRoot, $"segment-{segmentNumber:000}");
            Directory.CreateDirectory(segmentFolder);

            var sourceSegmentPath = Path.Combine(segmentFolder, "source.mp4");
            var payloadPath = Path.Combine(segmentFolder, "payload.mp4");

            if (NeedsRebuild(sourceEntry.FullPath, sourceSegmentPath))
            {
                log($"Creating source segment {segmentNumber}/{totalSegments} with ffmpeg: {sourceEntry.FileName}");
                await _ffmpegService.CreateSegmentMp4Async(
                    ffmpegPath,
                    sourceEntry.FullPath,
                    startSeconds,
                    durationSeconds,
                    sourceSegmentPath,
                    cancellationToken);
                TryUnblockPreparedMedia(sourceSegmentPath, log);
            }
            else
            {
                TryUnblockPreparedMedia(sourceSegmentPath, log);
            }

            if (NeedsRebuild(sourceSegmentPath, payloadPath))
            {
                log($"Creating Translation Studio payload segment {segmentNumber}/{totalSegments} with the same proxy pipeline as MainWindow: {sourceEntry.FileName}");
                await _ffmpegService.ConvertToProxyMp4Async(
                    ffmpegPath,
                    sourceSegmentPath,
                    payloadPath,
                    cancellationToken);
                TryUnblockPreparedMedia(payloadPath, log);
            }
            else
            {
                TryUnblockPreparedMedia(payloadPath, log);
            }

            segments.Add(new TranslationStudioSegment
            {
                JobId = sourceEntry.Id,
                SegmentId = segmentId,
                Index = segmentNumber,
                TotalSegments = totalSegments,
                Title = $"{sourceEntry.Title} [Part {segmentNumber}/{totalSegments}]",
                StartSeconds = startSeconds,
                DurationSeconds = durationSeconds,
                SourceSegmentPath = sourceSegmentPath,
                PayloadPath = payloadPath,
                PayloadRelativeUrl = BuildRelativeUrl(workspaceRoot, payloadPath),
                TranslatedAudioPath = Path.Combine(segmentFolder, "translated_audio", $"segment-{segmentNumber:000}.translated.mp3"),
                MergedOutputPath = Path.Combine(segmentFolder, "merged", $"segment-{segmentNumber:000}.translated.mp4"),
            });
        }

        return segments;
    }


    private static void UnblockPreparedJobFolder(string jobFolder, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(jobFolder))
            return;

        FfmpegService.TryUnblockDirectoryTree(jobFolder, log);

        foreach (var fileName in new[] { "source.mp4", "payload.mp4", "playlist.json", "player.html", "favicon.svg", "job-manifest.json" })
        {
            var path = Path.Combine(jobFolder, fileName);
            if (File.Exists(path))
                FfmpegService.TryUnblockFile(path, log);
        }
    }

    private static void TryUnblockPreparedMedia(string? path, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!FfmpegService.TryUnblockFile(path, log))
            log("Warning: Windows still reports prepared media as blocked, continuing because the media file was created: " + path);
    }

    internal static List<(double StartSeconds, double DurationSeconds)> BuildAutomaticSegmentPlan(
        double totalDurationSeconds,
        bool splitLongVideo,
        int splitThresholdMinutes,
        int targetSegmentMinutes)
    {
        var thresholdSeconds = Math.Max(1, splitThresholdMinutes) * 60d;
        var targetSeconds = Math.Max(1, targetSegmentMinutes) * 60d;

        if (!splitLongVideo || totalDurationSeconds <= thresholdSeconds + 1d)
            return [(0d, totalDurationSeconds)];

        var effectiveTargetSeconds = Math.Min(totalDurationSeconds, targetSeconds);
        var plan = new List<(double StartSeconds, double DurationSeconds)>();

        for (var startSeconds = 0d; startSeconds < totalDurationSeconds - 0.25d; startSeconds += effectiveTargetSeconds)
        {
            var duration = Math.Min(effectiveTargetSeconds, totalDurationSeconds - startSeconds);

            if (duration > 0.25d)
                plan.Add((startSeconds, duration));
        }

        if (plan.Count == 0)
            plan.Add((0d, totalDurationSeconds));

        return plan;
    }

    internal static string BuildPreparationSignature(
        string sourcePath,
        long sourceLength,
        long sourceLastWriteUtcTicks,
        double sourceDurationSeconds,
        bool splitLongVideo,
        int splitThresholdMinutes,
        int targetSegmentMinutes,
        IReadOnlyList<(double StartSeconds, double DurationSeconds)> segmentPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine(PreparationPipelineVersion);
        builder.AppendLine(Path.GetFullPath(sourcePath));
        builder.AppendLine(sourceLength.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(sourceLastWriteUtcTicks.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(sourceDurationSeconds.ToString("F6", CultureInfo.InvariantCulture));
        builder.AppendLine(splitLongVideo ? "1" : "0");
        builder.AppendLine(splitThresholdMinutes.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(targetSegmentMinutes.ToString(CultureInfo.InvariantCulture));

        foreach (var (startSeconds, durationSeconds) in segmentPlan)
            builder.AppendLine(startSeconds.ToString("F6", CultureInfo.InvariantCulture) + "|" + durationSeconds.ToString("F6", CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    internal static bool ShouldResetPreparedAssets(TranslationStudioJob? existingJob, string expectedPreparationSignature)
    {
        if (existingJob is null)
            return false;

        if (existingJob.SchemaVersion < CurrentSchemaVersion)
            return true;

        return !string.Equals(existingJob.PreparationSignature, expectedPreparationSignature, StringComparison.Ordinal);
    }

    private static string BuildRelativeUrl(string workspaceRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(workspaceRoot, absolutePath).Replace('\\', '/');
        return "/" + VideoWorkspace.EncodeUrlPath(relative);
    }

    private static bool NeedsRebuild(string sourcePath, string outputPath)
    {
        if (!File.Exists(outputPath))
            return true;

        var source = new FileInfo(sourcePath);
        var output = new FileInfo(outputPath);
        return output.Length <= 0 || output.LastWriteTimeUtc < source.LastWriteTimeUtc;
    }

    private static void ResetPreparedAssets(string jobFolder, string finalOutputPath, Action<string> log)
    {
        DeleteFileIfExists(GetManifestPath(jobFolder), log);
        DeleteFileIfExists(Path.Combine(jobFolder, "player.html"), log);
        DeleteFileIfExists(Path.Combine(jobFolder, "playlist.json"), log);
        DeleteFileIfExists(Path.Combine(jobFolder, "source.mp4"), log);
        DeleteFileIfExists(Path.Combine(jobFolder, "payload.mp4"), log);
        DeleteDirectoryIfExists(Path.Combine(jobFolder, "segments"), log);
        DeleteDirectoryIfExists(Path.Combine(jobFolder, "translated_audio"), log);
        DeleteDirectoryIfExists(Path.Combine(jobFolder, "merged"), log);
        DeleteFileIfExists(Path.Combine(jobFolder, "translation-studio.html"), log);
        DeleteFileIfExists(finalOutputPath, log);
    }

    private static void DeleteFileIfExists(string path, Action<string> log)
    {
        if (!File.Exists(path))
            return;

        FfmpegService.PrepareFreshOutputPath(path, log);
        if (File.Exists(path))
            throw new IOException("Failed to remove stale prepared file: " + path);
    }

    private static void DeleteDirectoryIfExists(string path, Action<string> log)
    {
        if (!Directory.Exists(path))
            return;

        Directory.Delete(path, recursive: true);
        if (Directory.Exists(path))
            throw new IOException("Failed to remove stale prepared folder: " + path);
    }

    private static void NormalizeSingleSegmentPaths(TranslationStudioJob job)
    {
        if (job.Segments.Count != 1)
            return;

        var segment = job.Segments[0];
        var normalizedMergedPath = Path.Combine(job.JobFolder, "merged", "segment-001.translated.mp4");
        var currentMergedPath = segment.MergedOutputPath ?? string.Empty;
        var currentFinalPath = job.FinalOutputPath ?? string.Empty;

        if (PathsEqual(currentMergedPath, normalizedMergedPath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(normalizedMergedPath)!);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(currentMergedPath) &&
            File.Exists(currentMergedPath) &&
            !PathsEqual(currentMergedPath, normalizedMergedPath) &&
            !File.Exists(normalizedMergedPath))
        {
            FfmpegService.CopyFileWithoutAlternateStreams(currentMergedPath, normalizedMergedPath);
            if (!FfmpegService.TryUnblockFile(normalizedMergedPath))
                throw new InvalidOperationException("Copied file is still blocked: " + normalizedMergedPath);
        }
        else if (!string.IsNullOrWhiteSpace(currentFinalPath) &&
                 File.Exists(currentFinalPath) &&
                 !File.Exists(normalizedMergedPath))
        {
            FfmpegService.CopyFileWithoutAlternateStreams(currentFinalPath, normalizedMergedPath);
            if (!FfmpegService.TryUnblockFile(normalizedMergedPath))
                throw new InvalidOperationException("Copied file is still blocked: " + normalizedMergedPath);
        }

        segment.MergedOutputPath = normalizedMergedPath;
    }

    private static void UnblockGeneratedFiles(TranslationStudioJob job)
    {
        FfmpegService.TryUnblockFile(job.FinalOutputPath);
        FfmpegService.TryUnblockFile(job.PayloadPath);
        FfmpegService.TryUnblockFile(job.TranslatedAudioPath);
        FfmpegService.TryUnblockFile(job.MergedOutputPath);

        foreach (var segment in job.Segments)
        {
            FfmpegService.TryUnblockFile(segment.SourceSegmentPath);
            FfmpegService.TryUnblockFile(segment.PayloadPath);
            FfmpegService.TryUnblockFile(segment.TranslatedAudioPath);
            FfmpegService.TryUnblockFile(segment.MergedOutputPath);
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }

}
