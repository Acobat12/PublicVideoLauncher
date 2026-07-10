using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PublicVideoLauncher.Services;

public sealed class VideoWorkspace
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".m4v", ".webm", ".ts", ".m2ts"
    };

    private static readonly HashSet<string> DirectPlayExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4"
    };

    public string? RootFolder { get; private set; }
    public bool ProxyTranslationEnabled { get; private set; }
    public string? CacheFolder => RootFolder is null ? null : Path.Combine(RootFolder, ".public-video-cache");
    public List<VideoEntry> Entries { get; } = new();

    public void Load(string folder, bool proxyTranslationEnabled)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Folder is empty.", nameof(folder));

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Folder not found: {folder}");

        RootFolder = folder;
        ProxyTranslationEnabled = proxyTranslationEnabled;
        Entries.Clear();

        Directory.CreateDirectory(CacheFolder!);

        var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".public-video-cache",
            ".git",
            "bin",
            "obj",
            ".tools",
            "_translation-studio",
            "merged_output",
            "translated_audio"
        };

        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var ext = Path.GetExtension(file);
                if (!SupportedExtensions.Contains(ext))
                    return false;

                var relativeDir = Path.GetRelativePath(folder, Path.GetDirectoryName(file) ?? string.Empty);
                var parts = relativeDir.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                return !parts.Any(part => excludedFolders.Contains(part));
            })
            .OrderBy(file => Path.GetRelativePath(folder, file), StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            var fileName = Path.GetFileName(file);
            var title = Path.GetFileNameWithoutExtension(file);

            var relativePath = Path.GetRelativePath(folder, file);
            var relativeUrl = EncodeUrlPath(relativePath);

            var baseSlug = NewSlug(relativePath);
            var slug = baseSlug;
            var n = 2;

            while (!usedSlugs.Add(slug))
            {
                slug = $"{baseSlug}-{n}";
                n++;
            }

            var originalUrl = "/" + relativeUrl;

            var playbackCacheFileName = $"{slug}.play.mp4";
            var playbackCachePath = Path.Combine(CacheFolder!, playbackCacheFileName);
            var playbackUrl = "/.public-video-cache/" + EncodeUrlPath(playbackCacheFileName);

            var proxyCacheFileName = $"{slug}.proxy.mp4";
            var proxyCachePath = Path.Combine(CacheFolder!, proxyCacheFileName);
            var proxyUrl = "/.public-video-cache/" + EncodeUrlPath(proxyCacheFileName);

            var votHelperUrl = "/.public-video-vot/" + EncodeUrlPath(slug) + ".mp4";

            var canDirectPlay = IsBrowserSafeDirectPlay(file);

            var entry = new VideoEntry
            {
                Id = slug,
                Title = title,
                FileName = fileName,
                FullPath = file,
                Extension = ext,
                RelativePath = relativePath,
                RelativeUrl = relativeUrl,
                FileSizeBytes = new FileInfo(file).Length,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(file),
                OriginalUrl = originalUrl,

                // Всегда ведём playback через cache, если direct play небезопасен.
                PlaybackUrl = canDirectPlay ? originalUrl : playbackUrl,

                // Для перевода можно оставить старую логику:
                TranslationUrl = !proxyTranslationEnabled
                    ? (canDirectPlay ? originalUrl : playbackUrl)
                    : proxyUrl,

                VotHelperUrl = votHelperUrl,

                // Cache path нужен всякий раз, когда direct play нельзя.
                PlaybackCachePath = canDirectPlay ? null : playbackCachePath,
                TranslationCachePath = proxyTranslationEnabled ? proxyCachePath : null,

                ProxyModeEnabled = proxyTranslationEnabled,
                CacheState = GetInitialCacheState(
                    sourcePath: file,
                    playbackCachePath: playbackCachePath,
                    needsPlaybackNormalization: !canDirectPlay,
                    proxyEnabled: proxyTranslationEnabled,
                    proxyCachePath: proxyCachePath)
            };

            Entries.Add(entry);
        }
    }

    private static bool IsBrowserSafeDirectPlay(string filePath)
    {
        var ext = Path.GetExtension(filePath);

        if (!DirectPlayExtensions.Contains(ext))
            return false;

        // Do not trust the .mp4 extension alone. Firefox may reject HEVC/H.265 MP4
        // even when Chrome/Edge can play it through Windows codecs.
        // Returning false forces ffprobe/ffmpeg cache normalization before publishing.
        // Safe H.264/AAC MP4 files are usually just remuxed with +faststart; HEVC is
        // transcoded to H.264/AAC, which works in Firefox.
        return false;
    }

    private static string GetInitialCacheState(
        string sourcePath,
        string playbackCachePath,
        bool needsPlaybackNormalization,
        bool proxyEnabled,
        string proxyCachePath)
    {
        if (!needsPlaybackNormalization && !proxyEnabled)
            return "Not needed";

        var sourceInfo = new FileInfo(sourcePath);

        var playbackReady = !needsPlaybackNormalization
            || (File.Exists(playbackCachePath)
                && new FileInfo(playbackCachePath).Length > 0
                && new FileInfo(playbackCachePath).LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc);

        var proxyReady = !proxyEnabled
            || (File.Exists(proxyCachePath)
                && new FileInfo(proxyCachePath).Length > 0
                && new FileInfo(proxyCachePath).LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc);

        if (!needsPlaybackNormalization && proxyEnabled)
            return proxyReady ? "Ready" : "Pending";

        return playbackReady && proxyReady ? "Ready" : "Pending";
    }

    public static string NewSlug(string text)
    {
        var slug = text.ToLowerInvariant();
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-");
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "video" : slug;
    }

    public static string EncodeUrlPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.None);
        return string.Join("/", segments.Select(Uri.EscapeDataString));
    }
}