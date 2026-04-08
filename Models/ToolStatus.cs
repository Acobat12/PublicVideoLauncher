namespace PublicVideoLauncher.Models;

public sealed class ToolStatus
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string DownloadUrl { get; init; }
    public required string InstallationHint { get; init; }
    public string? DetectedPath { get; set; }
    public bool SupportsAutomaticInstall { get; init; }

    public bool IsInstalled => !string.IsNullOrWhiteSpace(DetectedPath);
    public string StatusText => IsInstalled ? "Installed" : "Missing";
    public string InstallActionText => SupportsAutomaticInstall ? "Install automatically" : "Manual install required";
}
