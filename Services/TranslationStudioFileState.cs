using System;
using System.IO;
using System.Linq;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

internal static class TranslationStudioFileState
{
    public static bool IsUsableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool NeedsRefreshFromSource(string? outputPath, string? lastSourceUrl, string? incomingSourceUrl)
    {
        if (!IsUsableFile(outputPath))
            return true;

        if (string.IsNullOrWhiteSpace(incomingSourceUrl))
            return false;

        return !string.Equals(lastSourceUrl, incomingSourceUrl, StringComparison.Ordinal);
    }

    public static bool IsMergedSegmentCurrent(TranslationStudioSegment segment)
    {
        return IsOutputCurrent(
            segment.MergedOutputPath,
            segment.SourceSegmentPath,
            segment.TranslatedAudioPath);
    }

    public static bool IsFinalOutputCurrent(TranslationStudioJob job)
    {
        if (!IsUsableFile(job.FinalOutputPath))
            return false;

        if (job.Segments.Count == 0)
            return false;

        if (job.Segments.Count == 1)
        {
            var segment = job.Segments[0];
            return IsOutputCurrent(
                job.FinalOutputPath,
                segment.SourceSegmentPath,
                segment.TranslatedAudioPath);
        }

        var mergedPaths = job.Segments
            .OrderBy(x => x.Index)
            .Select(x => x.MergedOutputPath)
            .ToArray();

        return mergedPaths.Length == job.Segments.Count && IsOutputCurrent(job.FinalOutputPath, mergedPaths);
    }

    private static bool IsOutputCurrent(string outputPath, params string[] inputs)
    {
        if (!IsUsableFile(outputPath))
            return false;

        DateTime outputWriteTimeUtc;
        try
        {
            outputWriteTimeUtc = File.GetLastWriteTimeUtc(outputPath);
        }
        catch
        {
            return false;
        }

        foreach (var input in inputs)
        {
            if (!IsUsableFile(input))
                return false;

            DateTime inputWriteTimeUtc;
            try
            {
                inputWriteTimeUtc = File.GetLastWriteTimeUtc(input);
            }
            catch
            {
                return false;
            }

            if (inputWriteTimeUtc > outputWriteTimeUtc)
                return false;
        }

        return true;
    }
}
