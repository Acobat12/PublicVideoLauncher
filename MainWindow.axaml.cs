using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher;

public partial class MainWindow : Window
{
    private readonly TextBox _folderTextBox;
    private readonly Button _browseButton;
    private readonly Button _scanButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _copyUrlButton;
    private readonly Button _openUrlButton;
    private readonly Button _fixDnsButton;
    private readonly Button _dependenciesButton;
    private readonly NumericUpDown _portNumeric;
    private readonly CheckBox _proxyModeCheckBox;
    private readonly CheckBox _autostartBrowserCheckBox;
    private readonly Grid _detailedHeaderGrid;
    private readonly Grid _simpleHeaderGrid;
    private readonly ListBox _videosListBox;
    private readonly TextBox _logTextBox;
    private readonly TextBox _publicUrlTextBox;
    private readonly TextBlock _statusTextBlock;
    private readonly TextBlock _summaryTextBlock;

    private readonly ObservableCollection<VideoEntry> _videoItems = new();
    private readonly VideoWorkspace _workspace = new();
    private readonly FfmpegService _ffmpegService = new();
    private readonly PlayerFilesService _playerFilesService = new();
    private readonly ServerRunner _serverRunner = new();

    private CancellationTokenSource? _runCts;
    private string? _publicUrl;

