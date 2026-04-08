using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;


public sealed class ToolPaths
{
    public required string CloudflaredPath { get; init; }
    public required string CaddyPath { get; init; }
    public required string FfmpegPath { get; init; }
}

public static class ToolResolver
{
    private static readonly HttpClient Http = CreateHttpClient();


    public static ToolStatus[] InspectAll(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("rootFolder is empty.", nameof(rootFolder));

        return
        [
            CreateToolStatus("ffmpeg", rootFolder),
            CreateToolStatus("caddy", rootFolder),
            CreateToolStatus("cloudflared", rootFolder),
        ];
    }

    public static bool LooksLikeToolIssue(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("caddy", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cloudflared", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("install", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("download", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("GitHub asset", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> InstallToolAsync(
        string toolName,
        string rootFolder,
        Action<string> log,
        Action<ToolInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("rootFolder is empty.", nameof(rootFolder));

        try
        {
            return toolName switch
            {
                "ffmpeg" => await EnsureFfmpegAsync(rootFolder, log, cancellationToken, progress),
                "caddy" => await EnsureCaddyAsync(rootFolder, log, cancellationToken, progress),
                "cloudflared" => await EnsureCloudflaredAsync(rootFolder, log, cancellationToken, progress),
                _ => throw new InvalidOperationException($"Unsupported tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            progress?.Invoke(new ToolInstallProgress
            {
                ToolName = toolName,
                Stage = "Failed: " + ex.Message,
            });
            throw;
        }
    }

    public static async Task InstallMissingAutomaticToolsAsync(
        string rootFolder,
        Action<string> log,
        Action<ToolInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var status in InspectAll(rootFolder).Where(x => !x.IsInstalled && x.SupportsAutomaticInstall))
        {
            await InstallToolAsync(status.Name, rootFolder, log, progress, cancellationToken);
        }
    }

    public static async Task<ToolPaths> EnsureAllAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("rootFolder is empty.", nameof(rootFolder));

        var ffmpegPath = await EnsureFfmpegAsync(rootFolder, log, cancellationToken, progress: null);
        var caddyPath = await EnsureCaddyAsync(rootFolder, log, cancellationToken, progress: null);
        var cloudflaredPath = await EnsureCloudflaredAsync(rootFolder, log, cancellationToken, progress: null);

        return new ToolPaths
        {
            FfmpegPath = ffmpegPath,
            CaddyPath = caddyPath,
            CloudflaredPath = cloudflaredPath,
        };
    }

