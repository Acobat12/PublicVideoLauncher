namespace PublicVideoLauncher.Models;

public enum VideoSortBy
{
    Title,
    RelativePath,
    ModifiedUtc,
    Size
}

public sealed class VideoScanOptions
{
    public string RootFolder { get; set; } = string.Empty;
    public bool ProxyTranslationEnabled { get; set; }

    public bool Recursive { get; set; } = true;

    public string[] IncludePatterns { get; set; } =
    {
        "*.mp4", "*.mkv", "*.mov", "*.avi", "*.m4v", "*.webm"
    };

    public string[] ExcludedFolderNames { get; set; } =
    {
        ".public-video-cache", ".git", "bin", "obj"
    };

    public VideoSortBy SortBy { get; set; } = VideoSortBy.RelativePath;
    public bool SortDescending { get; set; }
}