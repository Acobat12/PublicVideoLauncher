# PublicVideoLauncher 1.0.1

## What's new
- Fixed **Proxy MP4 for translation** behavior.
- When the option is **OFF**, non-MP4 files are now published **as-is** with **no conversion**.
- The UI now hides **Translation** and **Cache** columns in OFF mode to avoid misleading expectations.
- Scan state and log messages are clearer for non-MP4 files.

## Downloads
Download the build for your platform from the release assets:
- Windows: `*-win-x64-setup.exe`, `*-win-arm64-setup.exe`
- macOS: `*-osx-x64.app.zip`, `*-osx-arm64.app.zip`
- Linux: `.deb`, `.rpm`, or `.AppImage`

## Important
`vot.user.js` is still required for local video translation.
Without it, the app can publish a local video through a temporary public link, but translation itself will not work.

## Notes
- In **OFF** mode, AVI / MKV / MOV / WEBM files may open, but translation may not work for those formats.
- In **ON** mode, the app prepares translation-compatible MP4 proxy files for non-MP4 inputs.
- The public tunnel URL changes on every run.
- Firefox DNS-over-HTTPS / Secure DNS settings can affect temporary tunnel links.
- Windows SmartScreen may warn on first launch because the app is unsigned.

## Helper tools reuse
The `.tools` folder can be copied or moved to another video folder so helper tools do not need to be downloaded again.
