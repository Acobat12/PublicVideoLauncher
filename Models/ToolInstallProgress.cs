using System;

namespace PublicVideoLauncher.Models;

public sealed class ToolInstallProgress
{
    public required string ToolName { get; init; }
    public required string Stage { get; init; }
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }

    public double? Percent => TotalBytes is > 0
        ? Math.Round((double)BytesReceived / TotalBytes.Value * 100d, 1)
        : null;
}
