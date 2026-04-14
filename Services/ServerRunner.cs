using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    private string? _activeTunnelProvider;
    private string? _tailscaleLocalUrl;
    private string? _tailscalePath;

    public bool IsRunning =>
        (_caddyProcess is { HasExited: false }) ||
        (_tunnelProcess is { HasExited: false }) ||
        (_activeTunnelProvider is not null && string.Equals(_activeTunnelProvider, "tailscale funnel", StringComparison.OrdinalIgnoreCase));

    public async Task<LaunchSession> StartAsync(
        VideoWorkspace workspace,
        int requestedPort,
        string tunnelProvider,
        ToolPaths tools,
        Action<string> log,
        CancellationToken cancellationToken,
        string? initialVideoSrc = null)
    {
        await StopAsync(log);

        if (workspace.RootFolder is null)
            throw new InvalidOperationException("Workspace root is not set.");

        var provider = NormalizeTunnelProvider(tunnelProvider);
        _activeTunnelProvider = provider;
        _tailscalePath = tools.TailscalePath;

        var port = PortHelper.GetFreeTcpPort(requestedPort);
        var localUrl = $"http://127.0.0.1:{port}/";
        log($"Local server: {localUrl}");

        _caddyStdoutFile = Path.GetTempFileName();
        _caddyStderrFile = Path.GetTempFileName();

        _caddyProcess = StartProcess(
            tools.CaddyPath,
            new[] { "file-server", "--listen", $"127.0.0.1:{port}", "--root", "." },
            workspace.RootFolder,
            _caddyStdoutFile,
            _caddyStderrFile);

        log("Launching Caddy...");
        var caddyReady = await WaitForPortAsync(_caddyProcess, port, 30, cancellationToken);
        if (!caddyReady)
        {
            throw new InvalidOperationException(
                "Caddy did not start in time.\n\nSTDOUT:\n" + ReadFileSafe(_caddyStdoutFile) +
                "\n\nSTDERR:\n" + ReadFileSafe(_caddyStderrFile));
        }

        _tunnelStdoutFile = Path.GetTempFileName();
        _tunnelStderrFile = Path.GetTempFileName();

        var publicBaseUrl = await StartTunnelAsync(provider, localUrl, workspace.RootFolder, tools, log, cancellationToken);

        var targetUrl = publicBaseUrl.TrimEnd('/') + "/player.html?list=/playlist.json";
        if (!string.IsNullOrWhiteSpace(initialVideoSrc))
            targetUrl += "&src=" + Uri.EscapeDataString(initialVideoSrc);

        if (port != requestedPort)
            log($"Requested port {requestedPort} is unavailable. Using port {port} instead.");

        log("Public URL: " + targetUrl);

        _ = PumpLogsAsync(_caddyProcess, _caddyStdoutFile, _caddyStderrFile, log, "caddy", cancellationToken);
        if (_tunnelProcess is not null)
            _ = PumpLogsAsync(_tunnelProcess, _tunnelStdoutFile, _tunnelStderrFile, log, provider, cancellationToken);

        return new LaunchSession
        {
            Port = port,
            LocalUrl = localUrl,
            PublicUrl = targetUrl,
        };
    }

    public async Task StopAsync(Action<string> log)
    {
        if (string.Equals(_activeTunnelProvider, "tailscale funnel", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_tailscaleLocalUrl) &&
            !string.IsNullOrWhiteSpace(_tailscalePath))
        {
            try
            {
                await StopTailscaleFunnelAsync(log);
            }
            catch (Exception ex)
            {
                log("[tailscale funnel] stop warning: " + ex.Message);
            }
        }

        await StopProcessAsync(_tunnelProcess, log, _activeTunnelProvider ?? "tunnel");
        await StopProcessAsync(_caddyProcess, log, "caddy");
        _tunnelProcess = null;
        _caddyProcess = null;
        _activeTunnelProvider = null;
        _tailscaleLocalUrl = null;
        _tailscalePath = null;
        CleanupTempFile(ref _caddyStdoutFile);
        CleanupTempFile(ref _caddyStderrFile);
        CleanupTempFile(ref _tunnelStdoutFile);
        CleanupTempFile(ref _tunnelStderrFile);
    }

    private async Task<string> StartTunnelAsync(string provider, string localUrl, string workingDirectory, ToolPaths tools, Action<string> log, CancellationToken cancellationToken)
    {
        switch (provider)
        {
            case "cloudflared":
                _tunnelProcess = StartProcess(
                    tools.CloudflaredPath ?? throw new InvalidOperationException("cloudflared path is not available."),
                    new[] { "tunnel", "--url", localUrl, "--no-autoupdate", "--protocol", "http2" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);
                log("Launching cloudflared...");
                return await WaitForRegexUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    "Cloudflared",
                    cancellationToken);

            case "ngrok":
                _tunnelProcess = StartProcess(
                    tools.NgrokPath ?? throw new InvalidOperationException("ngrok path is not available."),
                    new[] { "http", localUrl, "--log", "stdout", "--log-format", "json" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);
                log("Launching ngrok...");
                return await WaitForNgrokUrlAsync(_tunnelProcess, _tunnelStdoutFile!, _tunnelStderrFile!, cancellationToken);

            case "localhost.run":
                _tunnelProcess = StartProcess(
                    tools.SshPath ?? throw new InvalidOperationException("ssh path is not available."),
                    new[] { "-o", "StrictHostKeyChecking=no", "-o", "ServerAliveInterval=30", "-R", $"80:localhost:{new Uri(localUrl).Port}", "localhost.run" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);
                log("Launching localhost.run tunnel via ssh...");
                return await WaitForRegexUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    new Regex(@"https://[A-Za-z0-9.-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    "localhost.run",
                    cancellationToken);

            case "pinggy":
                _tunnelProcess = StartProcess(
                    tools.SshPath ?? throw new InvalidOperationException("ssh path is not available."),
                    new[] { "-p", "443", "-o", "StrictHostKeyChecking=no", "-o", "ServerAliveInterval=30", "-R", $"0:localhost:{new Uri(localUrl).Port}", "free.pinggy.io" },
                    workingDirectory,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!);
                log("Launching Pinggy tunnel via ssh...");
                return await WaitForRegexUrlAsync(
                    _tunnelProcess,
                    _tunnelStdoutFile!,
                    _tunnelStderrFile!,
                    new Regex(@"https://[A-Za-z0-9.-]+\.pinggy\.link", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    "Pinggy",
                    cancellationToken);

            case "tailscale funnel":
                _tailscaleLocalUrl = localUrl;
                return await StartTailscaleFunnelAsync(localUrl, tools, log, cancellationToken);

            default:
                throw new InvalidOperationException("Unsupported tunnel provider: " + provider);
        }
    }

    private async Task<string> StartTailscaleFunnelAsync(string localUrl, ToolPaths tools, Action<string> log, CancellationToken cancellationToken)
    {
        var tailscalePath = tools.TailscalePath ?? throw new InvalidOperationException("tailscale path is not available.");
        var port = new Uri(localUrl).Port;

        var start = await RunCommandCaptureAsync(
            tailscalePath,
            new[] { "funnel", "--bg", "--yes", $"localhost:{port}" },
            cancellationToken);

        log("Launching Tailscale Funnel...");
        if (!string.IsNullOrWhiteSpace(start.StdErr))
            AppendLineSafe(_tunnelStderrFile!, start.StdErr);
        if (!string.IsNullOrWhiteSpace(start.StdOut))
            AppendLineSafe(_tunnelStdoutFile!, start.StdOut);

        var status = await RunCommandCaptureAsync(
            tailscalePath,
            new[] { "funnel", "status", "--json" },
            cancellationToken);

        AppendLineSafe(_tunnelStdoutFile!, status.StdOut);
        var url = TryExtractTailscaleUrl(status.StdOut);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "Tailscale Funnel started, but the public URL could not be detected. Make sure Tailscale is signed in and Funnel is allowed for this tailnet.\n\nSTDOUT:\n" + status.StdOut +
                "\n\nSTDERR:\n" + status.StdErr);
        }

        return url;
    }

    private async Task StopTailscaleFunnelAsync(Action<string> log)
    {
        var result = await RunCommandCaptureAsync(
            _tailscalePath!,
            new[] { "funnel", _tailscaleLocalUrl!, "off" },
            CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(result.StdErr))
            log("[tailscale funnel] " + result.StdErr.Trim());
    }

    private static string TryExtractTailscaleUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var urls = new List<string>();
            CollectHttpsUrls(doc.RootElement, urls);
            return urls
                .Where(x => x.Contains(".ts.net", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void CollectHttpsUrls(JsonElement element, List<string> urls)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            urls.Add(value);
                    }

                    CollectHttpsUrls(property.Value, urls);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectHttpsUrls(item, urls);
                break;
        }
    }

    private static async Task<(string StdOut, string StdErr)> RunCommandCaptureAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        return (await stdoutTask, await stderrTask);
    }

    private static string NormalizeTunnelProvider(string provider) =>
        (provider ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cloudflare tunnel" => "cloudflared",
            "cloudflared" => "cloudflared",
            "ngrok" => "ngrok",
            "localhost.run" => "localhost.run",
            "pinggy" => "pinggy",
            "tailscale funnel" => "tailscale funnel",
            _ => throw new InvalidOperationException("Unsupported tunnel provider: " + provider),
        };

    private static Process StartProcess(string fileName, IEnumerable<string> args, string workingDirectory, string stdoutPath, string stderrPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLineSafe(stdoutPath, e.Data!); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLineSafe(stderrPath, e.Data!); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<bool> WaitForPortAsync(Process process, int port, int attempts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                return false;

            if (PortHelper.TestTcpPortOpen("127.0.0.1", port))
                return true;

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private static async Task<string> WaitForNgrokUrlAsync(Process process, string stdoutPath, string stderrPath, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            var url = TryExtractNgrokUrl(text);
            if (!string.IsNullOrWhiteSpace(url))
                return url;

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        throw new InvalidOperationException(
            "ngrok started, but the public URL was not detected. Check whether ngrok is installed and authenticated.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
            "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
    }

    private static string? TryExtractNgrokUrl(string text)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);

                if (doc.RootElement.TryGetProperty("url", out var urlProperty) && urlProperty.ValueKind == JsonValueKind.String)
                {
                    var url = urlProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return url;
                }

                if (doc.RootElement.TryGetProperty("msg", out var msgProperty) && msgProperty.ValueKind == JsonValueKind.String)
                {
                    var match = Regex.Match(msgProperty.GetString() ?? string.Empty, @"https://[A-Za-z0-9.-]+", RegexOptions.IgnoreCase);
                    if (match.Success)
                        return match.Value;
                }
            }
            catch
            {
                var match = Regex.Match(line, @"https://[A-Za-z0-9.-]+", RegexOptions.IgnoreCase);
                if (match.Success && match.Value.Contains("ngrok", StringComparison.OrdinalIgnoreCase))
                    return match.Value;
            }
        }

        return null;
    }

    private static async Task<string> WaitForRegexUrlAsync(Process process, string stdoutPath, string stderrPath, Regex regex, string providerName, CancellationToken cancellationToken)
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

        throw new InvalidOperationException(
            providerName + " started, but the public URL was not detected in logs.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
            "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
    }

    private static async Task PumpLogsAsync(Process? process, string? stdoutPath, string? stderrPath, Action<string> log, string prefix, CancellationToken cancellationToken)
    {
        if (process is null || string.IsNullOrWhiteSpace(stdoutPath) || string.IsNullOrWhiteSpace(stderrPath))
            return;

        long stdoutPos = 0;
        long stderrPos = 0;

        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            foreach (var line in ReadNewLines(stdoutPath, ref stdoutPos))
                log($"[{prefix}] {line}");

            foreach (var line in ReadNewLines(stderrPath, ref stderrPos))
                log($"[{prefix}] {line}");

            try { await Task.Delay(1000, cancellationToken); } catch { break; }
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
