using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PublicVideoLauncher.Models;

public sealed class VideoEntry : INotifyPropertyChanged
{
    private string _cacheState = "Pending";
    private bool _includeInBatch = true;

    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    public string OriginalUrl { get; set; } = string.Empty;
    public string PlaybackUrl { get; set; } = string.Empty;
    public string TranslationUrl { get; set; } = string.Empty;
    public string VotHelperUrl { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;
    public string RelativeUrl { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }

    public string? PlaybackCachePath { get; set; }
    public string? TranslationCachePath { get; set; }
    public bool ProxyModeEnabled { get; set; }

    public bool IsDirectMp4 => string.Equals(Extension, ".mp4", StringComparison.OrdinalIgnoreCase);
    public bool UsesProxyForTranslation =>
        !string.IsNullOrWhiteSpace(TranslationCachePath) &&
        !string.Equals(PlaybackUrl, TranslationUrl, StringComparison.OrdinalIgnoreCase);

    public bool UsesAnyCache => !string.IsNullOrWhiteSpace(PlaybackCachePath) || !string.IsNullOrWhiteSpace(TranslationCachePath);
    public bool ShowProxyColumns => ProxyModeEnabled;
    public bool ShowSimpleColumns => !ProxyModeEnabled;

    public string PlaybackKind => !string.IsNullOrWhiteSpace(PlaybackCachePath)
        ? "Playable cache MP4"
        : (IsDirectMp4 ? "Direct MP4" : "Original file");

    public string TranslationKind => UsesProxyForTranslation
        ? "Audio proxy MP4"
        : (ProxyModeEnabled ? "Same as playback" : "Translation proxy disabled");

    public string CacheState
    {
        get => _cacheState;
        set => SetField(ref _cacheState, value);
    }

    public bool IncludeInBatch
    {
        get => _includeInBatch;
        set => SetField(ref _includeInBatch, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
