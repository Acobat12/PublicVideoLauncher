using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PublicVideoLauncher.Models;
using PublicVideoLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher;

public sealed partial class TranslationStudioWindow : Window
{
    private readonly IReadOnlyList<VideoEntry> _entries;
    private readonly string _workspaceRoot;
    private readonly string _activeTunnelDisplayName;
    private readonly string _publicBaseUrl;
    private readonly Func<TranslationStudioPrepareRequest, CancellationToken, Task<TranslationStudioJob>> _prepareJobAsync;
    private readonly Func<VideoEntry, CancellationToken, Task<TranslationStudioJob?>> _loadExistingJobAsync;
    private readonly Func<TranslationStudioJob, TranslationStudioSegment?, CancellationToken, Task<string>> _openPlayerAsync;
    private readonly Func<TranslationStudioJob, TranslationStudioSegment, string, CancellationToken, Task<TranslationStudioJob>> _importAudioAsync;
    private readonly Func<TranslationStudioJob, TranslationStudioSegment, double, double, CancellationToken, Task<TranslationStudioJob>> _mergeSegmentAsync;
    private readonly Func<TranslationStudioJob, double, double, CancellationToken, Task<TranslationStudioJob>> _buildFinalAsync;
    private readonly Func<TranslationStudioJob, CancellationToken, Task<TranslationStudioJob>> _saveJobAsync;
    private readonly Func<TranslationStudioBatchRequest, CancellationToken, Task> _startBatchAsync;
    private readonly Func<Task> _stopBatchAsync;
    private readonly Func<Task> _retryFailedAsync;
    private readonly Func<bool> _canRetryFailed;
    private readonly Func<bool> _getAutoProcessNextVideoEnabled;
    private readonly Action<bool> _setAutoProcessNextVideoEnabled;
    private readonly AppLanguage _language;

    private readonly ComboBox _videoComboBox;
    private readonly CheckBox _autoMergeCheckBox;
    private readonly CheckBox _autoProcessNextVideoCheckBox;
    private readonly NumericUpDown _originalAudioVolumeNumeric;
    private readonly NumericUpDown _translatedAudioVolumeNumeric;
    private readonly CheckBox _splitLongVideoCheckBox;
    private readonly NumericUpDown _splitThresholdNumeric;
    private readonly NumericUpDown _targetSegmentNumeric;
    private readonly TextBox _summaryTextBox;
    private readonly TextBox _payloadPathTextBox;
    private readonly TextBox _playerUrlTextBox;
    private readonly TextBox _outputPathTextBox;
    private readonly ListBox _segmentsListBox;
    private readonly TextBox _logTextBox;
    private readonly TextBlock _studioStatusTextBlock;
    private readonly TextBlock _operationTextBlock;
    private readonly TextBlock _batchProgressTextBlock;
    private readonly ProgressBar _busyProgressBar;
    private readonly Button _loadJobButton;
    private readonly Button _prepareButton;
    private readonly Button _runBatchButton;
    private readonly Button _stopBatchButton;
    private readonly Button _retryFailedButton;
    private readonly Button _openFullPlaylistButton;
    private readonly Button _openNextUntranslatedButton;
    private readonly Button _openPlayerButton;
    private readonly Button _importAudioButton;
    private readonly Button _mergeSegmentButton;
    private readonly Button _buildFinalButton;
    private readonly Button _openJobFolderButton;
    private readonly Button _openMergedOutputButton;
    private readonly Button _copyUrlButton;
    private readonly Button _closeButton;

    private TranslationStudioJob? _currentJob;
    private bool _refreshingJobUi;
    private bool _autoFinalBuildInProgress;
    private bool _batchLocked;
    private bool _suppressSelectionLoad;

    public TranslationStudioWindow()
        : this(
            Array.Empty<VideoEntry>(),
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            static (_, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _) => Task.FromResult<TranslationStudioJob?>(null),
            static (_, _, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _, _, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _, _, _, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _, _, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _) => throw new InvalidOperationException("Designer constructor."),
            static (_, _) => Task.CompletedTask,
            static () => Task.CompletedTask,
            static () => Task.CompletedTask,
            static () => false,
            static () => false,
            static _ => { },
            AppLanguage.English)
    {
    }

