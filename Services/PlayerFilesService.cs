using PublicVideoLauncher.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicVideoLauncher.Services;

public sealed class PlayerFilesService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task WriteAllAsync(string rootFolder, IReadOnlyList<VideoEntry> entries, string? translationCallbackUrl, CancellationToken cancellationToken)
    {
        await WriteAllAsync(rootFolder, entries, translationCallbackUrl, AppLanguage.English, cancellationToken);
    }

    public async Task WriteAllAsync(
        string rootFolder,
        IReadOnlyList<VideoEntry> entries,
        string? translationCallbackUrl,
        AppLanguage language,
        CancellationToken cancellationToken)
    {
        await WritePlaylistAsync(rootFolder, entries, cancellationToken);
        await WritePlayerPageAsync(rootFolder, "player.html", translationCallbackUrl, "Local Video Player", language, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(rootFolder, "favicon.svg"), FaviconSvg, Utf8NoBom, cancellationToken);
    }

    public async Task WritePlayerPageAsync(
        string rootFolder,
        string fileName,
        string? translationCallbackUrl,
        string pageTitle,
        CancellationToken cancellationToken)
    {
        await WritePlayerPageAsync(rootFolder, fileName, translationCallbackUrl, pageTitle, AppLanguage.English, cancellationToken);
    }

    public async Task WritePlayerPageAsync(
        string rootFolder,
        string fileName,
        string? translationCallbackUrl,
        string pageTitle,
        AppLanguage language,
        CancellationToken cancellationToken)
    {
        var playerHtml = PlayerHtml
            .Replace("__PVL_CALLBACK_URL__", JsonSerializer.Serialize(translationCallbackUrl ?? string.Empty))
            .Replace("__PVL_PAGE_TITLE__", T(pageTitle, language))
            .Replace("__PVL_LANG__", language == AppLanguage.Russian ? "ru" : "en")
            .Replace("__PVL_LABELS__", BuildPlayerLabelsJson(language))
            .Replace("__PVL_LOCAL_VIDEO_PLAYER__", T("Local Video Player", language))
            .Replace("__PVL_WAITING_FOR_SOURCE__", T("Waiting for source...", language))
            .Replace("__PVL_PLAYLIST_EMPTY__", T("Playlist: --", language))
            .Replace("__PVL_STATIC_MP4_DELIVERY__", T("STATIC MP4 DELIVERY", language))
            .Replace("__PVL_LOADING_VIDEO__", T("Loading video", language))
            .Replace("__PVL_PREPARING_PLAYER__", T("Preparing the player...", language))
            .Replace("__PVL_PREVIOUS__", T("Previous", language))
            .Replace("__PVL_NEXT__", T("Next", language))
            .Replace("__PVL_AUTO_PLAY_NEXT__", T("Auto play next", language))
            .Replace("__PVL_NO_PLAYLIST_LOADED__", T("No playlist loaded", language))
            .Replace("__PVL_QUEUE__", T("Queue", language))
            .Replace("__PVL_WAITING_FOR_PLAYLIST__", T("Waiting for playlist...", language))
            .Replace("__PVL_UP_NEXT__", T("Up next", language))
            .Replace("__PVL_NOTHING_QUEUED__", T("Nothing queued", language));
        await File.WriteAllTextAsync(Path.Combine(rootFolder, fileName), playerHtml, Utf8NoBom, cancellationToken);
    }

    private static string T(string text, AppLanguage language) => UiLocalizer.Translate(text, language);

    private static string BuildPlayerLabelsJson(AppLanguage language)
    {
        var labels = new Dictionary<string, string>
        {
            ["endOfPlaylist"] = T("End of playlist", language),
            ["noNextVideo"] = T("No next video available.", language),
            ["playlistUnavailable"] = T("playlist.json is empty or unavailable.", language),
            ["singleVideoMode"] = T("Single video mode", language),
            ["playlist"] = T("Playlist", language),
            ["items"] = T("items", language),
            ["videos"] = T("video(s)", language),
            ["nowPlaying"] = T("Now playing", language),
            ["playing"] = T("Playing", language),
            ["of"] = T("of", language),
            ["noVideoSource"] = T("No video source", language),
            ["addSource"] = T("Add a source via ?src=/path/to/video.mp4", language),
            ["unsupportedSource"] = T("Unsupported source", language),
            ["expectedMp4"] = T("Expected a direct .mp4 file", language),
            ["loadingVideo"] = T("Loading video", language),
            ["fetchingMp4"] = T("Fetching MP4 file...", language),
            ["readyToPlay"] = T("Ready to play", language),
            ["autoplayBlocked"] = T("Autoplay was blocked. Press Play to continue.", language),
            ["sec"] = T("sec", language),
            ["buffering"] = T("Buffering", language),
            ["pleaseWait"] = T("Please wait...", language),
            ["playbackError"] = T("Playback error", language),
            ["fileCouldNotLoad"] = T("The file could not be loaded.", language),
            ["playbackAborted"] = T("Playback was aborted.", language),
            ["networkError"] = T("A network error occurred while loading the video.", language),
            ["decodeError"] = T("The video file is corrupted or cannot be decoded.", language),
            ["sourceNotSupported"] = T("This video source is not supported.", language),
            ["noVideosFound"] = T("No videos found", language),
            ["noSupportedVideos"] = T("playlist.json is empty or no supported videos were found.", language)
        };

        return JsonSerializer.Serialize(labels);
    }

    private static async Task WritePlaylistAsync(string rootFolder, IReadOnlyList<VideoEntry> entries, CancellationToken cancellationToken)
    {
        var items = entries.Select(x => new
        {
            id = x.Id,
            title = x.Title,
            src = x.PlaybackUrl,
            playbackSrc = x.PlaybackUrl,
            votSrc = x.VotHelperUrl,
            translateSrc = x.TranslationUrl,
            originalSrc = x.OriginalUrl,
            playbackKind = x.PlaybackKind,
            translationKind = x.TranslationKind,
        });

        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(rootFolder, "playlist.json"), json, Utf8NoBom, cancellationToken);
    }

    public const string FaviconSvg = """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <rect width="64" height="64" rx="14" fill="#081122"/>
  <circle cx="18" cy="32" r="6" fill="#22d3ee"/>
  <path d="M28 20h18a6 6 0 0 1 6 6v12a6 6 0 0 1-6 6H28z" fill="#e2e8f0" opacity="0.95"/>
</svg>
""";

    public const string PlayerHtml = """
<!doctype html>
<html lang="__PVL_LANG__">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>__PVL_PAGE_TITLE__</title>
  <style>
    :root {
      --bg: #0b0d12;
      --panel: rgba(255,255,255,0.06);
      --panel-strong: rgba(255,255,255,0.08);
      --panel-border: rgba(255,255,255,0.10);
      --text: #f5f7fb;
      --muted: #9aa6bd;
      --accent: #6ea8fe;
      --accent-2: rgba(110,168,254,0.18);
      --success: #72e29a;
      --shadow: 0 18px 50px rgba(0,0,0,.42);
      --radius: 20px;
      --radius-sm: 14px;
    }

    * { box-sizing: border-box; }

    html, body {
      margin: 0;
      width: 100%;
      min-height: 100%;
      background:
        radial-gradient(circle at top, rgba(110,168,254,0.14), transparent 30%),
        linear-gradient(180deg, #111827 0%, var(--bg) 48%, #07090d 100%);
      color: var(--text);
      font-family: Inter, Segoe UI, Roboto, Arial, sans-serif;
    }

    body {
      padding: 18px;
      box-sizing: border-box;
      min-height: 100dvh;
      overflow: hidden;
    }

    .app {
      width: 100%;
      height: calc(100dvh - 36px);
      display: grid;
      grid-template-columns: minmax(0, 1fr) 390px;
      gap: 12px;
      min-height: 0;
    }

    .main {
      min-width: 0;
      min-height: 0;
      display: grid;
      grid-template-rows: auto minmax(0, 1fr) auto;
      gap: 14px;
    }

    .panel {
      border: 1px solid var(--panel-border);
      background: var(--panel);
      backdrop-filter: blur(14px);
      border-radius: var(--radius-sm);
      box-shadow: var(--shadow);
    }

    .topbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 14px;
      padding: 12px 16px;
      min-width: 0;
    }

    .brand {
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 0;
    }

    .brand-dot {
      width: 10px;
      height: 10px;
      border-radius: 999px;
      background: var(--accent);
      box-shadow: 0 0 18px rgba(110,168,254,.85);
      flex: 0 0 auto;
    }

    .brand-copy { min-width: 0; }

    .title {
      font-size: 15px;
      font-weight: 700;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .subtitle {
      margin-top: 2px;
      font-size: 12px;
      color: var(--muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .badges {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }

    .pill {
      padding: 7px 10px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: .3px;
      border: 1px solid rgba(110,168,254,.22);
      background: rgba(110,168,254,.10);
      color: #dce9ff;
      white-space: nowrap;
    }

    .player-shell {
      min-width: 0;
      min-height: 0;
      padding: 14px;
      display: flex;
      place-items: center;
      overflow: hidden;
    }

    .video-wrap {
      position: relative;
      width: 100%;
      height: 100%;
      min-width: 0;
      min-height: 0;
      max-width: 100%;
      max-height: 100%;
      border-radius: var(--radius);
      overflow: hidden;
      background: #000;
      border: 1px solid var(--panel-border);
    }

    video {
      width: 100%;
      height: 100%;
      display: block;
      background: #000;
      object-fit: contain;
      object-position: center center;
    }

    .overlay {
      position: absolute;
      inset: 0;
      display: none;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background: linear-gradient(180deg, rgba(11,13,18,0.72), rgba(11,13,18,0.88));
      text-align: center;
    }

    .overlay.visible { display: flex; }

    .overlay-card {
      width: min(560px, 100%);
      padding: 24px;
      border-radius: 18px;
      border: 1px solid var(--panel-border);
      background: rgba(255,255,255,.06);
      backdrop-filter: blur(10px);
    }

    .overlay-title {
      margin: 0 0 8px;
      font-size: 20px;
      font-weight: 800;
    }

    .overlay-text {
      margin: 0;
      color: var(--muted);
      line-height: 1.55;
      font-size: 14px;
    }

    .overlay-text.error { color: #ffd6d6; }

    .controls {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 12px 16px;
      min-width: 0;
    }

    .controls-left,
    .controls-right {
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
      flex-wrap: wrap;
    }

    .btn {
      appearance: none;
      border: 1px solid var(--panel-border);
      background: var(--panel-strong);
      color: var(--text);
      border-radius: 12px;
      padding: 10px 14px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      transition: .18s ease;
    }

    .btn:hover:not(:disabled) {
      transform: translateY(-1px);
      border-color: rgba(110,168,254,.35);
      background: rgba(255,255,255,.10);
    }

    .btn:disabled { opacity: .45; cursor: not-allowed; }

    .btn.primary {
      background: var(--accent-2);
      border-color: rgba(110,168,254,.28);
      color: #e6f0ff;
    }

    .meta {
      font-size: 12px;
      color: var(--muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 420px;
    }

    .check {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--muted);
      user-select: none;
    }

    .check input { accent-color: var(--accent); }

    .sidebar {
      min-width: 0;
      min-height: 0;
      overflow: hidden;
      display: grid;
      grid-template-rows: auto auto minmax(0, 1fr);
      gap: 14px;
      padding: 14px;
    }

    .sidebar-title { font-size: 16px; font-weight: 800; }
    .sidebar-subtitle { font-size: 12px; color: var(--muted); margin-top: 4px; }

    .next-card {
      padding: 14px;
      border-radius: 16px;
      border: 1px solid var(--panel-border);
      background: rgba(255,255,255,.04);
    }

    .next-label {
      font-size: 11px;
      letter-spacing: .4px;
      text-transform: uppercase;
      color: var(--muted);
      margin-bottom: 8px;
    }

    .next-title { font-size: 14px; font-weight: 700; line-height: 1.4; }
    .next-empty { font-size: 13px; color: var(--muted); }

    .playlist {
      min-height: 0;
      overflow-y: auto;
      overflow-x: hidden;
      display: grid;
      gap: 10px;
      padding-right: 2px;
    }

    .playlist-item {
      width: 100%;
      text-align: left;
      cursor: pointer;
      border: 1px solid var(--panel-border);
      background: rgba(255,255,255,.035);
      color: var(--text);
      border-radius: 14px;
      padding: 12px;
      transition: .18s ease;
    }

    .playlist-item:hover {
      transform: translateY(-1px);
      background: rgba(255,255,255,.065);
      border-color: rgba(110,168,254,.28);
    }

    .playlist-item.active {
      border-color: rgba(110,168,254,.45);
      background: rgba(110,168,254,.14);
      box-shadow: inset 0 0 0 1px rgba(110,168,254,.15);
    }

    .playlist-top {
      display: flex;
      align-items: center;
      justify-content: flex-start;
      gap: 10px;
      margin-bottom: 6px;
    }

    .playlist-index {
      font-size: 11px;
      color: var(--muted);
      font-weight: 700;
      letter-spacing: .3px;
    }

    .playlist-state {
      font-size: 11px;
      color: var(--success);
      font-weight: 800;
      letter-spacing: .3px;
      text-transform: uppercase;
    }

    .playlist-name {
      font-size: 14px;
      font-weight: 700;
      line-height: 1.4;
      word-break: break-word;
    }

    .playlist-src {
      margin-top: 6px;
      font-size: 12px;
      color: var(--muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .empty {
      padding: 16px;
      border-radius: 14px;
      border: 1px dashed var(--panel-border);
      color: var(--muted);
      font-size: 13px;
      line-height: 1.55;
    }

    @media (max-width: 1100px) {
      body { overflow: auto; }
      .app {
        height: auto;
        min-height: calc(100dvh - 36px);
        grid-template-columns: 1fr;
      }
      .main { min-height: auto; }
      .player-shell { min-height: unset; overflow: visible; }
      .video-wrap {
        width: 100% !important;
        height: auto !important;
        aspect-ratio: 16 / 9;
        max-height: none;
      }
      .sidebar {
        min-height: unset;
        overflow: visible;
        grid-template-rows: auto auto auto;
      }
      .playlist { max-height: 360px; }
    }

    @media (max-width: 720px) {
      body { padding: 12px; }
      .topbar,
      .controls {
        flex-direction: column;
        align-items: stretch;
      }
      .badges,
      .controls-right {
        justify-content: flex-start;
      }
      .meta { max-width: none; }
    }
  </style>
</head>
<body>
  <div class="app">
    <section class="main">
      <div class="panel topbar">
        <div class="brand">
          <div class="brand-dot"></div>
          <div class="brand-copy">
            <div class="title" id="videoTitle">__PVL_LOCAL_VIDEO_PLAYER__</div>
            <div class="subtitle" id="videoSubtitle">__PVL_WAITING_FOR_SOURCE__</div>
          </div>
        </div>

        <div class="badges">
          <div class="pill" id="playlistBadge">__PVL_PLAYLIST_EMPTY__</div>
          <div class="pill">__PVL_STATIC_MP4_DELIVERY__</div>
        </div>
      </div>

      <div class="panel player-shell">
        <div class="video-wrap">
          <video id="video" controls preload="metadata" playsinline></video>

          <div class="overlay visible" id="overlay">
            <div class="overlay-card">
              <h1 class="overlay-title" id="overlayTitle">__PVL_LOADING_VIDEO__</h1>
              <p class="overlay-text" id="overlayText">__PVL_PREPARING_PLAYER__</p>
            </div>
          </div>
        </div>
      </div>

      <div class="panel controls">
        <div class="controls-left">
          <button class="btn" id="prevBtn" type="button">&larr; __PVL_PREVIOUS__</button>
          <button class="btn primary" id="nextBtn" type="button">__PVL_NEXT__ &rarr;</button>
          <label class="check">
            <input type="checkbox" id="autoplayNext" checked />
            __PVL_AUTO_PLAY_NEXT__
          </label>
        </div>

        <div class="controls-right">
          <div class="meta" id="metaInfo">__PVL_NO_PLAYLIST_LOADED__</div>
        </div>
      </div>
    </section>

    <aside class="panel sidebar">
      <div>
        <div class="sidebar-title">__PVL_QUEUE__</div>
        <div class="sidebar-subtitle" id="queueSubtitle">__PVL_WAITING_FOR_PLAYLIST__</div>
      </div>

      <div class="next-card">
        <div class="next-label">__PVL_UP_NEXT__</div>
        <div class="next-title" id="upNextTitle">__PVL_NOTHING_QUEUED__</div>
        <div class="next-empty" id="upNextMeta"></div>
      </div>

      <div class="playlist" id="playlist"></div>
    </aside>
  </div>

  <script>
    (function installTranslationAutoDownloadHook() {
      const seenTranslationDownloads = new Set();
      const startedTranslationKeys = new Set();
      const failedTranslationKeys = new Set();
      const translationCallbackUrl = __PVL_CALLBACK_URL__ || "/translation-ready";
      const translationReadyCallbackUrl = buildCallbackUrl("translation-ready");
      const translationStartedCallbackUrl = buildCallbackUrl("translation-started");
      const translationFailedCallbackUrl = buildCallbackUrl("translation-failed");
      window.__PVL_LAST_TRANSLATED_SUBTITLES_URL = null;
      const originalConsoleLog = console.log.bind(console);
      const originalConsoleDebug = console.debug ? console.debug.bind(console) : null;
      const originalConsoleInfo = console.info ? console.info.bind(console) : null;
      const originalFetch = window.fetch ? window.fetch.bind(window) : null;
      const OriginalXMLHttpRequest = window.XMLHttpRequest;
      const originalOpen = OriginalXMLHttpRequest && OriginalXMLHttpRequest.prototype.open;
      const originalSend = OriginalXMLHttpRequest && OriginalXMLHttpRequest.prototype.send;
      const mediaProto = window.HTMLMediaElement && window.HTMLMediaElement.prototype;
      const mediaSrcDescriptor = mediaProto ? Object.getOwnPropertyDescriptor(mediaProto, "src") : null;
      const originalSetAttribute = Element.prototype.setAttribute;

      function buildCallbackUrl(endpointName) {
        const base = translationCallbackUrl || "/translation-ready";
        try {
          const url = new URL(base, window.location.href);
          const trimmedPath = (url.pathname || "/").replace(/\/+$/, "");
          const slashIndex = trimmedPath.lastIndexOf("/");
          const prefix = slashIndex >= 0 ? trimmedPath.slice(0, slashIndex + 1) : "/";
          url.pathname = prefix + endpointName;
          url.search = "";
          url.hash = "";
          return url.toString();
        } catch {
          return "/" + endpointName;
        }
      }

      function extractFirstUrl(value) {
        if (!value) return null;
        const match = String(value).match(/https?:\/\/[^\s"'<>]+/i);
        return match ? match[0] : null;
      }

      function isTranslationAudioUrl(url) {
        if (!url) return false;
        const normalized = String(url).toLowerCase();
        if (!/^https?:\/\//.test(normalized)) return false;
        return normalized.includes("vtrans.s3-private.mds.yandex.net")
          || normalized.includes("/tts/")
          || normalized.includes("translation") && /\.(mp3|m4a|aac|wav|ogg)(?:$|[?#])/i.test(normalized)
          || /\.(mp3|m4a|aac|wav|ogg)(?:$|[?#])/i.test(normalized) && normalized.includes("x-amz-signature");
      }

      function isTranslationStartRequestUrl(url) {
        if (!url) return false;
        const normalized = String(url).toLowerCase();
        if (!/^https?:\/\//.test(normalized)) return false;
        if (isTranslationAudioUrl(normalized)) return false;
        if (/\.(mp3|m4a|aac|wav|ogg|vtt|srt)(?:$|[?#])/i.test(normalized)) return false;
        return normalized.includes("video-translation")
          || normalized.includes("/translate")
          || (normalized.includes("translation")
            && (normalized.includes("request")
              || normalized.includes("status")
              || normalized.includes("result")));
      }

      function getMessageFragments(args) {
        const fragments = [];
        for (const arg of args) {
          if (typeof arg === "string" && arg.trim()) {
            fragments.push(arg.trim());
            continue;
          }

          if (arg && typeof arg === "object") {
            const candidates = [arg.errorMessage, arg.message, arg.error, arg.reason, arg.statusText, arg.type, arg.state];
            for (const candidate of candidates) {
              if (typeof candidate === "string" && candidate.trim()) {
                fragments.push(candidate.trim());
              }
            }
          }
        }

        return fragments;
      }

      function extractTranslationStarted(args) {
        const joined = getMessageFragments(args).join(" ").toLowerCase();
        if (!joined) return false;
        return joined.includes("translation started")
          || joined.includes("starting translation")
          || joined.includes("start translation")
          || joined.includes("requesting translation")
          || joined.includes("request translation")
          || joined.includes("translation request started");
      }

      function inferTranslationErrorCode(message) {
        const normalized = String(message || "").toLowerCase();
        if (normalized.includes("try again later")) return "try-again-later";
        if (normalized.includes("too many requests") || normalized.includes("rate limit")) return "rate-limited";
        if (normalized.includes("not available")) return "translation-unavailable";
        return "translation-failed";
      }

      function extractTranslationFailure(args) {
        let explicitCode = null;
        const fragments = [];

        for (const arg of args) {
          if (typeof arg === "string" && arg.trim()) {
            fragments.push(arg.trim());
            continue;
          }

          if (arg && typeof arg === "object") {
            const codeCandidates = [arg.errorCode, arg.code, arg.status];
            for (const candidate of codeCandidates) {
              if ((typeof candidate === "string" || typeof candidate === "number") && String(candidate).trim()) {
                explicitCode = String(candidate).trim();
                break;
              }
            }

            const messageCandidates = [arg.errorMessage, arg.message, arg.error, arg.reason, arg.statusText];
            for (const candidate of messageCandidates) {
              if (typeof candidate === "string" && candidate.trim()) {
                fragments.push(candidate.trim());
              }
            }
          }
        }

        const joined = fragments.join(" | ");
        const lowered = joined.toLowerCase();
        if (!lowered || lowered.includes("afterupdatetranslation downloadtranslationurl")) {
          return null;
        }

        const isFailure =
          lowered.includes("translation failed")
          || lowered.includes("failed to translate")
          || lowered.includes("failed to get translation")
          || lowered.includes("could not get translation")
          || lowered.includes("translation error")
          || lowered.includes("try again later")
          || lowered.includes("too many requests")
          || lowered.includes("rate limit")
          || lowered.includes("translation unavailable");

        if (!isFailure) {
          return null;
        }

        return {
          errorCode: explicitCode || inferTranslationErrorCode(joined),
          errorMessage: joined
        };
      }

      function extractTranslationUrl(args) {
        for (const arg of args) {
          if (typeof arg === "string" && arg.includes("afterUpdateTranslation downloadTranslationUrl")) {
            const match = extractFirstUrl(arg);
            if (match) return match;
          }
        }

        const joined = args.map(x => typeof x === "string" ? x : "").join(" ");
        if (joined.includes("afterUpdateTranslation downloadTranslationUrl")) {
          const match = extractFirstUrl(joined);
          if (match) return match;
        }

        for (const arg of args) {
          if (typeof arg === "string" && isTranslationAudioUrl(arg.trim())) {
            return arg.trim();
          }
          if (arg && typeof arg === "object") {
            const candidates = [arg.url, arg.src, arg.href, arg.downloadTranslationUrl];
            for (const candidate of candidates) {
              if (typeof candidate === "string" && isTranslationAudioUrl(candidate)) {
                return candidate;
              }
            }
          }
        }

        const genericMatch = extractFirstUrl(joined);
        return isTranslationAudioUrl(genericMatch) ? genericMatch : null;
      }

      function sanitizeFileName(value) {
        return String(value || "translation")
          .replace(/[\/:*?"<>|]+/g, "-")
          .replace(/\s+/g, " ")
          .trim();
      }

      function getCurrentVideoName() {
        const currentVideo = document.getElementById("video");
        const currentSrc = (currentVideo && (currentVideo.currentSrc || currentVideo.getAttribute("src"))) || "";
        const params = new URLSearchParams(window.location.search);
        const src = currentSrc || params.get("src") || "video.mp4";
        const clean = String(src).split("?")[0].split("#")[0];
        const fileName = clean.split("/").pop() || "video.mp4";
        return fileName.replace(/\.[^.]+$/i, "");
      }

      function getPreferredVideoSrc() {
        const currentVideo = document.getElementById("video");
        return window.__PVL_VOT_SRC
          || (currentVideo && currentVideo.dataset ? currentVideo.dataset.votSrc : "")
          || (currentVideo && (currentVideo.currentSrc || currentVideo.getAttribute("src")))
          || "";
      }

      function getCurrentVideoKey() {
        return String(getPreferredVideoSrc() || window.location.href || "video").toLowerCase();
      }

      function getQueueEntryId() {
        const params = new URLSearchParams(window.location.search);
        return params.get("queueEntryId") || null;
      }

      function getQueueSessionId() {
        const params = new URLSearchParams(window.location.search);
        return params.get("queueSessionId") || null;
      }

      function buildPayload(url, subtitlesUrl, errorCode, errorMessage) {
        const preferredSrc = getPreferredVideoSrc();

        const baseName = sanitizeFileName(getCurrentVideoName());

        return {
          url: url || "",
          entryId: getQueueEntryId(),
          sessionId: getQueueSessionId(),
          videoSrc: preferredSrc,
          videoTitle: getCurrentVideoName(),
          suggestedFileName: baseName + ".translated.mp3",
          subtitlesUrl: subtitlesUrl || null,
          suggestedSubtitlesFileName: subtitlesUrl ? (baseName + ".translated.vtt") : null,
          errorCode: errorCode || null,
          errorMessage: errorMessage || null
        };
      }

      async function postJson(url, payload) {
        if (originalFetch) {
          return originalFetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            mode: "same-origin",
            credentials: "same-origin",
            cache: "no-store"
          });
        }

        return new Promise((resolve, reject) => {
          try {
            const xhr = new XMLHttpRequest();
            xhr.open("POST", url, true);
            xhr.setRequestHeader("Content-Type", "application/json");
            xhr.onreadystatechange = function() {
              if (xhr.readyState === 4) {
                resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status });
              }
            };
            xhr.onerror = reject;
            xhr.send(JSON.stringify(payload));
          } catch (err) {
            reject(err);
          }
        });
      }

      async function notifyTranslationStarted(source) {
        const videoKey = getCurrentVideoKey();
        if (!translationStartedCallbackUrl || !videoKey) {
          return false;
        }

        if (startedTranslationKeys.has(videoKey)) {
          return true;
        }

        startedTranslationKeys.add(videoKey);

        try {
          const payload = buildPayload("", window.__PVL_LAST_TRANSLATED_SUBTITLES_URL || null, null, null);
          const response = await postJson(translationStartedCallbackUrl, payload);
          if (!response.ok) {
            throw new Error("callback status " + response.status);
          }

          originalConsoleLog("[PVL] Translation started forwarded to app", source || "unknown", payload.videoTitle);
          return true;
        } catch (err) {
          startedTranslationKeys.delete(videoKey);
          originalConsoleLog("[PVL] Translation started callback failed", source || "unknown", err);
          return false;
        }
      }

      async function notifyTranslationFailed(errorCode, errorMessage, source) {
        const videoKey = getCurrentVideoKey();
        if (!translationFailedCallbackUrl || !videoKey) {
          return false;
        }

        if (failedTranslationKeys.has(videoKey)) {
          return true;
        }

        failedTranslationKeys.add(videoKey);

        try {
          const payload = buildPayload("", window.__PVL_LAST_TRANSLATED_SUBTITLES_URL || null, errorCode, errorMessage);
          const response = await postJson(translationFailedCallbackUrl, payload);
          if (!response.ok) {
            throw new Error("callback status " + response.status);
          }

          originalConsoleLog("[PVL] Translation failure forwarded to app", source || "unknown", payload.videoTitle, payload.errorCode, payload.errorMessage);
          return true;
        } catch (err) {
          failedTranslationKeys.delete(videoKey);
          originalConsoleLog("[PVL] Translation failed callback failed", source || "unknown", err);
          return false;
        }
      }

      async function notifyTranslationReady(url, source) {
        if (!translationReadyCallbackUrl || !isTranslationAudioUrl(url)) {
          return false;
        }

        await notifyTranslationStarted(source || "ready");

        try {
          const payload = buildPayload(url, window.__PVL_LAST_TRANSLATED_SUBTITLES_URL || null, null, null);
          const response = await postJson(translationReadyCallbackUrl, payload);
          if (!response.ok) {
            throw new Error("callback status " + response.status);
          }

          originalConsoleLog("[PVL] Translation ready forwarded to app", source || "unknown", payload.videoTitle, url);
          return true;
        } catch (err) {
          originalConsoleLog("[PVL] Translation ready callback failed", source || "unknown", err);
          return false;
        }
      }

      function handleTranslationUrl(url, source) {
        if (!url || seenTranslationDownloads.has(url) || !isTranslationAudioUrl(url)) {
          return;
        }

        seenTranslationDownloads.add(url);
        window.setTimeout(async () => {
          const handedOff = await notifyTranslationReady(url, source);
          if (!handedOff) {
            originalConsoleLog("[PVL] Translation URL was detected but could not be handed off to the app; browser auto-open fallback is disabled", source || "unknown", url);
          }
        }, 50);
      }

      function inspectConsoleArgs(args) {
        if (extractTranslationStarted(args)) {
          void notifyTranslationStarted("console");
        }

        const failure = extractTranslationFailure(args);
        if (failure) {
          void notifyTranslationFailed(failure.errorCode, failure.errorMessage, "console");
        }

        const url = extractTranslationUrl(args);
        if (url) {
          handleTranslationUrl(url, "console");
        }
      }

      console.log = (...args) => {
        inspectConsoleArgs(args);
        return originalConsoleLog(...args);
      };

      if (originalConsoleDebug) {
        console.debug = (...args) => {
          inspectConsoleArgs(args);
          return originalConsoleDebug(...args);
        };
      }

      if (originalConsoleInfo) {
        console.info = (...args) => {
          inspectConsoleArgs(args);
          return originalConsoleInfo(...args);
        };
      }

      if (originalFetch) {
        window.fetch = async (...args) => {
          const candidateUrl = typeof args[0] === "string" ? args[0] : args[0] && args[0].url;
          const isStartRequest = isTranslationStartRequestUrl(candidateUrl);
          if (isStartRequest) {
            void notifyTranslationStarted("fetch-request");
          }
          if (isTranslationAudioUrl(candidateUrl)) {
            handleTranslationUrl(candidateUrl, "fetch-request");
          }
          try {
            const response = await originalFetch(...args);
            try {
              const finalUrl = response && response.url;
              if (isTranslationAudioUrl(finalUrl)) {
                handleTranslationUrl(finalUrl, "fetch-response");
              }

              if (isStartRequest && response && !response.ok) {
                void notifyTranslationFailed(
                  "translation-request-failed",
                  "Translation request failed with status " + response.status + ".",
                  "fetch-response");
              }
            } catch {}
            return response;
          } catch (err) {
            if (isStartRequest) {
              const message = err && err.message
                ? "Translation request failed: " + err.message
                : "Translation request failed due to a network error.";
              void notifyTranslationFailed("translation-request-error", message, "fetch-error");
            }
            throw err;
          }
        };
      }

      if (originalOpen && originalSend && OriginalXMLHttpRequest) {
        OriginalXMLHttpRequest.prototype.open = function(method, url) {
          this.__pvlUrl = url;
          if (isTranslationStartRequestUrl(url)) {
            void notifyTranslationStarted("xhr-open");
          }
          if (isTranslationAudioUrl(url)) {
            handleTranslationUrl(url, "xhr-open");
          }
          return originalOpen.apply(this, arguments);
        };

        OriginalXMLHttpRequest.prototype.send = function() {
          this.addEventListener("readystatechange", function() {
            if (this.readyState === 2 || this.readyState === 4) {
              const responseUrl = this.responseURL || this.__pvlUrl;
              if (isTranslationAudioUrl(responseUrl)) {
                handleTranslationUrl(responseUrl, "xhr-response");
              }
              if (this.readyState === 4 && isTranslationStartRequestUrl(responseUrl) && this.status >= 400) {
                void notifyTranslationFailed(
                  "translation-request-failed",
                  "Translation request failed with status " + this.status + ".",
                  "xhr-response");
              }
            }
          });
          this.addEventListener("error", function() {
            const responseUrl = this.responseURL || this.__pvlUrl;
            if (isTranslationStartRequestUrl(responseUrl)) {
              void notifyTranslationFailed(
                "translation-request-error",
                "Translation request failed due to a network error.",
                "xhr-error");
            }
          });
          return originalSend.apply(this, arguments);
        };
      }

      if (mediaSrcDescriptor && mediaSrcDescriptor.set) {
        Object.defineProperty(mediaProto, "src", {
          configurable: true,
          enumerable: mediaSrcDescriptor.enumerable,
          get: mediaSrcDescriptor.get,
          set(value) {
            if (isTranslationAudioUrl(value)) {
              handleTranslationUrl(value, "media-src");
            }
            return mediaSrcDescriptor.set.call(this, value);
          }
        });
      }

      Element.prototype.setAttribute = function(name, value) {
        if (String(name).toLowerCase() === "src" && isTranslationAudioUrl(value)) {
          handleTranslationUrl(value, "setAttribute-src");
        }
        return originalSetAttribute.call(this, name, value);
      };

      window.addEventListener("beforeunload", () => {
        try {
          if (OriginalXMLHttpRequest && originalOpen) {
            OriginalXMLHttpRequest.prototype.open = originalOpen;
          }
        } catch {}

        try {
          if (OriginalXMLHttpRequest && originalSend) {
            OriginalXMLHttpRequest.prototype.send = originalSend;
          }
        } catch {}

        try {
          if (originalSetAttribute) {
            Element.prototype.setAttribute = originalSetAttribute;
          }
        } catch {}

        try {
          if (originalFetch) {
            window.fetch = originalFetch;
          }
        } catch {}

        try {
          if (mediaProto && mediaSrcDescriptor && mediaSrcDescriptor.set) {
            Object.defineProperty(mediaProto, "src", mediaSrcDescriptor);
          }
        } catch {}

        try {
          if (originalConsoleLog) {
            console.log = originalConsoleLog;
          }
        } catch {}

        try {
          if (originalConsoleDebug && console.debug) {
            console.debug = originalConsoleDebug;
          }
        } catch {}

        try {
          if (originalConsoleInfo && console.info) {
            console.info = originalConsoleInfo;
          }
        } catch {}
      });
    })();

    const params = new URLSearchParams(window.location.search);
    const rawSrc = params.get("src");
    const playlistUrl = params.get("list") || "/playlist.json";
    const L = __PVL_LABELS__;

    const video = document.getElementById("video");
    const overlay = document.getElementById("overlay");
    const overlayTitle = document.getElementById("overlayTitle");
    const overlayText = document.getElementById("overlayText");
    const videoTitle = document.getElementById("videoTitle");
    const videoSubtitle = document.getElementById("videoSubtitle");
    const playlistEl = document.getElementById("playlist");
    const prevBtn = document.getElementById("prevBtn");
    const nextBtn = document.getElementById("nextBtn");
    const autoplayNext = document.getElementById("autoplayNext");
    const metaInfo = document.getElementById("metaInfo");
    const playlistBadge = document.getElementById("playlistBadge");
    const queueSubtitle = document.getElementById("queueSubtitle");
    const upNextTitle = document.getElementById("upNextTitle");
    const upNextMeta = document.getElementById("upNextMeta");
    const playerShell = document.querySelector(".player-shell");
    const videoWrap = document.querySelector(".video-wrap");

    let playlist = [];
    let currentIndex = -1;

    function showOverlay(title, text, isError = false) {
      overlay.classList.add("visible");
      overlayTitle.textContent = title;
      overlayText.textContent = text;
      overlayText.className = isError ? "overlay-text error" : "overlay-text";
    }

    function hideOverlay() {
      overlay.classList.remove("visible");
    }

    function decodePath(v) {
      try { return decodeURIComponent(v); } catch { return v; }
    }

    function getCleanPath(path) {
      return String(path || "").split("?")[0].split("#")[0];
    }

    function getFileName(path) {
      const clean = getCleanPath(path);
      const parts = clean.split("/");
      return parts[parts.length - 1] || "video.mp4";
    }

    function isMp4File(path) {
      return /\.mp4(?:$|[?#])/i.test(String(path || ""));
    }

    function normalizeItem(item, index) {
      if (typeof item === "string") {
        return {
          title: getFileName(item),
          src: item,
          playbackSrc: item,
          votSrc: "",
          index
        };
      }

      return {
        title: item.title || getFileName(item.src || ""),
        src: item.src || "",
        playbackSrc: item.playbackSrc || item.src || "",
        votSrc: item.votSrc || "",
        index
      };
    }

    function setVotSource(src) {
      const normalized = decodePath(src || "");
      if (normalized) {
        video.dataset.votSrc = normalized;
        window.__PVL_VOT_SRC = normalized;
      } else {
        delete video.dataset.votSrc;
        delete window.__PVL_VOT_SRC;
      }
    }

    function samePath(a, b) {
      const left = getCleanPath(decodePath(a)).toLowerCase();
      const right = getCleanPath(decodePath(b)).toLowerCase();
      return left === right;
    }

    function getVideoAspect() {
      if (video.videoWidth > 0 && video.videoHeight > 0) {
        return video.videoWidth / video.videoHeight;
      }
      return 16 / 9;
    }

    function fitPlayer() {
      if (!playerShell || !videoWrap) return;

      if (window.innerWidth <= 1100) {
        videoWrap.style.width = "100%";
        videoWrap.style.height = "auto";
        videoWrap.style.aspectRatio = "16 / 9";
        return;
      }

      const shellRect = playerShell.getBoundingClientRect();
      const shellStyle = getComputedStyle(playerShell);

      const padX =
        parseFloat(shellStyle.paddingLeft || "0") +
        parseFloat(shellStyle.paddingRight || "0");

      const padY =
        parseFloat(shellStyle.paddingTop || "0") +
        parseFloat(shellStyle.paddingBottom || "0");

      const availableWidth = Math.max(0, shellRect.width - padX);
      const availableHeight = Math.max(0, shellRect.height - padY);

      if (!availableWidth || !availableHeight) return;

      const aspect = getVideoAspect();
      let width = availableWidth;
      let height = width / aspect;

      if (height > availableHeight) {
        height = availableHeight;
        width = height * aspect;
      }

      videoWrap.style.aspectRatio = "auto";
      videoWrap.style.width = Math.floor(width) + "px";
      videoWrap.style.height = Math.floor(height) + "px";
    }

    async function navigateToIndex(index, autoPlay = false) {
      if (index < 0 || index >= playlist.length) return;

      currentIndex = index;
      const item = playlist[index];

      const nextUrl = new URL(window.location.href);
      nextUrl.searchParams.set("src", item.src);
      nextUrl.searchParams.set("list", playlistUrl);
      history.replaceState(null, "", nextUrl.toString());

      renderPlaylist();
      setVideoMeta(item.playbackSrc || item.src);
      setVotSource(item.votSrc);
      updateNavButtons();
      updateUpNext();

      await loadVideoSource(item.src, autoPlay);
    }

    function updateNavButtons() {
      prevBtn.disabled = currentIndex <= 0;
      nextBtn.disabled = currentIndex < 0 || currentIndex >= playlist.length - 1;
    }

    function updateUpNext() {
      if (currentIndex >= 0 && currentIndex < playlist.length - 1) {
        const nextItem = playlist[currentIndex + 1];
        upNextTitle.textContent = nextItem.title;
        upNextMeta.textContent = nextItem.playbackSrc || nextItem.src;
      } else {
        upNextTitle.textContent = L.endOfPlaylist;
        upNextMeta.textContent = L.noNextVideo;
      }
    }

    function renderPlaylist() {
      playlistEl.innerHTML = "";

      if (!playlist.length) {
        playlistEl.innerHTML = '<div class="empty"></div>';
        playlistEl.querySelector(".empty").textContent = L.playlistUnavailable;
        queueSubtitle.textContent = L.singleVideoMode;
        playlistBadge.textContent = L.playlist + ": 0 " + L.items;
        updateNavButtons();
        updateUpNext();
        return;
      }

      queueSubtitle.textContent = playlist.length + " " + L.videos;
      playlistBadge.textContent = L.playlist + ": " + playlist.length + " " + L.items;

      playlist.forEach((item, index) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "playlist-item" + (index === currentIndex ? " active" : "");
        button.innerHTML = `
          <div class="playlist-top">
            <div class="playlist-index">#${String(index + 1).padStart(2, "0")}</div>
            ${index === currentIndex ? '<div class="playlist-state"></div>' : ""}
          </div>
          <div class="playlist-name"></div>
          <div class="playlist-src"></div>
        `;
        const state = button.querySelector(".playlist-state");
        if (state) state.textContent = L.nowPlaying;
        button.querySelector(".playlist-name").textContent = item.title;
        button.querySelector(".playlist-src").textContent = item.playbackSrc || item.src;
        button.addEventListener("click", () => navigateToIndex(index, true));
        playlistEl.appendChild(button);
      });

      updateNavButtons();
      updateUpNext();
    }

    function setVideoMeta(src) {
      const fileName = getFileName(src);
      videoTitle.textContent = fileName;
      videoSubtitle.textContent = src;

      if (currentIndex >= 0 && playlist[currentIndex]) {
        metaInfo.textContent = L.playing + " " + (currentIndex + 1) + " " + L.of + " " + playlist.length + " | " + playlist[currentIndex].title;
      } else {
        metaInfo.textContent = L.singleVideoMode + " • " + fileName;
      }
    }

    async function loadVideoSource(src, autoPlay = false) {
      const decodedSrc = decodePath(src);

      if (!decodedSrc) {
        showOverlay(L.noVideoSource, L.addSource, true);
        return;
      }

      if (!isMp4File(decodedSrc)) {
        showOverlay(L.unsupportedSource, L.expectedMp4, true);
        return;
      }

      setVideoMeta(decodedSrc);
      showOverlay(L.loadingVideo, L.fetchingMp4);

      video.pause();
      video.preload = "auto";
      video.src = decodedSrc;
      video.load();

      if (autoPlay) {
        try {
          await video.play();
          hideOverlay();
        } catch {
          showOverlay(
            L.readyToPlay,
            L.autoplayBlocked,
            false
          );
        }
      }
    }

    function attachVideoEvents() {
      video.addEventListener("loadedmetadata", () => {
        const duration = Math.round(video.duration || 0);
        const prefix = currentIndex >= 0
          ? (L.playing + " " + (currentIndex + 1) + " " + L.of + " " + playlist.length + " | ")
          : "";
        videoSubtitle.textContent =
          prefix + getFileName(video.currentSrc || video.src) + " | " + duration + " " + L.sec;
        fitPlayer();
      });

      video.addEventListener("canplay", () => {
        hideOverlay();
        fitPlayer();
      });

      video.addEventListener("waiting", () => {
        showOverlay(L.buffering, L.pleaseWait);
      });

      video.addEventListener("playing", () => {
        hideOverlay();
      });

      video.addEventListener("ended", async () => {
        if (!autoplayNext.checked) return;
        if (currentIndex >= 0 && currentIndex < playlist.length - 1) {
          await navigateToIndex(currentIndex + 1, true);
        }
      });

      video.addEventListener("error", () => {
        const mediaError = video.error;
        let message = L.fileCouldNotLoad;

        if (mediaError) {
          switch (mediaError.code) {
            case MediaError.MEDIA_ERR_ABORTED:
              message = L.playbackAborted;
              break;
            case MediaError.MEDIA_ERR_NETWORK:
              message = L.networkError;
              break;
            case MediaError.MEDIA_ERR_DECODE:
              message = L.decodeError;
              break;
            case MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED:
              message = L.sourceNotSupported;
              break;
          }
        }

        showOverlay(L.playbackError, message, true);
      });
    }

    async function tryLoadPlaylist() {
      try {
        const res = await fetch(playlistUrl, { cache: "no-store" });
        if (!res.ok) throw new Error("HTTP " + res.status);

        const data = await res.json();
        if (!Array.isArray(data)) throw new Error("playlist must be an array");

        playlist = data
          .map(normalizeItem)
          .filter(x => x.src && isMp4File(x.src));

        if (!playlist.length) {
          currentIndex = -1;
          renderPlaylist();
          return;
        }

        const decodedSrc = decodePath(rawSrc || "");
        currentIndex = playlist.findIndex(x => samePath(x.src, decodedSrc));

        if (currentIndex < 0 && decodedSrc && isMp4File(decodedSrc)) {
          playlist.unshift(normalizeItem({ src: decodedSrc, playbackSrc: decodedSrc, votSrc: "", title: getFileName(decodedSrc) }, 0));
          currentIndex = 0;
        }

        renderPlaylist();
      } catch {
        playlist = [];
        currentIndex = -1;
        renderPlaylist();
      }
    }

    prevBtn.addEventListener("click", async () => {
      if (currentIndex > 0) {
        await navigateToIndex(currentIndex - 1, false);
      }
    });

    nextBtn.addEventListener("click", async () => {
      if (currentIndex >= 0 && currentIndex < playlist.length - 1) {
        await navigateToIndex(currentIndex + 1, true);
      }
    });

    document.addEventListener("keydown", async (e) => {
      if (e.target && ["INPUT", "TEXTAREA"].includes(e.target.tagName)) return;

      if (e.key === "ArrowRight" && !nextBtn.disabled) {
        await navigateToIndex(currentIndex + 1, true);
      }

      if (e.key === "ArrowLeft" && !prevBtn.disabled) {
        await navigateToIndex(currentIndex - 1, false);
      }
    });

    window.addEventListener("resize", fitPlayer);

    if (typeof ResizeObserver !== "undefined" && playerShell) {
      const ro = new ResizeObserver(() => fitPlayer());
      ro.observe(playerShell);
    }

    (async function init() {
      attachVideoEvents();
      await tryLoadPlaylist();

      if (!rawSrc) {
        if (playlist.length > 0) {
          currentIndex = 0;
          renderPlaylist();
          updateNavButtons();
          updateUpNext();

          const firstItem = playlist[0];
          const nextUrl = new URL(window.location.href);
          nextUrl.searchParams.set("src", firstItem.src);
          nextUrl.searchParams.set("list", playlistUrl);
          history.replaceState(null, "", nextUrl.toString());

          setVotSource(firstItem.votSrc);
          await loadVideoSource(firstItem.src, false);
          return;
        }

        showOverlay(L.noVideosFound, L.noSupportedVideos, true);
        renderPlaylist();
        return;
      }

      if (currentIndex >= 0 && playlist[currentIndex]) {
        setVotSource(playlist[currentIndex].votSrc);
        await loadVideoSource(playlist[currentIndex].src, false);
      } else {
        setVotSource("");
        await loadVideoSource(rawSrc, false);
      }
    })();
  </script>
</body>
</html>
""";
}
