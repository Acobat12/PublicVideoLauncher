namespace PublicVideoLauncher.Models;

public sealed class TunnelLaunchSettings
{
    public string? TunnelAuth { get; set; }
    public string? CustomCliPath { get; set; }
    public string? CloudflareNamedTunnelName { get; set; }
    public string? CloudflareNamedHostname { get; set; }
}
