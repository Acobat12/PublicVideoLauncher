using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private string? _caddyConfigFile;

    public bool IsRunning =>
        (_caddyProcess is { HasExited: false }) ||
        (_cloudflaredProcess is { HasExited: false });

    public async Task<LaunchSession> StartAsync(
        VideoWorkspace workspace,
        int requestedPort,
        ToolPaths tools,
        Action<string> log,
        CancellationToken cancellationToken,
        string? initialVideoSrc = null,
        int? translationCallbackPort = null)
    {
        await StopAsync(log);

        if (workspace.RootFolder is null)
            throw new InvalidOperationException("Workspace root is not set.");

        var port = PortHelper.GetFreeTcpPort(requestedPort);
        var localBaseUrl = $"http://127.0.0.1:{port}";
        var localUrl = localBaseUrl + "/";
        var localPlaylistUrl = $"{localBaseUrl}/playlist.json";

        log($"Local server: {localUrl}");

        _caddyStdoutFile = Path.GetTempFileName();
        _caddyStderrFile = Path.GetTempFileName();
        _caddyConfigFile = Path.GetTempFileName();

        File.WriteAllText(_caddyConfigFile, BuildCaddyConfig(workspace.RootFolder, port, translationCallbackPort));

        log("Launching Caddy...");
        _caddyProcess = StartProcess(
            tools.CaddyPath,
            new[]
            {
                "run",
                "--config", _caddyConfigFile,
                "--adapter", "caddyfile"
            },
            workspace.RootFolder,
            _caddyStdoutFile,
            _caddyStderrFile);

        await WaitForLocalEndpointAsync(
            _caddyProcess,
            localPlaylistUrl,
            _caddyStdoutFile,
            _caddyStderrFile,
            log,
            cancellationToken);

        _cloudflaredStdoutFile = Path.GetTempFileName();
        _cloudflaredStderrFile = Path.GetTempFileName();

        log("Launching cloudflared...");
        _cloudflaredProcess = StartProcess(
            tools.CloudflaredPath,
            new[]
            {
                "tunnel",
                "--url", localBaseUrl,
                "--no-autoupdate"
            },
            workspace.RootFolder,
            _cloudflaredStdoutFile,
            _cloudflaredStderrFile);

        var publicBaseUrl = await WaitForTryCloudflareUrlAsync(
            _cloudflaredProcess,
            _cloudflaredStdoutFile,
            _cloudflaredStderrFile,
            cancellationToken);

        log($"Tunnel URL detected: {publicBaseUrl}");

        await WaitForCloudflaredConnectionAsync(
            _cloudflaredProcess,
            _cloudflaredStdoutFile,
            _cloudflaredStderrFile,
            log,
            cancellationToken);

        await TryWaitForPublicHttpAsync(
            _cloudflaredProcess,
            publicBaseUrl,
            "/playlist.json",
            _cloudflaredStdoutFile,
            _cloudflaredStderrFile,
            log,
            cancellationToken);

        var targetUrl = publicBaseUrl.TrimEnd('/') + "/player.html?list=/playlist.json";
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
            translationCallbackPort is int callbackPort
                ? $"  reverse_proxy @translation 127.0.0.1:{callbackPort}"
                : "  respond @translation 503",
            "  file_server",
            "}"
        };

        return string.Join(Environment.NewLine, lines);
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

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AppendLineSafe(stdoutPath, e.Data!);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AppendLineSafe(stderrPath, e.Data!);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
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

    private static async Task<string> WaitForTryCloudflareUrlAsync(
        Process process,
        string? stdoutPath,
        string? stderrPath,
        CancellationToken cancellationToken)
    {
        var regex = new Regex(
            @"https://[a-z0-9-]+\.trycloudflare\.com",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        for (var attempt = 1; attempt <= 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Cloudflared exited before publishing a public URL.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
                    "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
            }

            var text = ReadFileSafe(stdoutPath) + Environment.NewLine + ReadFileSafe(stderrPath);
            var match = regex.Match(text);
            if (match.Success)
                return match.Value;

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "Cloudflared started, but public URL was not detected in logs.\n\nSTDOUT:\n" + ReadFileSafe(stdoutPath) +
            "\n\nSTDERR:\n" + ReadFileSafe(stderrPath));
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
                log($"[{prefix}] {line}");

            foreach (var line in ReadNewLines(stderrPath, ref stderrPos))
                log($"[{prefix}] {line}");

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
        try
        {
            File.AppendAllText(path, text + Environment.NewLine);
        }
        catch
        {
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
