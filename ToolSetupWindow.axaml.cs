using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher;

public partial class ToolSetupWindow : Window
{
    private sealed class ToolUi
    {
        public required string Name { get; init; }
        public required string DownloadUrl { get; init; }
        public required TextBlock StatusTextBlock { get; init; }
        public required TextBox PathTextBox { get; init; }
        public required TextBox HintTextBox { get; init; }
        public required TextBlock AutoTextBlock { get; init; }
        public required ProgressBar ProgressBar { get; init; }
        public required TextBlock ProgressTextBlock { get; init; }
        public required Button InstallButton { get; init; }
        public required Button OpenButton { get; init; }
    }

    private readonly string _rootFolder;
    private readonly Action<string>? _log;
    private readonly DispatcherTimer _refreshTimer;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _folderTextBlock;
    private readonly TextBox _errorTextBox;
    private readonly Button _refreshButton;
    private readonly Button _installButton;
    private readonly Button _closeButton;
    private readonly Dictionary<string, ToolUi> _toolUi;
    private readonly AppLanguage _language;
    private CancellationTokenSource? _installCts;
    private string? _activeToolName;

    public ToolSetupWindow()
    : this(AppContext.BaseDirectory)
    {
    }

    public ToolSetupWindow(string rootFolder, string? startupError = null, Action<string>? log = null, AppLanguage language = AppLanguage.English)
    {
        _rootFolder = rootFolder;
        _log = log;
        _language = language;

        InitializeComponent();

        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock")!;
        _folderTextBlock = this.FindControl<TextBlock>("FolderTextBlock")!;
        _errorTextBox = this.FindControl<TextBox>("ErrorTextBox")!;
        _refreshButton = this.FindControl<Button>("RefreshButton")!;
        _installButton = this.FindControl<Button>("InstallButton")!;
        _closeButton = this.FindControl<Button>("CloseButton")!;

        _toolUi = new Dictionary<string, ToolUi>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = new ToolUi
            {
                Name = "ffmpeg",
                DownloadUrl = "https://ffmpeg.org/download.html",
                StatusTextBlock = this.FindControl<TextBlock>("FfmpegStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("FfmpegPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("FfmpegHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("FfmpegAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("FfmpegProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("FfmpegProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("FfmpegInstallButton")!,
                OpenButton = this.FindControl<Button>("FfmpegOpenButton")!,
            },
            ["caddy"] = new ToolUi
            {
                Name = "caddy",
                DownloadUrl = "https://caddyserver.com/download",
                StatusTextBlock = this.FindControl<TextBlock>("CaddyStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("CaddyPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("CaddyHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("CaddyAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("CaddyProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("CaddyProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("CaddyInstallButton")!,
                OpenButton = this.FindControl<Button>("CaddyOpenButton")!,
            },
            ["cloudflared"] = new ToolUi
            {
                Name = "cloudflared",
                DownloadUrl = "https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/",
                StatusTextBlock = this.FindControl<TextBlock>("CloudflaredStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("CloudflaredPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("CloudflaredHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("CloudflaredAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("CloudflaredProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("CloudflaredProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("CloudflaredInstallButton")!,
                OpenButton = this.FindControl<Button>("CloudflaredOpenButton")!,
            },
            ["ngrok"] = new ToolUi
            {
                Name = "ngrok",
                DownloadUrl = "https://ngrok.com/docs/agent",
                StatusTextBlock = this.FindControl<TextBlock>("NgrokStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("NgrokPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("NgrokHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("NgrokAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("NgrokProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("NgrokProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("NgrokInstallButton")!,
                OpenButton = this.FindControl<Button>("NgrokOpenButton")!,
            },
            ["ssh"] = new ToolUi
            {
                Name = "ssh",
                DownloadUrl = "https://localhost.run/docs/cli/",
                StatusTextBlock = this.FindControl<TextBlock>("SshStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("SshPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("SshHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("SshAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("SshProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("SshProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("SshInstallButton")!,
                OpenButton = this.FindControl<Button>("SshOpenButton")!,
            },
            ["tailscale"] = new ToolUi
            {
                Name = "tailscale",
                DownloadUrl = "https://tailscale.com/kb/1223/funnel",
                StatusTextBlock = this.FindControl<TextBlock>("TailscaleStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("TailscalePathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("TailscaleHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("TailscaleAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("TailscaleProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("TailscaleProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("TailscaleInstallButton")!,
                OpenButton = this.FindControl<Button>("TailscaleOpenButton")!,
            },
            ["zrok"] = new ToolUi
            {
                Name = "zrok",
                DownloadUrl = "https://docs.zrok.io/docs/getting-started/",
                StatusTextBlock = this.FindControl<TextBlock>("ZrokStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("ZrokPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("ZrokHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("ZrokAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("ZrokProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("ZrokProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("ZrokInstallButton")!,
                OpenButton = this.FindControl<Button>("ZrokOpenButton")!,
            },
            ["localtunnel"] = new ToolUi
            {
                Name = "localtunnel",
                DownloadUrl = "https://github.com/localtunnel/localtunnel",
                StatusTextBlock = this.FindControl<TextBlock>("LocalTunnelStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("LocalTunnelPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("LocalTunnelHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("LocalTunnelAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("LocalTunnelProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("LocalTunnelProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("LocalTunnelInstallButton")!,
                OpenButton = this.FindControl<Button>("LocalTunnelOpenButton")!,
            },
            ["xtunnel"] = new ToolUi
            {
                Name = "xtunnel",
                DownloadUrl = "https://xtunnel.ru/",
                StatusTextBlock = this.FindControl<TextBlock>("XTunnelStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("XTunnelPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("XTunnelHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("XTunnelAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("XTunnelProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("XTunnelProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("XTunnelInstallButton")!,
                OpenButton = this.FindControl<Button>("XTunnelOpenButton")!,
            },
            ["vk-tunnel"] = new ToolUi
            {
                Name = "vk-tunnel",
                DownloadUrl = "https://dev.vk.com/ru/libraries/tunnel",
                StatusTextBlock = this.FindControl<TextBlock>("VkTunnelStatusTextBlock")!,
                PathTextBox = this.FindControl<TextBox>("VkTunnelPathTextBox")!,
                HintTextBox = this.FindControl<TextBox>("VkTunnelHintTextBox")!,
                AutoTextBlock = this.FindControl<TextBlock>("VkTunnelAutoTextBlock")!,
                ProgressBar = this.FindControl<ProgressBar>("VkTunnelProgressBar")!,
                ProgressTextBlock = this.FindControl<TextBlock>("VkTunnelProgressTextBlock")!,
                InstallButton = this.FindControl<Button>("VkTunnelInstallButton")!,
                OpenButton = this.FindControl<Button>("VkTunnelOpenButton")!,
            },
        };

        foreach (var pair in _toolUi)
        {
            var toolName = pair.Key;
            pair.Value.InstallButton.Click += async (_, _) => await InstallSingleToolAsync(toolName);
            pair.Value.OpenButton.Click += (_, _) => OpenExternal(pair.Value.DownloadUrl);
            pair.Value.ProgressBar.Value = 0;
            pair.Value.ProgressBar.IsIndeterminate = false;
            pair.Value.ProgressTextBlock.Text = T("Waiting");
        }

        _folderTextBlock.Text = T("Project folder:") + " " + rootFolder;
        _errorTextBox.Text = startupError ?? string.Empty;

        _refreshButton.Click += RefreshButton_OnClick;
        _installButton.Click += InstallButton_OnClick;
        _closeButton.Click += (_, _) => Close();

        Opened += async (_, _) => await RefreshAsync();
        Closing += ToolSetupWindow_Closing;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        UiLocalizer.Apply(this, _language);
    }

    private void ToolSetupWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _installCts?.Cancel();
        _installCts?.Dispose();
        _installCts = null;
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async void InstallButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_installCts is not null)
            return;

        await RunAutomaticInstallAsync(
            ToolResolver.InspectAll(_rootFolder)
                .Where(x => !x.IsInstalled && x.SupportsAutomaticInstall)
                .Select(x => x.Name)
                .ToArray(),
            T("Installing all missing tools..."));
    }

    private async Task InstallSingleToolAsync(string toolName)
    {
        if (_installCts is not null)
            return;

        var status = ToolResolver.InspectAll(_rootFolder).Single(x => x.Name == toolName);
        if (status.IsInstalled)
        {
            _summaryTextBlock.Text = string.Format(T("{0} is already installed."), status.DisplayName);
            return;
        }

        if (!status.SupportsAutomaticInstall)
        {
            _summaryTextBlock.Text = string.Format(T("{0} must be installed manually on this platform. Open the download page and the window will detect it automatically."), status.DisplayName);
            return;
        }

        await RunAutomaticInstallAsync(new[] { toolName }, string.Format(T("Installing {0}..."), status.DisplayName));
    }

    private async Task RunAutomaticInstallAsync(IReadOnlyList<string> toolNames, string startText)
    {
        if (toolNames.Count == 0)
        {
            _summaryTextBlock.Text = T("No supported automatic installs are needed.");
            return;
        }

        _installCts = new CancellationTokenSource();
        _summaryTextBlock.Text = startText;
        SetGlobalBusyState(true);

        try
        {
            foreach (var toolName in toolNames)
            {
                _activeToolName = toolName;
                await ToolResolver.InstallToolAsync(
                    toolName,
                    _rootFolder,
                    AppendLog,
                    progress => UpdateProgress(progress),
                    _installCts.Token);
            }

            _errorTextBox.Text = string.Empty;
            _summaryTextBlock.Text = T("Installation finished. Refreshing tool status...");
        }
        catch (OperationCanceledException)
        {
            _summaryTextBlock.Text = T("Installation canceled.");
        }
        catch (Exception ex)
        {
            _errorTextBox.Text = ex.Message;
            _summaryTextBlock.Text = T("Installation stopped. Check the error and install the missing tool manually if needed.");
            AppendLog("Dependency setup failed: " + ex.Message);
        }
        finally
        {
            _activeToolName = null;
            _installCts.Dispose();
            _installCts = null;
            SetGlobalBusyState(false);
            await RefreshAsync();
        }
    }

    private void SetGlobalBusyState(bool isBusy)
    {
        _installButton.IsEnabled = !isBusy;
        _refreshButton.IsEnabled = !isBusy;

        foreach (var ui in _toolUi.Values)
        {
            ui.OpenButton.IsEnabled = !isBusy;
        }
    }

    private Task RefreshAsync()
    {
        var statuses = ToolResolver.InspectAll(_rootFolder);

        foreach (var status in statuses)
        {
            ApplyStatus(status, _toolUi[status.Name]);
        }

        var missing = statuses.Where(x => !x.IsInstalled).ToArray();
        if (_installCts is null)
        {
            if (missing.Length == 0)
            {
                _summaryTextBlock.Text = T("All helper tools are installed. The program can be started again.");
            }
            else
            {
                var names = string.Join(", ", missing.Select(x => x.DisplayName));
                _summaryTextBlock.Text = string.Format(T("Missing tools detected: {0}. Install the tools required for the tunnel provider you plan to use."), names);
            }
        }

        _installButton.IsEnabled = _installCts is null && missing.Any(x => x.SupportsAutomaticInstall);
        return Task.CompletedTask;
    }

    private void ApplyStatus(ToolStatus status, ToolUi ui)
    {
        ui.StatusTextBlock.Text = T(status.IsInstalled ? "Installed" : "Missing");
        ui.StatusTextBlock.Classes.Set("ok", status.IsInstalled);
        ui.StatusTextBlock.Classes.Set("warn", !status.IsInstalled);
        ui.PathTextBox.Text = status.DetectedPath ?? string.Empty;
        ui.HintTextBox.Text = LocalizeInstallationHint(status);
        ui.AutoTextBlock.Text = status.SupportsAutomaticInstall
            ? T("Automatic install is supported by the app for this tool on the current OS.")
            : T("Automatic install is not available here. Install it manually and this window will detect it.");

        ui.InstallButton.Content = T(status.SupportsAutomaticInstall ? "Install automatically" : "Manual install only");
        ui.InstallButton.IsEnabled = _installCts is null && !status.IsInstalled && status.SupportsAutomaticInstall;

        if (!string.Equals(_activeToolName, status.Name, StringComparison.OrdinalIgnoreCase))
        {
            ui.ProgressBar.IsIndeterminate = false;
            ui.ProgressBar.Value = status.IsInstalled ? 100 : 0;
            ui.ProgressTextBlock.Text = status.IsInstalled
                ? T("Installed and ready.")
                : status.SupportsAutomaticInstall
                    ? T("Waiting for install.")
                    : T("Manual install required on this platform.");
        }
    }

    private void UpdateProgress(ToolInstallProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_toolUi.TryGetValue(progress.ToolName, out var ui))
                return;

            _activeToolName = progress.ToolName;

            if (progress.Percent is double percent)
            {
                ui.ProgressBar.IsIndeterminate = false;
                ui.ProgressBar.Value = Math.Clamp(percent, 0, 100);
                ui.ProgressTextBlock.Text = $"{T(progress.Stage)} ({percent:0.0}%)";
            }
            else
            {
                ui.ProgressBar.IsIndeterminate = !string.Equals(progress.Stage, "Installed", StringComparison.OrdinalIgnoreCase) &&
                                                !progress.Stage.StartsWith("Already installed", StringComparison.OrdinalIgnoreCase) &&
                                                !progress.Stage.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase);
                ui.ProgressBar.Value = string.Equals(progress.Stage, "Installed", StringComparison.OrdinalIgnoreCase) ||
                                       progress.Stage.StartsWith("Already installed", StringComparison.OrdinalIgnoreCase)
                    ? 100
                    : ui.ProgressBar.Value;
                ui.ProgressTextBlock.Text = T(progress.Stage);
            }

            if (progress.Stage.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase))
            {
                ui.ProgressBar.IsIndeterminate = false;
                ui.ProgressBar.Value = 0;
            }
        });
    }

    private void AppendLog(string message) => _log?.Invoke(message);

    private string T(string text) => UiLocalizer.Translate(text, _language);

    private string LocalizeInstallationHint(ToolStatus status)
    {
        if (_language != AppLanguage.Russian)
            return status.InstallationHint;

        return status.Name switch
        {
            "ffmpeg" => OperatingSystem.IsWindows()
                ? $"Windows: программа скачает portable-архив ffmpeg, извлечёт ffmpeg.exe и ffprobe.exe в .tools\\ffmpeg и покажет прогресс в окне «Зависимости». Если нужно вручную: скопируйте ffmpeg.exe и ffprobe.exe сюда: {PortableToolPath("ffmpeg", "ffmpeg")}"
                : $"Linux/macOS: автоматическая установка ffmpeg здесь не встроена. Установите ffmpeg через пакетный менеджер или поместите бинарник сюда: {PortableToolPath("ffmpeg", "ffmpeg")}. Примеры: sudo apt install ffmpeg, sudo dnf install ffmpeg, brew install ffmpeg. Окно обновится автоматически, когда ffmpeg появится.",
            "caddy" =>
                $"Программа скачает актуальную сборку Caddy для этой ОС, покажет прогресс загрузки и извлечёт бинарник сюда: {PortableToolPath("caddy", "caddy")}. Если нужно вручную: установите Caddy в систему или скопируйте бинарник в эту папку.",
            "cloudflared" =>
                $"Программа скачает актуальную сборку cloudflared для этой ОС, покажет прогресс загрузки и сохранит бинарник сюда: {PortableToolPath("cloudflared", "cloudflared")}. Если нужно вручную: установите cloudflared в систему или скопируйте бинарник в эту папку.",
            "ngrok" =>
                $"Установите ngrok с официальной страницы загрузки. Поместите ngrok / ngrok.exe сюда: {PortableToolPath("ngrok", "ngrok")}. При первом запуске вставьте authtoken ngrok в поле Tunnel auth; программа выполнит настройку перед стартом туннеля. Если ngrok уже настроен на этой машине, Tunnel auth можно оставить пустым.",
            "ssh" =>
                "localhost.run использует обычный OpenSSH client. В современных Windows ssh.exe часто уже установлен. Если его нет, установите компонент OpenSSH Client или укажите путь к ssh.exe. На Linux/macOS обычно достаточно системного ssh.",
            "tailscale" =>
                "Установите клиент Tailscale и войдите в аккаунт на этой машине. Funnel должен быть разрешён в настройках Tailscale. Программа запустит Funnel с локальным Caddy как backend и дождётся публичной ссылки ts.net.",
            "zrok" =>
                $"Установите zrok с официальной документации или GitHub releases. Поместите zrok / zrok.exe сюда: {PortableToolPath("zrok", "zrok")}. Для первого запуска нужен account token zrok: вставьте его в Tunnel auth, и программа выполнит zrok enable перед созданием публичной ссылки. Если машина уже включена в zrok, Tunnel auth можно оставить пустым.",
            "localtunnel" =>
                "Установите LocalTunnel глобально через Node.js: npm install -g localtunnel. Также можно указать путь к lt / localtunnel / npx. У внешних пользователей может появиться пароль или consent-страница LocalTunnel; это поведение сервиса, не приложения.",
            "xtunnel" =>
                $"Установите xTunnel вручную с официального сайта. Поместите xtunnel / xTunnel.exe сюда: {PortableToolPath("xtunnel", OperatingSystem.IsWindows() ? "xTunnel" : "xtunnel")}. Если для xTunnel нужны аккаунт, лицензия или активация, вставьте ключ в Tunnel auth.",
            "vk-tunnel" =>
                "Установите Node.js, затем VK Tunnel глобально: npm install -g @vkontakte/vk-tunnel. Также можно указать путь к vk-tunnel.cmd / vk-tunnel.js / npx. При первом запуске VK Tunnel может открыть страницу авторизации VK в браузере.",
            _ => UiLocalizer.Translate(status.InstallationHint, _language)
        };
    }

    private string PortableToolPath(string toolName, string executableBaseName)
    {
        var fileName = OperatingSystem.IsWindows()
            ? executableBaseName + ".exe"
            : executableBaseName;

        return Path.Combine(_rootFolder, ".tools", toolName, fileName);
    }

    private static void OpenExternal(string url)
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
}
