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
        ".mp4", ".mkv", ".mov", ".avi", ".m4v", ".webm"
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

        if (proxyTranslationEnabled)
            Directory.CreateDirectory(CacheFolder!);

        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(file);
            if (!SupportedExtensions.Contains(ext))
                continue;

            var fileName = Path.GetFileName(file);
            var title = Path.GetFileNameWithoutExtension(file);
            var baseSlug = NewSlug(title);
            var slug = baseSlug;
            var n = 2;

            while (!usedSlugs.Add(slug))
            {
                slug = $"{baseSlug}-{n}";
                n++;
            }

            var originalUrl = "/" + EncodeUrlPath(fileName);
            var playbackCacheFileName = $"{slug}.play.mp4";
            var playbackCachePath = Path.Combine(CacheFolder!, playbackCacheFileName);
            var playbackUrl = "/.public-video-cache/" + EncodeUrlPath(playbackCacheFileName);

            var proxyCacheFileName = $"{slug}.proxy.mp4";
            var proxyCachePath = Path.Combine(CacheFolder!, proxyCacheFileName);
            var proxyUrl = "/.public-video-cache/" + EncodeUrlPath(proxyCacheFileName);
            var votHelperUrl = "/.public-video-vot/" + EncodeUrlPath(slug) + ".mp4";

            var isDirectMp4 = string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase);

            var entry = new VideoEntry
            {
                Id = slug,
                Title = title,
                FileName = fileName,
                FullPath = file,
                Extension = ext,
                OriginalUrl = originalUrl,
                PlaybackUrl = isDirectMp4 || !proxyTranslationEnabled ? originalUrl : playbackUrl,
                TranslationUrl = isDirectMp4 || !proxyTranslationEnabled ? originalUrl : proxyUrl,
                VotHelperUrl = votHelperUrl,
                PlaybackCachePath = isDirectMp4 || !proxyTranslationEnabled ? null : playbackCachePath,
                TranslationCachePath = isDirectMp4 || !proxyTranslationEnabled ? null : proxyCachePath,
                ProxyModeEnabled = proxyTranslationEnabled,
                CacheState = GetInitialCacheState(file, playbackCachePath, isDirectMp4, proxyTranslationEnabled, proxyCachePath)
            };

            Entries.Add(entry);
        }
    }

    private static string GetInitialCacheState(string sourcePath, string playbackCachePath, bool isDirectMp4, bool proxyEnabled, string proxyCachePath)
    {
        if (isDirectMp4)
            return "Not needed";

        if (!proxyEnabled)
            return "Disabled";

        var sourceInfo = new FileInfo(sourcePath);
        var playbackReady = File.Exists(playbackCachePath) && new FileInfo(playbackCachePath).Length > 0 && new FileInfo(playbackCachePath).LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc;
        var proxyReady = !proxyEnabled || (File.Exists(proxyCachePath) && new FileInfo(proxyCachePath).Length > 0 && new FileInfo(proxyCachePath).LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc);

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