    public MainWindow()
    {
        InitializeComponent();

        _folderTextBox = this.FindControl<TextBox>("FolderTextBox")!;
        _browseButton = this.FindControl<Button>("BrowseButton")!;
        _scanButton = this.FindControl<Button>("ScanButton")!;
        _startButton = this.FindControl<Button>("StartButton")!;
        _stopButton = this.FindControl<Button>("StopButton")!;
        _copyUrlButton = this.FindControl<Button>("CopyUrlButton")!;
        _openUrlButton = this.FindControl<Button>("OpenUrlButton")!;
        _fixDnsButton = this.FindControl<Button>("FixDnsButton")!;
        _dependenciesButton = this.FindControl<Button>("DependenciesButton")!;
        _portNumeric = this.FindControl<NumericUpDown>("PortNumeric")!;
        _proxyModeCheckBox = this.FindControl<CheckBox>("ProxyModeCheckBox")!;
        _autostartBrowserCheckBox = this.FindControl<CheckBox>("AutostartBrowserCheckBox")!;
        _detailedHeaderGrid = this.FindControl<Grid>("DetailedHeaderGrid")!;
        _simpleHeaderGrid = this.FindControl<Grid>("SimpleHeaderGrid")!;
        _videosListBox = this.FindControl<ListBox>("VideosListBox")!;
        _logTextBox = this.FindControl<TextBox>("LogTextBox")!;
        _publicUrlTextBox = this.FindControl<TextBox>("PublicUrlTextBox")!;
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")!;
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock")!;

        _videosListBox.ItemsSource = _videoItems;

        _browseButton.Click += BrowseButton_OnClick;
        _scanButton.Click += ScanButton_OnClick;
        _startButton.Click += StartButton_OnClick;
        _stopButton.Click += StopButton_OnClick;
        _copyUrlButton.Click += CopyUrlButton_OnClick;
        _openUrlButton.Click += OpenUrlButton_OnClick;
        _fixDnsButton.Click += FixDnsButton_OnClick;
        _dependenciesButton.Click += DependenciesButton_OnClick;
        Closing += MainWindow_Closing;
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select video folder",
                AllowMultiple = false,
            });

            if (folders.Count == 0)
                return;

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                await ShowMessageAsync("Folder", "Could not resolve a local path for the selected folder.");
                return;
            }

            _folderTextBox.Text = path;
            SetSummary("Folder selected.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Browse failed", ex.Message);
            AppendLog("Browse failed: " + ex);
        }
    }

    private async void ScanButton_OnClick(object? sender, RoutedEventArgs e) => await ScanAsync();
    private async void StartButton_OnClick(object? sender, RoutedEventArgs e) => await StartAsync();
    private async void StopButton_OnClick(object? sender, RoutedEventArgs e) => await StopAsync();

    private async void CopyUrlButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_publicUrl))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            await ShowMessageAsync("Clipboard", "Clipboard is not available.");
            return;
        }

        await topLevel.Clipboard.SetTextAsync(_publicUrl);
        AppendLog("Public URL copied to clipboard.");
        SetSummary("Public URL copied.");
    }

    private async void OpenUrlButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_publicUrl))
            return;

        try
        {
            OpenUrl(_publicUrl);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Open failed", ex.Message);
            AppendLog("Open failed: " + ex);
        }
    }

    private async void FixDnsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var host = GetPublicHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            await ShowMessageAsync(
                "Fix DNS",
                "Start the tunnel first. Then this button will check:\n\n" +
                "• system DNS\n• Cloudflare 1.1.1.1\n• Google 8.8.8.8\n\n" +
                "and tell you whether the problem is system DNS or browser Secure DNS / DNS-over-HTTPS.");
            return;
        }

        SetSummary("Running DNS diagnostics...");
        try
        {
            var report = await RunDnsDiagnosticsAsync(host);
            await ShowMessageAsync("DNS diagnostics", report);
            SetSummary("DNS diagnostics finished.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Fix DNS", ex.Message);
        }
    }

    private async void DependenciesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Folder required", "Select a valid folder first.");
            return;
        }

        await ShowDependencySetupWindowAsync(folder, null);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        try { await StopAsync(); } catch { }
    }

    private async Task ScanAsync()
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Folder required", "Select a valid folder first.");
            return;
        }

        SetBusy(true, "Scanning videos...");
        try
        {
            _workspace.Load(folder, _proxyModeCheckBox.IsChecked == true);
            RenderVideos();

            AppendLog("Workspace loaded: " + folder);
            AppendLog("Videos found: " + _workspace.Entries.Count);
            AppendLog(_proxyModeCheckBox.IsChecked == true
                ? "Proxy translation mode is ON. Non-MP4 files get playable MP4 + translation proxy MP4."
                : "Proxy translation mode is OFF. Non-MP4 files are published as-is with no conversion.");

            SetStatus("Ready");
            SetSummary(_workspace.Entries.Count > 0
                ? $"Found {_workspace.Entries.Count} video(s)."
                : "No supported videos found.");
        }
        catch (Exception ex)
        {
            AppendLog("Scan failed: " + ex);
            SetStatus("Scan failed");
            SetSummary(ex.Message);
            await ShowMessageAsync("Scan failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartAsync()
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Folder required", "Select a valid folder first.");
            return;
        }

        if (_workspace.RootFolder is null || !PathsEqual(_workspace.RootFolder, folder))
        {
            await ScanAsync();
        }

        if (_workspace.Entries.Count == 0)
        {
            await ShowMessageAsync("Nothing to start", "No supported videos were found in the selected folder.");
            return;
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        SetBusy(true, "Starting services...");
        try
        {
            var toolStatusesBeforeStart = ToolResolver.InspectAll(folder);
            foreach (var missingTool in toolStatusesBeforeStart.Where(x => !x.IsInstalled))
            {
                AppendLog($"Missing tool detected before start: {missingTool.DisplayName}.");
                AppendLog(missingTool.InstallationHint);
            }

            AppendLog("Ensuring helper tools...");
            var tools = await ToolResolver.EnsureAllAsync(folder, AppendLog, _runCts.Token);
            AppendLog("ffmpeg: " + tools.FfmpegPath);
            AppendLog("caddy: " + tools.CaddyPath);
            AppendLog("cloudflared: " + tools.CloudflaredPath);

            var entriesRequiringCache = _workspace.Entries
                .Where(x => !string.IsNullOrWhiteSpace(x.PlaybackCachePath) || !string.IsNullOrWhiteSpace(x.TranslationCachePath))
                .ToList();

            if (entriesRequiringCache.Count > 0)
            {
                AppendLog("Preparing playable / translation caches...");
                await _ffmpegService.EnsureCachesAsync(tools.FfmpegPath, entriesRequiringCache, AppendLog, _runCts.Token);
            }
            else
            {
                AppendLog("No cache preparation required for current mode.");
            }

            RenderVideos();

            AppendLog("Writing playlist.json and player.html...");
            await _playerFilesService.WriteAllAsync(folder, _workspace.Entries, _runCts.Token);

            AppendLog("Starting local server and tunnel...");
            var preferredPort = (int)(_portNumeric.Value ?? 8000m);
            var selectedEntry = _videosListBox.SelectedItem as VideoEntry;
            if (selectedEntry is not null)
            {
                AppendLog("Selected start video: " + selectedEntry.FileName);
            }

            var session = await _serverRunner.StartAsync(
                _workspace,
                preferredPort,
                tools,
                AppendLog,
                _runCts.Token,
                selectedEntry?.PlaybackUrl);

            _publicUrl = session.PublicUrl;
            _publicUrlTextBox.Text = _publicUrl;
            _copyUrlButton.IsEnabled = true;
            _openUrlButton.IsEnabled = true;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is not null)
            {
                await topLevel.Clipboard.SetTextAsync(_publicUrl);
                AppendLog("Public URL copied to clipboard.");
            }

            SetStatus("Running");
            SetSummary($"Local: {session.LocalUrl} | Public tunnel is ready.");

            if (_autostartBrowserCheckBox.IsChecked == true)
            {
                AppendLog("Tunnel URL detected and autostart is enabled.");
                AppendLog("Waiting for DNS propagation...");
                _ = OpenBrowserWhenDnsReadyAsync(_publicUrl!, _runCts?.Token ?? CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Start canceled.");
            SetStatus("Canceled");
            SetSummary("Operation canceled.");
        }
        catch (Exception ex)
        {
            AppendLog("Start failed: " + ex);
            SetStatus("Stopped");

            var looksLikeToolIssue = ToolResolver.LooksLikeToolIssue(ex);
            var currentToolStatuses = ToolResolver.InspectAll(folder);
            var hasMissingTools = currentToolStatuses.Any(x => !x.IsInstalled);

            if (looksLikeToolIssue || hasMissingTools)
            {
                SetSummary("Program stopped. Check helper tools in the Dependencies window.");
                await ShowDependencySetupWindowAsync(folder, ex.Message);
            }
            else
            {
                SetSummary(ex.Message);
                await ShowMessageAsync("Start failed", ex.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            _runCts?.Cancel();
            await _serverRunner.StopAsync(AppendLog);
            SetStatus("Idle");
            SetSummary("Stopped.");
        }
        catch (Exception ex)
        {
            AppendLog("Stop warning: " + ex);
            SetStatus("Stop warning");
            SetSummary(ex.Message);
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            _publicUrl = null;
            _publicUrlTextBox.Text = string.Empty;
            _copyUrlButton.IsEnabled = false;
            _openUrlButton.IsEnabled = false;
            SetBusy(false);
        }
    }

    private async Task ShowDependencySetupWindowAsync(string folder, string? startupError)
    {
        var window = new ToolSetupWindow(folder, startupError, AppendLog);
        await window.ShowDialog(this);
    }

    private void RenderVideos()
    {
        var showDetailed = _workspace.ProxyTranslationEnabled;
        _detailedHeaderGrid.IsVisible = showDetailed;
        _simpleHeaderGrid.IsVisible = !showDetailed;

        _videoItems.Clear();
        foreach (var entry in _workspace.Entries)
            _videoItems.Add(entry);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _browseButton.IsEnabled = !busy;
        _scanButton.IsEnabled = !busy;
        _fixDnsButton.IsEnabled = true;
        _dependenciesButton.IsEnabled = !busy;
        _proxyModeCheckBox.IsEnabled = !busy && !_serverRunner.IsRunning;
        _portNumeric.IsEnabled = !busy && !_serverRunner.IsRunning;
        _autostartBrowserCheckBox.IsEnabled = !busy;

        _startButton.IsEnabled = !busy && !_serverRunner.IsRunning;
        _stopButton.IsEnabled = busy || _serverRunner.IsRunning;

        if (!string.IsNullOrWhiteSpace(status))
            SetStatus(status);
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => _statusTextBlock.Text = text);
    private void SetSummary(string text) => Dispatcher.UIThread.Post(() => _summaryTextBlock.Text = text);

    private void AppendLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var current = _logTextBox.Text ?? string.Empty;
            _logTextBox.Text = string.IsNullOrWhiteSpace(current) ? line : current + Environment.NewLine + line;
            _logTextBox.CaretIndex = (_logTextBox.Text ?? string.Empty).Length;
        });
    }

    private string? GetPublicHost()
    {
        if (string.IsNullOrWhiteSpace(_publicUrl))
            return null;

        if (!Uri.TryCreate(_publicUrl, UriKind.Absolute, out var uri))
            return null;

        return uri.Host;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static string[] GetDnsAdviceLines()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                "1. Flush DNS: ipconfig /flushdns",
                "2. In Firefox, enable DNS over HTTPS / Secure DNS.",
                "3. In Chrome/Edge, enable Secure DNS if available.",
                "4. If needed, switch system DNS to 1.1.1.1 / 1.0.0.1 with admin rights.",
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "1. Flush DNS: sudo dscacheutil -flushcache; sudo killall -HUP mDNSResponder",
                "2. Check browser Secure DNS / DNS-over-HTTPS settings.",
                "3. If needed, switch macOS DNS to 1.1.1.1 / 1.0.0.1 in Network settings.",
            ];
        }

        return
        [
            "1. Try restarting systemd-resolved or NetworkManager if your distro uses them.",
            "2. Check browser Secure DNS / DNS-over-HTTPS settings.",
            "3. If needed, switch system DNS to 1.1.1.1 / 1.0.0.1 in your network configuration.",
        ];
    }

    private async Task OpenBrowserWhenDnsReadyAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            AppendLog("Autostart skipped: invalid public URL.");
            return;
        }
    
        var host = uri.Host;
        AppendLog("Autostart waiting for DNS: " + host);

        var resolved = await WaitUntilSystemDnsReady(host, TimeSpan.FromSeconds(60), cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            AppendLog("Autostart canceled before browser open.");
            return;
        }

        if (!resolved)
        {
            AppendLog("System DNS did not resolve host in time. Opening browser anyway: " + url);
        }
        else
        {
            AppendLog("System DNS resolved host. Opening browser: " + url);
        }
    
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            OpenUrl(url);
        });
    }
    private async Task<bool> WaitUntilSystemDnsReady(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var lastLoggedState = string.Empty;

        while (DateTime.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);

                var usable = addresses?
                    .Where(a => !IPAddress.IsLoopback(a))
                    .Distinct()
                    .ToArray();

                if (usable is { Length: > 0 })
                {
                    AppendLog("System DNS resolved: " + string.Join(", ", usable.Select(x => x.ToString())));
                    return true;
                }

                if (lastLoggedState != "empty")
                {
                    AppendLog("System DNS returned no usable addresses yet.");
                    lastLoggedState = "empty";
                }
            }
            catch
            {
                if (lastLoggedState != "not-resolved")
                {
                    AppendLog("System DNS: not resolved yet.");
                    lastLoggedState = "not-resolved";
                }
            }

            await Task.Delay(1500, cancellationToken);
        }

        return false;
    }

    private async Task<string> RunDnsDiagnosticsAsync(string host)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Host: " + host);
        sb.AppendLine();

        var system = await ResolveWithSystemAsync(host);
        var cloudflare = await ResolveWithNslookupAsync(host, "1.1.1.1");
        var google = await ResolveWithNslookupAsync(host, "8.8.8.8");

        sb.AppendLine("System DNS:");
        sb.AppendLine(string.IsNullOrWhiteSpace(system) ? "  not resolved" : "  " + system);
        sb.AppendLine();
        sb.AppendLine("Cloudflare 1.1.1.1:");
        sb.AppendLine(string.IsNullOrWhiteSpace(cloudflare) ? "  not resolved" : "  " + cloudflare);
        sb.AppendLine();
        sb.AppendLine("Google 8.8.8.8:");
        sb.AppendLine(string.IsNullOrWhiteSpace(google) ? "  not resolved" : "  " + google);
        sb.AppendLine();

        if (string.IsNullOrWhiteSpace(system) && (!string.IsNullOrWhiteSpace(cloudflare) || !string.IsNullOrWhiteSpace(google)))
        {
            sb.AppendLine("Diagnosis:");
            sb.AppendLine("  The tunnel exists, but your system resolver cannot see it yet.");
            sb.AppendLine();
            sb.AppendLine("What to do:");
            foreach (var line in GetDnsAdviceLines())
            {
                sb.AppendLine("  " + line);
            }
        }
        else if (!string.IsNullOrWhiteSpace(system))
        {
            sb.AppendLine("Diagnosis:");
            sb.AppendLine("  System DNS can resolve the tunnel. If a browser still fails, the problem is likely browser-specific Secure DNS / DoH settings.");
        }
        else
        {
            sb.AppendLine("Diagnosis:");
            sb.AppendLine("  Neither system DNS nor public resolvers returned the tunnel host. The tunnel may have expired or cloudflared did not start correctly.");
        }

        return sb.ToString();
    }

    private static async Task<string> ResolveWithSystemAsync(string host)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var usable = addresses.Where(x => !IPAddress.IsLoopback(x)).ToArray();
            return string.Join(", ", usable.Select(x => x.ToString()));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> ResolveWithNslookupAsync(string host, string server)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nslookup",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(host);
            psi.ArgumentList.Add(server);

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var text = await stdoutTask + Environment.NewLine + await stderrTask;

            var matches = Regex.Matches(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            var values = matches.Cast<Match>().Select(x => x.Value).Distinct().ToArray();
            return string.Join(", ", values);
        }
        catch
        {
            return string.Empty;
        }
    }
    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });

        AppendLog("Opening in default browser: " + url);
        SetSummary("Opening public URL...");
    }
    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 620,
            Height = 360,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B1220")),
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Classes = { "primary" }
        };
        okButton.Click += (_, _) => dialog.Close();

        var messageBox = new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#020817")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F8FAFC")),
        };

        ScrollViewer.SetVerticalScrollBarVisibility(messageBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(messageBox, ScrollBarVisibility.Auto);

        var layout = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12,
        };
        layout.Children.Add(messageBox);
        layout.Children.Add(okButton);
        Grid.SetRow(okButton, 1);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }
}
