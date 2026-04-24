using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PublicVideoLauncher.Services;

public enum AppLanguage
{
    English,
    Russian
}

public sealed record LanguageOption(AppLanguage Language, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class UiLocalizer
{
    private static readonly Dictionary<string, string> EnglishToRussian = new(StringComparer.Ordinal)
    {
        ["Public Video Launcher"] = "Public Video Launcher",
        ["Folder"] = "Папка",
        ["Browse..."] = "Выбрать...",
        ["Browse"] = "Обзор",
        ["Choose a folder with videos..."] = "Выберите папку с видео...",
        ["Port"] = "Порт",
        ["Tunnel"] = "Туннель",
        ["Convert to MP4"] = "Конвертация в MP4",
        ["Proxy MP4 for translation"] = "Конвертация в MP4",
        ["Open browser after start"] = "Открывать браузер после старта",
        ["Scan"] = "Сканировать",
        ["Scan Folder"] = "Сканировать папку",
        ["Public URL"] = "Публичная ссылка",
        ["Public URL will appear here after start..."] = "Публичная ссылка появится после старта...",
        ["Tunnel auth"] = "Ключ туннеля",
        ["zrok: account token. xTunnel: license key. Optional for Cloudflare / LocalTunnel / VK."] =
            "zrok: token аккаунта. xTunnel: license key. Для Cloudflare / LocalTunnel / VK необязательно.",
        ["CLI path"] = "Путь CLI",
        ["Optional path to zrok / lt / xTunnel.exe / vk-tunnel.cmd / npx"] =
            "Необязательный путь к zrok / lt / xTunnel.exe / vk-tunnel.cmd / npx",
        ["Setup..."] = "Настройка...",
        ["Select video folder"] = "Выберите папку с видео",
        ["Use Setup... to configure tunnel name, hostname and Cloudflare login."] =
            "Используйте Настройка..., чтобы задать имя туннеля, hostname и вход Cloudflare.",
        ["Optional custom path to cloudflared.exe"] = "Необязательный путь к cloudflared.exe",
        ["Paste your zrok account token here for first-time enable. Leave empty if this machine is already enabled."] =
            "Вставьте token аккаунта zrok для первого включения. Оставьте пустым, если эта машина уже включена.",
        ["Optional custom path to zrok.exe / zrok"] = "Необязательный путь к zrok.exe / zrok",
        ["Paste your ngrok authtoken here for first-time setup. Leave empty if ngrok is already configured."] =
            "Вставьте authtoken ngrok для первой настройки. Оставьте пустым, если ngrok уже настроен.",
        ["Optional custom path to ngrok.exe / ngrok"] = "Необязательный путь к ngrok.exe / ngrok",
        ["localhost.run uses a free SSH tunnel via nokey@localhost.run. Tunnel auth is not used here."] =
            "localhost.run использует бесплатный SSH-туннель через nokey@localhost.run. Ключ туннеля здесь не используется.",
        ["Optional custom path to ssh.exe / ssh"] = "Необязательный путь к ssh.exe / ssh",
        ["Tailscale Funnel uses your existing Tailscale login on this machine. Tunnel auth is not used here."] =
            "Tailscale Funnel использует текущий вход Tailscale на этой машине. Ключ туннеля здесь не используется.",
        ["Optional custom path to tailscale.exe / tailscale"] = "Необязательный путь к tailscale.exe / tailscale",
        ["Usually not needed for LocalTunnel. Some visitors may still see a password prompt on the public page."] =
            "Обычно не нужно для LocalTunnel. Некоторые посетители всё равно могут увидеть запрос пароля на публичной странице.",
        ["Optional path to lt / localtunnel / npx"] = "Необязательный путь к lt / localtunnel / npx",
        ["Paste your xTunnel key here. The app will activate xTunnel automatically if needed."] =
            "Вставьте ключ xTunnel. Программа активирует xTunnel автоматически, если нужно.",
        ["Optional custom path to xTunnel.exe"] = "Необязательный путь к xTunnel.exe",
        ["Usually not needed for VK Tunnel."] = "Обычно не нужно для VK Tunnel.",
        ["Optional path to vk-tunnel.cmd / vk-tunnel.js. If empty, the app will try npx."] =
            "Необязательный путь к vk-tunnel.cmd / vk-tunnel.js. Если пусто, программа попробует npx.",
        ["zrok: account token for enable. xTunnel: license key. Optional for Cloudflare / LocalTunnel / VK."] =
            "zrok: token аккаунта для enable. xTunnel: license key. Для Cloudflare / LocalTunnel / VK необязательно.",
        ["Cloudflare Name Setup"] = "Настройка Cloudflare Name",
        ["Example: trading-course-tunnel"] = "Пример: trading-course-tunnel",
        ["Example: video.example.com"] = "Пример: video.example.com",
        ["Cloudflare login detected on this machine."] = "Вход Cloudflare найден на этой машине.",
        ["Cloudflare login is not configured yet. Use Login to Cloudflare."] =
            "Вход Cloudflare ещё не настроен. Используйте Login to Cloudflare.",
        ["Login to Cloudflare"] = "Войти в Cloudflare",
        ["Save"] = "Сохранить",
        ["Cancel"] = "Отмена",
        ["OK"] = "OK",
        ["Starting Cloudflare login. Finish it in the browser window opened by cloudflared."] =
            "Запускаю вход Cloudflare. Завершите его в окне браузера, открытом cloudflared.",
        ["Cloudflare login completed. You can now save and use Start."] =
            "Вход Cloudflare завершён. Теперь можно сохранить и нажать Start.",
        ["Cloudflare login failed:"] = "Вход Cloudflare не удался:",
        ["Fill in a tunnel name and a valid public hostname."] =
            "Заполните имя туннеля и корректный публичный hostname.",
        ["Cloudflare Name settings saved."] = "Настройки Cloudflare Name сохранены.",
        ["Tunnel name"] = "Имя туннеля",
        ["Public hostname"] = "Публичный hostname",
        ["This provider uses a named public Cloudflare tunnel. The hostname must belong to a domain already added to your Cloudflare account."] =
            "Этот провайдер использует именованный публичный туннель Cloudflare. Hostname должен принадлежать домену, уже добавленному в ваш аккаунт Cloudflare.",
        ["Folder required"] = "Нужна папка",
        ["Select a valid folder first."] = "Сначала выберите корректную папку.",
        ["Title"] = "Название",
        ["Ext"] = "Тип",
        ["Playback"] = "Просмотр",
        ["Translation"] = "Перевод",
        ["Cache"] = "Кэш",
        ["Path"] = "Путь",
        ["Log"] = "Лог",
        ["Idle"] = "Ожидание",
        ["Ready."] = "Готово.",
        ["Auto merge translated audio and save output"] = "Автоматически наложить перевод и сохранить видео",
        ["Start"] = "Старт",
        ["Stop"] = "Стоп",
        ["Copy URL"] = "Копировать URL",
        ["Copy"] = "Копировать",
        ["Open"] = "Открыть",
        ["Translation Studio"] = "Студия перевода",
        ["Translation"] = "Перевод",
        ["Dependencies"] = "Зависимости",
        ["Tools"] = "Инструменты",
        ["Fix DNS"] = "Исправить DNS",
        ["DNS"] = "DNS",
        ["Working..."] = "Выполняется...",
        ["Scanning videos..."] = "Сканирование видео...",
        ["Starting services..."] = "Запуск сервисов...",
        ["Auto merge in progress..."] = "Автоматическая сборка перевода...",
        ["Running"] = "Работает",
        ["Canceled"] = "Отменено",
        ["Stopped"] = "Остановлено",
        ["Stop warning"] = "Предупреждение остановки",
        ["Setup required"] = "Нужна настройка",
        ["Scan failed"] = "Ошибка сканирования",
        ["Start failed"] = "Ошибка запуска",
        ["Open failed"] = "Ошибка открытия",
        ["Browse failed"] = "Ошибка выбора папки",
        ["Clipboard"] = "Буфер обмена",
        ["Clipboard is not available."] = "Буфер обмена недоступен.",
        ["Public URL copied."] = "Публичная ссылка скопирована.",
        ["Folder selected."] = "Папка выбрана.",
        ["Auto merge"] = "Автоналожение",
        ["Auto merge is enabled."] = "Автоматическое наложение включено.",
        ["Auto merge is disabled."] = "Автоматическое наложение выключено.",
        ["No supported videos found."] = "Поддерживаемые видео не найдены.",
        ["Found {0} video(s)."] = "Найдено видео: {0}.",
        ["Nothing to start"] = "Нечего запускать",
        ["No supported videos were found in the selected folder."] =
            "В выбранной папке не найдено поддерживаемых видео.",
        ["Cloudflare Name needs setup before start."] =
            "Перед запуском нужно настроить Cloudflare Name.",
        ["Cloudflare Name setup is incomplete."] =
            "Настройка Cloudflare Name не завершена.",
        ["Program stopped. Check helper tools in the Dependencies window."] =
            "Программа остановлена. Проверьте инструменты в окне Dependencies.",
        ["Operation canceled."] = "Операция отменена.",
        ["Stopped."] = "Остановлено.",
        ["Opening public URL..."] = "Открываю публичную ссылку...",
        ["Running DNS diagnostics..."] = "Проверяю DNS...",
        ["DNS diagnostics"] = "Диагностика DNS",
        ["DNS diagnostics finished."] = "Диагностика DNS завершена.",
        ["Start a tunnel first. Translation Studio is available only while an active public tunnel is running."] =
            "Сначала запустите туннель. Translation Studio доступна только при активном публичном туннеле.",
        ["Start a public tunnel first. Translation Studio uses its own page only while the tunnel is active."] =
            "Сначала запустите публичный туннель. Translation Studio работает со своей страницей только при активном туннеле.",
        ["Public tunnel is not running. Start the tunnel first."] =
            "Публичный туннель не запущен. Сначала запустите туннель.",
        ["Translation Studio job was not found."] =
            "Задание Translation Studio не найдено.",
        ["Translation Studio job does not contain any segments."] =
            "Задание Translation Studio не содержит сегментов.",
        ["Translated audio detected. Building output file..."] =
            "Переведённый звук найден. Собираю итоговое видео...",
        ["Translated audio was saved to translated_audio. Auto merge is off."] =
            "Переведённый звук сохранён в translated_audio. Автоналожение выключено.",
        ["Translated audio was merged successfully. Output saved to merged_output."] =
            "Переведённый звук успешно наложен. Итоговое видео сохранено в merged_output.",
        ["Helper tools"] = "Вспомогательные инструменты",
        ["The program needs ffmpeg, caddy, and one tunnel backend. cloudflared can be installed automatically here; ngrok, OpenSSH, Tailscale, zrok, LocalTunnel, xTunnel, and VK Tunnel are detected if already installed."] =
            "Программе нужны ffmpeg, caddy и один туннельный провайдер. cloudflared можно установить автоматически; ngrok, OpenSSH, Tailscale, zrok, LocalTunnel, xTunnel и VK Tunnel будут найдены, если уже установлены.",
        ["Refresh"] = "Обновить",
        ["Install all missing automatically"] = "Установить недостающие автоматически",
        ["Install automatically"] = "Установить автоматически",
        ["Manual install only"] = "Только ручная установка",
        ["Missing"] = "Отсутствует",
        ["Installed"] = "Установлен",
        ["Waiting"] = "Ожидание",
        ["Not detected"] = "Не найдено",
        ["Project folder:"] = "Папка проекта:",
        ["Last error will appear here if a tool failed to install or start..."] =
            "Здесь появится последняя ошибка, если инструмент не установился или не запустился...",
        ["Installing all missing tools..."] = "Установка всех недостающих инструментов...",
        ["Installing {0}..."] = "Установка {0}...",
        ["{0} is already installed."] = "{0} уже установлен.",
        ["{0} must be installed manually on this platform. Open the download page and the window will detect it automatically."] =
            "{0} нужно установить вручную для этой платформы. Откройте страницу загрузки, после установки окно найдёт инструмент автоматически.",
        ["No supported automatic installs are needed."] = "Автоматическая установка не требуется.",
        ["Installation finished. Refreshing tool status..."] = "Установка завершена. Обновляю статус инструментов...",
        ["Installation canceled."] = "Установка отменена.",
        ["Installation stopped. Check the error and install the missing tool manually if needed."] =
            "Установка остановлена. Проверьте ошибку и при необходимости установите инструмент вручную.",
        ["All helper tools are installed. The program can be started again."] =
            "Все вспомогательные инструменты установлены. Программу можно запускать снова.",
        ["Missing tools detected: {0}. Install the tools required for the tunnel provider you plan to use."] =
            "Найдены отсутствующие инструменты: {0}. Установите инструменты, нужные для выбранного туннеля.",
        ["Automatic install is supported by the app for this tool on the current OS."] =
            "Для этого инструмента поддерживается автоматическая установка на текущей ОС.",
        ["Automatic install is not available here. Install it manually and this window will detect it."] =
            "Автоматическая установка здесь недоступна. Установите вручную, окно найдёт инструмент автоматически.",
        ["Installed and ready."] = "Установлен и готов.",
        ["Waiting for install."] = "Ожидает установки.",
        ["Manual install required on this platform."] = "На этой платформе нужна ручная установка.",
        ["Open FFmpeg page"] = "Открыть страницу FFmpeg",
        ["Open Caddy page"] = "Открыть страницу Caddy",
        ["Open Cloudflared page"] = "Открыть страницу Cloudflared",
        ["Open ngrok page"] = "Открыть страницу ngrok",
        ["Open localhost.run page"] = "Открыть страницу localhost.run",
        ["Open Tailscale page"] = "Открыть страницу Tailscale",
        ["Open zrok page"] = "Открыть страницу zrok",
        ["Open LocalTunnel page"] = "Открыть страницу LocalTunnel",
        ["Open xTunnel page"] = "Открыть страницу xTunnel",
        ["Open VK Tunnel page"] = "Открыть страницу VK Tunnel",
        ["Local Video Player"] = "Локальный видеоплеер",
        ["Waiting for source..."] = "Ожидание источника...",
        ["Playlist: --"] = "Playlist: --",
        ["STATIC MP4 DELIVERY"] = "СТАТИЧЕСКАЯ MP4 ОТДАЧА",
        ["Loading video"] = "Загрузка видео",
        ["Preparing the player..."] = "Подготовка плеера...",
        ["Previous"] = "Назад",
        ["Next"] = "Далее",
        ["Auto play next"] = "Автоматически включать следующее",
        ["No playlist loaded"] = "Playlist не загружен",
        ["Queue"] = "Очередь",
        ["Waiting for playlist..."] = "Ожидание playlist...",
        ["Up next"] = "Следующее",
        ["Nothing queued"] = "В очереди пусто",
        ["End of playlist"] = "Конец playlist",
        ["No next video available."] = "Следующего видео нет.",
        ["playlist.json is empty or unavailable."] = "playlist.json пустой или недоступен.",
        ["Single video mode"] = "Режим одного видео",
        ["Playlist"] = "Playlist",
        ["items"] = "элем.",
        ["video(s)"] = "видео",
        ["Now playing"] = "Сейчас играет",
        ["Playing"] = "Воспроизведение",
        ["of"] = "из",
        ["No video source"] = "Нет источника видео",
        ["Add a source via ?src=/path/to/video.mp4"] =
            "Добавьте источник через ?src=/path/to/video.mp4",
        ["Unsupported source"] = "Источник не поддерживается",
        ["Expected a direct .mp4 file"] = "Ожидалась прямая ссылка на .mp4",
        ["Fetching MP4 file..."] = "Загрузка MP4 файла...",
        ["Ready to play"] = "Готово к просмотру",
        ["Autoplay was blocked. Press Play to continue."] =
            "Автозапуск заблокирован. Нажмите Play для продолжения.",
        ["sec"] = "сек",
        ["Buffering"] = "Буферизация",
        ["Playback error"] = "Ошибка воспроизведения",
        ["The file could not be loaded."] = "Файл не удалось загрузить.",
        ["Playback was aborted."] = "Воспроизведение было прервано.",
        ["A network error occurred while loading the video."] =
            "При загрузке видео произошла сетевая ошибка.",
        ["The video file is corrupted or cannot be decoded."] =
            "Видеофайл повреждён или не может быть декодирован.",
        ["This video source is not supported."] = "Этот источник видео не поддерживается.",
        ["No videos found"] = "Видео не найдены",
        ["playlist.json is empty or no supported videos were found."] =
            "playlist.json пустой или поддерживаемые видео не найдены.",

        ["Video"] = "Видео",
        ["Options"] = "Параметры",
        ["Segmentation"] = "Сегментация",
        ["Split threshold (min)"] = "Порог разделения (мин)",
        ["Target segment length (min)"] = "Длина сегмента (мин)",
        ["Job summary"] = "Сводка задания",
        ["Payload"] = "Пакет перевода",
        ["Player URL"] = "Ссылка плеера",
        ["Segments"] = "Сегменты",
        ["Final output"] = "Итоговое видео",
        ["Active tunnel"] = "Активный туннель",
        ["Ready"] = "Готово",
        ["Ready to prepare payload."] = "Готово к подготовке пакета перевода.",
        ["Ready to prepare."] = "Готово к подготовке.",
        ["Auto build final video after translated mp3 is downloaded"] =
            "Автоматически собрать итоговое видео после скачивания mp3 перевода",
        ["Split long videos into translation segments"] = "Разделять длинные видео на сегменты перевода",
        ["URL Copy"] = "Копировать URL",
        ["Load job"] = "Загрузить",
        ["Prepare"] = "Подготовить",
        ["Playlist"] = "Плейлист",
        ["Next"] = "Далее",
        ["Segment"] = "Сегмент",
        ["Import audio"] = "Импорт аудио",
        ["Merge"] = "Собрать",
        ["Build final"] = "Финал",
        ["Folder"] = "Папка",
        ["Output"] = "Итог",
        ["Workflow"] = "Порядок работы",
        ["Recent actions"] = "Последние действия",
        ["Load existing job"] = "Загрузить задание",
        ["Prepare payload"] = "Подготовить пакет",
        ["Open player page"] = "Открыть плеер",
        ["Open job playlist"] = "Открыть playlist задания",
        ["Open next untranslated"] = "Открыть следующий без перевода",
        ["Open selected segment"] = "Открыть выбранный сегмент",
        ["Import translated audio"] = "Импорт mp3 перевода",
        ["Merge selected segment"] = "Собрать выбранный сегмент",
        ["Build final video"] = "Собрать итоговое видео",
        ["Open job folder"] = "Открыть папку задания",
        ["Open final output"] = "Открыть итоговое видео",
        ["Close"] = "Закрыть",
        ["Quick path: Prepare → Open job playlist / next segment → wait for mp3 → Build final if auto build is off"] =
            "Быстрый путь: Подготовить → playlist задания / сегмент → дождаться mp3 → собрать итоговое видео, если автосборка выключена",
        ["Quick path: Prepare → Playlist / Next → wait for mp3 → Build final"] =
            "Быстрый путь: Подготовить → Плейлист / Далее → дождаться mp3 → Финал",
        ["1. Prepare payload. 2. Open next untranslated segment or full playlist. 3. Import or wait for translated audio. 4. Merge segments. 5. Build final video."] =
            "1. Подготовить payload. 2. Открыть следующий сегмент или весь playlist. 3. Импортировать или дождаться mp3. 4. Собрать сегменты. 5. Собрать итоговое видео.",
        ["Step 1. Prepare"] = "Шаг 1. Подготовить",
        ["Creates source.mp4, payload.mp4 and playlist.json."] =
            "Создаёт source.mp4, payload.mp4 и playlist.json.",
        ["Step 1. Prepare payload"] = "Шаг 1. Подготовить пакет",
        ["Creates source.mp4, 1 fps payload.mp4 and playlist.json for the selected video."] =
            "Создаёт source.mp4, 1 fps payload.mp4 и playlist.json для выбранного видео.",
        ["Step 2. Browser"] = "Шаг 2. Браузер",
        ["Open Playlist or Next."] = "Откройте «Плейлист» или «Далее».",
        ["Step 2. Translate in browser"] = "Шаг 2. Перевести в браузере",
        ["Open job playlist for all prepared segments, or open only the next untranslated segment."] =
            "Откройте playlist задания со всеми сегментами или только следующий непереведённый сегмент.",
        ["Step 3. Build final"] = "Шаг 3. Собрать итоговое видео",
        ["Translated mp3 is saved automatically. Build final if Auto build is off."] =
            "Переведённый mp3 сохраняется автоматически. Нажмите «Финал», если автосборка выключена.",
        ["Translated mp3 is saved automatically. If Auto build is enabled, final video starts automatically; otherwise click Build final video."] =
            "Переведённый mp3 сохраняется автоматически. Если автосборка включена, итоговое видео начнёт собираться само; иначе нажмите «Собрать итоговое видео».",
        ["Short jobs have one segment. Long jobs show all segments."] =
            "Короткие задания имеют один сегмент. Длинные показывают все сегменты.",
        ["Note: short jobs have one playlist item. Long jobs show all generated segments."] =
            "Важно: короткое видео даёт один пункт playlist. Длинное видео показывает все созданные сегменты.",
        ["Prepared"] = "Подготовлено",
        ["Playlist opened"] = "Playlist открыт",
        ["Segment opened"] = "Сегмент открыт",
        ["URL copied"] = "URL скопирован",
        ["Working"] = "Выполняется",
        ["Job loaded"] = "Задание загружено",
        ["Choose a video and click Prepare payload."] = "Выберите видео и нажмите «Подготовить пакет».",
        ["Choose a video and click Prepare."] = "Выберите видео и нажмите «Подготовить».",
        ["Job is ready for browser editing, merging and final build."] =
            "Задание готово для перевода в браузере, сборки сегментов и итогового видео.",
        ["Active tunnel: {0}"] = "Активный туннель: {0}",
        ["Defaults were filled automatically from the selected video."] =
            "Поля заполнены автоматически по выбранному видео.",
        ["Click Prepare payload. The translated mp3 is saved automatically through the callback."] =
            "Нажмите «Подготовить пакет». Переведённый mp3 сохранится автоматически через callback.",
        ["Click Prepare. The translated mp3 is saved automatically through the callback."] =
            "Нажмите «Подготовить». Переведённый mp3 сохранится автоматически через callback.",
        ["No video is selected."] = "Видео не выбрано.",
        ["Preparing payload files, playlist and browser page..."] =
            "Подготовка payload-файлов, playlist и страницы браузера...",
        ["Payload is ready. Segments: {0}. Open job playlist to translate prepared item(s)."] =
            "Пакет готов. Сегментов: {0}. Откройте playlist задания для перевода подготовленных элементов.",
        ["Payload is ready. Segments: {0}. Open Playlist to translate prepared item(s)."] =
            "Пакет готов. Сегментов: {0}. Откройте «Плейлист», чтобы перевести подготовленные элементы.",
        ["Prepared: {0}. Segments: {1}."] = "Подготовлено: {0}. Сегментов: {1}.",
        ["Prepare failed:"] = "Ошибка подготовки:",
        ["Prepare or load a job first."] = "Сначала подготовьте или загрузите задание.",
        ["Opening the job playlist in the browser..."] = "Открываю playlist задания в браузере...",
        ["The browser player now shows the whole job with all prepared segments."] =
            "Браузерный плеер теперь показывает всё задание со всеми подготовленными сегментами.",
        ["The browser player now shows all prepared items for this job."] =
            "Браузерный плеер теперь показывает все подготовленные элементы этого задания.",
        ["Player page opened:"] = "Страница плеера открыта:",
        ["Job playlist opened:"] = "Playlist задания открыт:",
        ["Open player page failed:"] = "Не удалось открыть страницу плеера:",
        ["Open job playlist failed:"] = "Не удалось открыть playlist задания:",
        ["Nothing to copy yet."] = "Пока нечего копировать.",
        ["Player URL copied to clipboard."] = "Ссылка плеера скопирована в буфер.",
        ["The current player or playlist URL was copied to the clipboard."] =
            "Текущая ссылка плеера или playlist скопирована в буфер.",
        ["All segments already have translated audio."] =
            "У всех сегментов уже есть переведённое аудио.",
        ["Opening the next untranslated segment {0}/{1}..."] =
            "Открываю следующий непереведённый сегмент {0}/{1}...",
        ["Opened segment {0}/{1}. Use Open job playlist to return to all prepared items."] =
            "Открыт сегмент {0}/{1}. Используйте Open job playlist, чтобы вернуться ко всем подготовленным элементам.",
        ["Opened segment {0}/{1}. Use Playlist to return to all prepared items."] =
            "Открыт сегмент {0}/{1}. Используйте «Плейлист», чтобы вернуться ко всем подготовленным элементам.",
        ["Opened next untranslated segment {0}/{1}: {2}"] =
            "Открыт следующий непереведённый сегмент {0}/{1}: {2}",
        ["Open next untranslated failed:"] = "Не удалось открыть следующий непереведённый сегмент:",
        ["Select a segment first."] = "Сначала выберите сегмент.",
        ["Opening segment {0}/{1} in the browser..."] =
            "Открываю сегмент {0}/{1} в браузере...",
        ["Opened segment {0}/{1}. Use Open job playlist to jump back to all prepared items."] =
            "Открыт сегмент {0}/{1}. Используйте Open job playlist, чтобы вернуться ко всем подготовленным элементам.",
        ["Opened segment {0}/{1}. Use Playlist to return to all prepared items."] =
            "Открыт сегмент {0}/{1}. Используйте «Плейлист», чтобы вернуться ко всем подготовленным элементам.",
        ["Opened segment {0}/{1}: {2}"] = "Открыт сегмент {0}/{1}: {2}",
        ["Open selected segment failed:"] = "Не удалось открыть выбранный сегмент:",
        ["Select translated audio"] = "Выберите переведённое аудио",
        ["Audio"] = "Аудио",
        ["Importing translated audio for segment {0}/{1}..."] =
            "Импорт переведённого аудио для сегмента {0}/{1}...",
        ["Audio imported"] = "Аудио импортировано",
        ["Translated audio is ready for segment {0}/{1}."] =
            "Переведённое аудио готово для сегмента {0}/{1}.",
        ["Imported audio for segment {0}/{1}."] =
            "Аудио импортировано для сегмента {0}/{1}.",
        ["Import audio failed:"] = "Ошибка импорта аудио:",
        ["Merging segment {0}/{1} into video..."] =
            "Наложение сегмента {0}/{1} в видео...",
        ["Segment merged"] = "Сегмент собран",
        ["Segment {0}/{1} is now merged back into video."] =
            "Сегмент {0}/{1} наложен обратно в видео.",
        ["Merged segment {0}/{1}."] = "Сегмент собран {0}/{1}.",
        ["Merge segment failed:"] = "Ошибка сборки сегмента:",
        ["Building the final translated video. This can take a while..."] =
            "Сборка итогового переведённого видео. Это может занять время...",
        ["Final video ready"] = "Итоговое видео готово",
        ["The final translated video was built successfully. You can open it from this window."] =
            "Итоговое переведённое видео успешно собрано. Его можно открыть из этого окна.",
        ["Final video built."] = "Итоговое видео собрано.",
        ["Build final video failed:"] = "Ошибка сборки итогового видео:",
        ["Build final failed:"] = "Ошибка кнопки «Финал»:",
        ["Translated mp3 is ready. Auto build is creating the final video..."] =
            "Mp3 перевода готов. Автосборка создаёт итоговое видео...",
        ["Final video built automatically."] =
            "Итоговое видео собрано автоматически.",
        ["Auto build final video failed:"] =
            "Ошибка автосборки итогового видео:",
        ["Auto build confirmation"] =
            "Подтверждение автосборки",
        ["Auto build is enabled and translated mp3 is already ready."] =
            "Автосборка включена, и mp3 перевода уже готов.",
        ["Selected video: {0}"] =
            "Выбранное видео: {0}",
        ["Final output: {0}"] =
            "Итоговый файл: {0}",
        ["Continue will build the final video now. Cancel will turn Auto build off for this job."] =
            "Нажмите «Продолжить», чтобы начать сборку итогового видео. Нажмите «Отмена», чтобы выключить автосборку для этого задания.",
        ["Continue"] =
            "Продолжить",
        ["Auto build was turned off. Click Build final video when you want to create the translated video."] =
            "Автосборка выключена. Нажмите «Собрать итоговое видео», когда захотите создать видео с переводом.",
        ["Auto build was turned off. Click Build final when you want to create the translated video."] =
            "Автосборка выключена. Нажмите «Финал», когда захотите создать видео с переводом.",
        ["Auto build canceled by user. Auto build was turned off for this job."] =
            "Автосборка отменена пользователем. Автосборка выключена для этого задания.",
        ["Final output already exists. Auto build was skipped."] =
            "Итоговый файл уже существует. Автосборка пропущена.",
        ["Final output already exists. Auto build skipped."] =
            "Итоговый файл уже существует. Автосборка пропущена.",
        ["Final output already exists. Build skipped."] =
            "Итоговый файл уже существует. Сборка пропущена.",
        ["Save job settings failed:"] =
            "Не удалось сохранить настройки задания:",
        ["No saved job"] = "Нет сохранённого задания",
        ["No prepared job was found for this video yet."] =
            "Для этого видео ещё нет подготовленного задания.",
        ["No saved job found."] = "Сохранённое задание не найдено.",
        ["Next step: click Prepare payload."] = "Следующий шаг: нажмите «Подготовить пакет».",
        ["Next step: click Prepare."] = "Следующий шаг: нажмите «Подготовить».",
        ["Existing job loaded"] = "Существующее задание загружено",
        ["This saved job was created by an older preparation pipeline. Click Prepare to refresh segmentation and payload files."] =
            "Это сохранённое задание было создано старым пайплайном подготовки. Нажмите «Подготовить», чтобы обновить сегментацию и payload-файлы.",
        ["Recommended next step: click Prepare."] =
            "Рекомендуемый следующий шаг: нажмите «Подготовить».",
        ["You can continue with browser editing, importing audio or final build."] =
            "Можно продолжить перевод в браузере, импорт аудио или финальную сборку.",
        ["Loaded existing job."] = "Существующее задание загружено.",
        ["Load existing job failed:"] = "Ошибка загрузки существующего задания:",
        ["job folder"] = "папка задания",
        ["final output"] = "итоговое видео",
        ["Opened {0}."] = "Открыто: {0}.",
        ["Open {0} failed: {1}"] = "Не удалось открыть {0}: {1}",
        ["Path is empty."] = "Путь пустой.",
        ["Path does not exist."] = "Путь не существует.",
        ["No job loaded."] = "Задание не загружено.",
        ["unknown"] = "неизвестно",
        ["Pending"] = "Ожидает",
        ["Source: {0} | Duration: {1} | Segments: {2} | Audio: {3}/{2} | Merged: {4}/{2} | Final: {5}"] =
            "Источник: {0} | Длительность: {1} | Сегменты: {2} | Аудио: {3}/{2} | Собрано: {4}/{2} | Итог: {5}",
        ["Active tunnel: {0} | Select a video first."] =
            "Активный туннель: {0} | Сначала выберите видео.",
        ["On"] = "Вкл",
        ["Off"] = "Выкл",
        ["Enabled after {0} min, target {1} min"] =
            "Включено после {0} мин, цель {1} мин",
        ["Selected: {0} | Active tunnel: {1} | Auto merge: {2} | Segmentation: {3}"] =
            "Выбрано: {0} | Активный туннель: {1} | Автоналожение: {2} | Сегментация: {3}",
        ["merged"] = "собрано",
        ["audio-ready"] = "аудио готово",
        ["prepared"] = "подготовлено",
        ["{0:000}/{1:000} | {2} | start {3} | len {4} | {5}"] =
            "{0:000}/{1:000} | {2} | старт {3} | длит. {4} | {5}",
        ["Source MP4: {0}{1}Payload MP4 (1 fps): {2}"] =
            "Source MP4: {0}{1}Payload MP4 (1 fps): {2}",
        ["If video is up to {0} min:{1}Source MP4: {2}{1}Payload MP4 (1 fps): {3}{1}{1}If video is longer:{1}Each segment gets source.mp4 and payload.mp4 under {4}"] =
            "Если видео до {0} мин:{1}Source MP4: {2}{1}Payload MP4 (1 fps): {3}{1}{1}Если видео длиннее:{1}каждый сегмент получит source.mp4 и payload.mp4 в {4}",
        ["Will be created after Prepare payload."] =
            "Будет создано после подготовки пакета.",
        ["Will be created after Prepare."] =
            "Будет создано после нажатия «Подготовить».",
        ["Videos up to {0} min stay as one job."] =
            "Видео до {0} мин остаются одним заданием.",
        ["Longer videos are split into about {0}-minute parts."] =
            "Более длинные видео делятся примерно на части по {0} мин.",
        ["Segmentation is disabled."] = "Сегментация выключена.",
        ["One source MP4 and one 1 fps payload MP4 will be created."] =
            "Будут созданы один source MP4 и один payload MP4 1 fps.",
        ["Load an existing prepared job for the selected video."] =
            "Загрузить существующее подготовленное задание для выбранного видео.",
        ["Create or refresh source.mp4, payload.mp4, playlist.json and player.html."] =
            "Создать или обновить source.mp4, payload.mp4, playlist.json и player.html.",
        ["Disabled until a job is prepared or loaded."] =
            "Недоступно, пока задание не подготовлено или не загружено.",
        ["Open the browser dashboard with the whole job and all segments."] =
            "Открыть в браузере всё задание со всеми сегментами.",
        ["Open the whole playlist for this job. Short jobs contain one item; segmented jobs contain all generated segments."] =
            "Открыть весь playlist этого задания. Короткие задания содержат один пункт; сегментированные содержат все созданные сегменты.",
        ["Open Playlist for the whole job. Short jobs contain one item; segmented jobs contain all generated segments."] =
            "Открыть «Плейлист» для всего задания. Короткие задания содержат один пункт, сегментированные — все созданные сегменты.",
        ["All segments already have translated audio or merged output."] =
            "У всех сегментов уже есть переведённое аудио или собранный результат.",
        ["Open the next segment that still needs translated audio."] =
            "Открыть следующий сегмент, которому ещё нужно переведённое аудио.",
        ["Open Next for the first segment that still needs translated audio."] =
            "Открыть «Далее» для первого сегмента, которому ещё нужно переведённое аудио.",
        ["Open only the selected segment in the player page."] =
            "Открыть только выбранный сегмент в плеере.",
        ["Open Segment for the selected item only."] =
            "Открыть «Сегмент» только для выбранного элемента.",
        ["Import translated audio manually for the selected segment."] =
            "Импортировать переведённое аудио вручную для выбранного сегмента.",
        ["Import audio manually for the selected segment."] =
            "Импортировать аудио вручную для выбранного сегмента.",
        ["Merge the selected segment with its translated audio."] =
            "Собрать выбранный сегмент с его переведённым аудио.",
        ["Build the final translated output from ready segments."] =
            "Собрать итоговый перевод из готовых сегментов.",
        ["Open the job folder in Explorer."] = "Открыть папку задания в Explorer.",
        ["Open the final translated output."] = "Открыть итоговое переведённое видео.",
        ["Disabled until the final translated output exists."] =
            "Недоступно, пока итоговое переведённое видео ещё не создано.",
        ["Disabled until a preview or prepared player URL is available."] =
            "Недоступно, пока нет preview или подготовленной ссылки плеера.",
        ["Copy the current player or playlist URL."] =
            "Скопировать текущую ссылку плеера или playlist.",
        ["{0} help"] = "Справка: {0}",
        ["Choose the source video. The other fields are filled automatically from this video.\n\nExample:\nPick one lesson from the list, then click Prepare payload."] =
            "Выберите исходное видео. Остальные поля заполняются автоматически из этого видео.\n\nПример:\nВыберите урок из списка, затем нажмите Prepare payload.",
        ["Choose the source video. The other fields are filled automatically from this video.\n\nExample:\nPick one lesson from the list, then click Prepare."] =
            "Выберите исходное видео. Остальные поля заполняются автоматически из этого видео.\n\nПример:\nВыберите урок из списка, затем нажмите «Подготовить».",
        ["If Auto merge is enabled, the app saves the translated mp3 automatically and merges it back into the prepared source MP4 when the callback arrives."] =
            "Если Auto merge включён, программа автоматически сохранит переведённый mp3 и наложит его обратно на подготовленный source MP4 после callback.",
        ["If enabled, videos longer than the split threshold are split into segments. Every segment gets two MP4 files: source.mp4 and payload.mp4."] =
            "Если включено, видео длиннее порога делятся на сегменты. Каждый сегмент получает два MP4-файла: source.mp4 и payload.mp4.",
        ["The video is split only if its real duration is longer than this value.\n\nExample:\n120 means videos up to 2 hours stay as one job."] =
            "Видео делится только если его реальная длительность больше этого значения.\n\nПример:\n120 означает, что видео до 2 часов остаются одним заданием.",
        ["Approximate length of each segment for long videos.\n\nExample:\n60 means a 6 hour video becomes about 6 segments."] =
            "Примерная длина каждого сегмента для длинных видео.\n\nПример:\n60 означает, что видео на 6 часов станет примерно 6 сегментами.",
        ["Shows the current source duration, segment count, translated audio count, merged count and final status."] =
            "Показывает текущую длительность источника, количество сегментов, готовое аудио, собранные сегменты и статус итогового видео.",
        ["Translation Studio prepares two MP4 files.\n\nsource.mp4 = full real video\npayload.mp4 = lightweight 1 fps MP4 over the full audio duration\n\nFor segmented jobs each segment gets the same pair."] =
            "Студия перевода готовит два MP4-файла.\n\nsource.mp4 = полное реальное видео\npayload.mp4 = лёгкий MP4 1 fps на всю длительность аудио\n\nДля сегментированных заданий каждый сегмент получает такую же пару.",
        ["This is the browser URL for the prepared job. Use URL Copy to share it. Open job playlist returns to all prepared items after you open one segment."] =
            "Это браузерная ссылка подготовленного задания. Используйте URL Copy, чтобы скопировать её. Open job playlist возвращает ко всем подготовленным элементам после открытия одного сегмента.",
        ["This is the browser URL for the prepared job. Use Copy to share it. Playlist returns to all prepared items after you open one segment."] =
            "Это браузерная ссылка подготовленного задания. Используйте «Копировать», чтобы скопировать её. «Плейлист» возвращает ко всем подготовленным элементам после открытия одного сегмента.",
        ["Shows the prepared segments and their status: prepared, audio-ready or merged."] =
            "Показывает подготовленные сегменты и их статус: prepared, audio-ready или merged.",
        ["This is the final merged translated video path that will be created in merged_output."] =
            "Это путь итогового переведённого видео, которое будет создано в merged_output.",
        ["No help is available for this item."] = "Для этого пункта справка недоступна.",
        ["Please wait..."] = "Подождите...",

        ["Converts videos and builds MP4 cache files for playback and translation."] =
            "Конвертирует видео и создаёт MP4-кэш для просмотра и перевода.",
        ["Runs the local web server that serves player.html and playlist.json."] =
            "Запускает локальный веб-сервер, который отдаёт player.html и playlist.json.",
        ["Creates the public tunnel URL that exposes the local player to the internet."] =
            "Создаёт публичную ссылку туннеля для доступа к локальному плееру из интернета.",
        ["Creates a public ngrok URL for the local player. First-time use usually needs an ngrok authtoken."] =
            "Создаёт публичную ссылку ngrok для локального плеера. При первом запуске обычно нужен authtoken ngrok.",
        ["Used by localhost.run. The app creates an SSH reverse tunnel to get a public HTTPS URL."] =
            "Используется для localhost.run. Программа создаёт обратный SSH-туннель и получает публичную HTTPS-ссылку.",
        ["Used for Tailscale Funnel. The machine must already have Tailscale installed, logged in, and allowed to use Funnel."] =
            "Используется для Tailscale Funnel. На компьютере уже должен быть установлен Tailscale, выполнен вход и разрешён Funnel.",
        ["Creates a public zrok share URL. First-time use usually needs a zrok account token to enable this machine."] =
            "Создаёт публичную ссылку zrok share. При первом запуске обычно нужен token аккаунта zrok для включения этой машины.",
        ["Creates a public LocalTunnel URL. Visitors may still see the service password or consent page controlled by LocalTunnel."] =
            "Создаёт публичную ссылку LocalTunnel. Посетители всё равно могут увидеть пароль или страницу согласия от самого LocalTunnel.",
        ["Creates a public xTunnel URL for the local player after client activation."] =
            "Создаёт публичную ссылку xTunnel для локального плеера после активации клиента.",
        ["Creates a public VK Tunnel URL for the local player when the provider is available."] =
            "Создаёт публичную ссылку VK Tunnel для локального плеера, если провайдер доступен.",
        ["Tip: this window refreshes every 2 seconds. If you install a missing tool manually, the status updates automatically."] =
            "Подсказка: это окно обновляется каждые 2 секунды. Если установить недостающий инструмент вручную, статус обновится автоматически."
    };

    private static readonly Dictionary<string, string> RussianToEnglish = EnglishToRussian
        .Where(x => !string.Equals(x.Key, x.Value, StringComparison.Ordinal))
        .GroupBy(x => x.Value, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.Ordinal);

    public static string Translate(string? value, AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        if (language == AppLanguage.Russian)
            return TranslateExactOrPrefixed(value, EnglishToRussian);

        return TranslateExactOrPrefixed(value, RussianToEnglish);
    }

    private static string TranslateExactOrPrefixed(string value, IReadOnlyDictionary<string, string> dictionary)
    {
        if (dictionary.TryGetValue(value, out var translated))
            return translated;

        foreach (var pair in dictionary)
        {
            if (!pair.Key.EndsWith(":", StringComparison.Ordinal))
                continue;

            var prefixWithSpace = pair.Key + " ";
            if (value.StartsWith(prefixWithSpace, StringComparison.Ordinal))
                return pair.Value + value[pair.Key.Length..];
        }

        return value;
    }

    public static void Apply(Control root, AppLanguage language)
    {
        if (root is Window window)
            window.Title = Translate(window.Title, language);

        var controls = root.GetVisualDescendants()
            .OfType<Control>()
            .Concat(root.GetLogicalDescendants().OfType<Control>())
            .Prepend(root)
            .Distinct();

        foreach (var control in controls)
        {
            switch (control)
            {
                case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                    textBlock.Text = Translate(textBlock.Text, language);
                    break;
                case TextBox textBox when textBox.Watermark is string watermark && !string.IsNullOrWhiteSpace(watermark):
                    textBox.Watermark = Translate(watermark, language);
                    break;
                case ContentControl contentControl when contentControl.Content is string content && content != "?":
                    contentControl.Content = Translate(content, language);
                    break;
            }
        }
    }
}
