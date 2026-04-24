using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public sealed class LaunchSession
{
    public required int Port { get; init; }
    public required string LocalUrl { get; init; }
    public required string PublicUrl { get; init; }
}

public sealed class ServerRunner
{
    private Process? _caddyProcess;
    private Process? _tunnelProcess;
    private string? _caddyStdoutFile;
    private string? _caddyStderrFile;
    private string? _tunnelStdoutFile;
    private string? _tunnelStderrFile;
    private string? _caddyConfigFile;
    private string? _activeTunnelProvider;
    private CancellationTokenSource? _liveLogCts;
    private Task? _caddyLiveLogTask;
    private Task? _tunnelLiveLogTask;
    private Task? _caddyWatchdogTask;
    private string? _caddyPath;
    private string? _caddyWorkingDirectory;
    private string? _caddyHealthUrl;
    private string? _lastWorkspaceRoot;
    private int? _lastTranslationCallbackPort;
    private readonly ProcessCleanupService _processCleanupService = new();
    private readonly SemaphoreSlim _caddyRestartGate = new(1, 1);

    public bool IsRunning =>
        (_caddyProcess is { HasExited: false }) ||
        (_tunnelProcess is { HasExited: false });

    public async Task<LaunchSession> StartAsync(
        VideoWorkspace workspace,
        int requestedPort,
        string tunnelProvider,
        ToolPaths tools,
        TunnelLaunchSettings settings,
        Action<string> log,
        CancellationToken cancellationToken,
        string? initialVideoSrc = null,
        int? translationCallbackPort = null)
    {
        await StopAsync(log);

        if (workspace.RootFolder is null)
            throw new InvalidOperationException("Workspace root is not set.");

        _lastWorkspaceRoot = workspace.RootFolder;
        _lastTranslationCallbackPort = translationCallbackPort;

        log("Cleaning up stale tunnel processes before start...");
        await _processCleanupService.CleanupAsync(
            workspace.RootFolder,
            ProcessCleanupService.BuildDefaultManagedPorts(translationCallbackPort),
            log,
            cancellationToken);

        var provider = NormalizeTunnelProvider(tunnelProvider);
        _activeTunnelProvider = provider;
        var port = PortHelper.GetFreeTcpPort(requestedPort);
        var localBaseUrl = $"http://127.0.0.1:{port}";
        var localUrl = localBaseUrl + "/";
        var localPlaylistUrl = $"{localBaseUrl}/playlist.json";

        log($"Local server: {localUrl}");

        _caddyStdoutFile = Path.GetTempFileName();
        _caddyStderrFile = Path.GetTempFileName();
        _caddyConfigFile = Path.GetTempFileName();
        _caddyPath = tools.CaddyPath;
        _caddyWorkingDirectory = workspace.RootFolder;
        _caddyHealthUrl = localPlaylistUrl;

        File.WriteAllText(_caddyConfigFile, BuildCaddyConfig(workspace.RootFolder, port, translationCallbackPort));

        _liveLogCts?.Cancel();
        _liveLogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await StartOrRestartCaddyAsync(log, cancellationToken, initialStart: true);

        _tunnelStdoutFile = Path.GetTempFileName();
        _tunnelStderrFile = Path.GetTempFileName();

        var publicBaseUrl = await StartTunnelAsync(
            provider,
            localUrl,
            workspace.RootFolder,
            tools,
            settings,
            log,
            cancellationToken);

        var targetUrl = publicBaseUrl.TrimEnd('/') + "/player.html?list=/playlist.json";
        if (!string.IsNullOrWhiteSpace(initialVideoSrc))
            targetUrl += "&src=" + Uri.EscapeDataString(initialVideoSrc);
        targetUrl += "&_ts=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        if (port != requestedPort)
            log($"Requested port {requestedPort} is unavailable. Using port {port} instead.");

        _caddyWatchdogTask = RunCaddyWatchdogAsync(log, _liveLogCts.Token);
        log("Public URL: " + targetUrl);

        return new LaunchSession
        {
            Port = port,
            LocalUrl = localUrl,
            PublicUrl = targetUrl,
        };
    }

    private static string FindTailscalePath()
    {
        var candidates = new[]
        {
        @"C:\Program Files\Tailscale\tailscale.exe",
        @"C:\Program Files (x86)\Tailscale\tailscale.exe"
    };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "tailscale";
    }