    public TranslationStudioWindow(
        IReadOnlyList<VideoEntry> entries,
        VideoEntry? selectedEntry,
        string workspaceRoot,
        string activeTunnelDisplayName,
        string publicBaseUrl,
        Func<TranslationStudioPrepareRequest, CancellationToken, Task<TranslationStudioJob>> prepareJobAsync,
        Func<VideoEntry, CancellationToken, Task<TranslationStudioJob?>> loadExistingJobAsync,
        Func<TranslationStudioJob, TranslationStudioSegment?, CancellationToken, Task<string>> openPlayerAsync,
        Func<TranslationStudioJob, TranslationStudioSegment, string, CancellationToken, Task<TranslationStudioJob>> importAudioAsync,
        Func<TranslationStudioJob, TranslationStudioSegment, double, double, CancellationToken, Task<TranslationStudioJob>> mergeSegmentAsync,
        Func<TranslationStudioJob, double, double, CancellationToken, Task<TranslationStudioJob>> buildFinalAsync,
        Func<TranslationStudioJob, CancellationToken, Task<TranslationStudioJob>> saveJobAsync,
        Func<TranslationStudioBatchRequest, CancellationToken, Task> startBatchAsync,
        Func<Task> stopBatchAsync,
        Func<Task> retryFailedAsync,
        Func<bool> canRetryFailed,
        Func<bool> getAutoProcessNextVideoEnabled,
        Action<bool> setAutoProcessNextVideoEnabled,
        AppLanguage language)
    {
        _entries = entries;
        _workspaceRoot = workspaceRoot;
        _activeTunnelDisplayName = activeTunnelDisplayName;
        _publicBaseUrl = publicBaseUrl.TrimEnd('/');
        _prepareJobAsync = prepareJobAsync;
        _loadExistingJobAsync = loadExistingJobAsync;
        _openPlayerAsync = openPlayerAsync;
        _importAudioAsync = importAudioAsync;
        _mergeSegmentAsync = mergeSegmentAsync;
        _buildFinalAsync = buildFinalAsync;
        _saveJobAsync = saveJobAsync;
        _startBatchAsync = startBatchAsync;
        _stopBatchAsync = stopBatchAsync;
        _retryFailedAsync = retryFailedAsync;
        _canRetryFailed = canRetryFailed;
        _getAutoProcessNextVideoEnabled = getAutoProcessNextVideoEnabled;
        _setAutoProcessNextVideoEnabled = setAutoProcessNextVideoEnabled;
        _language = language;

        InitializeComponent();

        _videoComboBox = this.FindControl<ComboBox>("VideoComboBox")!;
        _autoMergeCheckBox = this.FindControl<CheckBox>("AutoMergeCheckBox")!;
        _autoProcessNextVideoCheckBox = this.FindControl<CheckBox>("AutoProcessNextVideoCheckBox")!;
        _originalAudioVolumeNumeric = this.FindControl<NumericUpDown>("StudioOriginalAudioVolumeNumeric")!;
        _translatedAudioVolumeNumeric = this.FindControl<NumericUpDown>("StudioTranslatedAudioVolumeNumeric")!;
        _splitLongVideoCheckBox = this.FindControl<CheckBox>("SplitLongVideoCheckBox")!;
        _splitThresholdNumeric = this.FindControl<NumericUpDown>("SplitThresholdNumeric")!;
        _targetSegmentNumeric = this.FindControl<NumericUpDown>("TargetSegmentNumeric")!;
        _summaryTextBox = this.FindControl<TextBox>("SummaryTextBox")!;
        _payloadPathTextBox = this.FindControl<TextBox>("PayloadPathTextBox")!;
        _playerUrlTextBox = this.FindControl<TextBox>("PlayerUrlTextBox")!;
        _outputPathTextBox = this.FindControl<TextBox>("OutputPathTextBox")!;
        _segmentsListBox = this.FindControl<ListBox>("SegmentsListBox")!;
        _logTextBox = this.FindControl<TextBox>("LogTextBox")!;
        _studioStatusTextBlock = this.FindControl<TextBlock>("StudioStatusTextBlock")!;
        _operationTextBlock = this.FindControl<TextBlock>("OperationTextBlock")!;
        _batchProgressTextBlock = this.FindControl<TextBlock>("BatchProgressTextBlock")!;
        _busyProgressBar = this.FindControl<ProgressBar>("BusyProgressBar")!;
        _loadJobButton = this.FindControl<Button>("LoadJobButton")!;
        _prepareButton = this.FindControl<Button>("PrepareButton")!;
        _runBatchButton = this.FindControl<Button>("RunBatchButton")!;
        _stopBatchButton = this.FindControl<Button>("StopBatchButton")!;
        _retryFailedButton = this.FindControl<Button>("RetryFailedButton")!;
        _openFullPlaylistButton = this.FindControl<Button>("OpenFullPlaylistButton")!;
        _openNextUntranslatedButton = this.FindControl<Button>("OpenNextUntranslatedButton")!;
        _openPlayerButton = this.FindControl<Button>("OpenPlayerButton")!;
        _importAudioButton = this.FindControl<Button>("ImportAudioButton")!;
        _mergeSegmentButton = this.FindControl<Button>("MergeSegmentButton")!;
        _buildFinalButton = this.FindControl<Button>("BuildFinalButton")!;
        _openJobFolderButton = this.FindControl<Button>("OpenJobFolderButton")!;
        _openMergedOutputButton = this.FindControl<Button>("OpenMergedOutputButton")!;
        _copyUrlButton = this.FindControl<Button>("CopyUrlButton")!;
        _closeButton = this.FindControl<Button>("CloseButton")!;

        _videoComboBox.ItemsSource = _entries.Select(x => x.Title).ToArray();
        _videoComboBox.SelectedIndex = Math.Max(0, selectedEntry is null ? 0 : _entries.ToList().FindIndex(x => x.Id == selectedEntry.Id));
        _autoProcessNextVideoCheckBox.IsChecked = _getAutoProcessNextVideoEnabled();

        _videoComboBox.SelectionChanged += VideoComboBox_OnSelectionChanged;
        _autoMergeCheckBox.PropertyChanged += OptionControl_OnPropertyChanged;
        _autoProcessNextVideoCheckBox.PropertyChanged += OptionControl_OnPropertyChanged;
        _originalAudioVolumeNumeric.PropertyChanged += OptionControl_OnPropertyChanged;
        _translatedAudioVolumeNumeric.PropertyChanged += OptionControl_OnPropertyChanged;
        _splitLongVideoCheckBox.PropertyChanged += OptionControl_OnPropertyChanged;
        _splitThresholdNumeric.PropertyChanged += OptionControl_OnPropertyChanged;
        _targetSegmentNumeric.PropertyChanged += OptionControl_OnPropertyChanged;
        _segmentsListBox.SelectionChanged += (_, _) =>
        {
            RefreshDisplayedPlayerUrl();
            RefreshButtons();
        };

        _loadJobButton.Click += LoadJobButton_OnClick;
        _prepareButton.Click += PrepareButton_OnClick;
        _runBatchButton.Click += RunBatchButton_OnClick;
        _stopBatchButton.Click += StopBatchButton_OnClick;
        _retryFailedButton.Click += RetryFailedButton_OnClick;
        _openFullPlaylistButton.Click += OpenFullPlaylistButton_OnClick;
        _openNextUntranslatedButton.Click += OpenNextUntranslatedButton_OnClick;
        _openPlayerButton.Click += OpenPlayerButton_OnClick;
        _importAudioButton.Click += ImportAudioButton_OnClick;
        _mergeSegmentButton.Click += MergeSegmentButton_OnClick;
        _buildFinalButton.Click += BuildFinalButton_OnClick;
        _openJobFolderButton.Click += OpenJobFolderButton_OnClick;
        _openMergedOutputButton.Click += OpenMergedOutputButton_OnClick;
        _copyUrlButton.Click += CopyUrlButton_OnClick;
        _closeButton.Click += (_, _) => Close();
        Closing += TranslationStudioWindow_OnClosing;

        RefreshJobUi();
        Opened += async (_, _) => await TryLoadExistingJobForSelectedEntryAsync(true);

        SetStudioStatus("Ready", "Choose a video and click Prepare.");
        AppendLog(F("Active tunnel: {0}", _activeTunnelDisplayName));
        AppendLog("Defaults were filled automatically from the selected video.");
        AppendLog("Click Prepare. The translated mp3 is saved automatically through the callback.");
        UiLocalizer.Apply(this, _language);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void VideoComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionLoad)
            return;

