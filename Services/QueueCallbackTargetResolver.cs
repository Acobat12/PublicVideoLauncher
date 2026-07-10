using PublicVideoLauncher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PublicVideoLauncher.Services;

public static class QueueCallbackTargetResolver
{
    public static VideoEntry? ResolveQueueEntry(
        IEnumerable<VideoEntry> entries,
        string? entryId,
        string? videoSrc)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!string.IsNullOrWhiteSpace(entryId))
        {
            var matchedById = entries.FirstOrDefault(x => string.Equals(x.Id, entryId, StringComparison.OrdinalIgnoreCase));
            if (matchedById is not null)
                return matchedById;
        }

        if (string.IsNullOrWhiteSpace(videoSrc))
            return null;

        var normalized = NormalizeVideoSrc(videoSrc);
        return entries.FirstOrDefault(x =>
            string.Equals(NormalizeVideoSrc(x.VotHelperUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeVideoSrc(x.PlaybackUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeVideoSrc(x.TranslationUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeVideoSrc(x.OriginalUrl), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeVideoSrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        string path;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }
        else
        {
            var queryIndex = trimmed.IndexOfAny(['?', '#']);
            path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        }

        path = Uri.UnescapeDataString(path.Replace('\\', '/')).Trim();
        path = path.TrimStart('.');
        if (!path.StartsWith('/'))
            path = "/" + path.TrimStart('/');

        return path;
    }
}