    private static async Task<string> EnsureFfmpegAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var existing = GetCommandPathIfExists("ffmpeg", rootFolder);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            log($"ffmpeg found: {existing}");
            progress?.Invoke(new ToolInstallProgress { ToolName = "ffmpeg", Stage = "Already installed", BytesReceived = 1, TotalBytes = 1 });
            return existing;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new FileNotFoundException(
                "ffmpeg not found. On Linux/macOS install ffmpeg manually or place it into .tools/ffmpeg.");
        }

        log("ffmpeg not found. Installing portable ffmpeg...");
        progress?.Invoke(new ToolInstallProgress { ToolName = "ffmpeg", Stage = "Preparing download" });
        return await InstallFfmpegPortableAsync(rootFolder, log, cancellationToken, progress);
    }

    private static async Task<string> EnsureCaddyAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var existing = GetCommandPathIfExists("caddy", rootFolder);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            log($"caddy found: {existing}");
            progress?.Invoke(new ToolInstallProgress { ToolName = "caddy", Stage = "Already installed", BytesReceived = 1, TotalBytes = 1 });
            return existing;
        }

        log("caddy not found. Installing portable caddy...");
        progress?.Invoke(new ToolInstallProgress { ToolName = "caddy", Stage = "Preparing download" });
        return await InstallCaddyPortableAsync(rootFolder, log, cancellationToken, progress);
    }

    private static async Task<string> EnsureCloudflaredAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var existing = GetCommandPathIfExists("cloudflared", rootFolder);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            log($"cloudflared found: {existing}");
            progress?.Invoke(new ToolInstallProgress { ToolName = "cloudflared", Stage = "Already installed", BytesReceived = 1, TotalBytes = 1 });
            return existing;
        }

        log("cloudflared not found. Installing portable cloudflared...");
        progress?.Invoke(new ToolInstallProgress { ToolName = "cloudflared", Stage = "Preparing download" });
        return await InstallCloudflaredPortableAsync(rootFolder, log, cancellationToken, progress);
    }

    public static string GetToolsRoot(string rootFolder)
    {
        var toolsRoot = Path.Combine(rootFolder, ".tools");
        Directory.CreateDirectory(toolsRoot);
        return toolsRoot;
    }

    private static ToolStatus CreateToolStatus(string toolName, string rootFolder)
    {
        return toolName switch
        {
            "ffmpeg" => new ToolStatus
            {
                Name = "ffmpeg",
                DisplayName = "FFmpeg",
                Description = "Converts videos into playable MP4 files and prepares cache files for translation mode.",
                DownloadUrl = "https://ffmpeg.org/download.html",
                InstallationHint = GetFfmpegInstallationHint(rootFolder),
                SupportsAutomaticInstall = OperatingSystem.IsWindows(),
                DetectedPath = GetCommandPathIfExists("ffmpeg", rootFolder),
            },
            "caddy" => new ToolStatus
            {
                Name = "caddy",
                DisplayName = "Caddy",
                Description = "Runs the local HTTP server that exposes the generated player and playlist files.",
                DownloadUrl = "https://caddyserver.com/download",
                InstallationHint = GetCaddyInstallationHint(rootFolder),
                SupportsAutomaticInstall = true,
                DetectedPath = GetCommandPathIfExists("caddy", rootFolder),
            },
            "cloudflared" => new ToolStatus
            {
                Name = "cloudflared",
                DisplayName = "Cloudflared",
                Description = "Creates the public tunnel URL so the local video player becomes reachable from the internet.",
                DownloadUrl = "https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/",
                InstallationHint = GetCloudflaredInstallationHint(rootFolder),
                SupportsAutomaticInstall = true,
                DetectedPath = GetCommandPathIfExists("cloudflared", rootFolder),
            },
            _ => throw new InvalidOperationException($"Unsupported tool: {toolName}")
        };
    }

    private static string GetFfmpegInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("ffmpeg", rootFolder);

        if (OperatingSystem.IsWindows())
        {
            return "Windows: the app downloads a portable ffmpeg archive, extracts ffmpeg.exe into .tools/ffmpeg, and shows progress in the Dependencies window. " +
                   $"Manual fallback: copy ffmpeg.exe to: {portablePath}";
        }

        return $"Linux/macOS: automatic ffmpeg install is not built in here. Install ffmpeg with your package manager or place the binary into: {portablePath}. " +
               "Examples: sudo apt install ffmpeg, sudo dnf install ffmpeg, brew install ffmpeg. This window refreshes automatically when ffmpeg appears.";
    }

    private static string GetCaddyInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("caddy", rootFolder);
        return $"The app downloads the latest platform-specific Caddy build for this OS, shows download progress, and extracts the binary into {portablePath}. Manual fallback: install it system-wide or copy the binary there.";
    }

    private static string GetCloudflaredInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("cloudflared", rootFolder);
        return $"The app downloads the latest platform-specific cloudflared build for this OS, shows download progress, and extracts the binary into {portablePath}. Manual fallback: install it system-wide or copy the binary there.";
    }

    private static string GetPortableToolPath(string toolName, string rootFolder)
    {
        var toolsRoot = GetToolsRoot(rootFolder);

        return toolName switch
        {
            "ffmpeg" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "ffmpeg", "ffmpeg.exe")
                : Path.Combine(toolsRoot, "ffmpeg", "ffmpeg"),

            "caddy" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "caddy", "caddy.exe")
                : Path.Combine(toolsRoot, "caddy", "caddy"),

            "cloudflared" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "cloudflared", "cloudflared.exe")
                : Path.Combine(toolsRoot, "cloudflared", "cloudflared"),

            _ => throw new InvalidOperationException($"Unsupported tool: {toolName}")
        };
    }

    private static string? GetCommandPathIfExists(string toolName, string rootFolder)
    {
        var directPortable = GetPortableToolPath(toolName, rootFolder);
        if (File.Exists(directPortable))
            return directPortable;

        var extraPortableCandidates = toolName switch
        {
            "ffmpeg" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "ffmpeg", "bin", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"),
            },
            "caddy" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "caddy", "bin", OperatingSystem.IsWindows() ? "caddy.exe" : "caddy"),
            },
            "cloudflared" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "cloudflared", "bin", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared"),
            },
            _ => Array.Empty<string>()
        };

        foreach (var candidate in extraPortableCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var fromPath = FindInPath(OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName);
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath;

        if (OperatingSystem.IsWindows() && toolName.Equals("cloudflared", StringComparison.OrdinalIgnoreCase))
        {
            var knownCandidates = new List<string>();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                knownCandidates.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "cloudflared.exe"));

                var wingetRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(wingetRoot))
                {
                    try
                    {
                        var pkg = Directory.GetDirectories(wingetRoot)
                            .Select(x => new DirectoryInfo(x))
                            .Where(x =>
                                x.Name.StartsWith("Cloudflare.cloudflared_", StringComparison.OrdinalIgnoreCase) &&
                                !x.Name.Contains(".disabled", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => x.LastWriteTimeUtc)
                            .FirstOrDefault();

                        if (pkg is not null)
                        {
                            knownCandidates.Add(Path.Combine(pkg.FullName, "cloudflared.exe"));
                        }
                    }
                    catch
                    {
                    }
                }
            }

            foreach (var candidate in knownCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? FindInPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
            }
        }

        return null;
    }

    private static async Task<string> InstallFfmpegPortableAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var toolsRoot = GetToolsRoot(rootFolder);
        var dstDir = Path.Combine(toolsRoot, "ffmpeg");
        Directory.CreateDirectory(dstDir);

        var dstExe = GetPortableToolPath("ffmpeg", rootFolder);

        var zipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"ffmpeg_extract_{Guid.NewGuid():N}");

        const string ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        try
        {
            log("Downloading ffmpeg...");
            await DownloadFileAsync(ffmpegUrl, zipPath, cancellationToken, (received, total) =>
                ReportProgress("ffmpeg", "Downloading", progress, received, total));

            ReportProgress("ffmpeg", "Extracting archive", progress);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var foundExe = Directory.GetFiles(extractDir, fileName, SearchOption.AllDirectories).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(foundExe))
                throw new InvalidOperationException("ffmpeg executable not found inside extracted archive.");

            ReportProgress("ffmpeg", "Copying executable", progress);
            File.Copy(foundExe, dstExe, true);

            if (!OperatingSystem.IsWindows())
                await EnsureExecutableAsync(dstExe, cancellationToken);

            if (!File.Exists(dstExe))
                throw new InvalidOperationException("ffmpeg install failed. Open https://ffmpeg.org/download.html or place ffmpeg into .tools/ffmpeg.");

            ReportProgress("ffmpeg", "Installed", progress, 1, 1);
            log($"ffmpeg installed to: {dstExe}");
            return dstExe;
        }
        finally
        {
            SafeDeleteFile(zipPath);
            SafeDeleteDirectory(extractDir);
        }
    }

    private static async Task<string> InstallCloudflaredPortableAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var toolsRoot = GetToolsRoot(rootFolder);
        var dstDir = Path.Combine(toolsRoot, "cloudflared");
        Directory.CreateDirectory(dstDir);

        var dstExe = GetPortableToolPath("cloudflared", rootFolder);
        var tempPath = Path.Combine(Path.GetTempPath(), $"cloudflared_{Guid.NewGuid():N}");
        var extractDir = Path.Combine(Path.GetTempPath(), $"cloudflared_extract_{Guid.NewGuid():N}");

        try
        {
            var (assetUrl, assetName) = await GetGitHubAssetAsync(
                "https://api.github.com/repos/cloudflare/cloudflared/releases/latest",
                GetCloudflaredAssetPattern(),
                cancellationToken);

            log($"Downloading cloudflared asset: {assetName}");
            Directory.CreateDirectory(extractDir);

            var downloadPath = Path.HasExtension(assetName)
                ? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{assetName}")
                : tempPath;

            await DownloadFileAsync(assetUrl, downloadPath, cancellationToken, (received, total) =>
                ReportProgress("cloudflared", "Downloading", progress, received, total));

            if (assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress("cloudflared", "Extracting archive", progress);
                ExtractTarGz(downloadPath, extractDir);

                var fileName = OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared";
                var found = Directory.GetFiles(extractDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(found))
                    throw new InvalidOperationException("cloudflared executable not found in extracted archive.");

                ReportProgress("cloudflared", "Copying executable", progress);
                File.Copy(found, dstExe, true);
                SafeDeleteFile(downloadPath);
            }
            else
            {
                ReportProgress("cloudflared", "Saving executable", progress);
                File.Copy(downloadPath, dstExe, true);
                SafeDeleteFile(downloadPath);
            }

            if (!OperatingSystem.IsWindows())
                await EnsureExecutableAsync(dstExe, cancellationToken);

            if (!File.Exists(dstExe))
                throw new InvalidOperationException("cloudflared install failed. Open the Cloudflare downloads page or place cloudflared into .tools/cloudflared.");

            ReportProgress("cloudflared", "Installed", progress, 1, 1);
            log($"cloudflared installed to: {dstExe}");
            return dstExe;
        }
        finally
        {
            SafeDeleteDirectory(extractDir);
        }
    }

    private static async Task<string> InstallCaddyPortableAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var toolsRoot = GetToolsRoot(rootFolder);
        var dstDir = Path.Combine(toolsRoot, "caddy");
        Directory.CreateDirectory(dstDir);

        var dstExe = GetPortableToolPath("caddy", rootFolder);
        var extractDir = Path.Combine(Path.GetTempPath(), $"caddy_extract_{Guid.NewGuid():N}");

        try
        {
            var (assetUrl, assetName) = await GetGitHubAssetAsync(
                "https://api.github.com/repos/caddyserver/caddy/releases/latest",
                GetCaddyAssetPattern(),
                cancellationToken);

            log($"Downloading caddy asset: {assetName}");
            Directory.CreateDirectory(extractDir);

            var downloadPath = Path.Combine(Path.GetTempPath(), assetName);
            await DownloadFileAsync(assetUrl, downloadPath, cancellationToken, (received, total) =>
                ReportProgress("caddy", "Downloading", progress, received, total));

            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress("caddy", "Extracting archive", progress);
                ZipFile.ExtractToDirectory(downloadPath, extractDir, true);
            }
            else if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                     assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress("caddy", "Extracting archive", progress);
                ExtractTarGz(downloadPath, extractDir);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported caddy archive type: {assetName}");
            }

            var fileName = OperatingSystem.IsWindows() ? "caddy.exe" : "caddy";
            var found = Directory.GetFiles(extractDir, fileName, SearchOption.AllDirectories).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(found))
                throw new InvalidOperationException("caddy executable not found in extracted archive.");

            ReportProgress("caddy", "Copying executable", progress);
            File.Copy(found, dstExe, true);

            if (!OperatingSystem.IsWindows())
                await EnsureExecutableAsync(dstExe, cancellationToken);

            if (!File.Exists(dstExe))
                throw new InvalidOperationException("caddy install failed. Open the Caddy download page or place caddy into .tools/caddy.");

            ReportProgress("caddy", "Installed", progress, 1, 1);
            log($"caddy installed to: {dstExe}");
            return dstExe;
        }
        finally
        {
            SafeDeleteDirectory(extractDir);
        }
    }

    private static string GetCloudflaredAssetPattern()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsArm64()
                ? "^cloudflared-windows-arm64\\.exe$"
                : "^cloudflared-windows-amd64\\.exe$";
        }

        if (OperatingSystem.IsLinux())
        {
            return IsArm64()
                ? "^cloudflared-linux-arm64$"
                : "^cloudflared-linux-amd64$";
        }

        if (OperatingSystem.IsMacOS())
        {
            return IsArm64()
                ? "^cloudflared-darwin-arm64\\.tgz$"
                : "^cloudflared-darwin-amd64\\.tgz$";
        }

        throw new PlatformNotSupportedException("Unsupported platform for cloudflared.");
    }

    private static string GetCaddyAssetPattern()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsArm64()
                ? "^caddy_.*_windows_arm64\\.zip$"
                : "^caddy_.*_windows_amd64\\.zip$";
        }

        if (OperatingSystem.IsLinux())
        {
            return IsArm64()
                ? "^caddy_.*_linux_arm64\\.tar\\.gz$"
                : "^caddy_.*_linux_amd64\\.tar\\.gz$";
        }

        if (OperatingSystem.IsMacOS())
        {
            return IsArm64()
                ? "^caddy_.*_(mac|darwin)_arm64\\.tar\\.gz$"
                : "^caddy_.*_(mac|darwin)_amd64\\.tar\\.gz$";
        }

        throw new PlatformNotSupportedException("Unsupported platform for caddy.");
    }

    private static bool IsArm64() =>
        RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ||
        RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    private static async Task<(string AssetUrl, string AssetName)> GetGitHubAssetAsync(
        string apiUrl,
        string assetPattern,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub release response does not contain assets.");
        }

        var regex = new System.Text.RegularExpressions.Regex(
            assetPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!regex.IsMatch(name))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(url))
                continue;

            return (url, name);
        }

        throw new InvalidOperationException($"GitHub asset not found for pattern: {assetPattern}");
    }

    private static async Task DownloadFileAsync(
        string url,
        string outputPath,
        CancellationToken cancellationToken,
        Action<long, long?>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(outputPath);

        var buffer = new byte[81920];
        long totalRead = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            progress?.Invoke(totalRead, totalBytes);
        }
    }

    private static void ReportProgress(
        string toolName,
        string stage,
        Action<ToolInstallProgress>? progress,
        long bytesReceived = 0,
        long? totalBytes = null)
    {
        progress?.Invoke(new ToolInstallProgress
        {
            ToolName = toolName,
            Stage = stage,
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes,
        });
    }

    private static void ExtractTarGz(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzipStream, destinationDirectory, overwriteFiles: true);
    }

    private static async Task EnsureExecutableAsync(string filePath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return;

        var chmod = new ProcessStartInfo
        {
            FileName = "chmod",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        chmod.ArgumentList.Add("+x");
        chmod.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = chmod };
        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"chmod failed for {filePath}: {error}");
        }
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PublicVideoLauncher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.Timeout = TimeSpan.FromMinutes(20);
        return client;
    }
}