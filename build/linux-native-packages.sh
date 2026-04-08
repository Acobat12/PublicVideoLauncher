#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_ASSETS_DIR="$SCRIPT_DIR/assets"

APP_NAME="${APP_NAME:-PublicVideoLauncher}"
PACKAGE_NAME="${PACKAGE_NAME:-public-video-launcher}"
VERSION="${VERSION:-1.0.0}"
PUBLISH_ROOT="${PUBLISH_ROOT:-artifacts/publish}"
RELEASE_ROOT="${RELEASE_ROOT:-artifacts/releases}"
MAINTAINER="${MAINTAINER:-PublicVideoLauncher Maintainers <maintainers@example.com>}"
HOMEPAGE="${HOMEPAGE:-https://example.com/PublicVideoLauncher}"
LICENSE_NAME="${LICENSE_NAME:-Proprietary}"
NFPM_VERSION="${NFPM_VERSION:-2.45.0}"
APPIMAGETOOL_TAG="${APPIMAGETOOL_TAG:-continuous}"
TOOLS_DIR="${TOOLS_DIR:-.tools}"
APPDIR_ROOT="${RELEASE_ROOT}/appimage"
HOST_ARCH="$(uname -m)"
ICON_SOURCE="${ICON_SOURCE:-$BUILD_ASSETS_DIR/PublicVideoLauncher.png}"
ICON_SOURCE_256="${ICON_SOURCE_256:-$BUILD_ASSETS_DIR/PublicVideoLauncher-256.png}"

mkdir -p "$RELEASE_ROOT" "$TOOLS_DIR"

map_deb_arch() {
  case "$1" in
    linux-x64) echo amd64 ;;
    linux-arm64) echo arm64 ;;
    *) echo "$1" ;;
  esac
}

map_rpm_arch() {
  case "$1" in
    linux-x64) echo x86_64 ;;
    linux-arm64) echo aarch64 ;;
    *) echo "$1" ;;
  esac
}

ensure_nfpm() {
  local nfpm_bin="$TOOLS_DIR/nfpm"
  if [[ -x "$nfpm_bin" ]]; then
    echo "$nfpm_bin"
    return 0
  fi

  local tmpdir
  tmpdir="$(mktemp -d)"
  local arch="amd64"
  if [[ "$HOST_ARCH" == "aarch64" || "$HOST_ARCH" == "arm64" ]]; then
    arch="arm64"
  fi
  local archive="nfpm_${NFPM_VERSION}_Linux_${arch}.tar.gz"
  local url="https://github.com/goreleaser/nfpm/releases/download/v${NFPM_VERSION}/${archive}"
  echo "Downloading nFPM ${NFPM_VERSION}..."
  curl -fsSL "$url" -o "$tmpdir/$archive"
  tar -xzf "$tmpdir/$archive" -C "$tmpdir"
  install -m 0755 "$tmpdir/nfpm" "$nfpm_bin"
  rm -rf "$tmpdir"
  echo "$nfpm_bin"
}

ensure_appimagetool() {
  local rid="$1"
  local arch
  local url
  case "$rid" in
    linux-x64)
      arch="x86_64"
      url="https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_TAG}/appimagetool-${arch}.AppImage"
      ;;
    linux-arm64)
      arch="aarch64"
      url="https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_TAG}/appimagetool-${arch}.AppImage"
      ;;
    *)
      return 1
      ;;
  esac

  local bin="$TOOLS_DIR/appimagetool-${arch}.AppImage"
  if [[ ! -x "$bin" ]]; then
    echo "Downloading appimagetool ${APPIMAGETOOL_TAG} for ${arch}..."
    curl -fsSL "$url" -o "$bin"
    chmod +x "$bin"
  fi
  echo "$bin"
}

render_desktop_file() {
  local destination="$1"
  sed \
    -e "s#@APP_NAME@#${APP_NAME}#g" \
    -e "s#@EXECUTABLE@#public-video-launcher#g" \
    -e "s#@ICON_NAME@#PublicVideoLauncher#g" \
    $BUILD_ASSETS_DIR/PublicVideoLauncher.desktop.in > "$destination"
}

