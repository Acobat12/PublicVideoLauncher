# Tunnel setup guide / Руководство по настройке туннелей

## 1. Cloudflare Tunnel
EN:
1. Choose **Cloudflare Tunnel**.
2. Start the app. The app downloads `cloudflared` automatically when possible.
3. No token is required for quick tunnels.
4. If your network breaks Cloudflare, try another provider.

RU:
1. Выберите **Cloudflare Tunnel**.
2. Запустите программу. Она автоматически скачает `cloudflared`, если это возможно.
3. Для quick tunnel токен не нужен.
4. Если ваша сеть ломает Cloudflare, попробуйте другой провайдер.

## 2. xTunnel (Windows)
EN:
1. Download the official Windows zip from xTunnel.
2. Extract `xtunnel.exe` or `xTunnel.exe`.
3. Put it into `.tools/xtunnel/` or select it through **CLI path**.
4. Paste your license key into **Tunnel auth**.
5. The official first commands are:
   - `xtunnel.exe register <KEY>`
   - `xtunnel.exe http 8000`
6. The app now tries registration once and can retry the tunnel with `--force` when the license is busy.

RU:
1. Скачайте официальный zip для Windows с сайта xTunnel.
2. Распакуйте `xtunnel.exe` или `xTunnel.exe`.
3. Положите файл в `.tools/xtunnel/` или укажите его через **CLI path**.
4. Вставьте лицензионный ключ в поле **Tunnel auth**.
5. Официальные первые команды такие:
   - `xtunnel.exe register <КЛЮЧ>`
   - `xtunnel.exe http 8000`
6. Теперь программа пытается зарегистрировать ключ один раз и умеет повторить запуск туннеля с `--force`, если лицензия занята.

## 3. VK Tunnel
EN:
1. Install Node.js.
2. Install VK Tunnel globally:
   - `npm install -g @vkontakte/vk-tunnel`
3. Then either:
   - leave **CLI path** empty and let the app use `npx`, or
   - point **CLI path** to `vk-tunnel.cmd` or `vk-tunnel.js`.
4. Start the app.
5. Important: the official VK repository warns that the service may be temporarily unavailable.

RU:
1. Установите Node.js.
2. Установите VK Tunnel глобально:
   - `npm install -g @vkontakte/vk-tunnel`
3. После этого:
   - либо оставьте **CLI path** пустым, и программа попробует использовать `npx`,
   - либо укажите путь к `vk-tunnel.cmd` или `vk-tunnel.js`.
4. Запустите программу.
5. Важно: официальный репозиторий VK предупреждает, что сервис может быть временно недоступен.

## Notes / Примечания
EN:
- `Tunnel auth` means: zrok account token or xTunnel key.
- `CLI path` means: a direct path to the tunnel executable/script.
- If a tunnel starts but no public URL is detected, save the full log and inspect stdout/stderr.

RU:
- `Tunnel auth` — это токен zrok или ключ xTunnel.
- `CLI path` — это прямой путь к исполняемому файлу или скрипту туннеля.
- Если туннель стартует, но публичный URL не найден, сохраните полный лог и проверьте stdout/stderr.


## Extra troubleshooting / Дополнительная диагностика

### The app says: started, but the public URL was not detected in logs
EN:
1. Open the Dependencies window and confirm the exact executable path.
2. Run the same command manually in a terminal from the tool folder.
3. Check whether the CLI prints a public URL, a status like Connected, or an authentication error.
4. If the manual command works but the app still fails, send the full app log from the newest build.

RU:
1. Откройте окно Dependencies и проверьте точный путь к исполняемому файлу.
2. Запустите ту же команду вручную в терминале из папки инструмента.
3. Посмотрите, печатает ли CLI публичный URL, статус вроде Connected или ошибку авторизации.
4. Если вручную команда работает, а программа всё ещё падает, пришлите полный лог приложения из самой новой сборки.

### xTunnel manual commands / Ручные команды xTunnel
EN: On Windows the official flow is `xtunnel.exe register <KEY>` and then `xtunnel.exe http <PORT>`. If the service says the license is busy, retry with `xtunnel.exe http <PORT> --force`.
RU: На Windows официальный сценарий такой: `xtunnel.exe register <КЛЮЧ>`, затем `xtunnel.exe http <ПОРТ>`. Если сервис отвечает, что лицензия занята, повторите с `xtunnel.exe http <ПОРТ> --force`.

### VK Tunnel status / Статус VK Tunnel
EN: The official VK Tunnel repository currently warns that the service is unavailable because of maintenance. Even a correct local setup may still fail on the provider side.
RU: В официальном репозитории VK Tunnel сейчас есть предупреждение, что сервис недоступен из-за техработ. Даже при правильной локальной настройке он может не работать на стороне провайдера.
