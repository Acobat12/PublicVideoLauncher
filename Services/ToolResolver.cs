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
    public string? CloudflaredPath { get; init; }
    public string? NgrokPath { get; init; }
    public string? SshPath { get; init; }
    public string? TailscalePath { get; init; }
    public string? ZrokPath { get; init; }
    public string? LocalTunnelPath { get; init; }
    public string? XTunnelPath { get; init; }
    public string? VkTunnelPath { get; init; }
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
            CreateToolStatus("ngrok", rootFolder),
            CreateToolStatus("ssh", rootFolder),
            CreateToolStatus("tailscale", rootFolder),
            CreateToolStatus("zrok", rootFolder),
            CreateToolStatus("localtunnel", rootFolder),
            CreateToolStatus("xtunnel", rootFolder),
            CreateToolStatus("vk-tunnel", rootFolder),
        ];
    }


    public static string[] GetRequiredToolNames(string tunnelProvider)
    {
        var normalized = (tunnelProvider ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "cloudflare tunnel" or "cloudflared" => ["ffmpeg", "caddy", "cloudflared"],
            "cloudflare name" or "cloudflare-name" => ["ffmpeg", "caddy", "cloudflared"],
            "ngrok" => ["ffmpeg", "caddy", "ngrok"],
            "localhost.run" or "localhostrun" => ["ffmpeg", "caddy", "ssh"],
            "tailscale funnel" or "tailscale-funnel" => ["ffmpeg", "caddy", "tailscale"],
            "zrok" => ["ffmpeg", "caddy", "zrok"],
            "localtunnel" => ["ffmpeg", "caddy", "localtunnel"],
            "xtunnel" => ["ffmpeg", "caddy", "xtunnel"],
            "vk tunnel" or "vk-tunnel" => ["ffmpeg", "caddy", "vk-tunnel"],
            _ => ["ffmpeg", "caddy"],
        };
    }

    public static bool LooksLikeToolIssue(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("caddy", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cloudflared", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ngrok", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("localhost.run", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tailscale", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ssh", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("zrok", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("localtunnel", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("lt ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("xtunnel", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("vk-tunnel", StringComparison.OrdinalIgnoreCase) ||
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
                "zrok" => await EnsureZrokAsync(rootFolder, log, cancellationToken, progress),
                _ => throw new InvalidOperationException($"Unsupported automatic install for tool: {toolName}")
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

    public static async Task<ToolPaths> EnsureRequiredAsync(
        string rootFolder,
        string tunnelProvider,
        TunnelLaunchSettings settings,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("rootFolder is empty.", nameof(rootFolder));

        var ffmpegPath = await EnsureFfmpegAsync(rootFolder, log, cancellationToken, progress: null);
        var caddyPath = await EnsureCaddyAsync(rootFolder, log, cancellationToken, progress: null);

        string? cloudflaredPath = null;
        string? ngrokPath = null;
        string? sshPath = null;
        string? tailscalePath = null;
        string? zrokPath = null;
        string? localTunnelPath = null;
        string? xTunnelPath = null;
        string? vkTunnelPath = null;

        switch ((tunnelProvider ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "cloudflared":
            case "cloudflare-name":
                cloudflaredPath = await EnsureCloudflaredAsync(rootFolder, log, cancellationToken, progress: null);
                break;
            case "ngrok":
                ngrokPath = ResolveSelectedProviderPath(
                    "ngrok",
                    rootFolder,
                    settings,
                    "ngrok is required for the selected tunnel provider. Install ngrok or point the app to ngrok.exe, then try again.");
                break;
            case "localhost.run":
                sshPath = ResolveSelectedProviderPath(
                    "ssh",
                    rootFolder,
                    settings,
                    "OpenSSH client is required for localhost.run. Install ssh / OpenSSH client or point the app to ssh.exe, then try again.");
                break;
            case "tailscale-funnel":
                tailscalePath = ResolveSelectedProviderPath(
                    "tailscale",
                    rootFolder,
                    settings,
                    "Tailscale CLI is required for Tailscale Funnel. Install tailscale and sign in, or point the app to tailscale.exe, then try again.");
                break;
            case "zrok":
                zrokPath = ResolveCustomPathIfProvided(settings) ?? await EnsureZrokAsync(rootFolder, log, cancellationToken, progress: null);
                break;
            case "localtunnel":
                localTunnelPath = ResolveSelectedProviderPath(
                    "localtunnel",
                    rootFolder,
                    settings,
                    "LocalTunnel is required for the selected tunnel provider. Install localtunnel globally (lt/localtunnel), point the app to lt / localtunnel / npx, or make npx available in PATH, then try again.");
                break;
            case "xtunnel":
                xTunnelPath = ResolveSelectedProviderPath(
                    "xtunnel",
                    rootFolder,
                    settings,
                    "xTunnel is required for the selected tunnel provider. Install xTunnel or point the app to xTunnel.exe, then try again.");
                break;
            case "vk-tunnel":
                vkTunnelPath = ResolveSelectedProviderPath(
                    "vk-tunnel",
                    rootFolder,
                    settings,
                    "VK Tunnel is required for the selected tunnel provider. Install @vkontakte/vk-tunnel globally, point the app to vk-tunnel.cmd / vk-tunnel.js, or make npx available in PATH, then try again.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported tunnel provider: {tunnelProvider}");
        }

        return new ToolPaths
        {
            FfmpegPath = ffmpegPath,
            CaddyPath = caddyPath,
            CloudflaredPath = cloudflaredPath,
            NgrokPath = ngrokPath,
            SshPath = sshPath,
            TailscalePath = tailscalePath,
            ZrokPath = zrokPath,
            LocalTunnelPath = localTunnelPath,
            XTunnelPath = xTunnelPath,
            VkTunnelPath = vkTunnelPath,
        };
    }

    public static Task<string> EnsureFfmpegOnlyAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("rootFolder is empty.", nameof(rootFolder));

        return EnsureFfmpegAsync(rootFolder, log, cancellationToken, progress: null);
    }

    private static string RequireInstalledTool(string toolName, string rootFolder, string message)
    {
        var path = GetCommandPathIfExists(toolName, rootFolder);
        if (string.IsNullOrWhiteSpace(path))
            throw new FileNotFoundException(message);

        return path;
    }

    private static string? RequireInstalledToolOrNull(string toolName, string rootFolder)
    {
        var path = GetCommandPathIfExists(toolName, rootFolder);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? ResolveCustomPathIfProvided(TunnelLaunchSettings settings)
    {
        var customPath = (settings.CustomCliPath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(customPath))
            return null;

        if (!File.Exists(customPath))
            throw new FileNotFoundException($"The custom tunnel CLI path does not exist: {customPath}");

        return customPath;
    }

    private static string ResolveSelectedProviderPath(string toolName, string rootFolder, TunnelLaunchSettings settings, string message)
    {
        var customPath = ResolveCustomPathIfProvided(settings);
        if (!string.IsNullOrWhiteSpace(customPath))
            return customPath;

        if (toolName.Equals("vk-tunnel", StringComparison.OrdinalIgnoreCase))
        {
            var explicitVkTool = RequireInstalledToolOrNull(toolName, rootFolder);
            if (!string.IsNullOrWhiteSpace(explicitVkTool))
                return explicitVkTool;

            var npxPath = FindNpxPath(rootFolder);
            if (!string.IsNullOrWhiteSpace(npxPath))
                return npxPath;
        }

        if (toolName.Equals("localtunnel", StringComparison.OrdinalIgnoreCase))
        {
            var explicitLocalTunnel = RequireInstalledToolOrNull(toolName, rootFolder);
            if (!string.IsNullOrWhiteSpace(explicitLocalTunnel))
                return explicitLocalTunnel;

            var npxPath = FindNpxPath(rootFolder);
            if (!string.IsNullOrWhiteSpace(npxPath))
                return npxPath;
        }

        return RequireInstalledTool(toolName, rootFolder, message);
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

    private static Task<string> EnsureZrokAsync(
        string rootFolder,
        Action<string> log,
        CancellationToken cancellationToken,
        Action<ToolInstallProgress>? progress)
    {
        var existing = GetCommandPathIfExists("zrok", rootFolder);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            log($"zrok found: {existing}");
            progress?.Invoke(new ToolInstallProgress { ToolName = "zrok", Stage = "Already installed", BytesReceived = 1, TotalBytes = 1 });
            return Task.FromResult(existing);
        }

        return Task.FromException<string>(new FileNotFoundException(
            $"zrok not found. Install zrok manually or place it into {GetPortableToolPath("zrok", rootFolder)}."));
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
                Description = "Creates a public Cloudflare quick tunnel URL for the local player.",
                DownloadUrl = "https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/",
                InstallationHint = GetCloudflaredInstallationHint(rootFolder),
                SupportsAutomaticInstall = true,
                DetectedPath = GetCommandPathIfExists("cloudflared", rootFolder),
            },
            "ngrok" => new ToolStatus
            {
                Name = "ngrok",
                DisplayName = "ngrok",
                Description = "Creates a public ngrok URL for the local player. Usually requires an ngrok authtoken in Tunnel auth on first use.",
                DownloadUrl = "https://ngrok.com/docs/agent",
                InstallationHint = GetNgrokInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("ngrok", rootFolder),
            },
            "ssh" => new ToolStatus
            {
                Name = "ssh",
                DisplayName = "OpenSSH Client",
                Description = "Used by localhost.run. The app starts an SSH reverse tunnel to get a public HTTPS URL.",
                DownloadUrl = "https://localhost.run/docs/cli/",
                InstallationHint = GetSshInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("ssh", rootFolder),
            },
            "tailscale" => new ToolStatus
            {
                Name = "tailscale",
                DisplayName = "Tailscale",
                Description = "Used for Tailscale Funnel. The machine must already have Tailscale installed, logged in, and allowed to use Funnel.",
                DownloadUrl = "https://tailscale.com/kb/1223/funnel",
                InstallationHint = GetTailscaleInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("tailscale", rootFolder),
            },
            "zrok" => new ToolStatus
            {
                Name = "zrok",
                DisplayName = "zrok",
                Description = "Creates a public zrok share URL for the local player after the machine is enabled with a zrok account token.",
                DownloadUrl = "https://docs.zrok.io/docs/getting-started/",
                InstallationHint = GetZrokInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("zrok", rootFolder),
            },
            "localtunnel" => new ToolStatus
            {
                Name = "localtunnel",
                DisplayName = "LocalTunnel",
                Description = "Creates a public LocalTunnel URL for the local player. Some visitors may still see the service password/consent page.",
                DownloadUrl = "https://github.com/localtunnel/localtunnel",
                InstallationHint = GetLocalTunnelInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("localtunnel", rootFolder) ?? FindNpxPath(rootFolder),
            },
            "xtunnel" => new ToolStatus
            {
                Name = "xtunnel",
                DisplayName = "xTunnel",
                Description = "Creates a public xTunnel URL for the local player after client activation.",
                DownloadUrl = "https://xtunnel.ru/",
                InstallationHint = GetXTunnelInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = GetCommandPathIfExists("xtunnel", rootFolder),
            },
            "vk-tunnel" => new ToolStatus
            {
                Name = "vk-tunnel",
                DisplayName = "VK Tunnel",
                Description = "Creates a public VK Tunnel URL for the local player when the provider is available.",
                DownloadUrl = "https://dev.vk.com/ru/libraries/tunnel",
                InstallationHint = GetVkTunnelInstallationHint(rootFolder),
                SupportsAutomaticInstall = false,
                DetectedPath = RequireInstalledToolOrNull("vk-tunnel", rootFolder),
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

    private static string GetZrokInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("zrok", rootFolder);
        return $"""
EN:
  • Install zrok from the official docs or GitHub releases.
  • Put zrok / zrok.exe into:
    {portablePath}
    or point the app to it through the CLI path box.
  • First-time setup needs a zrok account token. Paste that token into Tunnel auth and the app will try 'zrok enable <TOKEN>' before creating the public share.
  • After the machine is already enabled, Tunnel auth can be left empty and the app will run 'zrok share public 127.0.0.1:<PORT>'.

RU:
  • Установите zrok с официальной документации или GitHub releases.
  • Поместите zrok / zrok.exe в:
    {portablePath}
    или укажите путь через поле CLI path.
  • Для первого запуска нужен account token zrok. Вставьте его в Tunnel auth, и программа попробует выполнить 'zrok enable <TOKEN>' перед созданием публичной ссылки.
  • Если эта машина уже была включена в zrok, поле Tunnel auth можно оставить пустым: программа запустит 'zrok share public 127.0.0.1:<ПОРТ>'.
""";
    }

    private static string GetNgrokInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("ngrok", rootFolder);
        return $"""
EN:
  • Install ngrok from the official download page.
  • Put ngrok / ngrok.exe into:
    {portablePath}
    or point the app to it through the CLI path box.
  • On first use, paste your ngrok authtoken into Tunnel auth. The app will run 'ngrok config add-authtoken <TOKEN>' before starting the tunnel.
  • After ngrok is already configured on this machine, Tunnel auth can be left empty.

RU:
  • Установите ngrok с официальной страницы загрузки.
  • Поместите ngrok / ngrok.exe в:
    {portablePath}
    или укажите путь через поле CLI path.
  • При первом запуске вставьте authtoken ngrok в Tunnel auth. Программа попробует выполнить 'ngrok config add-authtoken <TOKEN>' перед стартом туннеля.
  • Если ngrok уже настроен на этой машине, поле Tunnel auth можно оставить пустым.
""";
    }

    private static string GetSshInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("ssh", rootFolder);
        return $"""
EN:
  • localhost.run uses the standard OpenSSH client.
  • On modern Windows, ssh.exe is often already available. If not, install the optional OpenSSH Client Windows feature or point the app to ssh.exe.
  • You can also place ssh.exe into:
    {portablePath}
  • The app uses a free localhost.run tunnel via SSH: 'ssh -R 80:localhost:<PORT> nokey@localhost.run'.

RU:
  • localhost.run использует обычный OpenSSH client.
  • В современных Windows ssh.exe часто уже установлен. Если его нет, установите optional feature OpenSSH Client или укажите путь к ssh.exe.
  • Также можно положить ssh.exe в:
    {portablePath}
  • Программа запускает бесплатный localhost.run туннель через SSH: 'ssh -R 80:localhost:<PORT> nokey@localhost.run'.
""";
    }

    private static string GetTailscaleInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("tailscale", rootFolder);
        return $"""
EN:
  • Install the Tailscale client and sign in on this machine.
  • Make sure Funnel is enabled for your tailnet and this device is allowed to use it.
  • Put tailscale / tailscale.exe into:
    {portablePath}
    or point the app to it through the CLI path box.
  • The app starts Funnel with the local Caddy server as the backend, then waits for the public ts.net URL.

RU:
  • Установите клиент Tailscale и войдите в аккаунт на этой машине.
  • Убедитесь, что Funnel включён для вашего tailnet и это устройство имеет право его использовать.
  • Поместите tailscale / tailscale.exe в:
    {portablePath}
    или укажите путь через поле CLI path.
  • Программа запускает Funnel с локальным Caddy как backend и ждёт публичный URL вида ts.net.
""";
    }

    private static string GetLocalTunnelInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("localtunnel", rootFolder);
        return $"""
EN:
  • Install LocalTunnel globally with Node.js: npm install -g localtunnel
  • The app can use lt, localtunnel, or npx localtunnel.
  • You can also place lt / localtunnel into:
    {portablePath}
    or point the app to it through the CLI path box.
  • Public visitors may see LocalTunnel's password / consent page. That page is controlled by the LocalTunnel service, not by this app.

RU:
  • Установите LocalTunnel глобально через Node.js: npm install -g localtunnel
  • Программа умеет запускать lt, localtunnel или npx localtunnel.
  • Также можно положить lt / localtunnel в:
    {portablePath}
    или указать путь через поле CLI path.
  • У внешних пользователей может появиться страница с паролем / consent от LocalTunnel. Это поведение самого сервиса, а не приложения.
""";
    }

    private static string GetXTunnelInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("xtunnel", rootFolder);
        return $"""
EN:
  • Install xTunnel manually from the official site.
  • Put xtunnel.exe or xTunnel.exe into:
    {portablePath}
  • You can also keep it in the same .tools/xtunnel folder, in PATH, or point the app to it through the CLI path box.
  • If your xTunnel account or license requires activation, paste the key into Tunnel auth.
  • First-run flow usually looks like: xtunnel.exe register <KEY>, then xtunnel.exe http <PORT>.

RU:
  • Установите xTunnel вручную с официального сайта.
  • Поместите xtunnel.exe или xTunnel.exe в:
    {portablePath}
  • Также можно держать его в той же папке .tools/xtunnel, добавить в PATH или указать путь через поле CLI path.
  • Если для xTunnel нужны аккаунт, лицензия или активация, вставьте ключ в Tunnel auth.
  • Типичный первый запуск: xtunnel.exe register <КЛЮЧ>, затем xtunnel.exe http <ПОРТ>.
""";
    }

    private static string GetVkTunnelInstallationHint(string rootFolder)
    {
        var portablePath = GetPortableToolPath("vk-tunnel", rootFolder);
        return $"""
EN:
  • Install Node.js first.
  • Then install VK Tunnel globally: npm install -g @vkontakte/vk-tunnel
  • The app can use vk-tunnel.cmd, vk-tunnel.js, or npx when the package is installed globally.
  • You can also place vk-tunnel.cmd into:
    {portablePath}
    or point the app to it through the CLI path box.
  • On first launch VK Tunnel may open the official VK authorization page in your browser and wait for confirmation before the public URL appears.
  • In this window, plain npx alone is treated only as a fallback launcher, not as proof that @vkontakte/vk-tunnel is installed.

RU:
  • Сначала установите Node.js.
  • Затем установите VK Tunnel глобально: npm install -g @vkontakte/vk-tunnel
  • Программа умеет запускать vk-tunnel.cmd, vk-tunnel.js или npx, если пакет установлен глобально.
  • Также можно положить vk-tunnel.cmd в:
    {portablePath}
    или указать путь через поле CLI path.
  • При первом запуске VK Tunnel может открыть официальную страницу авторизации VK в браузере и ждать подтверждения перед выдачей публичного URL.
  • В этом окне plain npx считается только запасным launcher'ом, а не доказательством, что @vkontakte/vk-tunnel установлен.
""";
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

            "ngrok" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "ngrok", "ngrok.exe")
                : Path.Combine(toolsRoot, "ngrok", "ngrok"),

            "ssh" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "ssh", "ssh.exe")
                : Path.Combine(toolsRoot, "ssh", "ssh"),

            "tailscale" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "tailscale", "tailscale.exe")
                : Path.Combine(toolsRoot, "tailscale", "tailscale"),

            "zrok" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "zrok", "zrok.exe")
                : Path.Combine(toolsRoot, "zrok", "zrok"),

            "localtunnel" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "localtunnel", "lt.cmd")
                : Path.Combine(toolsRoot, "localtunnel", "lt"),

            "xtunnel" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "xtunnel", "xtunnel.exe")
                : Path.Combine(toolsRoot, "xtunnel", "xtunnel"),

            "vk-tunnel" => OperatingSystem.IsWindows()
                ? Path.Combine(toolsRoot, "vk-tunnel", "vk-tunnel.cmd")
                : Path.Combine(toolsRoot, "vk-tunnel", "vk-tunnel"),

            _ => string.Empty
        };
    }

    private static string? GetCommandPathIfExists(string toolName, string rootFolder)
    {
        var directPortable = GetPortableToolPath(toolName, rootFolder);
        if (!string.IsNullOrWhiteSpace(directPortable) && File.Exists(directPortable))
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
            "ngrok" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "ngrok", "bin", OperatingSystem.IsWindows() ? "ngrok.exe" : "ngrok"),
            },
            "ssh" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "ssh", "bin", OperatingSystem.IsWindows() ? "ssh.exe" : "ssh"),
            },
            "tailscale" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "tailscale", "bin", OperatingSystem.IsWindows() ? "tailscale.exe" : "tailscale"),
            },
            "zrok" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "zrok", "bin", OperatingSystem.IsWindows() ? "zrok.exe" : "zrok"),
            },
            "localtunnel" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "localtunnel", "bin", OperatingSystem.IsWindows() ? "lt.cmd" : "lt"),
                Path.Combine(GetToolsRoot(rootFolder), "localtunnel", OperatingSystem.IsWindows() ? "lt.cmd" : "lt"),
                Path.Combine(GetToolsRoot(rootFolder), "localtunnel", OperatingSystem.IsWindows() ? "localtunnel.cmd" : "localtunnel"),
            },
            "xtunnel" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "xtunnel", "bin", OperatingSystem.IsWindows() ? "xtunnel.exe" : "xtunnel"),
                Path.Combine(GetToolsRoot(rootFolder), "xtunnel", "bin", OperatingSystem.IsWindows() ? "xTunnel.exe" : "xtunnel"),
                Path.Combine(GetToolsRoot(rootFolder), "xtunnel", OperatingSystem.IsWindows() ? "xTunnel.exe" : "xtunnel"),
            },
            "vk-tunnel" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "vk-tunnel", "bin", OperatingSystem.IsWindows() ? "vk-tunnel.cmd" : "vk-tunnel"),
                Path.Combine(GetToolsRoot(rootFolder), "vk-tunnel", OperatingSystem.IsWindows() ? "vk-tunnel.cmd" : "vk-tunnel"),
                Path.Combine(GetToolsRoot(rootFolder), "vk-tunnel", "bin", "vk-tunnel.js"),
            },
            "npx" => new[]
            {
                Path.Combine(GetToolsRoot(rootFolder), "node", "npx.cmd"),
                Path.Combine(GetToolsRoot(rootFolder), "node", "npx.exe"),
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

        if (OperatingSystem.IsWindows() && toolName.Equals("vk-tunnel", StringComparison.OrdinalIgnoreCase))
        {
            fromPath = FindInPath("vk-tunnel.cmd") ?? FindInPath("vk-tunnel.exe");
            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;
        }

        if (toolName.Equals("localtunnel", StringComparison.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsWindows())
                fromPath = FindInPath("lt.cmd") ?? FindInPath("localtunnel.cmd") ?? FindInPath("lt.exe") ?? FindInPath("localtunnel.exe");
            else
                fromPath = FindInPath("lt") ?? FindInPath("localtunnel");

            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;
        }

        if (OperatingSystem.IsWindows() && toolName.Equals("xtunnel", StringComparison.OrdinalIgnoreCase))
        {
            fromPath = FindInPath("xTunnel.exe") ?? FindInPath("xtunnel.exe");
            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;
        }

        if (OperatingSystem.IsWindows() && toolName.Equals("npx", StringComparison.OrdinalIgnoreCase))
        {
            fromPath = FindInPath("npx.cmd") ?? FindInPath("npx.exe");
            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;
        }

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

        if (OperatingSystem.IsWindows() && toolName.Equals("ssh", StringComparison.OrdinalIgnoreCase))
        {
            var knownCandidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OpenSSH", "ssh.exe"),
            };

            foreach (var candidate in knownCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? FindNpxPath(string rootFolder)
    {
        var bundled = GetCommandPathIfExists("npx", rootFolder);
        if (!string.IsNullOrWhiteSpace(bundled))
            return bundled;

        if (OperatingSystem.IsWindows())
            return FindInPath("npx.cmd") ?? FindInPath("npx.exe");

        return FindInPath("npx");
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

            var ffmpegFileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var ffprobeFileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            var foundExe = Directory.GetFiles(extractDir, ffmpegFileName, SearchOption.AllDirectories).FirstOrDefault();
            var foundProbe = Directory.GetFiles(extractDir, ffprobeFileName, SearchOption.AllDirectories).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(foundExe))
                throw new InvalidOperationException("ffmpeg executable not found inside extracted archive.");

            ReportProgress("ffmpeg", "Copying executable", progress);
            File.Copy(foundExe, dstExe, true);

            if (!string.IsNullOrWhiteSpace(foundProbe))
            {
                var dstProbe = Path.Combine(Path.GetDirectoryName(dstExe)!, ffprobeFileName);
                File.Copy(foundProbe, dstProbe, true);

                if (!OperatingSystem.IsWindows())
                    await EnsureExecutableAsync(dstProbe, cancellationToken);
            }

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