        await TryLoadExistingJobForSelectedEntryAsync(false);
    }

    private void OptionControl_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_refreshingJobUi)
            return;

        if (e.Property == ToggleButton.IsCheckedProperty && sender == _autoMergeCheckBox && _currentJob is not null)
        {
            _currentJob.AutoMergeOnCallback = _autoMergeCheckBox.IsChecked == true;
            if (_currentJob.AutoMergeOnCallback)
            {
                _ = SaveCurrentJobSettingsAsync();
                _ = TryAutoBuildFinalFromReadyAudioAsync();
            }
            else
            {
                _ = SaveCurrentJobSettingsAsync();
            }
        }

        if (e.Property == ToggleButton.IsCheckedProperty && sender == _autoProcessNextVideoCheckBox)
        {
            _setAutoProcessNextVideoEnabled(_autoProcessNextVideoCheckBox.IsChecked == true);
        }

        if (e.Property == NumericUpDown.ValueProperty && _currentJob is not null)
        {
            UpdateCurrentJobAudioSettingsFromControls();
            _ = SaveCurrentJobSettingsAsync();
        }

        if (e.Property == ToggleButton.IsCheckedProperty || e.Property == NumericUpDown.ValueProperty)
            RefreshJobUi();
    }

    private async void LoadJobButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await TryLoadExistingJobForSelectedEntryAsync(true);
    }

    private async void PrepareButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            AppendLog("No video is selected.");
            return;
        }

        try
        {
            SetBusy(true, "Preparing payload files, playlist and browser page...");
            var request = new TranslationStudioPrepareRequest
            {
                SourceEntry = entry,
                AutoMergeOnCallback = _autoMergeCheckBox.IsChecked == true,
                SplitLongVideo = _splitLongVideoCheckBox.IsChecked == true,
                SplitIfLongerThanMinutes = (int)Math.Round(_splitThresholdNumeric.Value ?? 120),
                TargetSegmentMinutes = (int)Math.Round(_targetSegmentNumeric.Value ?? 60),
                OriginalAudioVolumePercent = GetOriginalAudioVolumePercent(),
                TranslatedAudioVolumePercent = GetTranslatedAudioVolumePercent()
            };

            _currentJob = await _prepareJobAsync(request, CancellationToken.None);
            RefreshJobUi();
            SetStudioStatus("Prepared", F("Payload is ready. Segments: {0}. Open Playlist to translate prepared item(s).", Math.Max(1, _currentJob.Segments.Count)));
            AppendLog(F("Prepared: {0}. Segments: {1}.", entry.Title, Math.Max(1, _currentJob.Segments.Count)));
        }
        catch (Exception ex)
        {
            AppendLog("Prepare failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RunBatchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Starting Studio batch...");
            await _startBatchAsync(BuildBatchRequest(), CancellationToken.None);
            AppendLog("Studio batch started.");
        }
        catch (Exception ex)
        {
            AppendLog(F("Run batch failed: {0}", ex.Message));
        }
        finally
        {
            SetBusy(false);
            if (_batchLocked)
                SetStudioStatus("Batch started", "Studio batch is processing selected videos sequentially.");
        }
    }

    private async void StopBatchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _stopBatchAsync();
            AppendLog("Studio batch stop requested.");
        }
        catch (Exception ex)
        {
            AppendLog(F("Stop batch failed: {0}", ex.Message));
        }
    }

    private async void RetryFailedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Preparing retry for failed videos...");
            await _retryFailedAsync();
            AppendLog("Retry failed batch started.");
        }
        catch (Exception ex)
        {
            AppendLog(F("Retry failed batch could not start: {0}", ex.Message));
        }
        finally
        {
            SetBusy(false);
            if (_batchLocked)
                SetStudioStatus("Batch started", "Studio batch is processing selected videos sequentially.");
        }
    }

    private void TranslationStudioWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_batchLocked)
            return;

        e.Cancel = true;
        _ = _stopBatchAsync();
        SetStudioStatus("Working", "Stopping Studio batch. Close the window after the current video finishes.");
        AppendLog("Studio batch stop requested before closing the window.");
    }

    private async void OpenFullPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        try
        {
            SetBusy(true, "Opening the job playlist in the browser...");
            _segmentsListBox.SelectedIndex = -1;
            var url = await _openPlayerAsync(_currentJob, null, CancellationToken.None);
            RefreshDisplayedPlayerUrl();
            SetStudioStatus("Playlist opened", "The browser player now shows all prepared items for this job.");
            AppendLog("Job playlist opened: " + url);
        }
        catch (Exception ex)
        {
            AppendLog("Open job playlist failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CopyUrlButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var textToCopy = BuildCurrentPlayerUrl();
        if (string.IsNullOrWhiteSpace(textToCopy))
        {
            AppendLog("Nothing to copy yet.");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            AppendLog("Clipboard is not available.");
            return;
        }

        await topLevel.Clipboard.SetTextAsync(textToCopy);
        AppendLog("Player URL copied to clipboard.");
        SetStudioStatus("URL copied", "The current player or playlist URL was copied to the clipboard.");
    }

    private async void OpenNextUntranslatedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        var nextSegment = GetNextUntranslatedSegment();
        if (nextSegment is null)
        {
            AppendLog("All segments already have translated audio.");
            return;
        }

        try
        {
            SetBusy(true, F("Opening the next untranslated segment {0}/{1}...", nextSegment.Index, nextSegment.TotalSegments));
            var segmentIndex = Math.Max(0, _currentJob.Segments.FindIndex(x => string.Equals(x.SegmentId, nextSegment.SegmentId, StringComparison.OrdinalIgnoreCase)));
            _segmentsListBox.SelectedIndex = segmentIndex;
            UpdateCurrentJobAudioSettingsFromControls();
            var url = await _openPlayerAsync(_currentJob, nextSegment, CancellationToken.None);
            RefreshDisplayedPlayerUrl();
            SetStudioStatus("Segment opened", F("Opened segment {0}/{1}. Use Playlist to return to all prepared items.", nextSegment.Index, nextSegment.TotalSegments));
            AppendLog(F("Opened next untranslated segment {0}/{1}: {2}", nextSegment.Index, nextSegment.TotalSegments, url));
        }
        catch (Exception ex)
        {
            AppendLog("Open next untranslated failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OpenPlayerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        var selectedSegment = GetSelectedSegment();
        if (selectedSegment is null)
        {
            AppendLog("Select a segment first.");
            return;
        }

        try
        {
            SetBusy(true, F("Opening segment {0}/{1} in the browser...", selectedSegment.Index, selectedSegment.TotalSegments));
            UpdateCurrentJobAudioSettingsFromControls();
            var url = await _openPlayerAsync(_currentJob, selectedSegment, CancellationToken.None);
            RefreshDisplayedPlayerUrl();
            SetStudioStatus("Segment opened", F("Opened segment {0}/{1}. Use Playlist to return to all prepared items.", selectedSegment.Index, selectedSegment.TotalSegments));
            AppendLog(F("Opened segment {0}/{1}: {2}", selectedSegment.Index, selectedSegment.TotalSegments, url));
        }
        catch (Exception ex)
        {
            AppendLog("Open selected segment failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ImportAudioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        var selectedSegment = GetSelectedSegment();
        if (selectedSegment is null)
        {
            AppendLog("Select a segment first.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Select translated audio"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio")
                {
                    Patterns = ["*.mp3", "*.wav", "*.m4a", "*.aac", "*.ogg", "*.flac"]
                }
            ]
        });

        var sourcePath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        try
        {
            SetBusy(true, F("Importing translated audio for segment {0}/{1}...", selectedSegment.Index, selectedSegment.TotalSegments));
            _currentJob = await _importAudioAsync(_currentJob, selectedSegment, sourcePath, CancellationToken.None);
            RefreshJobUi();
            SetStudioStatus("Audio imported", F("Translated audio is ready for segment {0}/{1}.", selectedSegment.Index, selectedSegment.TotalSegments));
            AppendLog(F("Imported audio for segment {0}/{1}.", selectedSegment.Index, selectedSegment.TotalSegments));
        }
        catch (Exception ex)
        {
            AppendLog("Import audio failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void MergeSegmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        var selectedSegment = GetSelectedSegment();
        if (selectedSegment is null)
        {
            AppendLog("Select a segment first.");
            return;
        }

        try
        {
            SetBusy(true, F("Merging segment {0}/{1} into video...", selectedSegment.Index, selectedSegment.TotalSegments));
            UpdateCurrentJobAudioSettingsFromControls();
            _currentJob = await _mergeSegmentAsync(_currentJob, selectedSegment, GetOriginalAudioVolumePercent(), GetTranslatedAudioVolumePercent(), CancellationToken.None);
            RefreshJobUi();
            SetStudioStatus("Segment merged", F("Segment {0}/{1} is now merged back into video.", selectedSegment.Index, selectedSegment.TotalSegments));
            AppendLog(F("Merged segment {0}/{1}.", selectedSegment.Index, selectedSegment.TotalSegments));
        }
        catch (Exception ex)
        {
            AppendLog("Merge segment failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BuildFinalButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentJob is null)
        {
            AppendLog("Prepare or load a job first.");
            return;
        }

        try
        {
            if (TranslationStudioFileState.IsFinalOutputCurrent(_currentJob))
            {
                AppendLog("Final output already exists. Build skipped.");
                SetStudioStatus("Final video already exists", "The final translated video already exists. Build skipped.");
                return;
            }

            SetBusy(true, "Building the final translated video. This can take a while...");
            UpdateCurrentJobAudioSettingsFromControls();
            _currentJob = await _buildFinalAsync(_currentJob, GetOriginalAudioVolumePercent(), GetTranslatedAudioVolumePercent(), CancellationToken.None);
            RefreshJobUi();
            SetStudioStatus("Final video ready", "The final translated video was built successfully. You can open it from this window.");
            AppendLog("Final video built.");
        }
        catch (Exception ex)
        {
            AppendLog("Build final failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TryAutoBuildFinalFromReadyAudioAsync()
    {
        if (_autoFinalBuildInProgress || _currentJob is null || !_currentJob.AutoMergeOnCallback)
            return;

        if (_currentJob.Segments.Count == 0 || _currentJob.Segments.Any(x => !x.HasTranslatedAudio))
            return;

        if (TranslationStudioFileState.IsFinalOutputCurrent(_currentJob))
        {
            AppendLog("Final output already exists. Auto build skipped.");
            SetStudioStatus("Final video already exists", "Auto build skipped because the final video already exists.");
            return;
        }

        try
        {
            _autoFinalBuildInProgress = true;

            var shouldContinue = await ConfirmAutoBuildAsync(_currentJob);
            if (!shouldContinue)
            {
                _currentJob.AutoMergeOnCallback = false;
                _autoMergeCheckBox.IsChecked = false;
                _currentJob = await _saveJobAsync(_currentJob, CancellationToken.None);
                RefreshJobUi();
                SetStudioStatus("Job loaded", "Auto build was turned off. Click Build final when you want to create the translated video.");
                AppendLog("Auto build canceled by user. Auto build was turned off for this job.");
                return;
            }

            SetBusy(true, "Translated mp3 is ready. Auto build is creating the final video...");
            UpdateCurrentJobAudioSettingsFromControls();
            _currentJob = await _buildFinalAsync(_currentJob, GetOriginalAudioVolumePercent(), GetTranslatedAudioVolumePercent(), CancellationToken.None);
            RefreshJobUi();
            SetStudioStatus("Final video ready", "The final translated video was built successfully. You can open it from this window.");
            AppendLog("Final video built automatically.");
        }
        catch (Exception ex)
        {
            AppendLog("Auto build final video failed: " + ex.Message);
        }
        finally
        {
            _autoFinalBuildInProgress = false;
            SetBusy(false);
        }
    }

    private async Task SaveCurrentJobSettingsAsync()
    {
        if (_currentJob is null)
            return;

        try
        {
            _currentJob = await _saveJobAsync(_currentJob, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog("Save job settings failed: " + ex.Message);
        }
    }

    private async Task<bool> ConfirmAutoBuildAsync(TranslationStudioJob job)
    {
        var message = string.Join(Environment.NewLine + Environment.NewLine,
            T("Auto build is enabled and translated mp3 is already ready."),
            F("Selected video: {0}", job.SourceVideoTitle),
            F("Final output: {0}", job.FinalOutputPath),
            T("Continue will build the final video now. Cancel will turn Auto build off for this job."));

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brush.Parse("#E5E7EB"),
            Margin = new Avalonia.Thickness(0, 0, 0, 16)
        };

        var cancelButton = new Button
        {
            Content = T("Cancel"),
            Width = 110,
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };

        var continueButton = new Button
        {
            Content = T("Continue"),
            Width = 120
        };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(continueButton);

        var panel = new DockPanel { Margin = new Avalonia.Thickness(18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(textBlock);

        var dialog = new Window
        {
            Title = T("Auto build confirmation"),
            Width = 620,
            Height = 300,
            MinWidth = 520,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#0B1220"),
            Content = panel
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        continueButton.Click += (_, _) => dialog.Close(true);

        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private void OpenJobFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenPathWithFeedback(_currentJob?.JobFolder, "job folder");
    }

    private void OpenMergedOutputButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenPathWithFeedback(_currentJob?.FinalOutputPath, "final output");
    }

    private async void HelpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var key = button.Tag?.ToString() ?? string.Empty;
        await ShowHelpAsync(key, GetHelpText(key));
    }

    private async Task TryLoadExistingJobForSelectedEntryAsync(bool logWhenMissing)
    {
        var entry = GetSelectedEntry();
        if (entry is null)
            return;

        try
        {
            var loaded = await _loadExistingJobAsync(entry, CancellationToken.None);
            if (loaded is null)
            {
                _currentJob = null;
                RefreshJobUi();

                if (logWhenMissing)
                {
                    SetStudioStatus("No saved job", "No prepared job was found for this video yet.");
                    AppendLog("No saved job found.");
                    AppendLog("Next step: click Prepare.");
                }

                return;
            }

            _currentJob = loaded;
            RefreshJobUi();
            if (loaded.PreparationRefreshRecommended)
            {
                SetStudioStatus("Existing job loaded", "This saved job was created by an older preparation pipeline. Click Prepare to refresh segmentation and payload files.");
                AppendLog("Saved job was created by an older preparation pipeline.");
                AppendLog("Recommended next step: click Prepare.");
            }
            else
            {
                SetStudioStatus("Existing job loaded", "You can continue with browser editing, importing audio or final build.");
            }
            if (logWhenMissing)
                AppendLog("Loaded existing job.");

            if (loaded.AutoMergeOnCallback)
                _ = TryAutoBuildFinalFromReadyAudioAsync();
        }
        catch (Exception ex)
        {
            AppendLog("Load existing job failed: " + ex.Message);
        }
    }

    private TranslationStudioBatchRequest BuildBatchRequest()
    {
        return new TranslationStudioBatchRequest
        {
            SplitLongVideo = _splitLongVideoCheckBox.IsChecked == true,
            SplitIfLongerThanMinutes = (int)Math.Round(_splitThresholdNumeric.Value ?? 120),
            TargetSegmentMinutes = (int)Math.Round(_targetSegmentNumeric.Value ?? 60),
            OriginalAudioVolumePercent = GetOriginalAudioVolumePercent(),
            TranslatedAudioVolumePercent = GetTranslatedAudioVolumePercent()
        };
    }

    public async Task SelectEntryForBatchAsync(VideoEntry entry, CancellationToken cancellationToken)
    {
        var targetIndex = _entries
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            ?.index ?? -1;

        if (targetIndex < 0)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _suppressSelectionLoad = true;
            try
            {
                _videoComboBox.SelectedIndex = targetIndex;
                _currentJob = null;
                RefreshJobUi();
                SetStudioStatus("Batch selected video", F("Studio batch selected: {0}", entry.Title));
            }
            finally
            {
                _suppressSelectionLoad = false;
            }
        }, Avalonia.Threading.DispatcherPriority.Normal, cancellationToken);
    }

    public async Task DisplayBatchPreparedJobAsync(
        TranslationStudioJob job,
        TranslationStudioSegment? openedSegment,
        string title,
        string details,
        CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _currentJob = job;
            RefreshJobUi();
            if (openedSegment is not null)
            {
                var segmentIndex = Math.Max(0, job.Segments.FindIndex(x => string.Equals(x.SegmentId, openedSegment.SegmentId, StringComparison.OrdinalIgnoreCase)));
                _segmentsListBox.SelectedIndex = segmentIndex;
            }

            SetStudioStatus(title, details);
            RefreshDisplayedPlayerUrl();
        }, Avalonia.Threading.DispatcherPriority.Normal, cancellationToken);
    }

    public void SetBatchLocked(bool locked)
    {
        _batchLocked = locked;
        Dispatcher.UIThread.Post(RefreshButtons);
    }

    public void SetBatchProgressText(string? progressText)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _batchProgressTextBlock.Text = string.IsNullOrWhiteSpace(progressText) ? string.Empty : progressText;
            _batchProgressTextBlock.IsVisible = !string.IsNullOrWhiteSpace(progressText);
        });
    }

    public async Task ShowBatchReportAsync(string reportText, CancellationToken cancellationToken)
    {
        var dialogTask = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var textBox = new TextBox
            {
                Text = reportText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Background = Avalonia.Media.Brush.Parse("#020817"),
                Margin = new Avalonia.Thickness(0, 0, 0, 12),
                MinHeight = 180
            };

            var okButton = new Button
            {
                Content = T("OK"),
                Width = 100,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };

            var layout = new DockPanel { Margin = new Avalonia.Thickness(16) };
            DockPanel.SetDock(okButton, Dock.Bottom);
            layout.Children.Add(okButton);
            layout.Children.Add(textBox);

            var dialog = new Window
            {
                Title = T("Translation Studio"),
                Width = 620,
                Height = 360,
                MinWidth = 520,
                MinHeight = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brush.Parse("#0B1220"),
                Content = layout
            };

            okButton.Click += (_, _) => dialog.Close();
            return dialog.ShowDialog(this);
        }, Avalonia.Threading.DispatcherPriority.Normal, cancellationToken);

        await dialogTask;
    }

    private VideoEntry? GetSelectedEntry()
    {
        var index = _videoComboBox.SelectedIndex;
        return index >= 0 && index < _entries.Count ? _entries[index] : null;
    }

    private TranslationStudioSegment? GetSelectedSegment()
    {
        if (_currentJob is null || _currentJob.Segments.Count == 0)
            return null;

        var index = _segmentsListBox.SelectedIndex;
        return index >= 0 && index < _currentJob.Segments.Count ? _currentJob.Segments[index] : null;
    }

    private TranslationStudioSegment? GetNextUntranslatedSegment()
    {
        if (_currentJob is null)
            return null;

        return _currentJob.Segments
            .OrderBy(x => x.Index)
            .FirstOrDefault(x => !x.HasTranslatedAudio)
            ?? _currentJob.Segments.OrderBy(x => x.Index).FirstOrDefault(x => !x.HasMergedOutput);
    }

    private void RefreshJobUi()
    {
        _refreshingJobUi = true;
        try
        {
        if (_currentJob is not null)
        {
            _autoMergeCheckBox.IsChecked = _currentJob.AutoMergeOnCallback;
            _originalAudioVolumeNumeric.Value = (decimal)_currentJob.OriginalAudioVolumePercent;
            _translatedAudioVolumeNumeric.Value = (decimal)_currentJob.TranslatedAudioVolumePercent;
            _payloadPathTextBox.Text = _currentJob.PayloadPath;
            RefreshDisplayedPlayerUrl();
            _outputPathTextBox.Text = _currentJob.FinalOutputPath ?? string.Empty;
            _summaryTextBox.Text = BuildJobSummary(_currentJob);
            _segmentsListBox.ItemsSource = _currentJob.Segments.Select(BuildSegmentLabel).ToList();
            _segmentsListBox.SelectedIndex = _currentJob.Segments.Count > 0 ? 0 : -1;
        }
        else
        {
            var entry = GetSelectedEntry();
            _payloadPathTextBox.Text = entry is null ? string.Empty : BuildPreviewPayloadPath(entry);
            _playerUrlTextBox.Text = entry is null ? string.Empty : BuildPreviewPlayerUrl(entry);
            _outputPathTextBox.Text = entry is null ? string.Empty : BuildPreviewFinalOutputPath(entry);
            _summaryTextBox.Text = BuildPreviewSummary(entry);
            _segmentsListBox.ItemsSource = BuildPreviewSegmentItems();
            _segmentsListBox.SelectedIndex = -1;
        }
        }
        finally
        {
            _refreshingJobUi = false;
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var hasJob = _currentJob is not null;
        var selectedSegment = GetSelectedSegment();
        var hasSegment = selectedSegment is not null;
        var hasNextUntranslated = GetNextUntranslatedSegment() is not null;
        var hasSegmentAudio = selectedSegment?.HasTranslatedAudio == true;
        var manualEnabled = !_batchLocked;

        _loadJobButton.IsEnabled = manualEnabled;
        _prepareButton.IsEnabled = manualEnabled;
        _runBatchButton.IsEnabled = manualEnabled && _autoProcessNextVideoCheckBox.IsChecked == true;
        _stopBatchButton.IsEnabled = _batchLocked;
        _retryFailedButton.IsEnabled = manualEnabled && _canRetryFailed();
        _openFullPlaylistButton.IsEnabled = manualEnabled && hasJob;
        _openNextUntranslatedButton.IsEnabled = manualEnabled && hasJob && hasNextUntranslated;
        _openPlayerButton.IsEnabled = manualEnabled && hasJob && hasSegment;
        _importAudioButton.IsEnabled = manualEnabled && hasJob && hasSegment;
        _mergeSegmentButton.IsEnabled = manualEnabled && hasJob && hasSegment && hasSegmentAudio;
        _buildFinalButton.IsEnabled = manualEnabled && hasJob;
        _openJobFolderButton.IsEnabled = manualEnabled && hasJob;
        _openMergedOutputButton.IsEnabled = manualEnabled && _currentJob?.HasFinalOutput == true;
        _copyUrlButton.IsEnabled = manualEnabled && !string.IsNullOrWhiteSpace(_playerUrlTextBox.Text);
        _videoComboBox.IsEnabled = manualEnabled;
        _autoMergeCheckBox.IsEnabled = manualEnabled;
        _autoProcessNextVideoCheckBox.IsEnabled = manualEnabled;
        _originalAudioVolumeNumeric.IsEnabled = manualEnabled;
        _translatedAudioVolumeNumeric.IsEnabled = manualEnabled;
        _splitLongVideoCheckBox.IsEnabled = manualEnabled;
        _splitThresholdNumeric.IsEnabled = manualEnabled;
        _targetSegmentNumeric.IsEnabled = manualEnabled;
        _segmentsListBox.IsEnabled = manualEnabled;
        _closeButton.IsEnabled = manualEnabled;
        RefreshActionHints();
    }

    private void SetBusy(bool busy, string? operationText = null)
    {
        var selectedSegment = GetSelectedSegment();
        var hasSegmentAudio = selectedSegment?.HasTranslatedAudio == true;
        var manualEnabled = !busy && !_batchLocked;

        _loadJobButton.IsEnabled = manualEnabled;
        _prepareButton.IsEnabled = manualEnabled;
        _runBatchButton.IsEnabled = manualEnabled && _autoProcessNextVideoCheckBox.IsChecked == true;
        _stopBatchButton.IsEnabled = _batchLocked;
        _retryFailedButton.IsEnabled = manualEnabled && _canRetryFailed();
        _openFullPlaylistButton.IsEnabled = manualEnabled && _currentJob is not null;
        _openNextUntranslatedButton.IsEnabled = manualEnabled && _currentJob is not null && GetNextUntranslatedSegment() is not null;
        _openPlayerButton.IsEnabled = manualEnabled && _currentJob is not null && selectedSegment is not null;
        _importAudioButton.IsEnabled = manualEnabled && _currentJob is not null && selectedSegment is not null;
        _mergeSegmentButton.IsEnabled = manualEnabled && _currentJob is not null && selectedSegment is not null && hasSegmentAudio;
        _buildFinalButton.IsEnabled = manualEnabled && _currentJob is not null;
        _openJobFolderButton.IsEnabled = manualEnabled && _currentJob is not null;
        _openMergedOutputButton.IsEnabled = manualEnabled && _currentJob?.HasFinalOutput == true;
        _copyUrlButton.IsEnabled = manualEnabled && !string.IsNullOrWhiteSpace(_playerUrlTextBox.Text);
        _videoComboBox.IsEnabled = manualEnabled;
        _autoMergeCheckBox.IsEnabled = manualEnabled;
        _autoProcessNextVideoCheckBox.IsEnabled = manualEnabled;
        _originalAudioVolumeNumeric.IsEnabled = manualEnabled;
        _translatedAudioVolumeNumeric.IsEnabled = manualEnabled;
        _splitLongVideoCheckBox.IsEnabled = manualEnabled;
        _splitThresholdNumeric.IsEnabled = manualEnabled;
        _targetSegmentNumeric.IsEnabled = manualEnabled;
        _segmentsListBox.IsEnabled = manualEnabled;
        _closeButton.IsEnabled = manualEnabled;
        _busyProgressBar.IsVisible = busy;

        if (busy)
        {
            SetStudioStatus("Working", string.IsNullOrWhiteSpace(operationText) ? "Please wait..." : operationText);
        }
        else
        {
            if (_currentJob?.HasFinalOutput == true)
                SetStudioStatus("Final video ready", "The final translated video was built successfully. You can open it from this window.");
            else
                SetStudioStatus(_currentJob is null ? "Ready" : "Job loaded", _currentJob is null ? "Choose a video and click Prepare." : "Job is ready for browser editing, merging and final build.");
        }

        RefreshActionHints();
    }

    private void SetStudioStatus(string title, string details)
    {
        _studioStatusTextBlock.Text = T(title);
        _operationTextBlock.Text = T(details);
    }

    private string T(string text) => UiLocalizer.Translate(text, _language);

    private string F(string format, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(format), args);

    private string BuildCurrentPlayerUrl()
    {
        if (_currentJob is null)
            return _playerUrlTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_publicBaseUrl))
            return _currentJob.PlayerRelativeUrl;

        var selectedSegment = GetSelectedSegment();
        if (selectedSegment is null)
        {
            return AppendCacheBuster(_publicBaseUrl + BuildJobPlayerRelativeUrl(_currentJob));
        }

        var url = _publicBaseUrl + _currentJob.PlayerRelativeUrl;
        var separator = url.Contains('?') ? "&" : "?";
        return AppendCacheBuster(url + separator + "src=" + Uri.EscapeDataString(selectedSegment.PayloadRelativeUrl));
    }

    private static string BuildJobPlayerRelativeUrl(TranslationStudioJob job)
    {
        var playlistRelativeUrl = job.PlaylistRelativeUrl;
        var playerRelativeUrl = "/" + string.Join("/", playlistRelativeUrl.TrimStart('/').Split('/').SkipLast(1)) + "/player.html";
        return $"{playerRelativeUrl}?list={Uri.EscapeDataString(playlistRelativeUrl)}";
    }

    private void RefreshDisplayedPlayerUrl()
    {
        if (_currentJob is not null)
            _playerUrlTextBox.Text = BuildCurrentPlayerUrl();
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {T(message)}";
        var lines = (_logTextBox.Text ?? string.Empty)
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        lines.Add(line);
        if (lines.Count > 3)
            lines = lines.Skip(lines.Count - 3).ToList();
        _logTextBox.Text = string.Join(Environment.NewLine, lines);
        _logTextBox.CaretIndex = (_logTextBox.Text ?? string.Empty).Length;
    }

    private string BuildJobSummary(TranslationStudioJob? job)
    {
        if (job is null)
            return T("No job loaded.");

        var duration = job.SourceDurationSeconds > 0
            ? TimeSpan.FromSeconds(job.SourceDurationSeconds).ToString(@"hh\:mm\:ss")
            : T("unknown");

        var segmentCount = Math.Max(1, job.Segments.Count);
        var audioReady = job.Segments.Count(x => File.Exists(x.TranslatedAudioPath));
        var mergedReady = job.Segments.Count(x => File.Exists(x.MergedOutputPath));
        var finalReady = !string.IsNullOrWhiteSpace(job.FinalOutputPath) && File.Exists(job.FinalOutputPath);

        return F("Source: {0} | Duration: {1} | Segments: {2} | Audio: {3}/{2} | Merged: {4}/{2} | Final: {5}",
            job.SourceVideoTitle,
            duration,
            segmentCount,
            audioReady,
            mergedReady,
            finalReady ? T("Ready") : T("Pending"));
    }

    private void UpdateCurrentJobAudioSettingsFromControls()
    {
        if (_currentJob is null)
            return;

        _currentJob.OriginalAudioVolumePercent = GetOriginalAudioVolumePercent();
        _currentJob.TranslatedAudioVolumePercent = GetTranslatedAudioVolumePercent();
        _currentJob.AutoMergeOnCallback = _autoMergeCheckBox.IsChecked == true;
    }

    private string BuildPreviewSummary(VideoEntry? entry)
    {
        if (entry is null)
            return F("Active tunnel: {0} | Select a video first.", _activeTunnelDisplayName);

        var splitThreshold = (int)Math.Round(_splitThresholdNumeric.Value ?? 120);
        var targetSegment = (int)Math.Round(_targetSegmentNumeric.Value ?? 60);
        var autoMerge = _autoMergeCheckBox.IsChecked == true ? T("On") : T("Off");
        var segmentation = _splitLongVideoCheckBox.IsChecked == true
            ? F("Enabled after {0} min, target {1} min", splitThreshold, targetSegment)
            : T("Off");

        return F("Selected: {0} | Active tunnel: {1} | Auto merge: {2} | Original: {3:0.#}% | MP3: {4:0.#}% | Segmentation: {5}",
            entry.Title,
            _activeTunnelDisplayName,
            autoMerge,
            GetOriginalAudioVolumePercent(),
            GetTranslatedAudioVolumePercent(),
            segmentation);
    }

    private double GetOriginalAudioVolumePercent()
        => Convert.ToDouble(_originalAudioVolumeNumeric.Value ?? 8, CultureInfo.InvariantCulture);

    private double GetTranslatedAudioVolumePercent()
        => Convert.ToDouble(_translatedAudioVolumeNumeric.Value ?? 100, CultureInfo.InvariantCulture);

    private string BuildSegmentLabel(TranslationStudioSegment segment)
    {
        var hasAudio = File.Exists(segment.TranslatedAudioPath);
        var hasMerged = File.Exists(segment.MergedOutputPath);
        var status = hasMerged ? T("merged") : hasAudio ? T("audio-ready") : T("prepared");
        var start = TimeSpan.FromSeconds(Math.Max(0, segment.StartSeconds)).ToString(@"hh\:mm\:ss");
        var duration = TimeSpan.FromSeconds(Math.Max(0, segment.DurationSeconds)).ToString(@"hh\:mm\:ss");
        return F("{0:000}/{1:000} | {2} | start {3} | len {4} | {5}",
            segment.Index,
            segment.TotalSegments,
            status,
            start,
            duration,
            segment.Title);
    }

    private string BuildPreviewPayloadPath(VideoEntry entry)
    {
        var jobFolder = TranslationStudioService.GetJobFolder(_workspaceRoot, entry.Id);
        var sourceMp4 = Path.Combine(jobFolder, "source.mp4");
        var payloadMp4 = Path.Combine(jobFolder, "payload.mp4");

        if (_splitLongVideoCheckBox.IsChecked != true)
            return F("Source MP4: {0}{1}Payload MP4 (1 fps): {2}", sourceMp4, Environment.NewLine, payloadMp4);

        var splitThreshold = (int)Math.Round(_splitThresholdNumeric.Value ?? 120);
        var segmentsRoot = Path.Combine(jobFolder, "segments");
        return F("If video is up to {0} min:{1}Source MP4: {2}{1}Payload MP4 (1 fps): {3}{1}{1}If video is longer:{1}Each segment gets source.mp4 and payload.mp4 under {4}",
            splitThreshold,
            Environment.NewLine,
            sourceMp4,
            payloadMp4,
            segmentsRoot);
    }

    private string BuildPreviewPlayerUrl(VideoEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_publicBaseUrl))
            return T("Will be created after Prepare.");

        var playerRelativeUrl = BuildWorkspaceRelativeUrl(Path.Combine(
            TranslationStudioService.GetJobFolder(_workspaceRoot, entry.Id),
            "player.html"));
        var playlistRelativeUrl = BuildWorkspaceRelativeUrl(Path.Combine(
            TranslationStudioService.GetJobFolder(_workspaceRoot, entry.Id),
            "playlist.json"));

        return string.IsNullOrWhiteSpace(playerRelativeUrl) || string.IsNullOrWhiteSpace(playlistRelativeUrl)
            ? T("Will be created after Prepare.")
            : AppendCacheBuster(_publicBaseUrl + playerRelativeUrl + "?list=" + Uri.EscapeDataString(playlistRelativeUrl));
    }

    private static string AppendCacheBuster(string url)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + "_ts=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private string BuildPreviewFinalOutputPath(VideoEntry entry)
    {
        return Path.Combine(
            _workspaceRoot,
            "merged_output",
            Path.GetFileNameWithoutExtension(entry.FileName) + ".studio.translated.mp4");
    }

    private string BuildWorkspaceRelativeUrl(string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(_workspaceRoot, fullPath).Replace('\\', '/');
            return relative.StartsWith("../", StringComparison.Ordinal)
                ? string.Empty
                : "/" + VideoWorkspace.EncodeUrlPath(relative);
        }
        catch
        {
            return string.Empty;
        }
    }

    private List<string> BuildPreviewSegmentItems()
    {
        var splitThreshold = (int)Math.Round(_splitThresholdNumeric.Value ?? 120);
        var targetSegment = (int)Math.Round(_targetSegmentNumeric.Value ?? 60);

        if (_splitLongVideoCheckBox.IsChecked == true)
        {
            return
            [
                F("Videos up to {0} min stay as one job.", splitThreshold),
                F("Longer videos are split into about {0}-minute parts.", targetSegment),
                T("Next step: click Prepare.")
            ];
        }

        return
        [
            T("Segmentation is disabled."),
            T("One source MP4 and one 1 fps payload MP4 will be created."),
            T("Next step: click Prepare.")
        ];
    }

    private void RefreshActionHints()
    {
        SetTip(_loadJobButton, "Load an existing prepared job for the selected video.");
        SetTip(_prepareButton, "Create or refresh source.mp4, payload.mp4, playlist.json and player.html.");
        SetTip(_openFullPlaylistButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
                : "Open Playlist for the whole job. Short jobs contain one item; segmented jobs contain all generated segments.");
        SetTip(_openNextUntranslatedButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : GetNextUntranslatedSegment() is null
                ? "All segments already have translated audio or merged output."
                : "Open Next for the first segment that still needs translated audio.");
        SetTip(_openPlayerButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : GetSelectedSegment() is null
                ? "Select a segment first."
                : "Open Segment for the selected item only.");
        SetTip(_importAudioButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : GetSelectedSegment() is null
                ? "Select a segment first."
                : "Import audio manually for the selected segment.");
        SetTip(_mergeSegmentButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : GetSelectedSegment() is null
                ? "Select a segment first."
                : "Merge the selected segment with its translated audio.");
        SetTip(_buildFinalButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : "Build the final translated output from ready segments.");
        SetTip(_openJobFolderButton, _currentJob is null
            ? "Disabled until a job is prepared or loaded."
            : "Open the job folder in Explorer.");
        SetTip(_openMergedOutputButton, _currentJob?.HasFinalOutput == true
            ? "Open the final translated output."
            : "Disabled until the final translated output exists.");
        SetTip(_copyUrlButton, string.IsNullOrWhiteSpace(_playerUrlTextBox.Text)
            ? "Disabled until a preview or prepared player URL is available."
            : "Copy the current player or playlist URL.");
    }

    private void SetTip(Control control, string text) => ToolTip.SetTip(control, T(text));

    private string GetHelpText(string key)
    {
        return key switch
        {
            "Video" => "Choose the source video. The other fields are filled automatically from this video.\n\nExample:\nPick one lesson from the list, then click Prepare.",
            "Options" => "If Auto merge is enabled, the app saves the translated mp3 automatically and merges it back into the prepared source MP4 when the callback arrives.",
            "Segmentation" => "If enabled, videos longer than the split threshold are split into segments. Every segment gets two MP4 files: source.mp4 and payload.mp4.",
            "SplitThreshold" => "The video is split only if its real duration is longer than this value.\n\nExample:\n120 means videos up to 2 hours stay as one job.",
            "TargetSegment" => "Approximate length of each segment for long videos.\n\nExample:\n60 means a 6 hour video becomes about 6 segments.",
            "JobSummary" => "Shows the current source duration, segment count, translated audio count, merged count and final status.",
            "Payload" => "Translation Studio prepares two MP4 files.\n\nsource.mp4 = full real video\npayload.mp4 = lightweight 1 fps MP4 over the full audio duration\n\nFor segmented jobs each segment gets the same pair.",
            "PlayerUrl" => "This is the browser URL for the prepared job. Use Copy to share it. Playlist returns to all prepared items after you open one segment.",
            "Segments" => "Shows the prepared segments and their status: prepared, audio-ready or merged.",
            "FinalOutput" => "This is the final merged translated video path that will be created in merged_output.",
            _ => "No help is available for this item."
        };
    }

    private async Task ShowHelpAsync(string title, string helpText)
    {
        var textBox = new TextBox
        {
            Text = T(helpText),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Background = Avalonia.Media.Brush.Parse("#020817"),
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
            MinHeight = 180
        };

        var closeButton = new Button
        {
            Content = T("Close"),
            Width = 90,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var panel = new DockPanel { Margin = new Avalonia.Thickness(16) };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        panel.Children.Add(closeButton);
        panel.Children.Add(textBox);

        var helpWindow = new Window
        {
            Title = F("{0} help", T(title)),
            Width = 560,
            Height = 340,
            MinWidth = 460,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#0B1220"),
            Content = panel
        };

        closeButton.Click += (_, _) => helpWindow.Close();
        await helpWindow.ShowDialog(this);
    }

    private void OpenPathWithFeedback(string? path, string label)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            FfmpegService.TryUnblockFile(path);

        if (TryOpenPath(path, out var error))
        {
            AppendLog(F("Opened {0}.", T(label)));
            return;
        }

        AppendLog(F("Open {0} failed: {1}", T(label), T(error)));
    }

    private static bool TryOpenPath(string? path, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = explorerPath,
                        Arguments = "/select,\"" + path + "\"",
                        UseShellExecute = false
                    });
                    return true;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }

            var parentDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDirectory) && Directory.Exists(parentDirectory))
            {
                if (OperatingSystem.IsWindows())
                {
                    var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = explorerPath,
                        Arguments = "\"" + parentDirectory + "\"",
                        UseShellExecute = false
                    });
                    return true;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = parentDirectory,
                    UseShellExecute = true
                });
                return true;
            }

            error = "Path does not exist.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
