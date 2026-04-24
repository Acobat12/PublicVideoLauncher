using PublicVideoLauncher.Services;

namespace PublicVideoLauncher.Tests;

public sealed class ProcessCleanupServiceTests
{
    [Fact]
    public void ShouldTerminateProcessForCleanup_KillsKnownTunnelProcesses()
    {
        Assert.True(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "cloudflared",
            null,
            null,
            null,
            isListeningOnManagedPort: false));

        Assert.True(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "caddy.exe",
            null,
            null,
            null,
            isListeningOnManagedPort: false));
    }

    [Fact]
    public void ShouldTerminateProcessForCleanup_DoesNotKillGenericNodeOrSsh()
    {
        Assert.False(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "node",
            "node server.js",
            null,
            null,
            isListeningOnManagedPort: true));

        Assert.False(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "ssh",
            "ssh user@example.com",
            null,
            null,
            isListeningOnManagedPort: false));
    }

    [Fact]
    public void ShouldTerminateProcessForCleanup_KillsTunnelNodeAndLocalhostRunSsh()
    {
        Assert.True(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "node",
            "node ./node_modules/.bin/localtunnel --port 8014",
            null,
            null,
            isListeningOnManagedPort: false));

        Assert.True(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "ssh",
            "ssh -R 80:localhost:8014 nokey@localhost.run",
            null,
            null,
            isListeningOnManagedPort: false));
    }

    [Fact]
    public void ShouldTerminateProcessForCleanup_OnlyKillsTailscaleFunnelCommand()
    {
        Assert.False(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "tailscale",
            "tailscaled --state=/var/lib/tailscale/tailscaled.state",
            null,
            null,
            isListeningOnManagedPort: false));

        Assert.True(ProcessCleanupService.ShouldTerminateProcessForCleanup(
            "tailscale",
            "tailscale funnel 8014",
            null,
            null,
            isListeningOnManagedPort: false));
    }

    [Fact]
    public void BuildDefaultManagedPorts_IncludesAppPortRangeAndCallbackPort()
    {
        var ports = ProcessCleanupService.BuildDefaultManagedPorts(18761);

        Assert.Contains(8000, ports);
        Assert.Contains(8050, ports);
        Assert.Contains(18761, ports);
    }
}