    public async Task StopAsync(Action<string> log)
    {
        _liveLogCts?.Cancel();

        var provider = _activeTunnelProvider;

        await StopProcessAsync(_tunnelProcess, log, provider ?? "tunnel");
        await StopProcessAsync(_caddyProcess, log, "caddy");

        if (_tunnelLiveLogTask is not null)
            await IgnoreTaskAsync(_tunnelLiveLogTask);
        if (_caddyLiveLogTask is not null)
            await IgnoreTaskAsync(_caddyLiveLogTask);
        if (_caddyWatchdogTask is not null)
            await IgnoreTaskAsync(_caddyWatchdogTask);

        // provider-specific cleanup after process stop
        if (string.Equals(provider, "tailscale-funnel", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                log("Cleaning up Tailscale Funnel config...");
                await ResetTailscaleFunnelAsync(
                    FindTailscalePath(),
                    Environment.CurrentDirectory,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                log("Tailscale cleanup warning: " + ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastWorkspaceRoot))
        {
            log("Verifying stale tunnel processes after stop...");
            await _processCleanupService.CleanupAsync(
                _lastWorkspaceRoot,
                ProcessCleanupService.BuildDefaultManagedPorts(_lastTranslationCallbackPort),
                log,
                CancellationToken.None);
        }

        _tunnelProcess = null;
        _caddyProcess = null;
        _activeTunnelProvider = null;
        _tunnelLiveLogTask = null;
        _caddyLiveLogTask = null;
        _caddyWatchdogTask = null;
        _liveLogCts?.Dispose();
        _liveLogCts = null;
        _caddyPath = null;
        _caddyWorkingDirectory = null;
        _caddyHealthUrl = null;
        _lastWorkspaceRoot = null;
        _lastTranslationCallbackPort = null;

        CleanupTempFile(ref _caddyStdoutFile);
        CleanupTempFile(ref _caddyStderrFile);
        CleanupTempFile(ref _tunnelStdoutFile);
        CleanupTempFile(ref _tunnelStderrFile);
        CleanupTempFile(ref _caddyConfigFile);
    }

    private static string BuildCaddyConfig(string rootFolder, int port, int? translationCallbackPort)
    {
        var escapedRoot = rootFolder.Replace("\\", "/");
        var lines = new[]
        {
            "{",
            "  auto_https off",
            "  admin off",
            "}",
            $":{port} {{",
            $"  root * \"{escapedRoot}\"",
            "  @translation path /translation-ready",
            "  @dynamicFiles path_regexp dynamicFiles (?i).*(?:\\.html|\\.json)$",
            translationCallbackPort is int callbackPort
                ? $"  reverse_proxy @translation 127.0.0.1:{callbackPort}"
                : "  respond @translation 503",
            "  header @dynamicFiles Cache-Control \"no-store, max-age=0, must-revalidate\"",
            "  header @dynamicFiles Pragma \"no-cache\"",
            "  header @dynamicFiles Expires \"0\"",
            "  file_server",
            "}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private async Task StartOrRestartCaddyAsync(Action<string> log, CancellationToken cancellationToken, bool initialStart)
    {
        if (string.IsNullOrWhiteSpace(_caddyPath) ||
            string.IsNullOrWhiteSpace(_caddyConfigFile) ||
            string.IsNullOrWhiteSpace(_caddyWorkingDirectory) ||
            string.IsNullOrWhiteSpace(_caddyStdoutFile) ||
            string.IsNullOrWhiteSpace(_caddyStderrFile) ||
            string.IsNullOrWhiteSpace(_caddyHealthUrl))
        {
            throw new InvalidOperationException("Caddy restart state is incomplete.");
        }

        if (!initialStart)
        {
            await StopProcessAsync(_caddyProcess, log, "caddy");
            if (_caddyLiveLogTask is not null)
                await IgnoreTaskAsync(_caddyLiveLogTask);

            _caddyProcess = null;
            _caddyLiveLogTask = null;
        }

        log(initialStart ? "Launching Caddy..." : "Restarting Caddy on the active local port...");
        _caddyProcess = StartProcess(
            _caddyPath,
            new[]
            {
                "run",
                "--config", _caddyConfigFile,
                "--adapter", "caddyfile"
            },
            _caddyWorkingDirectory,
            _caddyStdoutFile,
            _caddyStderrFile);

        _caddyLiveLogTask = PumpLogsAsync(_caddyProcess, _caddyStdoutFile, _caddyStderrFile, log, "caddy", _liveLogCts?.Token ?? cancellationToken);

        await WaitForLocalEndpointAsync(
            _caddyProcess,
            _caddyHealthUrl,
            _caddyStdoutFile,
            _caddyStderrFile,
            log,
            cancellationToken);
    }

    private async Task RunCaddyWatchdogAsync(Action<string> log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_caddyHealthUrl))
            return;

        using var http = CreateHttpClient();
        var consecutiveProbeFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var process = _caddyProcess;
            if (process is null)
                continue;

            if (process.HasExited)
            {
                consecutiveProbeFailures = 0;
                await RecoverCaddyAsync("process exited", log, cancellationToken);
                continue;
            }

            var isHealthy = await TryProbeEndpointAsync(http, _caddyHealthUrl, cancellationToken);
            if (isHealthy)
            {
                consecutiveProbeFailures = 0;
                continue;
            }

            consecutiveProbeFailures++;
            if (consecutiveProbeFailures < 3)
                continue;

            consecutiveProbeFailures = 0;
            await RecoverCaddyAsync("local readiness probe failed 3 times", log, cancellationToken);
        }
    }

    private async Task RecoverCaddyAsync(string reason, Action<string> log, CancellationToken cancellationToken)
    {
        await _caddyRestartGate.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(_caddyHealthUrl))
                return;

            using var http = CreateHttpClient();
            if (_caddyProcess is { HasExited: false } &&
                await TryProbeEndpointAsync(http, _caddyHealthUrl, cancellationToken))
            {
                return;
            }

            log($"Caddy became unhealthy ({reason}). Restarting it on the current local port...");
            await StartOrRestartCaddyAsync(log, cancellationToken, initialStart: false);
            log("Caddy watchdog restored the local HTTP server.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log("Caddy watchdog restart failed: " + ex.Message);
        }
        finally
        {
            _caddyRestartGate.Release();
        }
    }

    private async Task<string> StartTunnelAsync(
        string provider,
        string localUrl,
        string workingDirectory,
        ToolPaths tools,
        TunnelLaunchSettings settings,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        switch (provider)
        {
            case "cloudflared":
            {
                _tunnelProcess = StartProcess(
                    tools.CloudflaredPath ?? throw new InvalidOperationException("cloudflared path is not available."),
                    new[] { "tunnel", "--url", localUrl, "--no-autoupdate", "--protocol", "http2" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching cloudflared...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "cloudflared", _liveLogCts?.Token ?? cancellationToken);

                var publicUrl = await WaitForRegexUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    "Cloudflared",
                    cancellationToken);

                log($"Tunnel URL detected: {publicUrl}");

                await WaitForCloudflaredConnectionAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);

                await TryWaitForPublicHttpAsync(
                    _tunnelProcess,
                    publicUrl,
                    "/playlist.json",
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);

                return publicUrl;
            }

            case "cloudflare-name":
            {
                var cloudflaredPath = tools.CloudflaredPath ?? throw new InvalidOperationException("cloudflared path is not available.");
                var tunnelName = (settings.CloudflareNamedTunnelName ?? string.Empty).Trim();
                var hostname = (settings.CloudflareNamedHostname ?? string.Empty).Trim();
                var prepared = await CloudflareNamedTunnelService.PrepareAsync(
                    workingDirectory,
                    cloudflaredPath,
                    workingDirectory,
                    tunnelName,
                    hostname,
                    localUrl,
                    log,
                    cancellationToken);

                _tunnelProcess = StartProcess(
                    cloudflaredPath,
                    new[] { "tunnel", "--config", prepared.ConfigPath, "--no-autoupdate", "run", prepared.TunnelId },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching named cloudflared tunnel...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "cloudflared", _liveLogCts?.Token ?? cancellationToken);

                await WaitForCloudflaredConnectionAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);

                await TryWaitForPublicHttpAsync(
                    _tunnelProcess,
                    prepared.PublicBaseUrl,
                    "/playlist.json",
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);

                return prepared.PublicBaseUrl;
            }

