#!/usr/bin/env bash
set -euo pipefail

PUBLISH_ROOT="${PUBLISH_ROOT:-artifacts/publish}"
RELEASE_ROOT="${RELEASE_ROOT:-artifacts/releases}"
APP_NAME="${APP_NAME:-PublicVideoLauncher}"
VERSION="${VERSION:-1.0.0}"

mkdir -p "$RELEASE_ROOT"

zip_dir() {
  local source_dir="$1"
  local out_file="$2"
  if command -v zip >/dev/null 2>&1; then
    (cd "$source_dir" && zip -qr "$out_file" .)
  else
    python3 - "$source_dir" "$out_file" <<'PY2'
import pathlib, sys, zipfile
source = pathlib.Path(sys.argv[1]).resolve()
out = pathlib.Path(sys.argv[2]).resolve()
out.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(out, 'w', compression=zipfile.ZIP_DEFLATED) as zf:
    for path in source.rglob('*'):
        if path.is_file():
            zf.write(path, path.relative_to(source).as_posix())
PY2
  fi
}

package_dir() {
  local rid="$1"
  local dir="$PUBLISH_ROOT/$rid"
  if [ ! -d "$dir" ]; then
    echo "Skipping $rid: $dir not found"
    return 0
  fi

  local base="$APP_NAME-$VERSION-$rid"
  local out
  if [[ "$rid" == win-* ]]; then
    out="$RELEASE_ROOT/$base.zip"
    rm -f "$out"
    zip_dir "$dir" "$out"
  else
    out="$RELEASE_ROOT/$base.tar.gz"
    rm -f "$out"
    tar -C "$dir" -czf "$out" .
  fi

  echo "Packaged $out"
}

if [ "$#" -eq 0 ]; then
  for dir in "$PUBLISH_ROOT"/*; do
    [ -d "$dir" ] || continue
    package_dir "$(basename "$dir")"
  done
else
  for rid in "$@"; do
    package_dir "$rid"
  done
fi
