#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${VERSION:-1.0.7}"
APP_NAME="${APP_NAME:-PublicVideoLauncher}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
SINGLE_FILE="${SINGLE_FILE:-false}"
OUTPUT_ROOT="${OUTPUT_ROOT:-artifacts/publish}"
RELEASE_ROOT="${RELEASE_ROOT:-artifacts/releases}"
SOURCE_ROOT="${SOURCE_ROOT:-artifacts/source}"
CREATE_SOURCE_ARCHIVE="${CREATE_SOURCE_ARCHIVE:-true}"
CREATE_LINUX_NATIVE_PACKAGES="${CREATE_LINUX_NATIVE_PACKAGES:-true}"
CREATE_MACOS_APP_BUNDLES="${CREATE_MACOS_APP_BUNDLES:-true}"
SIGN_MACOS_APP="${SIGN_MACOS_APP:-false}"
APPLE_SIGN_IDENTITY="${APPLE_SIGN_IDENTITY:-}"
APPLE_ENTITLEMENTS="${APPLE_ENTITLEMENTS:-}"

if [ "$#" -eq 0 ]; then
  RUNTIMES=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)
else
  RUNTIMES=("$@")
fi

contains_runtime_prefix() {
  local prefix="$1"
  for rid in "${RUNTIMES[@]}"; do
    [[ "$rid" == "$prefix"* ]] && return 0
  done
  return 1
}

chmod +x build/*.sh build/assets/*.sh
CONFIGURATION="$CONFIGURATION" VERSION="$VERSION" SELF_CONTAINED="$SELF_CONTAINED" SINGLE_FILE="$SINGLE_FILE" OUTPUT_ROOT="$OUTPUT_ROOT" ./build/publish.sh "${RUNTIMES[@]}"
VERSION="$VERSION" APP_NAME="$APP_NAME" PUBLISH_ROOT="$OUTPUT_ROOT" RELEASE_ROOT="$RELEASE_ROOT" ./build/package.sh "${RUNTIMES[@]}"

if [ "$CREATE_SOURCE_ARCHIVE" = "true" ]; then
  VERSION="$VERSION" APP_NAME="$APP_NAME" OUTPUT_ROOT="$SOURCE_ROOT" ./build/create-source-archive.sh
fi

if [ "$CREATE_LINUX_NATIVE_PACKAGES" = "true" ] && contains_runtime_prefix "linux-"; then
  if [[ "$OSTYPE" == linux* ]]; then
    VERSION="$VERSION" APP_NAME="$APP_NAME" PUBLISH_ROOT="$OUTPUT_ROOT" RELEASE_ROOT="$RELEASE_ROOT" ./build/linux-native-packages.sh "${RUNTIMES[@]}"
  else
    echo "Skipping Linux native package creation on non-Linux host"
  fi
fi

if [ "$CREATE_MACOS_APP_BUNDLES" = "true" ] && contains_runtime_prefix "osx-"; then
  if [[ "$OSTYPE" == darwin* ]]; then
    mkdir -p "$RELEASE_ROOT"
    for rid in osx-x64 osx-arm64; do
      [ -d "$OUTPUT_ROOT/$rid" ] || continue
      ./build/macos-app-bundle.sh "$OUTPUT_ROOT/$rid" "$APP_NAME" "com.example.publicvideolauncher" "$VERSION" "$RELEASE_ROOT"
      if [ "$SIGN_MACOS_APP" = "true" ] && [ -n "$APPLE_SIGN_IDENTITY" ]; then
        APPLE_ID="${APPLE_ID:-}" \
        APPLE_APP_PASSWORD="${APPLE_APP_PASSWORD:-}" \
        APPLE_TEAM_ID="${APPLE_TEAM_ID:-}" \
        APPLE_CERT_BASE64="${APPLE_CERT_BASE64:-}" \
        APPLE_CERT_PASSWORD="${APPLE_CERT_PASSWORD:-}" \
        ./build/macos-sign-and-notarize.sh "$RELEASE_ROOT/${APP_NAME}.app" "$RELEASE_ROOT/${APP_NAME}-${VERSION}-${rid}.app.zip" "$APPLE_SIGN_IDENTITY" "$APPLE_ENTITLEMENTS"
      else
        ditto -c -k --sequesterRsrc --keepParent "$RELEASE_ROOT/${APP_NAME}.app" "$RELEASE_ROOT/${APP_NAME}-${VERSION}-${rid}.app.zip"
      fi
      rm -rf "$RELEASE_ROOT/${APP_NAME}.app"
    done
  else
    echo "Skipping macOS .app bundle creation on non-macOS host"
  fi
fi

if [ "$CREATE_SOURCE_ARCHIVE" = "true" ]; then
  ./build/write-checksums.sh artifacts/SHA256SUMS.txt "$RELEASE_ROOT" "$SOURCE_ROOT"
else
  ./build/write-checksums.sh artifacts/SHA256SUMS.txt "$RELEASE_ROOT"
fi

echo "Release assets are in $RELEASE_ROOT"
echo "Source archives are in $SOURCE_ROOT"