            case "ngrok":
            {
                var ngrokPath = tools.NgrokPath ?? throw new InvalidOperationException("ngrok path is not available.");
                var ngrokToken = (settings.TunnelAuth ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(ngrokToken))
                    await TryConfigureNgrokAuthAsync(ngrokPath, ngrokToken, workingDirectory, log, cancellationToken);

                _tunnelProcess = StartProcess(
                    ngrokPath,
                    new[] { "http", localUrl, "--log", "stdout", "--log-format", "json" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching ngrok...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "ngrok", _liveLogCts?.Token ?? cancellationToken);

                return await WaitForRegexUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    new Regex(@"https://[a-z0-9.-]+\.ngrok(?:-free)?\.(?:app|dev|io)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    "ngrok",
                    cancellationToken);
            }

            case "localhost.run":
            {
                var sshPath = tools.SshPath ?? throw new InvalidOperationException("ssh path is not available.");
                var localPort = new Uri(localUrl).Port;
                _tunnelProcess = StartProcess(
                    sshPath,
                    new[]
                    {
                        "-o", "StrictHostKeyChecking=no",
                        "-o", "ServerAliveInterval=20",
                        "-o", "ExitOnForwardFailure=yes",
                        "-T",
                        "-R", $"80:localhost:{localPort}",
                        "nokey@localhost.run"
                    },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching localhost.run...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "localhost.run", _liveLogCts?.Token ?? cancellationToken);

                return await WaitForLocalhostRunUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    cancellationToken);
            }

            case "tailscale-funnel":
                {
                    var tailscalePath = tools.TailscalePath ?? throw new InvalidOperationException("tailscale path is not available.");
                    var localPort = new Uri(localUrl).Port;

                    return await StartTailscaleFunnelAsync(
                        tailscalePath,
                        localPort,
                        workingDirectory,
                        log,
                        cancellationToken);
                }

            case "zrok":
            {
                var zrokPath = tools.ZrokPath ?? throw new InvalidOperationException("zrok path is not available.");
                var zrokToken = (settings.TunnelAuth ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(zrokToken))
                    await TryEnableZrokAsync(zrokPath, zrokToken, workingDirectory, log, cancellationToken);

                _tunnelProcess = StartProcess(
                    zrokPath,
                    new[] { "share", "public", $"127.0.0.1:{new Uri(localUrl).Port}" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching zrok public share...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "zrok", _liveLogCts?.Token ?? cancellationToken);

                return await WaitForPublicUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    "zrok",
                    cancellationToken,
                    hostHint: null);
            }

            case "localtunnel":
            {
                var launch = BuildLocalTunnelCommand(
                    tools.LocalTunnelPath ?? throw new InvalidOperationException("localtunnel path is not available."),
                    new Uri(localUrl).Port);

                _tunnelProcess = StartProcess(
                    launch.FileName,
                    launch.Arguments,
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);

                log("Launching LocalTunnel...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "localtunnel", _liveLogCts?.Token ?? cancellationToken);

                var localTunnelUrl = await WaitForPublicUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    "LocalTunnel",
                    cancellationToken,
                    hostHint: null);

                log("LocalTunnel may show its own password / consent page to external visitors. That page is controlled by the LocalTunnel service.");
                return localTunnelUrl;
            }

            case "xtunnel":
            {
                var xTunnelPath = tools.XTunnelPath ?? throw new InvalidOperationException("xtunnel path is not available.");
                var xTunnelKey = (settings.TunnelAuth ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(xTunnelKey))
                    await TryActivateXTunnelAsync(xTunnelPath, xTunnelKey, workingDirectory, log, cancellationToken);

                log("Launching xTunnel...");
                var xTunnelPort = new Uri(localUrl).Port;
                return await StartXTunnelWithFallbackAsync(
                    xTunnelPath,
                    xTunnelPort,
                    workingDirectory,
                    log,
                    cancellationToken);
            }

