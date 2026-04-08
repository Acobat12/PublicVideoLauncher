#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ "$#" -lt 2 ]; then
  echo "Usage: $0 <published-macos-dir> <AppName> [bundle-id] [version] [output-dir]"
  exit 1
fi

PUBLISH_DIR="$1"
APP_NAME="$2"
BUNDLE_ID="${3:-com.example.publicvideolauncher}"
VERSION="${4:-1.0.0}"
OUTPUT_DIR="${5:-.}"
APP_DIR="$OUTPUT_DIR/${APP_NAME}.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
EXECUTABLE="$MACOS_DIR/PublicVideoLauncher"
ICON_SOURCE="${ICON_SOURCE:-$REPO_ROOT/Assets/AppIcon.icns}"
ICON_BASENAME="AppIcon.icns"
ICON_PLIST_ENTRY=""

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp -R "$PUBLISH_DIR"/. "$MACOS_DIR"/
chmod +x "$EXECUTABLE" || true

if [[ -f "$ICON_SOURCE" ]]; then
  cp "$ICON_SOURCE" "$RESOURCES_DIR/$ICON_BASENAME"
  ICON_PLIST_ENTRY="    <key>CFBundleIconFile</key>
    <string>$ICON_BASENAME</string>"
fi

cat > "$CONTENTS_DIR/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleExecutable</key>
    <string>PublicVideoLauncher</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
$ICON_PLIST_ENTRY
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
  </dict>
</plist>
PLIST

echo "Created $APP_DIR"
