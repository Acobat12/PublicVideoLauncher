namespace PublicVideoLauncher.Models;

public enum TunnelProvider
{
    Cloudflare,
    CloudflareName,
    Ngrok,
    LocalhostRun,
    TailscaleFunnel,
    Zrok,
    LocalTunnel,
    XTunnel,
    VkTunnel,
}

public static class TunnelProviderInfo
{
    public static readonly TunnelProvider[] All =
    [
        TunnelProvider.Cloudflare,
        TunnelProvider.CloudflareName,
        TunnelProvider.Ngrok,
        TunnelProvider.LocalhostRun,
        TunnelProvider.TailscaleFunnel,
        TunnelProvider.Zrok,
        TunnelProvider.LocalTunnel,
        TunnelProvider.XTunnel,
        TunnelProvider.VkTunnel,
    ];

    public static string GetDisplayName(TunnelProvider provider) => provider switch
    {
        TunnelProvider.Cloudflare => "Cloudflare Tunnel",
        TunnelProvider.CloudflareName => "Cloudflare Name",
        TunnelProvider.Ngrok => "ngrok",
        TunnelProvider.LocalhostRun => "localhost.run",
        TunnelProvider.TailscaleFunnel => "Tailscale Funnel",
        TunnelProvider.Zrok => "zrok",
        TunnelProvider.LocalTunnel => "LocalTunnel",
        TunnelProvider.XTunnel => "xTunnel",
        TunnelProvider.VkTunnel => "VK Tunnel",
        _ => provider.ToString(),
    };

    public static string GetToolName(TunnelProvider provider) => provider switch
    {
        TunnelProvider.Cloudflare => "cloudflared",
        TunnelProvider.CloudflareName => "cloudflared",
        TunnelProvider.Ngrok => "ngrok",
        TunnelProvider.LocalhostRun => "ssh",
        TunnelProvider.TailscaleFunnel => "tailscale",
        TunnelProvider.Zrok => "zrok",
        TunnelProvider.LocalTunnel => "localtunnel",
        TunnelProvider.XTunnel => "xtunnel",
        TunnelProvider.VkTunnel => "vk-tunnel",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static string GetLaunchLabel(TunnelProvider provider) => provider switch
    {
        TunnelProvider.Cloudflare => "cloudflared",
        TunnelProvider.CloudflareName => "cloudflared",
        TunnelProvider.Ngrok => "ngrok",
        TunnelProvider.LocalhostRun => "ssh",
        TunnelProvider.TailscaleFunnel => "tailscale",
        TunnelProvider.Zrok => "zrok",
        TunnelProvider.LocalTunnel => "localtunnel",
        TunnelProvider.XTunnel => "xtunnel",
        TunnelProvider.VkTunnel => "vk-tunnel",
        _ => "tunnel",
    };
}
