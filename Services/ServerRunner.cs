using System;
using System.Diagnostics;
using System.IO;
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
    private Process? _cloudflaredProcess;
    private string? _caddyStdoutFile;
    private string? _caddyStderrFile;
    private string? _cloudflaredStdoutFile;
    private string? _cloudflaredStderrFile;

    public bool IsRunning =>
        (_caddyProcess is { HasExited: false }) ||
        (_cloudflaredProcess is { HasExited: false });

    public async Task<LaunchSession> StartAsync(
        VideoWorkspace workspace,
        int requestedPort,
        ToolPaths tools,
        Action<string> log,
        CancellationToken cancellationToken,
        string? initialVideoSrc = null)
    {
        await StopAsync(log);

        if (workspace.RootFolder is null)
            throw new InvalidOperationException("Workspace root is not set.");

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

        _cloudflaredStdoutFile = Path.GetTempFileName();
        _cloudflaredStderrFile = Path.GetTempFileName();

        _cloudflaredProcess = StartProcess(
            tools.CloudflaredPath,
            new[] { "tunnel", "--url", localUrl, "--no-autoupdate", "--protocol", "http2" },
            workspace.RootFolder,
            _cloudflaredStdoutFile,
            _cloudflaredStderrFile);

        log("Launching cloudflared...");
        var publicUrl = await WaitForPublicUrlAsync(_cloudflaredProcess, _cloudflaredStdoutFile, _cloudflaredStderrFile, cancellationToken);
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            throw new InvalidOperationException(
                "Could not parse cloudflared public URL.\n\nSTDOUT:\n" + ReadFileSafe(_cloudflaredStdoutFile) +
                "\n\nSTDERR:\n" + ReadFileSafe(_cloudflaredStderrFile));
        }

        var targetUrl = publicUrl.TrimEnd('/') + "/player.html?list=/playlist.json";
        if (!string.IsNullOrWhiteSpace(initialVideoSrc))
        {
            targetUrl += "&src=" + Uri.EscapeDataString(initialVideoSrc);
        }

        if (port != requestedPort)
        {
            log($"Requested port {requestedPort} is unavailable. Using port {port} instead.");
        }

        log("Public URL: " + targetUrl);

        _ = PumpLogsAsync(_caddyProcess, _caddyStdoutFile, _caddyStderrFile, log, "caddy", cancellationToken);
        _ = PumpLogsAsync(_cloudflaredProcess, _cloudflaredStdoutFile, _cloudflaredStderrFile, log, "cloudflared", cancellationToken);

        return new LaunchSession
        {
            Port = port,
            LocalUrl = localUrl,
            PublicUrl = targetUrl,
        };
    }

    public async Task StopAsync(Action<string> log)
    {
        await StopProcessAsync(_cloudflaredProcess, log, "cloudflared");
        await StopProcessAsync(_caddyProcess, log, "caddy");
        _cloudflaredProcess = null;
        _caddyProcess = null;
        CleanupTempFile(ref _caddyStdoutFile);
        CleanupTempFile(ref _caddyStderrFile);
        CleanupTempFile(ref _cloudflaredStdoutFile);
        CleanupTempFile(ref _cloudflaredStderrFile);
    }

    private static Process StartProcess(string fileName, string[] args, string workingDirectory, string stdoutPath, string stderrPath)
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

    private static async Task<string?> WaitForPublicUrlAsync(
        Process process,
        string stdoutPath,
        string stderrPath,
        CancellationToken cancellationToken)
    {
        var regex = new Regex(
            @"https://[a-z0-9-]+\.trycloudflare\.com",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        string? foundUrl = null;
        string? foundHost = null;

        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);

            if (foundUrl is null)
            {
                var match = regex.Match(text);
                if (match.Success)
                {
                    foundUrl = match.Value;
                    try
                    {
                        foundHost = new Uri(foundUrl).Host;
                    }
                    catch
                    {
                        foundHost = null;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(foundHost) && IsHostResolvable(foundHost))
            {
                return foundUrl;
            }

            if (process.HasExited)
                break;

            await Task.Delay(1000, cancellationToken);
        }

        return null;
    }

    private static bool IsHostResolvable(string host)
    {
        try
        {
            var addresses = System.Net.Dns.GetHostAddresses(host);
            return addresses is { Length: > 0 };
        }
        catch
        {
            return false;
        }
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

    private static async Task StopProcessAsync(Process? process, Action<string> log, string name)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                log($"Stopped {name}.");
            }
        }
        catch (Exception ex)
        {
            log($"Failed to stop {name}: {ex.Message}");
        }
        finally
        {
            process.Dispose();
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

    private static void AppendLineSafe(string path, string text)
    {
        try { File.AppendAllText(path, text + Environment.NewLine); } catch { }
    }

    private static void CleanupTempFile(ref string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch { }
        finally { path = null; }
    }
}
