using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
    private readonly Button _translationStudioButton;
    private readonly Button _fixDnsButton;
    private readonly Button _dependenciesButton;
    private readonly Button _tunnelSetupButton;
    private readonly NumericUpDown _portNumeric;
    private readonly ComboBox _tunnelProviderComboBox;
    private readonly CheckBox _proxyModeCheckBox;
    private readonly CheckBox _autostartBrowserCheckBox;
    private readonly CheckBox _mergeTranslationCheckBox;
    private readonly Grid _detailedHeaderGrid;
    private readonly Grid _simpleHeaderGrid;
    private readonly ListBox _videosListBox;
    private readonly TextBox _logTextBox;
    private readonly TextBox _publicUrlTextBox;
    private readonly TextBox _tunnelAuthTextBox;
    private readonly TextBox _tunnelCliPathTextBox;
    private readonly ComboBox _languageComboBox;
    private readonly TextBlock _statusTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _busyTextBlock;
    private readonly ProgressBar _busyProgressBar;

    private readonly ObservableCollection<VideoEntry> _videoItems = new();
    private readonly VideoWorkspace _workspace = new();
    private readonly FfmpegService _ffmpegService = new();
    private readonly PlayerFilesService _playerFilesService = new();
    private readonly ServerRunner _serverRunner = new();
    private readonly TranslationStudioService _translationStudioService = new();

    private readonly SemaphoreSlim _autoMergeSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _autoMergeEventTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TranslationStudioJob> _translationStudioJobsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TranslationStudioSegment> _translationStudioSegmentsByPayloadUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _translationHttpClient = new(new HttpClientHandler { AllowAutoRedirect = true });
    private readonly object _logFileLock = new();
    private readonly string _persistentLogPath = Path.Combine(AppContext.BaseDirectory, "log.txt");
    private readonly Queue<string> _uiLogLines = new();
    private volatile bool _persistentLogEnabled;
    private const int MaxUiLogLines = 1500;

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _downloadsWatcherCts;
    private FileSystemWatcher? _downloadsWatcher;
    private CancellationTokenSource? _translationCallbackCts;
    private HttpListener? _translationCallbackListener;
    private Task? _translationCallbackLoopTask;
    private string? _translationCallbackUrl;
    private string? _publicUrl;
    private string? _lastAutoMergedAudioPath;
    private DateTime _lastAutoMergedAtUtc;
    private AppLanguage _uiLanguage = AppLanguage.English;
    private TunnelLaunchSettings _loadedTunnelSettings = new();
    private readonly TunnelProviderOption[] _tunnelProviders =
    [
        new() { Key = "cloudflared", DisplayName = "Cloudflare Tunnel" },
        new() { Key = "cloudflare-name", DisplayName = "Cloudflare Name", SupportsSetup = true },
        //new() { Key = "ngrok", DisplayName = "ngrok" },
        new() { Key = "localhost.run", DisplayName = "localhost.run" },
        new() { Key = "tailscale-funnel", DisplayName = "Tailscale Funnel" },
        new() { Key = "zrok", DisplayName = "zrok" },
        //new() { Key = "localtunnel", DisplayName = "LocalTunnel" },
        new() { Key = "xtunnel", DisplayName = "xTunnel" },
        //new() { Key = "vk-tunnel", DisplayName = "VK Tunnel" },
    ];


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
        _translationStudioButton = this.FindControl<Button>("TranslationStudioButton")!;
        _fixDnsButton = this.FindControl<Button>("FixDnsButton")!;
        _dependenciesButton = this.FindControl<Button>("DependenciesButton")!;
        _tunnelSetupButton = this.FindControl<Button>("TunnelSetupButton")!;
        _portNumeric = this.FindControl<NumericUpDown>("PortNumeric")!;
        _tunnelProviderComboBox = this.FindControl<ComboBox>("TunnelProviderComboBox")!;
        _proxyModeCheckBox = this.FindControl<CheckBox>("ProxyModeCheckBox")!;
        _autostartBrowserCheckBox = this.FindControl<CheckBox>("AutostartBrowserCheckBox")!;
        _mergeTranslationCheckBox = this.FindControl<CheckBox>("MergeTranslationCheckBox")!;
        _detailedHeaderGrid = this.FindControl<Grid>("DetailedHeaderGrid")!;
        _simpleHeaderGrid = this.FindControl<Grid>("SimpleHeaderGrid")!;
        _videosListBox = this.FindControl<ListBox>("VideosListBox")!;
        _logTextBox = this.FindControl<TextBox>("LogTextBox")!;
        _publicUrlTextBox = this.FindControl<TextBox>("PublicUrlTextBox")!;
        _tunnelAuthTextBox = this.FindControl<TextBox>("TunnelAuthTextBox")!;
        _tunnelCliPathTextBox = this.FindControl<TextBox>("TunnelCliPathTextBox")!;
        _languageComboBox = this.FindControl<ComboBox>("LanguageComboBox")!;
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")!;
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock")!;
        _busyTextBlock = this.FindControl<TextBlock>("BusyTextBlock")!;
        _busyProgressBar = this.FindControl<ProgressBar>("BusyProgressBar")!;

        _videosListBox.ItemsSource = _videoItems;
        _tunnelProviderComboBox.ItemsSource = _tunnelProviders;
        _tunnelProviderComboBox.SelectedItem = _tunnelProviders[0];
        var languageOptions = new LanguageOption[]
        {
            new(AppLanguage.English, "English"),
            new(AppLanguage.Russian, "Русский")
        };
        _languageComboBox.ItemsSource = languageOptions;
        _uiLanguage = AppLanguage.English;
        _languageComboBox.SelectedItem = languageOptions.FirstOrDefault(x => x.Language == _uiLanguage)
            ?? languageOptions[0];
        ApplyTunnelFieldHints();

        _browseButton.Click += BrowseButton_OnClick;
        _scanButton.Click += ScanButton_OnClick;
        _startButton.Click += StartButton_OnClick;
        _stopButton.Click += StopButton_OnClick;
        _copyUrlButton.Click += CopyUrlButton_OnClick;
        _openUrlButton.Click += OpenUrlButton_OnClick;
        _translationStudioButton.Click += TranslationStudioButton_OnClick;
        _fixDnsButton.Click += FixDnsButton_OnClick;
        _dependenciesButton.Click += DependenciesButton_OnClick;
        _tunnelSetupButton.Click += TunnelSetupButton_OnClick;
        _tunnelProviderComboBox.SelectionChanged += TunnelProviderComboBox_OnSelectionChanged;
        _languageComboBox.SelectionChanged += LanguageComboBox_OnSelectionChanged;
        _mergeTranslationCheckBox.IsCheckedChanged += MergeTranslationCheckBox_OnChanged;
        Closing += MainWindow_Closing;

        SetBusy(false);
        ApplyUiLanguage();
    }

    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_languageComboBox.SelectedItem is not LanguageOption option)
            return;

        _uiLanguage = option.Language;
        UiSettingsStore.SaveLanguage(_uiLanguage);
        ApplyUiLanguage();
    }

    private void ApplyUiLanguage()
    {
        UiLocalizer.Apply(this, _uiLanguage);
        ApplyTunnelFieldHints();
    }

    private string T(string text) => UiLocalizer.Translate(text, _uiLanguage);

    private TunnelLaunchSettings CollectTunnelSettings() => new()
    {
        TunnelAuth = (_tunnelAuthTextBox.Text ?? string.Empty).Trim(),
        CustomCliPath = (_tunnelCliPathTextBox.Text ?? string.Empty).Trim(),
        CloudflareNamedTunnelName = _loadedTunnelSettings.CloudflareNamedTunnelName,
        CloudflareNamedHostname = _loadedTunnelSettings.CloudflareNamedHostname,
    };

    private void LoadTunnelSettingsForFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _loadedTunnelSettings = new TunnelLaunchSettings();
            return;
        }

        var settings = TunnelSettingsStore.Load(folder);
        _loadedTunnelSettings = settings;
        _tunnelAuthTextBox.Text = settings.TunnelAuth ?? string.Empty;
        _tunnelCliPathTextBox.Text = settings.CustomCliPath ?? string.Empty;
        ApplyTunnelFieldHints();
    }

    private void SaveTunnelSettingsForFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        TunnelSettingsStore.Save(folder, CollectTunnelSettings());
    }

    private void ApplyTunnelFieldHints()
    {
        var provider = GetSelectedTunnelProvider().Key;
        switch (provider)
        {
            case "cloudflare-name":
                _tunnelAuthTextBox.IsEnabled = false;
                _tunnelAuthTextBox.Watermark = T("Use Setup... to configure tunnel name, hostname and Cloudflare login.");
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to cloudflared.exe");
                _tunnelSetupButton.IsVisible = true;
                break;
            case "zrok":
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Paste your zrok account token here for first-time enable. Leave empty if this machine is already enabled.");
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to zrok.exe / zrok");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "ngrok":
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Paste your ngrok authtoken here for first-time setup. Leave empty if ngrok is already configured.");
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to ngrok.exe / ngrok");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "localhost.run":
                _tunnelAuthTextBox.IsEnabled = false;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("localhost.run uses a free SSH tunnel via nokey@localhost.run. Tunnel auth is not used here.");
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to ssh.exe / ssh");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "tailscale-funnel":
                _tunnelAuthTextBox.IsEnabled = false;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Tailscale Funnel uses your existing Tailscale login on this machine. Tunnel auth is not used here.");
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to tailscale.exe / tailscale");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "localtunnel":
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Usually not needed for LocalTunnel. Some visitors may still see a password prompt on the public page.");
                _tunnelCliPathTextBox.Watermark = T("Optional path to lt / localtunnel / npx");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "xtunnel":
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Paste your xTunnel key here. The app will activate xTunnel automatically if needed.");
                _tunnelCliPathTextBox.Watermark = T("Optional custom path to xTunnel.exe");
                _tunnelSetupButton.IsVisible = false;
                break;
            case "vk-tunnel":
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("Usually not needed for VK Tunnel.");
                _tunnelCliPathTextBox.Watermark = T("Optional path to vk-tunnel.cmd / vk-tunnel.js. If empty, the app will try npx.");
                _tunnelSetupButton.IsVisible = false;
                break;
            default:
                _tunnelAuthTextBox.IsEnabled = true;
                _tunnelCliPathTextBox.IsEnabled = true;
                _tunnelAuthTextBox.Watermark = T("zrok: account token for enable. xTunnel: license key. Optional for Cloudflare / LocalTunnel / VK.");
                _tunnelCliPathTextBox.Watermark = T("Optional path to zrok / lt / xTunnel.exe / vk-tunnel.cmd / npx");
                _tunnelSetupButton.IsVisible = false;
                break;
        }
    }

    private void TunnelProviderComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyTunnelFieldHints();
    }

    private async void TunnelSetupButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Folder required", "Select a valid folder first.");
            return;
        }

        await ShowCloudflareNamedSetupWindowAsync(folder);
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = T("Select video folder"),
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
            LoadTunnelSettingsForFolder(path);
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
                "вЂў system DNS\nвЂў Cloudflare 1.1.1.1\nвЂў Google 8.8.8.8\n\n" +
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

    private async void TranslationStudioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_serverRunner.IsRunning || string.IsNullOrWhiteSpace(_publicUrl))
        {
            await ShowMessageAsync("Translation Studio", "Start a tunnel first. Translation Studio is available only while an active public tunnel is running.");
            return;
        }

        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Folder required", "Select a valid folder first.");
            return;
        }

        if (_workspace.RootFolder is null || !PathsEqual(_workspace.RootFolder, folder))
            await ScanAsync();

        if (_workspace.Entries.Count == 0)
        {
            await ShowMessageAsync("Translation Studio", "No supported videos were found in the selected folder.");
            return;
        }

        var window = new TranslationStudioWindow(
            _workspace.Entries.ToList(),
            _videosListBox.SelectedItem as VideoEntry,
            folder,
            GetSelectedTunnelProvider().DisplayName,
            GetPublicBaseUrl() ?? string.Empty,
            PrepareTranslationStudioJobAsync,
            LoadExistingTranslationStudioJobAsync,
            OpenTranslationStudioPlayerAsync,
            ImportTranslationStudioAudioAsync,
            MergeTranslationStudioSegmentAsync,
            BuildTranslationStudioFinalVideoAsync,
            SaveTranslationStudioJobAsync,
            _uiLanguage);

        await window.ShowDialog(this);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            StopAutoMergeWatcher();
            await StopTranslationCallbackServerAsync();
            try { await StopAsync(); } catch { }
        }
        finally
        {
            _translationHttpClient.Dispose();
            _autoMergeSemaphore.Dispose();
        }
    }

    private void MergeTranslationCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_mergeTranslationCheckBox.IsChecked == true)
        {
            if (_serverRunner.IsRunning)
            {
                try
                {
                    StartAutoMergeWatcher();
                    SetSummary("Auto merge is enabled.");
                }
                catch (Exception ex)
                {
                    AppendLog("Auto-merge watcher failed to start: " + ex);
                    _ = ShowMessageAsync("Auto merge", ex.Message);
                }
            }
        }
        else
        {
            StopAutoMergeWatcher();
            if (_serverRunner.IsRunning)
                SetSummary("Auto merge is disabled.");
        }
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
                ? string.Format(T("Found {0} video(s)."), _workspace.Entries.Count)
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
        BeginPersistentLogSession(folder, GetSelectedTunnelProvider().DisplayName);

        var selectedTunnel = GetSelectedTunnelProvider();
        if (selectedTunnel.Key.Equals("cloudflare-name", StringComparison.OrdinalIgnoreCase))
        {
            var currentSettings = CollectTunnelSettings();
            if (string.IsNullOrWhiteSpace(currentSettings.CloudflareNamedTunnelName) ||
                string.IsNullOrWhiteSpace(currentSettings.CloudflareNamedHostname) ||
                !CloudflareNamedTunnelService.HasOriginCertificate())
            {
                SetBusy(false);
                SetStatus("Setup required");
                SetSummary("Cloudflare Name needs setup before start.");
                await ShowCloudflareNamedSetupWindowAsync(folder);
                return;
            }
        }

        SetBusy(true, "Starting services...");
        try
        {
            var requiredToolNames = ToolResolver.GetRequiredToolNames(GetSelectedTunnelProvider().Key);
            var toolStatusesBeforeStart = ToolResolver.InspectAll(folder)
                .Where(x => requiredToolNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            foreach (var missingTool in toolStatusesBeforeStart.Where(x => !x.IsInstalled))
            {
                AppendLog($"Missing tool detected before start: {missingTool.DisplayName}.");
                AppendLog(missingTool.InstallationHint);
            }

            AppendLog("Ensuring helper tools...");
            var tunnelSettings = CollectTunnelSettings();
            SaveTunnelSettingsForFolder(folder);
            AppendLog("Selected tunnel provider: " + selectedTunnel.DisplayName);
            if (!string.IsNullOrWhiteSpace(tunnelSettings.CustomCliPath)) AppendLog("Custom tunnel CLI path: " + tunnelSettings.CustomCliPath);
            var tools = await ToolResolver.EnsureRequiredAsync(folder, selectedTunnel.Key, tunnelSettings, AppendLog, _runCts.Token);
            AppendLog("ffmpeg: " + tools.FfmpegPath);
            AppendLog("caddy: " + tools.CaddyPath);
            if (!string.IsNullOrWhiteSpace(tools.CloudflaredPath)) AppendLog("cloudflared: " + tools.CloudflaredPath);
            if (!string.IsNullOrWhiteSpace(tools.NgrokPath)) AppendLog("ngrok: " + tools.NgrokPath);
            if (!string.IsNullOrWhiteSpace(tools.SshPath)) AppendLog("ssh: " + tools.SshPath);
            if (!string.IsNullOrWhiteSpace(tools.TailscalePath)) AppendLog("tailscale: " + tools.TailscalePath);
            if (!string.IsNullOrWhiteSpace(tools.ZrokPath)) AppendLog("zrok: " + tools.ZrokPath);
            if (!string.IsNullOrWhiteSpace(tools.LocalTunnelPath)) AppendLog("localtunnel: " + tools.LocalTunnelPath);
            if (!string.IsNullOrWhiteSpace(tools.XTunnelPath)) AppendLog("xtunnel: " + tools.XTunnelPath);
            if (!string.IsNullOrWhiteSpace(tools.VkTunnelPath)) AppendLog("vk-tunnel: " + tools.VkTunnelPath);

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
            await EnsureTranslationCallbackServerAsync();
            await _playerFilesService.WriteAllAsync(folder, _workspace.Entries, "/translation-ready", _uiLanguage, _runCts.Token);

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
                selectedTunnel.Key,
                tools,
                tunnelSettings,
                AppendLog,
                _runCts.Token,
                selectedEntry?.PlaybackUrl,
                GetTranslationCallbackPort());

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
            SetSummary($"Local: {session.LocalUrl} | {selectedTunnel.DisplayName} is ready.");

            if (_autostartBrowserCheckBox.IsChecked == true)
            {
                AppendLog("Tunnel URL detected and autostart is enabled.");
                AppendLog("Waiting for DNS propagation...");
                _ = OpenBrowserWhenDnsReadyAsync(_publicUrl!, _runCts?.Token ?? CancellationToken.None);
            }

            if (_mergeTranslationCheckBox.IsChecked == true)
            {
                StartAutoMergeWatcher();
                AppendLog("Translation pipeline is active. The app will wait for direct translation callbacks and merge output automatically when possible.");
            }
            else
            {
                StopAutoMergeWatcher();
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

            if (selectedTunnel.Key.Equals("cloudflare-name", StringComparison.OrdinalIgnoreCase) &&
                (ex.Message.Contains("Cloudflare Name requires", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("Cloudflare login is required", StringComparison.OrdinalIgnoreCase)))
            {
                SetSummary("Cloudflare Name setup is incomplete.");
                await ShowCloudflareNamedSetupWindowAsync(folder);
                return;
            }

            var looksLikeToolIssue = ToolResolver.LooksLikeToolIssue(ex);
            var requiredToolNames = ToolResolver.GetRequiredToolNames(GetSelectedTunnelProvider().Key);
            var currentToolStatuses = ToolResolver.InspectAll(folder)
                .Where(x => requiredToolNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
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
            StopAutoMergeWatcher();
            await StopTranslationCallbackServerAsync();
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



    private int? GetTranslationCallbackPort()
    {
        if (string.IsNullOrWhiteSpace(_translationCallbackUrl))
            return null;

        return Uri.TryCreate(_translationCallbackUrl, UriKind.Absolute, out var uri) ? uri.Port : null;
    }

    private TunnelProviderOption GetSelectedTunnelProvider()
    {
        return _tunnelProviderComboBox.SelectedItem as TunnelProviderOption
            ?? _tunnelProviders[0];
    }

    private async Task ShowDependencySetupWindowAsync(string folder, string? startupError)
    {
        var window = new ToolSetupWindow(folder, startupError, AppendLog, _uiLanguage);
        await window.ShowDialog(this);
    }

    private async Task ShowCloudflareNamedSetupWindowAsync(string folder)
    {
        var settings = CollectTunnelSettings();

        var dialog = new Window
        {
            Title = T("Cloudflare Name Setup"),
            Width = 720,
            Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B1220")),
        };

        var tunnelNameBox = new TextBox
        {
            Text = settings.CloudflareNamedTunnelName ?? string.Empty,
            Watermark = T("Example: trading-course-tunnel")
        };

        var hostnameBox = new TextBox
        {
            Text = settings.CloudflareNamedHostname ?? string.Empty,
            Watermark = T("Example: video.example.com")
        };

        var statusBlock = new TextBlock
        {
            Text = CloudflareNamedTunnelService.HasOriginCertificate()
                ? T("Cloudflare login detected on this machine.")
                : T("Cloudflare login is not configured yet. Use Login to Cloudflare."),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#93C5FD")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var loginButton = new Button
        {
            Content = T("Login to Cloudflare"),
            Width = 180,
            Classes = { "warn" }
        };

        var saveButton = new Button
        {
            Content = T("Save"),
            Width = 110,
            Classes = { "primary" }
        };

        var cancelButton = new Button
        {
            Content = T("Cancel"),
            Width = 110
        };

        loginButton.Click += async (_, _) =>
        {
            try
            {
                statusBlock.Text = T("Starting Cloudflare login. Finish it in the browser window opened by cloudflared.");
                var tools = await ToolResolver.EnsureRequiredAsync(folder, "cloudflare-name", settings, AppendLog, CancellationToken.None);
                await CloudflareNamedTunnelService.LoginAsync(tools.CloudflaredPath!, folder, AppendLog, CancellationToken.None);
                statusBlock.Text = T("Cloudflare login completed. You can now save and use Start.");
            }
            catch (Exception ex)
            {
                statusBlock.Text = T("Cloudflare login failed:") + " " + ex.Message;
            }
        };

        saveButton.Click += (_, _) =>
        {
            var tunnelName = (tunnelNameBox.Text ?? string.Empty).Trim();
            var hostname = (hostnameBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(tunnelName) || string.IsNullOrWhiteSpace(hostname) || !hostname.Contains('.', StringComparison.Ordinal))
            {
                statusBlock.Text = T("Fill in a tunnel name and a valid public hostname.");
                return;
            }

            _loadedTunnelSettings.CloudflareNamedTunnelName = tunnelName;
            _loadedTunnelSettings.CloudflareNamedHostname = hostname;
            SaveTunnelSettingsForFolder(folder);
            statusBlock.Text = T("Cloudflare Name settings saved.");
            dialog.Close(true);
        };

        cancelButton.Click += (_, _) => dialog.Close(false);

        var layout = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowSpacing = 12,
            ColumnSpacing = 12
        };

        layout.Children.Add(new TextBlock { Text = T("Tunnel name"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        layout.Children.Add(tunnelNameBox);
        Grid.SetColumn(tunnelNameBox, 1);

        var hostnameLabel = new TextBlock { Text = T("Public hostname"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        layout.Children.Add(hostnameLabel);
        Grid.SetRow(hostnameLabel, 1);

        layout.Children.Add(hostnameBox);
        Grid.SetRow(hostnameBox, 1);
        Grid.SetColumn(hostnameBox, 1);

        var helpBlock = new TextBlock
        {
            Text = T("This provider uses a named public Cloudflare tunnel. The hostname must belong to a domain already added to your Cloudflare account."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CBD5E1"))
        };
        layout.Children.Add(helpBlock);
        Grid.SetRow(helpBlock, 2);
        Grid.SetColumnSpan(helpBlock, 2);

        layout.Children.Add(statusBlock);
        Grid.SetRow(statusBlock, 3);
        Grid.SetColumnSpan(statusBlock, 2);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        buttons.Children.Add(loginButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);

        layout.Children.Add(buttons);
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 2);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    private async Task EnsureTranslationCallbackServerAsync()
    {
        if (_translationCallbackListener is not null && _translationCallbackCts is { IsCancellationRequested: false } && !string.IsNullOrWhiteSpace(_translationCallbackUrl))
            return;

        await StopTranslationCallbackServerAsync();

        var port = PortHelper.GetFreeTcpPort(18761, 100);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        _translationCallbackListener = listener;
        _translationCallbackCts = new CancellationTokenSource();
        _translationCallbackUrl = $"http://127.0.0.1:{port}/translation-ready";
        _translationCallbackLoopTask = Task.Run(() => RunTranslationCallbackLoopAsync(listener, _translationCallbackCts.Token));

        AppendLog("Translation callback endpoint is ready: " + _translationCallbackUrl);
    }

    private async Task StopTranslationCallbackServerAsync()
    {
        var cts = _translationCallbackCts;
        var listener = _translationCallbackListener;
        var loopTask = _translationCallbackLoopTask;

        _translationCallbackCts = null;
        _translationCallbackListener = null;
        _translationCallbackLoopTask = null;
        _translationCallbackUrl = null;

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }

        if (loopTask is not null)
        {
            try { await loopTask; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (HttpListenerException) { }
        }

        cts?.Dispose();
    }

    private async Task<TranslationStudioJob> PrepareTranslationStudioJobAsync(TranslationStudioPrepareRequest request, CancellationToken cancellationToken)
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new InvalidOperationException("Select a valid folder first.");

        await EnsureTranslationCallbackServerAsync();

        var tools = await ToolResolver.EnsureRequiredAsync(
            folder,
            GetSelectedTunnelProvider().Key,
            CollectTunnelSettings(),
            AppendLog,
            cancellationToken);

        var job = await _translationStudioService.PrepareJobAsync(
            folder,
            request,
            tools.FfmpegPath,
            "/translation-ready",
            _uiLanguage,
            AppendLog,
            cancellationToken);

        RegisterTranslationStudioJob(job);
        AppendLog($"Translation Studio job prepared: {request.SourceEntry.Title}. Segments: {Math.Max(1, job.Segments.Count)}.");
        return job;
    }

    private async Task<TranslationStudioJob?> LoadExistingTranslationStudioJobAsync(VideoEntry sourceEntry, CancellationToken cancellationToken)
    {
        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new InvalidOperationException("Select a valid folder first.");

        var job = await _translationStudioService.LoadJobAsync(folder, sourceEntry.Id, cancellationToken);
        if (job is not null)
            RegisterTranslationStudioJob(job);

        return job;
    }

    private async Task<string> OpenTranslationStudioPlayerAsync(TranslationStudioJob job, TranslationStudioSegment? segment, CancellationToken cancellationToken)
    {
        if (!_serverRunner.IsRunning || string.IsNullOrWhiteSpace(_publicUrl))
            throw new InvalidOperationException("Start a public tunnel first. Translation Studio uses its own page only while the tunnel is active.");

        var publicBaseUrl = GetPublicBaseUrl();
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
            throw new InvalidOperationException("Public tunnel is not running. Start the tunnel first.");

        var playerUrl = _translationStudioService.BuildPublicPlayerUrl(publicBaseUrl, job, segment);
        await Dispatcher.UIThread.InvokeAsync(() => OpenUrl(playerUrl));
        return playerUrl;
    }

    private void RegisterTranslationStudioJob(TranslationStudioJob job)
    {
        _translationStudioJobsById[job.JobId] = job;

        foreach (var staleSegment in _translationStudioSegmentsByPayloadUrl.Where(x => string.Equals(x.Value.JobId, job.JobId, StringComparison.OrdinalIgnoreCase)).ToArray())
            _translationStudioSegmentsByPayloadUrl.TryRemove(staleSegment.Key, out _);

        foreach (var segment in job.Segments)
        {
            var key = NormalizeVideoSrc(segment.PayloadRelativeUrl);
            if (!string.IsNullOrWhiteSpace(key))
                _translationStudioSegmentsByPayloadUrl[key] = segment;
        }
    }

    private TranslationStudioSegment? ResolveTranslationStudioSegment(string? videoSrc)
    {
        var key = NormalizeVideoSrc(videoSrc);
        return string.IsNullOrWhiteSpace(key)
            ? null
            : _translationStudioSegmentsByPayloadUrl.TryGetValue(key, out var segment) ? segment : null;
    }

    private TranslationStudioJob? ResolveTranslationStudioJobById(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        return _translationStudioJobsById.TryGetValue(jobId, out var job) ? job : null;
    }

    private async Task<TranslationStudioJob> EnsureTranslationStudioJobLoadedAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = ResolveTranslationStudioJobById(jobId);
        if (job is not null)
            return job;

        var folder = (_folderTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new InvalidOperationException("Select a valid folder first.");

        job = await _translationStudioService.LoadJobAsync(folder, jobId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException("Translation Studio job was not found.");

        RegisterTranslationStudioJob(job);
        return job;
    }

    private async Task<TranslationStudioJob> ImportTranslationStudioAudioAsync(TranslationStudioJob job, TranslationStudioSegment segment, string sourceAudioPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceAudioPath) || !File.Exists(sourceAudioPath))
            throw new FileNotFoundException("Translated audio file was not found.", sourceAudioPath);

        Directory.CreateDirectory(Path.GetDirectoryName(segment.TranslatedAudioPath)!);
        FfmpegService.PrepareFreshOutputPath(segment.TranslatedAudioPath, AppendLog);
        File.Copy(sourceAudioPath, segment.TranslatedAudioPath, overwrite: true);
        if (!FfmpegService.TryUnblockFile(segment.TranslatedAudioPath, AppendLog))
            throw new InvalidOperationException("Copied file is still blocked: " + segment.TranslatedAudioPath);
        segment.LastTranslationSourceUrl = sourceAudioPath;
        segment.LastCallbackHandledAtUtc = DateTime.UtcNow;
        job.LastTranslationSourceUrl = sourceAudioPath;
        job.LastCallbackHandledAtUtc = DateTime.UtcNow;
        await _translationStudioService.SaveManifestAsync(job, cancellationToken);
        RegisterTranslationStudioJob(job);
        return job;
    }

    private async Task<TranslationStudioJob> MergeTranslationStudioSegmentAsync(TranslationStudioJob job, TranslationStudioSegment segment, CancellationToken cancellationToken)
    {
        var ffmpegPath = await ToolResolver.EnsureFfmpegOnlyAsync(job.WorkspaceRoot, AppendLog, cancellationToken);
        await MergeTranslationStudioSegmentCoreAsync(segment, ffmpegPath, cancellationToken);
        await _translationStudioService.SaveManifestAsync(job, cancellationToken);
        RegisterTranslationStudioJob(job);
        return job;
    }

    private async Task<TranslationStudioJob> BuildTranslationStudioFinalVideoAsync(TranslationStudioJob job, CancellationToken cancellationToken)
    {
        var ffmpegPath = await ToolResolver.EnsureFfmpegOnlyAsync(job.WorkspaceRoot, AppendLog, cancellationToken);
        await BuildTranslationStudioFinalVideoCoreAsync(job, ffmpegPath, cancellationToken);
        await _translationStudioService.SaveManifestAsync(job, cancellationToken);
        RegisterTranslationStudioJob(job);
        return job;
    }

    private async Task<TranslationStudioJob> SaveTranslationStudioJobAsync(TranslationStudioJob job, CancellationToken cancellationToken)
    {
        await _translationStudioService.SaveManifestAsync(job, cancellationToken);
        RegisterTranslationStudioJob(job);
        return job;
    }

    private async Task HandleTranslationStudioCallbackAsync(TranslationStudioJob job, TranslationStudioSegment segment, TranslationCallbackPayload payload, CancellationToken cancellationToken)
    {

        await _autoMergeSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.Url) &&
                string.Equals(segment.LastTranslationSourceUrl, payload.Url, StringComparison.OrdinalIgnoreCase) &&
                segment.LastCallbackHandledAtUtc is DateTime lastHandledAtUtc &&
                DateTime.UtcNow - lastHandledAtUtc < TimeSpan.FromSeconds(30))
            {
                return;
            }

            var shouldRefreshAudio = TranslationStudioFileState.NeedsRefreshFromSource(
                segment.TranslatedAudioPath,
                segment.LastTranslationSourceUrl,
                payload.Url);

            if (!shouldRefreshAudio)
            {
                AppendLog($"Translation Studio audio already exists for segment {segment.Index}/{segment.TotalSegments}. Skipping download: {segment.TranslatedAudioPath}");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(segment.TranslatedAudioPath)!);
                await DownloadTranslationAudioAsync(payload, segment.TranslatedAudioPath, cancellationToken);
                AppendLog($"Translation Studio audio saved for segment {segment.Index}/{segment.TotalSegments}: {segment.TranslatedAudioPath}");
                segment.LastTranslationSourceUrl = payload.Url;
                job.LastTranslationSourceUrl = payload.Url;
            }

            if (!string.IsNullOrWhiteSpace(payload.SubtitlesUrl) &&
    !string.IsNullOrWhiteSpace(segment.TranslatedSubtitlesPath))
            {
                if (TranslationStudioFileState.IsUsableFile(segment.TranslatedSubtitlesPath) && !shouldRefreshAudio)
                {
                    AppendLog($"Translation Studio subtitles already exist for segment {segment.Index}/{segment.TotalSegments}. Skipping download: {segment.TranslatedSubtitlesPath}");
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(segment.TranslatedSubtitlesPath)!);
                    await DownloadTextFileAsync(payload.SubtitlesUrl, segment.TranslatedSubtitlesPath, cancellationToken);
                    AppendLog($"Translation Studio subtitles saved for segment {segment.Index}/{segment.TotalSegments}: {segment.TranslatedSubtitlesPath}");
                }
            }
            segment.LastCallbackHandledAtUtc = DateTime.UtcNow;
            job.LastCallbackHandledAtUtc = DateTime.UtcNow;

            if (job.AutoMergeOnCallback)
            {
                var ffmpegPath = await ToolResolver.EnsureFfmpegOnlyAsync(job.WorkspaceRoot, AppendLog, cancellationToken);
                await MergeTranslationStudioSegmentCoreAsync(segment, ffmpegPath, cancellationToken);
                await TryFinalizeTranslationStudioJobAsync(job, ffmpegPath, cancellationToken);
            }

            await _translationStudioService.SaveManifestAsync(job, cancellationToken);
            RegisterTranslationStudioJob(job);
        }
        catch (Exception ex)
        {
            AppendLog($"Translation Studio callback failed for segment {segment.Index}/{segment.TotalSegments}: {ex.Message}");
        }
        finally
        {
            _autoMergeSemaphore.Release();
        }
    }

    private async Task MergeTranslationStudioSegmentCoreAsync(TranslationStudioSegment segment, string ffmpegPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(segment.TranslatedAudioPath))
            throw new InvalidOperationException($"Translated audio is missing for segment {segment.Index}/{segment.TotalSegments}.");

        if (TranslationStudioFileState.IsMergedSegmentCurrent(segment))
        {
            AppendLog($"Translation Studio merged segment already exists. Skipping merge: {segment.MergedOutputPath}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(segment.MergedOutputPath)!);
        await _ffmpegService.MergeVideoWithExternalAudioAsync(
            ffmpegPath,
            segment.SourceSegmentPath,
            segment.TranslatedAudioPath,
            segment.MergedOutputPath,
            AppendLog,
            cancellationToken);

        AppendLog($"Translation Studio merged segment saved: {segment.MergedOutputPath}");
    }

    private async Task TryFinalizeTranslationStudioJobAsync(TranslationStudioJob job, string ffmpegPath, CancellationToken cancellationToken)
    {
        foreach (var segment in job.Segments.OrderBy(x => x.Index))
        {
            if (TranslationStudioFileState.IsMergedSegmentCurrent(segment))
                continue;

            if (!File.Exists(segment.TranslatedAudioPath))
                continue;

            await MergeTranslationStudioSegmentCoreAsync(segment, ffmpegPath, cancellationToken);
        }

        var mergedSegments = job.Segments
            .OrderBy(x => x.Index)
            .Where(TranslationStudioFileState.IsMergedSegmentCurrent)
            .ToList();

        if (job.Segments.Count == 1)
        {
            if (mergedSegments.Count == 1)
                await BuildTranslationStudioFinalVideoCoreAsync(job, ffmpegPath, cancellationToken);

            return;
        }

        if (mergedSegments.Count != job.Segments.Count)
        {
            AppendLog($"Translation Studio is waiting for more translated segments before final concat: {mergedSegments.Count}/{job.Segments.Count} ready.");
            return;
        }

        await BuildTranslationStudioFinalVideoCoreAsync(job, ffmpegPath, cancellationToken);
    }

    private async Task BuildTranslationStudioFinalVideoCoreAsync(TranslationStudioJob job, string ffmpegPath, CancellationToken cancellationToken)
    {
        if (job.Segments.Count == 0)
            throw new InvalidOperationException("Translation Studio job does not contain any segments.");

        if (TranslationStudioFileState.IsFinalOutputCurrent(job))
        {
            AppendLog("Translation Studio final output already exists. Skipping build: " + job.FinalOutputPath);
            return;
        }

        if (job.Segments.Count == 1)
        {
            var segment = job.Segments[0];
            if (!File.Exists(segment.TranslatedAudioPath))
                throw new InvalidOperationException("Translated audio for the only segment is missing.");

            Directory.CreateDirectory(Path.GetDirectoryName(job.FinalOutputPath)!);
            await _ffmpegService.MergeVideoWithExternalAudioAsync(
                ffmpegPath,
                segment.SourceSegmentPath,
                segment.TranslatedAudioPath,
                job.FinalOutputPath,
                AppendLog,
                cancellationToken);

            AppendLog("Translation Studio final video saved to: " + job.FinalOutputPath);
            return;
        }

        foreach (var segment in job.Segments.OrderBy(x => x.Index))
        {
            if (TranslationStudioFileState.IsMergedSegmentCurrent(segment))
                continue;

            if (File.Exists(segment.TranslatedAudioPath))
            {
                await MergeTranslationStudioSegmentCoreAsync(segment, ffmpegPath, cancellationToken);
                continue;
            }
        }

        var missingSegments = job.Segments
            .Where(x => !TranslationStudioFileState.IsMergedSegmentCurrent(x))
            .OrderBy(x => x.Index)
            .ToList();

        if (missingSegments.Count > 0)
            throw new InvalidOperationException($"Cannot build final video yet. Merged segments ready: {job.Segments.Count - missingSegments.Count}/{job.Segments.Count}.");

        await _ffmpegService.ConcatVideosAsync(
            ffmpegPath,
            job.Segments.OrderBy(x => x.Index).Select(x => x.MergedOutputPath).ToList(),
            job.FinalOutputPath,
            cancellationToken);

        AppendLog("Translation Studio final video saved to: " + job.FinalOutputPath);
    }

    private async Task HandleTranslationStudioApiAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"method not allowed\"}");
                return;
            }

            var jobId = context.Request.QueryString["jobId"];
            if (string.IsNullOrWhiteSpace(jobId))
            {
                context.Response.StatusCode = 400;
                await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"jobId is required\"}");
                return;
            }

            var job = await EnsureTranslationStudioJobLoadedAsync(jobId, cancellationToken);

            if (path.EndsWith("/import-audio", StringComparison.OrdinalIgnoreCase))
            {
                var segmentId = context.Request.QueryString["segmentId"];
                var segment = job.Segments.FirstOrDefault(x => string.Equals(x.SegmentId, segmentId, StringComparison.OrdinalIgnoreCase));
                if (segment is null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"segment not found\"}");
                    return;
                }

                if (!context.Request.HasEntityBody)
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"audio payload is missing\"}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(segment.TranslatedAudioPath)!);
                await using (var output = new FileStream(segment.TranslatedAudioPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await context.Request.InputStream.CopyToAsync(output, cancellationToken);
                }

                segment.LastTranslationSourceUrl = context.Request.Headers["X-File-Name"] ?? "browser-upload";
                segment.LastCallbackHandledAtUtc = DateTime.UtcNow;
                job.LastTranslationSourceUrl = segment.LastTranslationSourceUrl;
                job.LastCallbackHandledAtUtc = segment.LastCallbackHandledAtUtc;

                await _translationStudioService.SaveManifestAsync(job, cancellationToken);
                RegisterTranslationStudioJob(job);
                await WriteJsonResponseAsync(context.Response, JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return;
            }

            if (path.EndsWith("/merge-segment", StringComparison.OrdinalIgnoreCase))
            {
                var segmentId = context.Request.QueryString["segmentId"];
                var segment = job.Segments.FirstOrDefault(x => string.Equals(x.SegmentId, segmentId, StringComparison.OrdinalIgnoreCase));
                if (segment is null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"segment not found\"}");
                    return;
                }

                job = await MergeTranslationStudioSegmentAsync(job, segment, cancellationToken);
                await WriteJsonResponseAsync(context.Response, JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return;
            }

            if (path.EndsWith("/build-final", StringComparison.OrdinalIgnoreCase))
            {
                job = await BuildTranslationStudioFinalVideoAsync(job, cancellationToken);
                await WriteJsonResponseAsync(context.Response, JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return;
            }

            context.Response.StatusCode = 404;
            await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"unknown studio action\"}");
        }
        catch (Exception ex)
        {
            AppendLog("Translation Studio API failed: " + ex.Message);
            context.Response.StatusCode = 500;
            await WriteJsonResponseAsync(context.Response, JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
        }
    }

    private async Task RunTranslationCallbackLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleTranslationCallbackAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleTranslationCallbackAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS, GET";

            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            var requestPath = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (!requestPath.Equals("/translation-ready", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            TranslationCallbackPayload? payload;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                var body = await reader.ReadToEndAsync(cancellationToken);
                payload = JsonSerializer.Deserialize<TranslationCallbackPayload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Url))
            {
                context.Response.StatusCode = 400;
                await WriteJsonResponseAsync(context.Response, "{\"ok\":false,\"error\":\"translation url is missing\"}");
                return;
            }

            AppendLog("Received translation URL from player: " + payload.Url);
            context.Response.StatusCode = 202;
            await WriteJsonResponseAsync(context.Response, "{\"ok\":true}");

            var studioSegment = ResolveTranslationStudioSegment(payload.VideoSrc);
            var studioJob = ResolveTranslationStudioJobById(studioSegment?.JobId);
            if (studioSegment is not null && studioJob is not null)
            {
                AppendLog($"Translation callback matched Translation Studio segment {studioSegment.Index}/{studioSegment.TotalSegments}: {studioJob.SourceVideoTitle}");
                _ = Task.Run(() => HandleTranslationStudioCallbackAsync(studioJob, studioSegment, payload, _translationCallbackCts?.Token ?? CancellationToken.None));
                return;
            }

            var normalizedVideoSrc = NormalizeVideoSrc(payload.VideoSrc);
            if (!string.IsNullOrWhiteSpace(normalizedVideoSrc) &&
                normalizedVideoSrc.Contains("/_translation-studio/", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("Translation Studio callback did not match any prepared payload: " + normalizedVideoSrc);
            }

            _ = Task.Run(() => TryAutoMergeFromTranslationUrlAsync(payload, _translationCallbackCts?.Token ?? CancellationToken.None));
        }
        catch (Exception ex)
        {
            AppendLog("Translation callback handling failed: " + ex);
            try
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context.Response, "{\"ok\":false}");
            }
            catch { }
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse response, string json)
    {
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private void StartAutoMergeWatcher()
    {
        StopAutoMergeWatcher();

        _downloadsWatcherCts = new CancellationTokenSource();
        AppendLog("Auto merge is armed. The app will use direct translation callbacks and save audio into translated_audio.");
    }

    private void StopAutoMergeWatcher()
    {
        _downloadsWatcherCts?.Cancel();
        _downloadsWatcherCts?.Dispose();
        _downloadsWatcherCts = null;

        if (_downloadsWatcher is not null)
        {
            _downloadsWatcher.EnableRaisingEvents = false;
            _downloadsWatcher.Created -= DownloadsWatcher_OnChanged;
            _downloadsWatcher.Changed -= DownloadsWatcher_OnChanged;
            _downloadsWatcher.Renamed -= DownloadsWatcher_OnRenamed;
            _downloadsWatcher.Dispose();
            _downloadsWatcher = null;
        }

        _autoMergeEventTimes.Clear();
    }

    private void DownloadsWatcher_OnChanged(object sender, FileSystemEventArgs e) => QueueAutoMerge(e.FullPath);

    private void DownloadsWatcher_OnRenamed(object sender, RenamedEventArgs e) => QueueAutoMerge(e.FullPath);

    private void QueueAutoMerge(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !IsSupportedTranslatedAudio(fullPath))
            return;

        var now = DateTime.UtcNow;
        if (_autoMergeEventTimes.TryGetValue(fullPath, out var lastEventUtc) && (now - lastEventUtc) < TimeSpan.FromSeconds(3))
            return;

        _autoMergeEventTimes[fullPath] = now;
        _ = Task.Run(() => TryAutoMergeDownloadedAudioAsync(fullPath, _downloadsWatcherCts?.Token ?? CancellationToken.None));
    }

    private async Task TryAutoMergeDownloadedAudioAsync(string translatedAudioPath, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1200, cancellationToken);
            if (!await WaitForFileReadyAsync(translatedAudioPath, cancellationToken))
                return;

            var callbackPayload = new TranslationCallbackPayload
            {
                Url = new Uri(translatedAudioPath).AbsoluteUri,
                VideoSrc = null,
                VideoTitle = Path.GetFileNameWithoutExtension(translatedAudioPath),
                SuggestedFileName = Path.GetFileName(translatedAudioPath),
            };

            await TryAutoMergeCoreAsync(
                callbackPayload,
                cancellationToken,
                async (storedAudioPath, _, innerToken) =>
                {
                    FfmpegService.PrepareFreshOutputPath(storedAudioPath, AppendLog);
                    File.Copy(translatedAudioPath, storedAudioPath, true);
                    if (!FfmpegService.TryUnblockFile(storedAudioPath, AppendLog))
                        throw new InvalidOperationException("Copied file is still blocked: " + storedAudioPath);
                    await Task.CompletedTask;
                    AppendLog("Copied translated audio to: " + storedAudioPath);
                    return translatedAudioPath;
                });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendLog("Auto-merge failed: " + ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _ = ShowMessageAsync("Auto merge", ex.Message);
            });
        }
    }

    private async Task TryAutoMergeFromTranslationUrlAsync(TranslationCallbackPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            await TryAutoMergeCoreAsync(
                payload,
                cancellationToken,
                async (storedAudioPath, sourceEntry, innerToken) =>
                {
                    await DownloadTranslationAudioAsync(payload, storedAudioPath, innerToken);
                    AppendLog("Downloaded translated audio to: " + storedAudioPath);
                    return payload.Url;
                });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendLog("Direct translation pipeline failed: " + ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _ = ShowMessageAsync("Auto merge", ex.Message);
            });
        }
    }

    private async Task TryAutoMergeCoreAsync(
        TranslationCallbackPayload payload,
        CancellationToken cancellationToken,
        Func<string, VideoEntry, CancellationToken, Task<string>> acquireAudioAsync)
    {
        await _autoMergeSemaphore.WaitAsync(cancellationToken);
        try
        {
            var autoMergeEnabled = await Dispatcher.UIThread.InvokeAsync(() => _mergeTranslationCheckBox.IsChecked == true);
            if (!_serverRunner.IsRunning)
                return;

            if (!string.IsNullOrWhiteSpace(payload.Url)
                && string.Equals(_lastAutoMergedAudioPath, payload.Url, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - _lastAutoMergedAtUtc < TimeSpan.FromSeconds(30))
            {
                return;
            }

            var selectedEntry = await Dispatcher.UIThread.InvokeAsync(() => _videosListBox.SelectedItem as VideoEntry);
            var targetEntry = ResolveTargetVideoEntry(payload, selectedEntry);
            if (targetEntry is null)
            {
                AppendLog("Auto-merge skipped: no matching video was found for the translated audio.");
                return;
            }

            var folder = await Dispatcher.UIThread.InvokeAsync(() => (_folderTextBox.Text ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                AppendLog("Auto-merge skipped: working folder is unavailable.");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => SetBusy(true, "Auto merge in progress..."));
            SetSummary("Translated audio detected. Building output file...");

            var selectedTunnel = await Dispatcher.UIThread.InvokeAsync(GetSelectedTunnelProvider);
            var tunnelSettings = await Dispatcher.UIThread.InvokeAsync(CollectTunnelSettings);
            var tools = await ToolResolver.EnsureRequiredAsync(folder, selectedTunnel.Key, tunnelSettings, AppendLog, cancellationToken);

            var outputFolder = Path.Combine(folder, "merged_output");
            var outputPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(targetEntry.FileName) + ".translated.mp4");
            if (autoMergeEnabled && IsUsableFile(outputPath))
            {
                AppendLog("Merged translated video already exists. Skipping conversion: " + outputPath);
                MarkTranslationPayloadHandled(payload);
                SetStatus("Running");
                SetSummary("Translated video already exists. Conversion was skipped.");
                return;
            }

            var storedAudioPath = BuildStoredTranslatedAudioPath(folder, targetEntry.FileName, payload.SuggestedFileName ?? payload.Url);
            Directory.CreateDirectory(Path.GetDirectoryName(storedAudioPath)!);
            var sourceId = payload.Url;
            if (IsUsableFile(storedAudioPath))
            {
                AppendLog("Translated audio already exists. Skipping download/copy: " + storedAudioPath);
            }
            else
            {
                sourceId = await acquireAudioAsync(storedAudioPath, targetEntry, cancellationToken);
            }

            _lastAutoMergedAudioPath = sourceId;
            _lastAutoMergedAtUtc = DateTime.UtcNow;

            if (!autoMergeEnabled)
            {
                AppendLog("Translated audio saved to: " + storedAudioPath);
                SetStatus("Running");
                SetSummary("Translated audio was saved to translated_audio. Auto merge is off.");
                return;
            }

            await _ffmpegService.MergeVideoWithExternalAudioAsync(
                tools.FfmpegPath,
                targetEntry.FullPath,
                storedAudioPath,
                outputPath,
                AppendLog,
                cancellationToken);

            AppendLog("Merged translated video saved to: " + outputPath);
            SetStatus("Running");
            SetSummary("Translated audio was merged successfully. Output saved to merged_output.");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => SetBusy(false));
            _autoMergeSemaphore.Release();
        }
    }

    private void MarkTranslationPayloadHandled(TranslationCallbackPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Url))
            return;

        _lastAutoMergedAudioPath = payload.Url;
        _lastAutoMergedAtUtc = DateTime.UtcNow;
    }

    private VideoEntry? ResolveTargetVideoEntry(TranslationCallbackPayload payload, VideoEntry? selectedEntry)
    {
        if (!string.IsNullOrWhiteSpace(payload.VideoSrc))
        {
            var normalized = NormalizeVideoSrc(payload.VideoSrc);
            var matched = _workspace.Entries.FirstOrDefault(x =>
                string.Equals(NormalizeVideoSrc(x.VotHelperUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeVideoSrc(x.PlaybackUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeVideoSrc(x.TranslationUrl), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeVideoSrc(x.OriginalUrl), normalized, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
                return matched;
        }

        return selectedEntry;
    }

    private static string NormalizeVideoSrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        string path;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }
        else
        {
            var queryIndex = trimmed.IndexOfAny(['?', '#']);
            path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        }

        path = Uri.UnescapeDataString(path.Replace('\\', '/')).Trim();
        path = path.TrimStart('.');
        if (!path.StartsWith('/'))
            path = "/" + path.TrimStart('/');

        return path;
    }
    private async Task DownloadTextFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        if (IsUsableFile(destinationPath))
        {
            AppendLog("Text file already exists. Skipping download: " + destinationPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = Uri.TryCreate(_publicUrl, UriKind.Absolute, out var publicUri) ? publicUri : null;
        request.Headers.UserAgent.ParseAdd("PublicVideoLauncher/1.0");

        using var response = await _translationHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        await File.WriteAllTextAsync(destinationPath, text, Encoding.UTF8, cancellationToken);
        FfmpegService.TryUnblockFile(destinationPath);
    }
    private async Task DownloadTranslationAudioAsync(TranslationCallbackPayload payload, string destinationPath, CancellationToken cancellationToken)
    {
        if (IsUsableFile(destinationPath))
        {
            AppendLog("Translated audio already exists. Skipping download: " + destinationPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        FfmpegService.PrepareFreshOutputPath(destinationPath, AppendLog);

        using var request = new HttpRequestMessage(HttpMethod.Get, payload.Url);
        request.Headers.Referrer = Uri.TryCreate(_publicUrl, UriKind.Absolute, out var publicUri) ? publicUri : null;
        request.Headers.UserAgent.ParseAdd("PublicVideoLauncher/1.0");

        using var response = await _translationHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        if (!FfmpegService.TryUnblockFile(destinationPath, AppendLog))
            throw new InvalidOperationException("Downloaded file is still blocked: " + destinationPath);
    }

    private static bool IsUsableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildStoredTranslatedAudioPath(string rootFolder, string videoFileName, string translatedAudioSource)
    {
        var extension = GetTranslatedAudioExtension(translatedAudioSource);
        var baseName = Path.GetFileNameWithoutExtension(videoFileName);
        var outputFolder = Path.Combine(rootFolder, "translated_audio");
        return Path.Combine(outputFolder, baseName + ".translated" + extension);
    }

    private static string GetTranslatedAudioExtension(string translatedAudioSource)
    {
        var extension = Path.GetExtension(translatedAudioSource);
        if (string.IsNullOrWhiteSpace(extension) && Uri.TryCreate(translatedAudioSource, UriKind.Absolute, out var uri))
            extension = Path.GetExtension(uri.AbsolutePath);

        return string.IsNullOrWhiteSpace(extension) ? ".mp3" : extension;
    }

    private static async Task<bool> WaitForFileReadyAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                await Task.Delay(400, cancellationToken);
                continue;
            }

            try
            {
                var initialLength = new FileInfo(path).Length;
                await Task.Delay(700, cancellationToken);
                var nextLength = new FileInfo(path).Length;
                if (initialLength != nextLength || nextLength <= 0)
                    continue;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return stream.Length > 0;
            }
            catch (IOException)
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return false;
    }

    private static bool IsSupportedTranslatedAudio(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TranslationCallbackPayload
    {
        public string Url { get; set; } = string.Empty;
        public string? VideoSrc { get; set; }
        public string? VideoTitle { get; set; }
        public string? SuggestedFileName { get; set; }

        public string? SubtitlesUrl { get; set; }
        public string? SuggestedSubtitlesFileName { get; set; }
    }

    private static string? GetDownloadsFolderPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var downloads = Path.Combine(home, "Downloads");
            if (Directory.Exists(downloads))
                return downloads;

            downloads = Path.Combine(home, "Р—Р°РіСЂСѓР·РєРё");
            if (Directory.Exists(downloads))
                return downloads;
        }

        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Directory.Exists(fallback) ? fallback : null;
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
        _translationStudioButton.IsEnabled = !busy && _serverRunner.IsRunning;
        _tunnelSetupButton.IsEnabled = !busy && !_serverRunner.IsRunning;
        _mergeTranslationCheckBox.IsEnabled = !busy;
        _proxyModeCheckBox.IsEnabled = !busy && !_serverRunner.IsRunning;
        _portNumeric.IsEnabled = !busy && !_serverRunner.IsRunning;
        _tunnelProviderComboBox.IsEnabled = !busy && !_serverRunner.IsRunning;
        _autostartBrowserCheckBox.IsEnabled = !busy;

        _startButton.IsEnabled = !busy && !_serverRunner.IsRunning;
        _stopButton.IsEnabled = busy || _serverRunner.IsRunning;

        Dispatcher.UIThread.Post(() =>
        {
            _busyProgressBar.IsVisible = busy;
            _busyTextBlock.IsVisible = busy;
            _busyTextBlock.Text = busy
                ? T(string.IsNullOrWhiteSpace(status) ? "Working..." : status)
                : string.Empty;
        });

        if (!string.IsNullOrWhiteSpace(status))
            SetStatus(status);
        else if (!busy && !_serverRunner.IsRunning)
            SetStatus("Idle");
        else if (!busy && _serverRunner.IsRunning)
            SetStatus("Running");
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => _statusTextBlock.Text = T(text));
    private void SetSummary(string text) => Dispatcher.UIThread.Post(() => _summaryTextBlock.Text = T(text));

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {T(message)}";
        if (_persistentLogEnabled)
            AppendPersistentLogLine(line);

        Dispatcher.UIThread.Post(() =>
        {
            _uiLogLines.Enqueue(line);
            while (_uiLogLines.Count > MaxUiLogLines)
                _uiLogLines.Dequeue();

            _logTextBox.Text = string.Join(Environment.NewLine, _uiLogLines);
            _logTextBox.CaretIndex = (_logTextBox.Text ?? string.Empty).Length;
        });
    }

    private void BeginPersistentLogSession(string folder, string selectedTunnelDisplayName)
    {
        try
        {
            Directory.CreateDirectory(AppContext.BaseDirectory);
            var divider = new string('-', 80);
            var header = $"{divider}{Environment.NewLine}START {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Tunnel: {selectedTunnelDisplayName} | Folder: {folder}{Environment.NewLine}";
            lock (_logFileLock)
            {
                File.AppendAllText(_persistentLogPath, header, Encoding.UTF8);
            }

            _persistentLogEnabled = true;
        }
        catch
        {
            _persistentLogEnabled = false;
        }
    }

    private void AppendPersistentLogLine(string line)
    {
        try
        {
            lock (_logFileLock)
            {
                File.AppendAllText(_persistentLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            _persistentLogEnabled = false;
        }
    }

    private string? GetPublicHost()
    {
        if (string.IsNullOrWhiteSpace(_publicUrl))
            return null;

        if (!Uri.TryCreate(_publicUrl, UriKind.Absolute, out var uri))
            return null;

        return uri.Host;
    }

    private string? GetPublicBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_publicUrl))
            return null;

        return Uri.TryCreate(_publicUrl, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
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
            Title = T(title),
            Width = 620,
            Height = 360,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B1220")),
        };

        var okButton = new Button
        {
            Content = T("OK"),
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Classes = { "primary" }
        };
        okButton.Click += (_, _) => dialog.Close();

        var messageBox = new TextBox
        {
            Text = T(message),
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
