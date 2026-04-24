using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class ProcessCleanupService
{
    private static readonly HashSet<string> DirectTunnelProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "caddy",
        "cloudflared",
        "ngrok",
        "zrok",
        "xtunnel",
        "xTunnel",
        "vk-tunnel",
        "lt",
        "localtunnel"
    };

    private static readonly HashSet<string> ConditionalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node",
        "nodejs",
        "npx",
        "npx.cmd",
        "ssh",
        "ssh.exe",
        "tailscale",
        "tailscale.exe"
    };

    private static readonly string[] TunnelCommandMarkers =
    [
        "localtunnel",
        "@vkontakte/vk-tunnel",
        "vk-tunnel",
        "localhost.run",
        "nokey@localhost.run",
        "cloudflared tunnel",
        "ngrok http",
        "zrok share",
        "xtunnel",
        "xTunnel"
    ];

    public static IReadOnlyList<int> BuildDefaultManagedPorts(int? translationCallbackPort = null)
    {
        var ports = Enumerable.Range(8000, 51).ToList();
        if (translationCallbackPort is int callbackPort && callbackPort > 0)
            ports.Add(callbackPort);

        return ports.Distinct().ToArray();
    }

    public async Task CleanupAsync(
        string? workspaceRoot,
        IEnumerable<int> managedPorts,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = NormalizePath(workspaceRoot);
        var portSet = managedPorts.Where(x => x > 0).Distinct().ToHashSet();
        var listeningPids = await TryGetListeningPidsAsync(portSet, cancellationToken);
        var killed = 0;

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.Id == Environment.ProcessId)
            {
                process.Dispose();
                continue;
            }

            string processName;
            try
            {
                processName = process.ProcessName;
            }
            catch
            {
                process.Dispose();
                continue;
            }

            var isListeningOnManagedPort = listeningPids.Contains(process.Id);
            var executablePath = TryGetMainModulePath(process);
            string? commandLine = null;

            if (ShouldFetchCommandLine(processName, executablePath, normalizedRoot, isListeningOnManagedPort))
                commandLine = await TryGetCommandLineAsync(process.Id, cancellationToken);

            if (!ShouldTerminateProcessForCleanup(processName, commandLine, executablePath, normalizedRoot, isListeningOnManagedPort))
            {
                process.Dispose();
                continue;
            }

            try
            {
                if (!process.HasExited)
                {
                    log($"Killing stale tunnel process {processName} (PID {process.Id}).");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                    killed++;
                }
            }
            catch (OperationCanceledException)
            {
                process.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                log($"Cleanup warning for {processName} (PID {process.Id}): {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        log(killed == 0
            ? "Tunnel cleanup finished. No stale tunnel processes found."
            : $"Tunnel cleanup finished. Killed stale process count: {killed}.");
    }

    public static bool ShouldTerminateProcessForCleanup(
        string processName,
        string? commandLine,
        string? executablePath,
        string? workspaceRoot,
        bool isListeningOnManagedPort)
    {
        var cleanName = NormalizeProcessName(processName);

        if (DirectTunnelProcessNames.Contains(cleanName))
            return true;

        if (IsPathUnderWorkspace(executablePath, workspaceRoot))
            return true;

        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            if (cleanName.Equals("tailscale", StringComparison.OrdinalIgnoreCase) ||
                cleanName.Equals("tailscale.exe", StringComparison.OrdinalIgnoreCase))
            {
                return commandLine.Contains(" funnel", StringComparison.OrdinalIgnoreCase) ||
                       commandLine.Contains("funnel --", StringComparison.OrdinalIgnoreCase);
            }

            if (TunnelCommandMarkers.Any(marker => commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        if (!isListeningOnManagedPort)
            return false;

        return DirectTunnelProcessNames.Contains(cleanName) ||
               (ConditionalProcessNames.Contains(cleanName) &&
                !string.IsNullOrWhiteSpace(commandLine) &&
                TunnelCommandMarkers.Any(marker => commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ShouldFetchCommandLine(
        string processName,
        string? executablePath,
        string? workspaceRoot,
        bool isListeningOnManagedPort)
    {
        var cleanName = NormalizeProcessName(processName);
        return isListeningOnManagedPort ||
               ConditionalProcessNames.Contains(cleanName) ||
               IsPathUnderWorkspace(executablePath, workspaceRoot);
    }

    private static string NormalizeProcessName(string processName)
    {
        var clean = Path.GetFileName(processName.Trim());
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];

        return clean;
    }

    private static bool IsPathUnderWorkspace(string? path, string? workspaceRoot)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(workspaceRoot);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedRoot))
            return false;

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGetCommandLineAsync(int pid, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var powershell = FindPowerShell();
            if (string.IsNullOrWhiteSpace(powershell))
                return null;

            return await RunCaptureAsync(
                powershell,
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"(Get-CimInstance Win32_Process -Filter \"ProcessId = {pid}\").CommandLine"
                ],
                cancellationToken);
        }

        return await RunCaptureAsync(
            "/bin/ps",
            ["-p", pid.ToString(CultureInfo.InvariantCulture), "-o", "command="],
            cancellationToken);
    }

    private static string? FindPowerShell()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var windowsPowerShell = string.IsNullOrWhiteSpace(systemRoot)
            ? null
            : Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

        if (!string.IsNullOrWhiteSpace(windowsPowerShell) && File.Exists(windowsPowerShell))
            return windowsPowerShell;

        return "powershell.exe";
    }

    private static async Task<HashSet<int>> TryGetListeningPidsAsync(HashSet<int> ports, CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
            return new HashSet<int>();

        try
        {
            return OperatingSystem.IsWindows()
                ? await TryGetListeningPidsWithNetstatAsync(ports, cancellationToken)
                : await TryGetListeningPidsWithLsofAsync(ports, cancellationToken);
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private static async Task<HashSet<int>> TryGetListeningPidsWithNetstatAsync(HashSet<int> ports, CancellationToken cancellationToken)
    {
        var output = await RunCaptureAsync("netstat", ["-ano", "-p", "tcp"], cancellationToken);
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(output))
            return result;

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = Regex.Split(line.Trim(), @"\s+");
            if (parts.Length < 5)
                continue;

            var localEndpoint = parts[1];
            if (!TryGetEndpointPort(localEndpoint, out var port) || !ports.Contains(port))
                continue;

            if (int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                result.Add(pid);
        }

        return result;
    }

    private static async Task<HashSet<int>> TryGetListeningPidsWithLsofAsync(HashSet<int> ports, CancellationToken cancellationToken)
    {
        var result = new HashSet<int>();
        foreach (var port in ports)
        {
            var output = await RunCaptureAsync(
                "lsof",
                ["-nP", $"-iTCP:{port}", "-sTCP:LISTEN", "-t"],
                cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
                continue;

            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                    result.Add(pid);
            }
        }

        return result;
    }

    private static bool TryGetEndpointPort(string endpoint, out int port)
    {
        port = 0;
        var index = endpoint.LastIndexOf(':');
        if (index < 0 || index == endpoint.Length - 1)
            return false;

        return int.TryParse(endpoint[(index + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
    }

    private static async Task<string?> RunCaptureAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return (await stdoutTask).Trim();
        }
        catch
        {
            return null;
        }
    }
}