package_linux_runtime() {
  local rid="$1"
  local publish_dir="$PUBLISH_ROOT/$rid"
  if [[ ! -d "$publish_dir" ]]; then
    echo "Skipping Linux native packages for $rid: $publish_dir not found"
    return 0
  fi

  local nfpm_bin
  nfpm_bin="$(ensure_nfpm)"
  local tmpdir
  tmpdir="$(mktemp -d)"
  local desktop_file="$tmpdir/PublicVideoLauncher.desktop"
  render_desktop_file "$desktop_file"

  local deb_arch rpm_arch
  deb_arch="$(map_deb_arch "$rid")"
  rpm_arch="$(map_rpm_arch "$rid")"
  local config_file="$tmpdir/nfpm.yaml"

  cat > "$config_file" <<YAML
name: "${PACKAGE_NAME}"
arch: "${deb_arch}"
platform: linux
version: "${VERSION}"
section: video
priority: optional
maintainer: "${MAINTAINER}"
description: |
  PublicVideoLauncher desktop app for sharing a local video folder over a public tunnel.
homepage: "${HOMEPAGE}"
license: "${LICENSE_NAME}"
contents:
  - src: "${publish_dir}/"
    dst: "/opt/${APP_NAME}"
    type: tree
  - src: "$BUILD_ASSETS_DIR/public-video-launcher.sh"
    dst: "/usr/bin/public-video-launcher"
    file_info:
      mode: 0755
  - src: "${desktop_file}"
    dst: "/usr/share/applications/PublicVideoLauncher.desktop"
  - src: "$ICON_SOURCE"
    dst: "/usr/share/icons/hicolor/512x512/apps/PublicVideoLauncher.png"
  - src: "$ICON_SOURCE_256"
    dst: "/usr/share/icons/hicolor/256x256/apps/PublicVideoLauncher.png"
overrides:
  deb:
    arch: "${deb_arch}"
    depends:
      - libx11-6
      - libice6
      - libsm6
      - libfontconfig1
  rpm:
    arch: "${rpm_arch}"
    depends:
      - libX11
      - libICE
      - libSM
      - fontconfig
YAML

  "$nfpm_bin" package --config "$config_file" --packager deb --target "$RELEASE_ROOT/${PACKAGE_NAME}_${VERSION}_${deb_arch}.deb"
  "$nfpm_bin" package --config "$config_file" --packager rpm --target "$RELEASE_ROOT/${PACKAGE_NAME}-${VERSION}-1.${rpm_arch}.rpm"

  local expected_host_arch=""
  case "$rid" in
    linux-x64) expected_host_arch="x86_64" ;;
    linux-arm64) expected_host_arch="aarch64" ;;
  esac

  if [[ -n "$expected_host_arch" && "$HOST_ARCH" == "$expected_host_arch" ]]; then
    local appimagetool
    appimagetool="$(ensure_appimagetool "$rid")"
    local appdir="$APPDIR_ROOT/${rid}/${APP_NAME}.AppDir"
    rm -rf "$appdir"
    mkdir -p "$appdir/usr/bin"
    cp -R "$publish_dir"/. "$appdir/usr/bin"/
    install -m 0644 "$ICON_SOURCE" "$appdir/${APP_NAME}.png"
    install -m 0644 "$ICON_SOURCE" "$appdir/.DirIcon"
    render_desktop_file "$appdir/${APP_NAME}.desktop"
    sed -i 's#Exec=public-video-launcher %U#Exec=PublicVideoLauncher#' "$appdir/${APP_NAME}.desktop"
    sed -i 's#Icon=PublicVideoLauncher#Icon=PublicVideoLauncher#' "$appdir/${APP_NAME}.desktop"
    sed -e "s#PublicVideoLauncher#${APP_NAME}#g" "$BUILD_ASSETS_DIR/AppRun.in" > "$appdir/AppRun"
    chmod +x "$appdir/AppRun" "$appdir/usr/bin/PublicVideoLauncher"
    ARCH="$expected_host_arch" APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" "$appdir" "$RELEASE_ROOT/${APP_NAME}-${VERSION}-${rid}.AppImage"
  else
    echo "Skipping AppImage for $rid on host architecture $HOST_ARCH. Build it on a matching Linux runner to produce an AppImage."
  fi

  rm -rf "$tmpdir"
}

if [[ "$#" -eq 0 ]]; then
  set -- linux-x64 linux-arm64
fi

for rid in "$@"; do
  case "$rid" in
    linux-*) package_linux_runtime "$rid" ;;
    *) echo "Skipping non-Linux runtime $rid" ;;
  esac
done