            case "vk-tunnel":
            {
                var launch = BuildVkTunnelCommand(tools.VkTunnelPath ?? throw new InvalidOperationException("vk-tunnel path is not available."), new Uri(localUrl).Port);
                _tunnelProcess = StartProcess(
                    launch.FileName,
                    launch.Arguments,
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);
                log("Launching VK Tunnel...");
                _tunnelLiveLogTask = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, log, "vk-tunnel", _liveLogCts?.Token ?? cancellationToken);
                return await WaitForVkTunnelUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);
            }

            default:
                throw new InvalidOperationException("Unsupported tunnel provider: " + provider);
        }
    }

    private static async Task TryEnableZrokAsync(
        string zrokPath,
        string token,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var stdoutPath = Path.GetTempFileName();
        var stderrPath = Path.GetTempFileName();
        try
        {
            log("Attempting zrok enable...");
            using var process = StartProcess(zrokPath, new[] { "enable", token }, workingDirectory, stdoutPath, stderrPath);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var text = stdout + Environment.NewLine + stderr;

            if (process.ExitCode == 0 ||
                text.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
                log("zrok enable step completed.");
                return;
            }

            throw new InvalidOperationException(
                "zrok enable did not confirm success. Check your zrok account token.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);
        }
        finally
        {
            try { File.Delete(stdoutPath); } catch { }
            try { File.Delete(stderrPath); } catch { }
        }
    }

    private static async Task TryConfigureNgrokAuthAsync(
        string ngrokPath,
        string token,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var stdoutPath = Path.GetTempFileName();
        var stderrPath = Path.GetTempFileName();
        try
        {
            log("Attempting ngrok authtoken setup...");
            using var process = StartProcess(ngrokPath, new[] { "config", "add-authtoken", token }, workingDirectory, stdoutPath, stderrPath);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var text = stdout + Environment.NewLine + stderr;

            if (process.ExitCode == 0 ||
                text.Contains("authtoken saved", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("updated", StringComparison.OrdinalIgnoreCase))
            {
                log("ngrok authtoken step completed.");
                return;
            }

            throw new InvalidOperationException(
                "ngrok authtoken setup did not confirm success. Check your ngrok authtoken.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);
        }
        finally
        {
            try { File.Delete(stdoutPath); } catch { }
            try { File.Delete(stderrPath); } catch { }
        }
    }

    private static (string FileName, string[] Arguments) BuildLocalTunnelCommand(string configuredPath, int port)
    {
        var fileNameOnly = Path.GetFileName(configuredPath);
        if (fileNameOnly.Equals("npx", StringComparison.OrdinalIgnoreCase) ||
            fileNameOnly.Equals("npx.cmd", StringComparison.OrdinalIgnoreCase) ||
            fileNameOnly.Equals("npx.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (configuredPath, new[] { "-y", "localtunnel", "--port", port.ToString(), "--local-host", "127.0.0.1" });
        }

        return (configuredPath, new[] { "--port", port.ToString(), "--local-host", "127.0.0.1" });
    }

    private static async Task TryActivateXTunnelAsync(string xTunnelPath, string key, string workingDirectory, Action<string> log, CancellationToken cancellationToken)
    {
        var stdoutPath = Path.GetTempFileName();
        var stderrPath = Path.GetTempFileName();
        try
        {
            log("Attempting xTunnel activation...");
            using var process = StartProcess(xTunnelPath, new[] { "register", key }, workingDirectory, stdoutPath, stderrPath);
            await process.WaitForExitAsync(cancellationToken);
            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            if (process.ExitCode == 0 ||
                text.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("activated", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("успеш", StringComparison.OrdinalIgnoreCase))
            {
                log("xTunnel activation step completed.");
                return;
            }

            log("xTunnel activation did not confirm success. The tunnel will still be started and may already be activated.");
        }
        catch (Exception ex)
        {
            log("xTunnel activation attempt warning: " + ex.Message);
        }
        finally
        {
            try { File.Delete(stdoutPath); } catch { }
            try { File.Delete(stderrPath); } catch { }
        }
    }

    private async Task<string> StartXTunnelWithFallbackAsync(
        string xTunnelPath,
        int port,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var attempts = new (string[] Args, string Label)[]
        {
            (BuildXTunnelArgs(port, force: true, headless: true), "headless mode"),
            (BuildXTunnelArgs(port, force: true, headless: false), "console-compatible mode")
        };

        Exception? lastError = null;

        foreach (var attempt in attempts)
        {
            ResetTempFile(_tunnelStdoutFile!);
            ResetTempFile(_tunnelStderrFile!);

            _tunnelProcess = StartProcess(
                xTunnelPath,
                attempt.Args,
                workingDirectory,
                _tunnelStdoutFile!,
                _tunnelStderrFile!,
                redirectStandardInput: false);

            _tunnelLiveLogTask = PumpLogsAsync(
                _tunnelProcess,
                _tunnelStdoutFile!,
                _tunnelStderrFile!,
                log,
                "xtunnel",
                _liveLogCts?.Token ?? cancellationToken);

            try
            {
                await WaitForXTunnelConnectedAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    log,
                    cancellationToken);

                return await WaitForPublicUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    "xTunnel",
                    cancellationToken,
                    hostHint: null);
            }
            catch (Exception ex)
            {
                lastError = ex;
                log($"xTunnel start failed in {attempt.Label}: {ex.Message}");
                await StopProcessAsync(_tunnelProcess, log, "xtunnel");
                if (_tunnelLiveLogTask is not null)
                    await IgnoreTaskAsync(_tunnelLiveLogTask);

                _tunnelProcess = null;
                _tunnelLiveLogTask = null;
            }
        }

        throw lastError ?? new InvalidOperationException("xTunnel failed to start.");
    }

    private static string[] BuildXTunnelArgs(int port, bool force, bool headless)
    {
        if (headless)
        {
            return force
                ? new[] { "http", port.ToString(), "--monitor", "false", "--force" }
                : new[] { "http", port.ToString(), "--monitor", "false" };
        }

        return force
            ? new[] { "http", port.ToString(), "--force" }
            : new[] { "http", port.ToString() };
    }

    private static void ResetTempFile(string path)
    {
        try
        {
            File.WriteAllText(path, string.Empty);
        }
        catch
        {
        }
    }

    private static (string FileName, string[] Arguments) BuildVkTunnelCommand(string configuredPath, int port)
    {
        var fileNameOnly = Path.GetFileName(configuredPath);
        if (fileNameOnly.Equals("npx", StringComparison.OrdinalIgnoreCase) ||
            fileNameOnly.Equals("npx.cmd", StringComparison.OrdinalIgnoreCase) ||
            fileNameOnly.Equals("npx.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (configuredPath, new[]
            {
                "-y",
                "@vkontakte/vk-tunnel",
                "--insecure=1",
                "--http-protocol=http",
                "--ws-protocol=ws",
                "--host=127.0.0.1",
                "--port=" + port,
                "--timeout=5000"
            });
        }

        if (configuredPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            var nodePath = FindNodePath();
            if (string.IsNullOrWhiteSpace(nodePath))
                throw new InvalidOperationException("vk-tunnel.js was provided, but node.exe was not found in PATH.");

            return (nodePath, new[]
            {
                configuredPath,
                "--insecure=1",
                "--http-protocol=http",
                "--ws-protocol=ws",
                "--host=127.0.0.1",
                "--port=" + port,
                "--timeout=5000"
            });
        }

        var directArgs = new[]
        {
            "--insecure=1",
            "--http-protocol=http",
            "--ws-protocol=ws",
            "--host=127.0.0.1",
            "--port=" + port,
            "--timeout=5000"
        };

        if (OperatingSystem.IsWindows() &&
            (configuredPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
             configuredPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", new[] { "/c", configuredPath }.Concat(directArgs).ToArray());
        }

        return (configuredPath, directArgs);
    }

    private static string? FindNodePath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "node.exe" : "node");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
            }
        }

        return null;
    }

    private static string NormalizeTunnelProvider(string provider) =>
        (provider ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cloudflare tunnel" => "cloudflared",
            "cloudflared" => "cloudflared",
            "cloudflare name" => "cloudflare-name",
            "cloudflare-name" => "cloudflare-name",
            "ngrok" => "ngrok",
            "localhost.run" => "localhost.run",
            "localhostrun" => "localhost.run",
            "tailscale funnel" => "tailscale-funnel",
            "tailscale-funnel" => "tailscale-funnel",
            "zrok" => "zrok",
            "localtunnel" => "localtunnel",
            "local tunnel" => "localtunnel",
            "xtunnel" => "xtunnel",
            "vk tunnel" => "vk-tunnel",
            "vk-tunnel" => "vk-tunnel",
            _ => throw new InvalidOperationException("Unsupported tunnel provider: " + provider),
        };

    private static Process StartProcess(string fileName, IEnumerable<string> args, string workingDirectory, string stdoutPath, string stderrPath, bool redirectStandardInput = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        process.Start();
        _ = MirrorStreamAsync(process.StandardOutput, stdoutPath);
        _ = MirrorStreamAsync(process.StandardError, stderrPath);

        return process;
    }

    private static async Task MirrorStreamAsync(StreamReader reader, string path)
    {
        var buffer = new char[4096];

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                AppendTextSafe(path, new string(buffer, 0, read));
            }
        }
        catch
        {
        }
    }

    private static async Task WaitForXTunnelConnectedAsync(
        Process process,
        string stdoutPath,
        string stderrPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            if (text.Contains("LICENSE_BUSY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("xTunnel reported LICENSE_BUSY.");

            if (text.Contains("Full reconnect sequence complete", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("All 4 connection(s) connected to ProxyEntry", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("State: Connected", StringComparison.OrdinalIgnoreCase))
            {
                log("xTunnel reported Connected.");
                return;
            }

            if (process.HasExited)
            {   
                throw new InvalidOperationException($"""
    xTunnel exited before reaching Connected.

    STDOUT:
    {ReadFileSafe(stdoutPath)}

    STDERR:
    {ReadFileSafe(stderrPath)}
    """);
                

            }

            await Task.Delay(500, cancellationToken);
        }

        log("xTunnel did not report Connected in time; continuing to public URL detection.");
    }

    private static async Task<string> WaitForVkTunnelUrlAsync(
    Process process,
    string stdoutPath,
    string stderrPath,
    Action<string> log,
    CancellationToken cancellationToken)
    {
        var browserOpened = false;
        var enterMode = false;
        DateTimeOffset? authPromptSeenAt = null;
        DateTimeOffset lastEnterSentAt = DateTimeOffset.MinValue;

        for (var i = 0; i < 180; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var text = stdout + Environment.NewLine + stderr;

            var publicUrl = ExtractVkTunnelPublicUrl(text);
            if (!string.IsNullOrWhiteSpace(publicUrl))
                return publicUrl;

            var authUrl = ExtractVkAuthorizationUrl(text);
            if (!string.IsNullOrWhiteSpace(authUrl))
            {
                authPromptSeenAt ??= DateTimeOffset.UtcNow;

                if (!browserOpened)
                {
                    log("VK Tunnel requested browser authorization. Opening the official VK authorization page...");
                    OpenUrlInBrowser(authUrl);
                    browserOpened = true;
                }

                // После появления auth URL считаем, что дальше CLI может ждать ENTER,
                // даже если эта строка не попала в лог из-за ANSI / интерактивного режима.
                if (!enterMode && DateTimeOffset.UtcNow - authPromptSeenAt.Value >= TimeSpan.FromSeconds(8))
                {
                    log("VK Tunnel authorization page was opened. Waiting briefly, then confirming in the tunnel process...");
                    enterMode = true;
                }
            }

            if (enterMode)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - lastEnterSentAt >= TimeSpan.FromSeconds(3))
                {
                    try
                    {
                        await process.StandardInput.WriteLineAsync();
                        await process.StandardInput.FlushAsync();
                        lastEnterSentAt = now;
                        log("Sent ENTER to VK Tunnel to continue after browser authorization...");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to send ENTER to VK Tunnel.\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}\n\nReason: {ex.Message}",
                            ex);
                    }
                }
            }

            if (process.HasExited)
            {
                ThrowPublicUrlDetectionError("VK Tunnel", stdout, stderr);
                return string.Empty;
            }


            await Task.Delay(1000, cancellationToken);
        }

        var finalStdout = ReadFileSafe(stdoutPath);
        var finalStderr = ReadFileSafe(stderrPath);
        var finalText = finalStdout + Environment.NewLine + finalStderr;
        var finalAuthUrl = ExtractVkAuthorizationUrl(finalText);

        if (!string.IsNullOrWhiteSpace(finalAuthUrl))
        {
            throw new InvalidOperationException($"""
            VK Tunnel is still waiting after browser authorization.

            The official VK authorization page was opened:
            {finalAuthUrl}

            Authorization may have completed in the browser, but the CLI did not continue to a public URL in time.

            STDOUT:
            {finalStdout}

            STDERR:
            {finalStderr}
            """);
        }

        ThrowPublicUrlDetectionError("VK Tunnel", finalStdout, finalStderr);
        return string.Empty;
    }

    private static string? ExtractVkAuthorizationUrl(string text)
    {
        foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (value.Contains("oauth.vk.ru/code_auth", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static string? ExtractVkTunnelPublicUrl(string text)
    {
        foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (value.Contains("oauth.vk.ru/code_auth", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                continue;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                continue;

            if (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                continue;

            return value;
        }

        return null;
    }

    private static void OpenUrlInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private static async Task WaitForLocalEndpointAsync(
        Process process,
        string url,
        string? stdoutPath,
        string? stderrPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Caddy exited during startup.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
                    "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
            }

            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    log($"Local readiness probe OK: {url} -> {(int)response.StatusCode}");
                    return;
                }

                log($"Local readiness probe attempt {attempt}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                log($"Local readiness probe attempt {attempt} failed: {ex.Message}");
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            "Caddy did not become ready in time.\n\n" +
            $"Last local probe URL: {url}\n" +
            $"Last local probe error: {lastError?.Message ?? "(none)"}\n\n" +
            "STDOUT:\n" + ReadFileSafe(stdoutPath) +
            "\n\nSTDERR:\n" + ReadFileSafe(stderrPath),
            lastError);
    }

    private static async Task<string> WaitForRegexUrlAsync(
        Process process,
        string stdoutPath,
        string stderrPath,
        Regex regex,
        string providerName,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            var match = regex.Match(text);
            if (match.Success)
                return match.Value;

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        ThrowPublicUrlDetectionError(providerName, ReadFileSafe(stdoutPath), ReadFileSafe(stderrPath));
        return string.Empty;
    }

    private static async Task<string> WaitForPublicUrlAsync(
        Process process,
        string stdoutPath,
        string stderrPath,
        string providerName,
        CancellationToken cancellationToken,
        string? hostHint)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var candidate = providerName.Equals("xTunnel", StringComparison.OrdinalIgnoreCase)
                ? ExtractXTunnelPublicUrl(stdout, stderr)
                : ExtractPublicUrl(stdout + Environment.NewLine + stderr, hostHint);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        ThrowPublicUrlDetectionError(providerName, ReadFileSafe(stdoutPath), ReadFileSafe(stderrPath));
        return string.Empty;
    }
    private async Task<string> StartTailscaleFunnelAsync(
    string tailscalePath,
    int localPort,
    string workingDirectory,
    Action<string> log,
    CancellationToken cancellationToken)
    {
        // 1. Пробуем включить Funnel
        _tunnelProcess = StartProcess(
            tailscalePath,
            new[] { "funnel", $"127.0.0.1:{localPort}" },
            workingDirectory,
            _tunnelStdoutFile!,
            _tunnelStderrFile!);

        log("Launching Tailscale Funnel...");
        _tunnelLiveLogTask = PumpLogsAsync(
            _tunnelProcess,
            _tunnelStdoutFile!,
            _tunnelStderrFile!,
            log,
            "tailscale",
            _liveLogCts?.Token ?? cancellationToken);

        return await WaitForTailscaleFunnelUrlAsync(
            tailscalePath,
            localPort,
            workingDirectory,
            _tunnelProcess,
            _tunnelStdoutFile!,
            _tunnelStderrFile!,
            log,
            cancellationToken);
    }

    private static async Task ResetTailscaleFunnelAsync(
    string tailscalePath,
    string? workingDirectory,
    CancellationToken cancellationToken)
    {
        var stdoutPath = Path.GetTempFileName();
        var stderrPath = Path.GetTempFileName();

        try
        {
            using var process = StartProcess(
                tailscalePath,
                new[] { "funnel", "reset" },
                workingDirectory ?? Environment.CurrentDirectory,
                stdoutPath,
                stderrPath);

            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            try { File.Delete(stdoutPath); } catch { }
            try { File.Delete(stderrPath); } catch { }
        }
    }

    private async Task<string?> RecreateTailscaleFunnelAfterResetAsync(
    string tailscalePath,
    int localPort,
    string workingDirectory,
    Action<string> log,
    CancellationToken cancellationToken)
    {
        log("Resetting previous Tailscale Funnel config...");
        await ResetTailscaleFunnelAsync(tailscalePath, workingDirectory, cancellationToken);

        _tunnelProcess = StartProcess(
            tailscalePath,
            new[] { "funnel", $"127.0.0.1:{localPort}" },
            workingDirectory,
            _tunnelStdoutFile!,
            _tunnelStderrFile!);

        log("Recreating Tailscale Funnel after reset...");
        _tunnelLiveLogTask = PumpLogsAsync(
            _tunnelProcess,
            _tunnelStdoutFile!,
            _tunnelStderrFile!,
            log,
            "tailscale",
            _liveLogCts?.Token ?? cancellationToken);

        for (var i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = await TryGetTailscaleFunnelUrlFromStatusAsync(tailscalePath, workingDirectory, cancellationToken);
            if (!string.IsNullOrWhiteSpace(url))
                return url;

            if (_tunnelProcess.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        return null;
    }

    private static string? ExtractTailscaleDirectUrl(string text)
    {
        foreach (Match match in Regex.Matches(
            text,
            @"https://[a-z0-9.-]+\.ts\.net/?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (!string.IsNullOrWhiteSpace(value))
                return value.TrimEnd('/');
        }

        return null;
    }


    private async Task<string> WaitForTailscaleFunnelUrlAsync(
    string tailscalePath,
    int localPort,
    string workingDirectory,
    Process process,
    string stdoutPath,
    string stderrPath,
    Action<string> log,
    CancellationToken cancellationToken)
    {
        var activationOpened = false;

        for (var i = 0; i < 30; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var combined = stdout + Environment.NewLine + stderr;
            var directUrl = ExtractTailscaleDirectUrl(combined);
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                log("Tailscale Funnel public URL detected from stdout.");
                return directUrl;
            }

            // 1. Если Funnel не включён — достаём activation URL и открываем его
            var activationUrl = ExtractTailscaleActivationUrl(combined);
            if (!string.IsNullOrWhiteSpace(activationUrl))
            {
                if (!activationOpened)
                {
                    log("Tailscale Funnel requires activation for this node. Opening activation page...");
                    OpenUrlInBrowser(activationUrl);
                    activationOpened = true;
                }

                throw new InvalidOperationException(
                    "Tailscale Funnel is not enabled for this device yet. " +
                    "The activation page was opened in your browser. Enable Funnel there and start again.\n\n" +
                    "Activation URL:\n" + activationUrl + "\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);
            }

            // 2. Если конфиг уже существует — не считаем это fatal, а пробуем читать status
            if (combined.Contains("listener already exists for port 443", StringComparison.OrdinalIgnoreCase))
            {
                log("Tailscale Funnel listener on port 443 already exists. Trying to reuse existing funnel config...");
                var existing = await TryGetTailscaleFunnelUrlFromStatusAsync(tailscalePath, workingDirectory, cancellationToken);
                if (!string.IsNullOrWhiteSpace(existing))
                    return existing;

                log("Existing Funnel config could not be reused. Resetting and recreating...");
                var recreated = await RecreateTailscaleFunnelAfterResetAsync(
                    tailscalePath,
                    localPort,
                    workingDirectory,
                    log,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(recreated))
                    return recreated;
            }

            // 3. Если была гонка serve config — даём шанс и читаем status повторно
            if (combined.Contains("etag mismatch", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Another client is changing the serve config", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Preconditions failed", StringComparison.OrdinalIgnoreCase))
            {
                log("Tailscale serve/funnel config changed concurrently. Waiting and retrying status...");
                await Task.Delay(1000, cancellationToken);

                var existing = await TryGetTailscaleFunnelUrlFromStatusAsync(tailscalePath, workingDirectory: null, cancellationToken);
                if (!string.IsNullOrWhiteSpace(existing))
                    return existing;
            }

            // 4. Нормальный путь: читаем текущий funnel status
            var fromStatus = await TryGetTailscaleFunnelUrlFromStatusAsync(tailscalePath, workingDirectory: null, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fromStatus))
                return fromStatus;

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        ThrowPublicUrlDetectionError("Tailscale Funnel", ReadFileSafe(stdoutPath), ReadFileSafe(stderrPath));
        return string.Empty;
    }

    private static string? ExtractTailscaleActivationUrl(string text)
    {
        foreach (Match match in Regex.Matches(text, @"https://login\.tailscale\.com/f/funnel\?[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static async Task<string?> TryGetTailscaleFunnelUrlFromStatusAsync(
    string tailscalePath,
    string? workingDirectory,
    CancellationToken cancellationToken)
    {
        var stdoutPath = Path.GetTempFileName();
        var stderrPath = Path.GetTempFileName();

        try
        {
            using var statusProcess = StartProcess(
                tailscalePath,
                new[] { "funnel", "status", "--json" },
                workingDirectory ?? Environment.CurrentDirectory,
                stdoutPath,
                stderrPath);

            await statusProcess.WaitForExitAsync(cancellationToken);

            var stdout = ReadFileSafe(stdoutPath);
            if (string.IsNullOrWhiteSpace(stdout))
                return null;

            using var doc = JsonDocument.Parse(stdout);
            return ExtractTsNetUrlFromJson(doc.RootElement);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(stdoutPath); } catch { }
            try { File.Delete(stderrPath); } catch { }
        }
    }

    private static string? ExtractTsNetUrlFromJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString() ?? string.Empty;
                    if (Regex.IsMatch(value, @"^https://[a-z0-9.-]+\.ts\.net/?$", RegexOptions.IgnoreCase))
                        return value.TrimEnd('/');
                }

                var nested = ExtractTsNetUrlFromJson(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractTsNetUrlFromJson(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static async Task<string> WaitForLocalhostRunUrlAsync(
        Process process,
        string stdoutPath,
        string stderrPath,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stdout = ReadFileSafe(stdoutPath);
            var stderr = ReadFileSafe(stderrPath);
            var candidate = ExtractLocalhostRunPublicUrl(stdout + Environment.NewLine + stderr);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        ThrowPublicUrlDetectionError("localhost.run", ReadFileSafe(stdoutPath), ReadFileSafe(stderrPath));
        return string.Empty;
    }

    private static string? ExtractXTunnelPublicUrl(string stdout, string stderr)
    {
        var combined = NormalizeTextForXtunnelParsing(stdout + Environment.NewLine + stderr);

        var fromJson = ExtractXTunnelPublicUrlFromJsonLines(combined);
        if (!string.IsNullOrWhiteSpace(fromJson))
            return fromJson;

        foreach (Match match in Regex.Matches(combined, @"Public URL:\s*(https?://[^\s""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (IsLikelyXTunnelPublicUrl(value))
                return value;
        }

        // In redirected text-mode runs the TUI line "Public URL:" may be absent,
        // while diagnostics still emit the assigned endpoint explicitly.
        foreach (Match match in Regex.Matches(combined, @"PublicEndpointAssigned:\s*(https?://[^\s""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (IsLikelyXTunnelPublicUrl(value))
                return value;
        }

        foreach (Match match in Regex.Matches(combined, @"PublicUrl assigned:\s*(https?://[^\s""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (IsLikelyXTunnelPublicUrl(value))
                return value;
        }

        return null;
    }

    private static string NormalizeTextForXtunnelParsing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var withoutAnsi = Regex.Replace(
            text,
            @"\x1B(?:\[[0-?]*[ -/]*[@-~]|[@-_])",
            string.Empty,
            RegexOptions.Compiled);

        var normalizedControls = Regex.Replace(
            withoutAnsi,
            @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]+",
            " ",
            RegexOptions.Compiled);

        normalizedControls = normalizedControls.Replace('\r', '\n');
        normalizedControls = Regex.Replace(normalizedControls, @"[ \t]+", " ", RegexOptions.Compiled);
        normalizedControls = Regex.Replace(normalizedControls, @"\n+", "\n", RegexOptions.Compiled);

        return normalizedControls;
    }

    private static string? ExtractXTunnelPublicUrlFromJsonLines(string text)
    {
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var root = document.RootElement;

                if (TryGetXtunnelJsonUrl(root, out var url))
                    return url;

                if (root.TryGetProperty("msg", out var msgElement) &&
                    msgElement.ValueKind == JsonValueKind.String)
                {
                    var message = msgElement.GetString() ?? string.Empty;
                    var fromMessage = ExtractXTunnelUrlFromMessage(message);
                    if (!string.IsNullOrWhiteSpace(fromMessage))
                        return fromMessage;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool TryGetXtunnelJsonUrl(JsonElement element, out string? url)
    {
        url = null;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var stringValue = property.Value.GetString() ?? string.Empty;
                    if (IsXtunnelUrlPropertyName(property.Name) && IsLikelyXTunnelPublicUrl(stringValue))
                    {
                        url = stringValue;
                        return true;
                    }

                    var fromMessage = ExtractXTunnelUrlFromMessage(stringValue);
                    if (!string.IsNullOrWhiteSpace(fromMessage))
                    {
                        url = fromMessage;
                        return true;
                    }
                }
                else if (TryGetXtunnelJsonUrl(property.Value, out url))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetXtunnelJsonUrl(item, out url))
                    return true;
            }
        }

        return false;
    }

    private static bool IsXtunnelUrlPropertyName(string propertyName)
    {
        return propertyName.Equals("PublicEndpointAssigned", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("PublicUrl", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("Url", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractXTunnelUrlFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        foreach (Match match in Regex.Matches(message, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (IsLikelyXTunnelPublicUrl(value))
                return value;
        }

        return null;
    }

    private static void ThrowPublicUrlDetectionError(string providerName, string stdout, string stderr)
    {
        var combined = stdout + Environment.NewLine + stderr;

        if (combined.Contains("Failed to fetch tunnel configuration from server", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                providerName + " authorization succeeded, but the service denied tunnel creation (Access denied). " +
                "VK Tunnel is currently unavailable on the service side. Please choose another tunnel provider.\n\n" +
                "STDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);
        }

        if (combined.Contains("token must be specified", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(providerName + " requires authentication data. Fill in the Tunnel auth box and try again.\n\nSTDERR:\n" + stderr);

        if (providerName.Equals("zrok", StringComparison.OrdinalIgnoreCase) &&
            (combined.Contains("enable", StringComparison.OrdinalIgnoreCase) ||
             (combined.Contains("environment", StringComparison.OrdinalIgnoreCase) && combined.Contains("not", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("zrok requires an enabled environment on this machine. Paste your zrok account token into Tunnel auth for the first run, or enable zrok manually, then try again.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        if (providerName.Equals("ngrok", StringComparison.OrdinalIgnoreCase) &&
            (combined.Contains("authtoken", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("ERR_NGROK", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("failed to authenticate", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("ngrok requires a valid authtoken on this machine. Paste your ngrok authtoken into Tunnel auth or configure ngrok manually, then try again.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        if (providerName.Equals("localhost.run", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("localhost.run rejected the SSH tunnel. For free tunnels the app uses nokey@localhost.run. If your network blocks SSH or the service is unavailable, try again later or switch to another provider.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        if (providerName.Equals("Tailscale Funnel", StringComparison.OrdinalIgnoreCase))
        {
            var directUrl = ExtractTailscaleDirectUrl(combined);
            if (!string.IsNullOrWhiteSpace(directUrl))
                return;

            var activationUrl = ExtractTailscaleActivationUrl(combined);

            if (!string.IsNullOrWhiteSpace(activationUrl))
                throw new InvalidOperationException(
                    "Tailscale Funnel is not enabled for this node yet. Open and approve this activation URL, then try again:\n\n" +
                    activationUrl + "\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

            if (combined.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("tailscale up", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Tailscale is installed, but this device is not signed in. Sign in to Tailscale first, then try again.\n\n" +
                    "STDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

            if (combined.Contains("etag mismatch", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Another client is changing the serve config", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Preconditions failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Tailscale Serve/Funnel config changed concurrently. Retry once after a short delay.\n\n" +
                    "STDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

            if (combined.Contains("listener already exists for port 443", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Tailscale Funnel listener is still present. Stop should clear it automatically; if not, use Stop once more or restart Tailscale.\n\n" +
                    "STDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);
        }
        if (providerName.Equals("LocalTunnel", StringComparison.OrdinalIgnoreCase) &&
            (combined.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("tunneling socket could not be established", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("LocalTunnel could not connect to the service. Try again later, or switch to another tunnel provider.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        if (combined.Contains("LICENSE_BUSY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(providerName + " reported LICENSE_BUSY. This license already has an active tunnel. Close the other tunnel or retry with --force.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        if (combined.Contains("license", StringComparison.OrdinalIgnoreCase) || combined.Contains("ключ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(providerName + " needs activation or a license key. Fill in the Tunnel auth box and try again.\n\nSTDOUT:\n" + stdout + "\n\nSTDERR:\n" + stderr);

        throw new InvalidOperationException(
            providerName + " started, but the public URL was not detected in logs.\n\nSTDOUT:\n" + stdout +
            "\n\nSTDERR:\n" + stderr);
    }

    private static string? ExtractPublicUrl(string text, string? hostHint)
    {
        foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (IsLikelyPublicUrl(value, hostHint))
                return value;
        }

        foreach (Match match in Regex.Matches(text, @"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63})(?::\d+)?(?:/[^\s""'<>]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            if (IsLikelyPublicUrl(value, hostHint))
                return value;
        }

        return null;
    }

    private static string? ExtractLocalhostRunPublicUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains("tcpip-forward", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("\"type\":\"opened\"", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("\"type\": \"opened\"", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("tunneled with tls termination", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("\"address\":", StringComparison.OrdinalIgnoreCase))
            {
                var fromLine = ExtractPublicUrl(line, hostHint: null);
                if (!string.IsNullOrWhiteSpace(fromLine))
                    return fromLine;
            }
        }

        return null;
    }

    private static bool IsLikelyPublicUrl(string value, string? hostHint)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("0.", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(hostHint) && !host.Contains(hostHint, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsLikelyXTunnelPublicUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (host.Equals("tunnel4.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 12002)
            return false;

        if (host.Equals("tunnel4.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 12301)
            return false;

        return host.Contains(".tunnel4.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".xtn.sh", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".xtunnel.ru", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitForCloudflaredConnectionAsync(
        Process process,
        string? stdoutPath,
        string? stderrPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Cloudflared exited before registering a tunnel connection.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
                    "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
            }

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            if (text.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
            {
                log("Cloudflared connection registered.");
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "Cloudflared published a URL, but never reported a registered tunnel connection.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
            "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
    }

    private static async Task TryWaitForPublicHttpAsync(
        Process process,
        string publicBaseUrl,
        string relativePath,
        string? stdoutPath,
        string? stderrPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();

        var probeUrl = publicBaseUrl.TrimEnd('/') + relativePath;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 15; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                log("Cloudflared exited while waiting for public readiness.");
                return;
            }

            try
            {
                using var response = await http.GetAsync(probeUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    log($"Tunnel HTTP became ready: {probeUrl} -> {(int)response.StatusCode}");
                    return;
                }

                log($"Tunnel HTTP probe attempt {attempt}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                log($"Tunnel HTTP probe attempt {attempt} failed: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                lastError = ex;
                log($"Tunnel HTTP probe attempt {attempt} timed out: {ex.Message}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        log(
            "Public probe did not confirm readiness from this machine. " +
            "The tunnel is registered, but local DNS or edge reachability may still be lagging.\n" +
            $"Probe URL: {probeUrl}\n" +
            $"Last HTTP error: {lastError?.Message ?? "(none)"}\n\n" +
            "STDERR:\n" + ReadFileSafe(stderrPath));
    }

    private static async Task<bool> TryProbeEndpointAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static async Task PumpLogsAsync(
        Process? process,
        string? stdoutPath,
        string? stderrPath,
        Action<string> log,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (process is null || string.IsNullOrWhiteSpace(stdoutPath) || string.IsNullOrWhiteSpace(stderrPath))
            return;

        long stdoutPos = 0;
        long stderrPos = 0;

        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            foreach (var line in ReadNewLines(stdoutPath, ref stdoutPos))
            {
                if (ShouldLogProcessLine(prefix, line))
                    log($"[{prefix}] {line}");
            }

            foreach (var line in ReadNewLines(stderrPath, ref stderrPos))
            {
                if (ShouldLogProcessLine(prefix, line))
                    log($"[{prefix}] {line}");
            }

            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch
            {
                break;
            }
        }
    }

    private static bool ShouldLogProcessLine(string prefix, string line)
    {
        if (!prefix.Equals("xtunnel", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (line.Contains("[WORKER-", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[WRITELOOP-", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[CLIENT] Queued:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[CLIENT CloseRequest]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[CLIENT] Starting backend request", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[CLIENT] Backend request completed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Heartbeat PONG", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[HEALTH] Probe", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Health check loop started", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Health check loop stopped", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[PROXY]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[PROXY] Parsed headers:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DIAGNOSTIC] Raw payload preview", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DIAGNOSTIC] Received frame", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DIAGNOSTIC] Attempting to deserialize", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DIAGNOSTIC] Deserialization SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("POST ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("range:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("pragma:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("cache-control:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("accept:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("accept-language:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("accept-encoding:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("user-agent:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("referer:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("sec-fetch-", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("priority:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("connection:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("te:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("x-forwarded-", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("x-routing-key:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static async Task IgnoreTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static string[] ReadNewLines(string path, ref long position)
    {
        try
        {
            if (!File.Exists(path))
                return Array.Empty<string>();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (position > stream.Length)
                position = 0;

            stream.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            position = stream.Position;

            return string.IsNullOrWhiteSpace(text)
                ? Array.Empty<string>()
                : text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void ResetFile(string path)
    {
        try
        {
            File.WriteAllText(path, string.Empty);
        }
        catch
        {
        }
    }

    private static string ReadFileSafe(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? File.ReadAllText(path)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AppendLineSafe(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void AppendTextSafe(string path, string text)
    {
        try
        {
            File.AppendAllText(path, text);
        }
        catch
        {
        }
    }

    private static async Task StopProcessAsync(Process? process, Action<string> log, string name)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                log($"Stopping {name}...");
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.Delay(300);
            }
        }
        catch (Exception ex)
        {
            log($"Stop warning for {name}: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void CleanupTempFile(ref string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
        finally
        {
            path = null;
        }
    }
}
