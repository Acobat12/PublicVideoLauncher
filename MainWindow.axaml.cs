using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
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
    private readonly Button _fixDnsButton;
    private readonly Button _dependenciesButton;
    private readonly NumericUpDown _portNumeric;
    private readonly CheckBox _proxyModeCheckBox;
    private readonly CheckBox _autostartBrowserCheckBox;
    private readonly CheckBox _mergeTranslationCheckBox;
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

    private readonly SemaphoreSlim _autoMergeSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _autoMergeEventTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _translationHttpClient = new(new HttpClientHandler { AllowAutoRedirect = true });

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
        _mergeTranslationCheckBox = this.FindControl<CheckBox>("MergeTranslationCheckBox")!;
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
        _mergeTranslationCheckBox.Checked += MergeTranslationCheckBox_OnChanged;
        _mergeTranslationCheckBox.Unchecked += MergeTranslationCheckBox_OnChanged;
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
                    SetSummary("Автоподхват перевода включён.");
                }
                catch (Exception ex)
                {
                    AppendLog("Auto-merge watcher failed to start: " + ex);
                    _ = ShowMessageAsync("Автоподхват перевода", ex.Message);
                }
            }
        }
        else
        {
            StopAutoMergeWatcher();
            if (_serverRunner.IsRunning)
                SetSummary("Автоподхват перевода выключен.");
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
            await EnsureTranslationCallbackServerAsync();
            await _playerFilesService.WriteAllAsync(folder, _workspace.Entries, "/translation-ready", _runCts.Token);

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
            SetSummary($"Local: {session.LocalUrl} | Public tunnel is ready.");

            if (_autostartBrowserCheckBox.IsChecked == true)
            {
                AppendLog("Tunnel URL detected and autostart is enabled.");
                AppendLog("Waiting for DNS propagation...");
                _ = OpenBrowserWhenDnsReadyAsync(_publicUrl!, _runCts?.Token ?? CancellationToken.None);
            }

            if (_mergeTranslationCheckBox.IsChecked == true)
            {
                StartAutoMergeWatcher();
                AppendLog("Translation pipeline is active. Waiting for direct translation URLs. Downloads watcher is only a fallback.");
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

    private async Task ShowDependencySetupWindowAsync(string folder, string? startupError)
    {
        var window = new ToolSetupWindow(folder, startupError, AppendLog);
        await window.ShowDialog(this);
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
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";

            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (!context.Request.Url?.AbsolutePath.Equals("/translation-ready", StringComparison.OrdinalIgnoreCase) ?? true)
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

        var downloadsFolder = GetDownloadsFolderPath();
        if (string.IsNullOrWhiteSpace(downloadsFolder) || !Directory.Exists(downloadsFolder))
        {
            AppendLog("Downloads fallback is unavailable. Direct translation URL pipeline will stay active.");
            return;
        }

        _downloadsWatcherCts = new CancellationTokenSource();
        _downloadsWatcher = new FileSystemWatcher(downloadsFolder)
        {
            IncludeSubdirectories = false,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        _downloadsWatcher.Created += DownloadsWatcher_OnChanged;
        _downloadsWatcher.Changed += DownloadsWatcher_OnChanged;
        _downloadsWatcher.Renamed += DownloadsWatcher_OnRenamed;

        AppendLog("Watching Downloads as fallback for translated audio: " + downloadsFolder);
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
                    File.Copy(translatedAudioPath, storedAudioPath, true);
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
                _ = ShowMessageAsync("Автосборка перевода", ex.Message);
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
                _ = ShowMessageAsync("Автосборка перевода", ex.Message);
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

            await Dispatcher.UIThread.InvokeAsync(() => SetBusy(true, "Автосборка перевода..."));
            SetSummary("Перевод найден. Собираю итоговый файл...");

            var tools = await ToolResolver.EnsureAllAsync(folder, AppendLog, cancellationToken);

            var storedAudioPath = BuildStoredTranslatedAudioPath(folder, targetEntry.FileName, payload.SuggestedFileName ?? payload.Url);
            Directory.CreateDirectory(Path.GetDirectoryName(storedAudioPath)!);
            var sourceId = await acquireAudioAsync(storedAudioPath, targetEntry, cancellationToken);

            _lastAutoMergedAudioPath = sourceId;
            _lastAutoMergedAtUtc = DateTime.UtcNow;

            if (!autoMergeEnabled)
            {
                AppendLog("Translated audio saved to: " + storedAudioPath);
                SetStatus("Running");
                SetSummary("Перевод получен и сохранён. Автосклейка отключена.");
                return;
            }

            var outputFolder = Path.Combine(folder, "merged_output");
            var outputPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(targetEntry.FileName) + ".translated.mp4");

            await _ffmpegService.MergeVideoWithExternalAudioAsync(
                tools.FfmpegPath,
                targetEntry.FullPath,
                storedAudioPath,
                outputPath,
                AppendLog,
                cancellationToken);

            SetStatus("Running");
            SetSummary("Перевод подхвачен автоматически. Итоговый файл сохранён в merged_output.");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => SetBusy(false));
            _autoMergeSemaphore.Release();
        }
    }

    private VideoEntry? ResolveTargetVideoEntry(TranslationCallbackPayload payload, VideoEntry? selectedEntry)
    {
        if (!string.IsNullOrWhiteSpace(payload.VideoSrc))
        {
            var normalized = NormalizeVideoSrc(payload.VideoSrc);
            var matched = _workspace.Entries.FirstOrDefault(x =>
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
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.PathAndQuery.Trim();

        return trimmed.TrimStart('.');
    }

    private async Task DownloadTranslationAudioAsync(TranslationCallbackPayload payload, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, payload.Url);
        request.Headers.Referrer = Uri.TryCreate(_publicUrl, UriKind.Absolute, out var publicUri) ? publicUri : null;
        request.Headers.UserAgent.ParseAdd("PublicVideoLauncher/1.0");

        using var response = await _translationHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
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
    }

    private static string? GetDownloadsFolderPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var downloads = Path.Combine(home, "Downloads");
            if (Directory.Exists(downloads))
                return downloads;

            downloads = Path.Combine(home, "Загрузки");
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
        _mergeTranslationCheckBox.IsEnabled = !busy;
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
