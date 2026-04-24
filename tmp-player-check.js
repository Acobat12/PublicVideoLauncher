    (function installTranslationAutoDownloadHook() {
      const seenTranslationDownloads = new Set();
      const translationCallbackUrl = "/translation-ready" || "/translation-ready";
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

      function buildPayload(url) {
        const currentVideo = document.getElementById("video");
        const preferredSrc =
          window.__PVL_VOT_SRC ||
          (currentVideo && currentVideo.dataset ? currentVideo.dataset.votSrc : "") ||
          (currentVideo && (currentVideo.currentSrc || currentVideo.getAttribute("src"))) ||
          "";
        return {
          url,
          videoSrc: preferredSrc,
          videoTitle: getCurrentVideoName(),
          suggestedFileName: sanitizeFileName(getCurrentVideoName()) + ".translated.mp3"
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

      async function notifyApp(url, source) {
        if (!translationCallbackUrl || !isTranslationAudioUrl(url)) {
          return false;
        }

        try {
          const payload = buildPayload(url);
          const response = await postJson(translationCallbackUrl, payload);
          if (!response.ok) {
            throw new Error("callback status " + response.status);
          }

          originalConsoleLog("[PVL] Translation URL forwarded to app", source || "unknown", payload.videoTitle, url);
          return true;
        } catch (err) {
          originalConsoleLog("[PVL] Direct app handoff failed", source || "unknown", err);
          return false;
        }
      }

      function handleTranslationUrl(url, source) {
        if (!url || seenTranslationDownloads.has(url) || !isTranslationAudioUrl(url)) {
          return;
        }

        seenTranslationDownloads.add(url);
        window.setTimeout(async () => {
          const handedOff = await notifyApp(url, source);
          if (!handedOff) {
            originalConsoleLog("[PVL] Translation URL was detected but could not be handed off to the app; browser auto-open fallback is disabled", source || "unknown", url);
          }
        }, 50);
      }

      function inspectConsoleArgs(args) {
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
          if (isTranslationAudioUrl(candidateUrl)) {
            handleTranslationUrl(candidateUrl, "fetch-request");
          }
          const response = await originalFetch(...args);
          try {
            const finalUrl = response && response.url;
            if (isTranslationAudioUrl(finalUrl)) {
              handleTranslationUrl(finalUrl, "fetch-response");
            }
          } catch {}
          return response;
        };
      }

      if (originalOpen && originalSend && OriginalXMLHttpRequest) {
        OriginalXMLHttpRequest.prototype.open = function(method, url) {
          this.__pvlUrl = url;
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
        upNextTitle.textContent = "End of playlist";
        upNextMeta.textContent = "No next video available.";
      }
    }

    function renderPlaylist() {
      playlistEl.innerHTML = "";

      if (!playlist.length) {
        playlistEl.innerHTML = '<div class="empty">playlist.json is empty or unavailable.</div>';
        queueSubtitle.textContent = "Single video mode";
        playlistBadge.textContent = "Playlist: 0 items";
        updateNavButtons();
        updateUpNext();
        return;
      }

      queueSubtitle.textContent = playlist.length + " video(s)";
      playlistBadge.textContent = "Playlist: " + playlist.length + " items";

      playlist.forEach((item, index) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "playlist-item" + (index === currentIndex ? " active" : "");
        button.innerHTML = `
          <div class="playlist-top">
            <div class="playlist-index">#${String(index + 1).padStart(2, "0")}</div>
            ${index === currentIndex ? '<div class="playlist-state">Now playing</div>' : ""}
          </div>
          <div class="playlist-name"></div>
          <div class="playlist-src"></div>
        `;
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
        metaInfo.textContent = "Playing " + (currentIndex + 1) + " of " + playlist.length + " | " + playlist[currentIndex].title;
      } else {
        metaInfo.textContent = "Single video mode • " + fileName;
      }
    }

    async function loadVideoSource(src, autoPlay = false) {
      const decodedSrc = decodePath(src);

      if (!decodedSrc) {
        showOverlay("No video source", "Add a source via ?src=/path/to/video.mp4", true);
        return;
      }

      if (!isMp4File(decodedSrc)) {
        showOverlay("Unsupported source", "Expected a direct .mp4 file", true);
        return;
      }

      setVideoMeta(decodedSrc);
      showOverlay("Loading video", "Fetching MP4 file...");

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
            "Ready to play",
            "Autoplay was blocked. Press Play to continue.",
            false
          );
        }
      }
    }

    function attachVideoEvents() {
      video.addEventListener("loadedmetadata", () => {
        const duration = Math.round(video.duration || 0);
        const prefix = currentIndex >= 0
          ? ("Playing " + (currentIndex + 1) + " of " + playlist.length + " | ")
          : "";
        videoSubtitle.textContent =
          prefix + getFileName(video.currentSrc || video.src) + " | " + duration + " sec";
        fitPlayer();
      });

      video.addEventListener("canplay", () => {
        hideOverlay();
        fitPlayer();
      });

      video.addEventListener("waiting", () => {
        showOverlay("Buffering", "Please wait...");
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
        let message = "The file could not be loaded.";

        if (mediaError) {
          switch (mediaError.code) {
            case MediaError.MEDIA_ERR_ABORTED:
              message = "Playback was aborted.";
              break;
            case MediaError.MEDIA_ERR_NETWORK:
              message = "A network error occurred while loading the video.";
              break;
            case MediaError.MEDIA_ERR_DECODE:
              message = "The video file is corrupted or cannot be decoded.";
              break;
            case MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED:
              message = "This video source is not supported.";
              break;
          }
        }

        showOverlay("Playback error", message, true);
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

        showOverlay("No videos found", "playlist.json is empty or no supported videos were found.", true);
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
