#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 3 ]]; then
  echo "Usage: $0 <app-bundle> <output-zip> <sign-identity> [entitlements]"
  exit 1
fi

APP_PATH="$1"
OUTPUT_ZIP="$2"
SIGN_IDENTITY="$3"
ENTITLEMENTS="${4:-${ENTITLEMENTS:-}}"
APPLE_ID="${APPLE_ID:-}"
APPLE_APP_PASSWORD="${APPLE_APP_PASSWORD:-}"
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
APPLE_CERT_BASE64="${APPLE_CERT_BASE64:-}"
APPLE_CERT_PASSWORD="${APPLE_CERT_PASSWORD:-}"
KEYCHAIN_PASSWORD="${KEYCHAIN_PASSWORD:-pvl-temporary-keychain-password}"
KEYCHAIN_PATH="${RUNNER_TEMP:-$(mktemp -d)}/pvl-signing.keychain-db"
CERT_PATH="${RUNNER_TEMP:-$(mktemp -d)}/developer-id.p12"
NOTARY_ZIP="${RUNNER_TEMP:-$(mktemp -d)}/$(basename "$OUTPUT_ZIP" .zip)-notary.zip"

if [[ ! -d "$APP_PATH" ]]; then
  echo "App bundle not found: $APP_PATH" >&2
  exit 1
fi

cleanup() {
  rm -f "$CERT_PATH" "$NOTARY_ZIP" || true
  if security list-keychains -d user | grep -Fq "$KEYCHAIN_PATH"; then
    security delete-keychain "$KEYCHAIN_PATH" || true
  fi
}
trap cleanup EXIT

if [[ -n "$APPLE_CERT_BASE64" ]]; then
  (echo "$APPLE_CERT_BASE64" | base64 --decode > "$CERT_PATH") 2>/dev/null || (echo "$APPLE_CERT_BASE64" | base64 -D > "$CERT_PATH")
  security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"
  security set-keychain-settings -lut 21600 "$KEYCHAIN_PATH"
  security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"
  security import "$CERT_PATH" -k "$KEYCHAIN_PATH" -P "$APPLE_CERT_PASSWORD" -A -T /usr/bin/codesign -T /usr/bin/security >/dev/null
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH" >/dev/null
  security list-keychains -d user -s "$KEYCHAIN_PATH" login.keychain-db
fi

while IFS= read -r -d '' candidate; do
  if [[ -n "$ENTITLEMENTS" && "$candidate" == *.dylib ]]; then
    codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$candidate"
  elif [[ -n "$ENTITLEMENTS" ]]; then
    codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$candidate"
  else
    codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$candidate"
  fi
done < <(find "$APP_PATH/Contents" -type f \( -name '*.dylib' -o -name '*.so' -o -perm -111 \) -print0)

if [[ -n "$ENTITLEMENTS" ]]; then
  codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$APP_PATH"
else
  codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$APP_PATH"
fi

codesign --verify --deep --strict --verbose=2 "$APP_PATH"

if [[ -n "$APPLE_ID" && -n "$APPLE_APP_PASSWORD" && -n "$APPLE_TEAM_ID" ]]; then
  ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$NOTARY_ZIP"
  xcrun notarytool submit "$NOTARY_ZIP" --apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD" --team-id "$APPLE_TEAM_ID" --wait
  xcrun stapler staple "$APP_PATH"
  xcrun stapler validate "$APP_PATH"
else
  echo "Skipping notarization because APPLE_ID / APPLE_APP_PASSWORD / APPLE_TEAM_ID are not fully configured."
fi

rm -f "$OUTPUT_ZIP"
ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$OUTPUT_ZIP"
echo "Signed macOS bundle archive created: $OUTPUT_ZIP"
