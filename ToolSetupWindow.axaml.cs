using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private CancellationTokenSource? _installCts;
    private string? _activeToolName;

    public ToolSetupWindow()
    : this(AppContext.BaseDirectory)
    {
    }

    public ToolSetupWindow(string rootFolder, string? startupError = null, Action<string>? log = null)
    {
        _rootFolder = rootFolder;
        _log = log;

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
        };

        foreach (var pair in _toolUi)
        {
            var toolName = pair.Key;
            pair.Value.InstallButton.Click += async (_, _) => await InstallSingleToolAsync(toolName);
            pair.Value.OpenButton.Click += (_, _) => OpenExternal(pair.Value.DownloadUrl);
            pair.Value.ProgressBar.Value = 0;
            pair.Value.ProgressBar.IsIndeterminate = false;
            pair.Value.ProgressTextBlock.Text = "Waiting";
        }

        _folderTextBlock.Text = "Project folder: " + rootFolder;
        _errorTextBox.Text = startupError ?? string.Empty;

        _refreshButton.Click += RefreshButton_OnClick;
        _installButton.Click += InstallButton_OnClick;
        _closeButton.Click += (_, _) => Close();

        Opened += async (_, _) => await RefreshAsync();
        Closing += ToolSetupWindow_Closing;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
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
            "Installing all missing tools...");
    }

    private async Task InstallSingleToolAsync(string toolName)
    {
        if (_installCts is not null)
            return;

        var status = ToolResolver.InspectAll(_rootFolder).Single(x => x.Name == toolName);
        if (status.IsInstalled)
        {
            _summaryTextBlock.Text = status.DisplayName + " is already installed.";
            return;
        }

        if (!status.SupportsAutomaticInstall)
        {
            _summaryTextBlock.Text = status.DisplayName + " must be installed manually on this platform. Open the download page and the window will detect it automatically.";
            return;
        }

        await RunAutomaticInstallAsync(new[] { toolName }, "Installing " + status.DisplayName + "...");
    }

    private async Task RunAutomaticInstallAsync(IReadOnlyList<string> toolNames, string startText)
    {
        if (toolNames.Count == 0)
        {
            _summaryTextBlock.Text = "No supported automatic installs are needed.";
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
            _summaryTextBlock.Text = "Installation finished. Refreshing tool status...";
        }
        catch (OperationCanceledException)
        {
            _summaryTextBlock.Text = "Installation canceled.";
        }
        catch (Exception ex)
        {
            _errorTextBox.Text = ex.Message;
            _summaryTextBlock.Text = "Installation stopped. Check the error and install the missing tool manually if needed.";
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
                _summaryTextBlock.Text = "All helper tools are installed. The program can be started again.";
            }
            else
            {
                var names = string.Join(", ", missing.Select(x => x.DisplayName));
                _summaryTextBlock.Text = $"Program is stopped until the missing tools are available: {names}.";
            }
        }

        _installButton.IsEnabled = _installCts is null && missing.Any(x => x.SupportsAutomaticInstall);
        return Task.CompletedTask;
    }

    private void ApplyStatus(ToolStatus status, ToolUi ui)
    {
        ui.StatusTextBlock.Text = status.IsInstalled ? "Installed" : "Missing";
        ui.StatusTextBlock.Classes.Set("ok", status.IsInstalled);
        ui.StatusTextBlock.Classes.Set("warn", !status.IsInstalled);
        ui.PathTextBox.Text = status.DetectedPath ?? string.Empty;
        ui.HintTextBox.Text = status.InstallationHint;
        ui.AutoTextBlock.Text = status.SupportsAutomaticInstall
            ? "Automatic install is supported by the app for this tool on the current OS."
            : "Automatic install is not available here. Install it manually and this window will detect it.";

        ui.InstallButton.Content = status.SupportsAutomaticInstall ? "Install automatically" : "Manual install only";
        ui.InstallButton.IsEnabled = _installCts is null && !status.IsInstalled && status.SupportsAutomaticInstall;

        if (!string.Equals(_activeToolName, status.Name, StringComparison.OrdinalIgnoreCase))
        {
            ui.ProgressBar.IsIndeterminate = false;
            ui.ProgressBar.Value = status.IsInstalled ? 100 : 0;
            ui.ProgressTextBlock.Text = status.IsInstalled
                ? "Installed and ready."
                : status.SupportsAutomaticInstall
                    ? "Waiting for install."
                    : "Manual install required on this platform.";
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
                ui.ProgressTextBlock.Text = $"{progress.Stage} ({percent:0.0}%)";
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
                ui.ProgressTextBlock.Text = progress.Stage;
            }

            if (progress.Stage.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase))
            {
                ui.ProgressBar.IsIndeterminate = false;
                ui.ProgressBar.Value = 0;
            }
        });
    }

    private void AppendLog(string message) => _log?.Invoke(message);

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
