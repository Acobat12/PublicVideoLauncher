using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class CloudflareNamedTunnelPreparation
{
    public required string TunnelId { get; init; }
    public required string TunnelName { get; init; }
    public required string Hostname { get; init; }
    public required string ConfigPath { get; init; }
    public string PublicBaseUrl => "https://" + Hostname.Trim();
}

public static class CloudflareNamedTunnelService
{
    public static bool HasOriginCertificate() => File.Exists(GetOriginCertificatePath());

    public static string GetOriginCertificatePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cloudflared", "cert.pem");
    }

    public static async Task LoginAsync(string cloudflaredPath, string workingDirectory, Action<string> log, CancellationToken cancellationToken)
    {
        log("Starting cloudflared tunnel login...");
        var result = await RunProcessCaptureAsync(
            cloudflaredPath,
            new[] { "tunnel", "login" },
            workingDirectory,
            cancellationToken);

        if (result.ExitCode != 0 || !HasOriginCertificate())
        {
            throw new InvalidOperationException(
                "cloudflared login did not complete successfully.\n\nSTDOUT:\n" + result.Stdout +
                "\n\nSTDERR:\n" + result.Stderr);
        }

        log("Cloudflare login completed.");
    }

    public static async Task<CloudflareNamedTunnelPreparation> PrepareAsync(
        string rootFolder,
        string cloudflaredPath,
        string workingDirectory,
        string tunnelName,
        string hostname,
        string localUrl,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!HasOriginCertificate())
            throw new InvalidOperationException("Cloudflare login is required first. Open Setup... and use Login to Cloudflare.");

        var normalizedName = (tunnelName ?? string.Empty).Trim();
        var normalizedHostname = (hostname ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Cloudflare Name requires a tunnel name.");
        if (string.IsNullOrWhiteSpace(normalizedHostname))
            throw new InvalidOperationException("Cloudflare Name requires a public hostname.");

        var tunnelId = await GetTunnelIdByNameAsync(cloudflaredPath, workingDirectory, normalizedName, cancellationToken);
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            log("Creating named Cloudflare tunnel: " + normalizedName);
            var create = await RunProcessCaptureAsync(
                cloudflaredPath,
                new[] { "tunnel", "create", normalizedName },
                workingDirectory,
                cancellationToken);

            if (create.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "cloudflared tunnel create failed.\n\nSTDOUT:\n" + create.Stdout +
                    "\n\nSTDERR:\n" + create.Stderr);
            }

            tunnelId = await GetTunnelIdByNameAsync(cloudflaredPath, workingDirectory, normalizedName, cancellationToken);
            if (string.IsNullOrWhiteSpace(tunnelId))
                throw new InvalidOperationException("Tunnel was created but its ID could not be resolved.");
        }

        log("Ensuring Cloudflare DNS route: " + normalizedHostname);
        var route = await RunProcessCaptureAsync(
            cloudflaredPath,
            new[] { "tunnel", "route", "dns", "--overwrite-dns", normalizedName, normalizedHostname },
            workingDirectory,
            cancellationToken);

        if (route.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "cloudflared tunnel route dns failed.\n\nSTDOUT:\n" + route.Stdout +
                "\n\nSTDERR:\n" + route.Stderr);
        }

        var credentialsFile = Path.Combine(Path.GetDirectoryName(GetOriginCertificatePath())!, tunnelId + ".json");
        if (!File.Exists(credentialsFile))
            throw new InvalidOperationException("Tunnel credentials file was not found: " + credentialsFile);

        var configPath = WriteConfigFile(rootFolder, tunnelId, normalizedHostname, localUrl, credentialsFile);
        return new CloudflareNamedTunnelPreparation
        {
            TunnelId = tunnelId,
            TunnelName = normalizedName,
            Hostname = normalizedHostname,
            ConfigPath = configPath,
        };
    }

    private static async Task<string?> GetTunnelIdByNameAsync(string cloudflaredPath, string workingDirectory, string tunnelName, CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            cloudflaredPath,
            new[] { "tunnel", "list", "-o", "json", "-n", tunnelName },
            workingDirectory,
            cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return null;

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            var first = document.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return null;

            if (first.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                return idElement.GetString();
        }
        catch
        {
        }

        return null;
    }

    private static string WriteConfigFile(string rootFolder, string tunnelId, string hostname, string localUrl, string credentialsFile)
    {
        var folder = Path.Combine(rootFolder, ".tools", "cloudflare-name");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.yml");

        var yaml = string.Join(Environment.NewLine, new[]
        {
            $"tunnel: {tunnelId}",
            $"credentials-file: {ToYamlPath(credentialsFile)}",
            "ingress:",
            $"  - hostname: {hostname}",
            $"    service: {localUrl.TrimEnd('/')}",
            "  - service: http_status:404",
        });

        File.WriteAllText(path, yaml, Encoding.UTF8);
        return path;
    }

    private static string ToYamlPath(string path) => "\"" + path.Replace("\\", "/") + "\"";

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessCaptureAsync(
        string fileName,
        string[] args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
