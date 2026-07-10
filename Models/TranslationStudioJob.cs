using System;
using System.IO;
using System.Collections.Generic;
using PublicVideoLauncher.Services;

namespace PublicVideoLauncher.Models;

public sealed class TranslationStudioJob
{
    public string TranslatedSubtitlesPath { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string PreparationSignature { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string JobFolder { get; set; } = string.Empty;
    public string SourceVideoId { get; set; } = string.Empty;
    public string SourceVideoTitle { get; set; } = string.Empty;
    public string SourceVideoFileName { get; set; } = string.Empty;
    public string SourceVideoPath { get; set; } = string.Empty;
    public double SourceDurationSeconds { get; set; }
    public bool AutoMergeOnCallback { get; set; }
    public double OriginalAudioVolumePercent { get; set; } = 8;
    public double TranslatedAudioVolumePercent { get; set; } = 100;
    public bool IsSegmented { get; set; }
    public int SplitIfLongerThanMinutes { get; set; }
    public int TargetSegmentMinutes { get; set; }
    public string PayloadPath { get; set; } = string.Empty;
    public string PayloadRelativeUrl { get; set; } = string.Empty;
    public string PlaylistRelativeUrl { get; set; } = string.Empty;
    public string PlayerRelativeUrl { get; set; } = string.Empty;
    public string TranslatedAudioPath { get; set; } = string.Empty;
    public string MergedOutputPath { get; set; } = string.Empty;
    public string FinalOutputPath { get; set; } = string.Empty;
    public string? LastTranslationSourceUrl { get; set; }
    public DateTime PreparedAtUtc { get; set; }
    public DateTime? LastCallbackHandledAtUtc { get; set; }
    public List<TranslationStudioSegment> Segments { get; set; } = new();
    public bool PreparationRefreshRecommended { get; set; }
    public bool HasFinalOutput => !string.IsNullOrWhiteSpace(FinalOutputPath) && File.Exists(FinalOutputPath);
    public string FinalOutputRelativeUrl
    {
        get
        {
            if (!HasFinalOutput || string.IsNullOrWhiteSpace(WorkspaceRoot) || string.IsNullOrWhiteSpace(FinalOutputPath))
                return string.Empty;

            try
            {
                var relative = Path.GetRelativePath(WorkspaceRoot, FinalOutputPath).Replace('\\', '/');
                return relative.StartsWith("../", StringComparison.Ordinal) ? string.Empty : "/" + VideoWorkspace.EncodeUrlPath(relative);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

public sealed class TranslationStudioSegment
{
    public string TranslatedSubtitlesPath { get; set; } = string.Empty;
    public bool HasTranslatedSubtitles => !string.IsNullOrWhiteSpace(TranslatedSubtitlesPath) && File.Exists(TranslatedSubtitlesPath);
    public string JobId { get; set; } = string.Empty;
    public string SegmentId { get; set; } = string.Empty;
    public int Index { get; set; }
    public int TotalSegments { get; set; }
    public string Title { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public string SourceSegmentPath { get; set; } = string.Empty;
    public string PayloadPath { get; set; } = string.Empty;
    public string PayloadRelativeUrl { get; set; } = string.Empty;
    public string TranslatedAudioPath { get; set; } = string.Empty;
    public string MergedOutputPath { get; set; } = string.Empty;
    public string? LastTranslationSourceUrl { get; set; }
    public DateTime? LastCallbackHandledAtUtc { get; set; }
    public bool HasTranslatedAudio => !string.IsNullOrWhiteSpace(TranslatedAudioPath) && File.Exists(TranslatedAudioPath);
    public bool HasMergedOutput => !string.IsNullOrWhiteSpace(MergedOutputPath) && File.Exists(MergedOutputPath);
}

public sealed class TranslationStudioPrepareRequest
{
    public required VideoEntry SourceEntry { get; init; }
    public bool AutoMergeOnCallback { get; init; }
    public bool SplitLongVideo { get; init; }
    public int SplitIfLongerThanMinutes { get; init; }
    public int TargetSegmentMinutes { get; init; }
    public double OriginalAudioVolumePercent { get; init; } = 8;
    public double TranslatedAudioVolumePercent { get; init; } = 100;
}

public sealed class TranslationStudioBatchRequest
{
    public bool SplitLongVideo { get; init; }
    public int SplitIfLongerThanMinutes { get; init; }
    public int TargetSegmentMinutes { get; init; }
    public double OriginalAudioVolumePercent { get; init; } = 8;
    public double TranslatedAudioVolumePercent { get; init; } = 100;
}
