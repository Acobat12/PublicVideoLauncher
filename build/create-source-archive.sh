#!/usr/bin/env bash
set -euo pipefail

VERSION="${VERSION:-1.0.0}"
APP_NAME="${APP_NAME:-PublicVideoLauncher}"
OUTPUT_ROOT="${OUTPUT_ROOT:-artifacts/source}"
ZIP_OUT="$OUTPUT_ROOT/${APP_NAME}-source-$VERSION.zip"
TAR_OUT="$OUTPUT_ROOT/${APP_NAME}-source-$VERSION.tar.gz"

mkdir -p "$OUTPUT_ROOT"
rm -f "$ZIP_OUT" "$TAR_OUT"

EXCLUDES=(
  --exclude='./bin' --exclude='./bin/*'
  --exclude='./obj' --exclude='./obj/*'
  --exclude='./artifacts' --exclude='./artifacts/*'
  --exclude='./.tools' --exclude='./.tools/*'
  --exclude='./.public-video-cache' --exclude='./.public-video-cache/*'
  --exclude='./.git' --exclude='./.git/*'
  --exclude='./.vs' --exclude='./.vs/*'
)

tar -czf "$TAR_OUT" "${EXCLUDES[@]}" .
echo "Created $TAR_OUT"

if command -v zip >/dev/null 2>&1; then
  zip -qr "$ZIP_OUT" . -x 'bin/*' 'obj/*' 'artifacts/*' '.tools/*' '.public-video-cache/*' '.git/*' '.vs/*'
else
  ZIP_OUT="$ZIP_OUT" python3 - <<'PY2'
import os
import pathlib
import zipfile
root = pathlib.Path('.').resolve()
out = pathlib.Path(os.environ['ZIP_OUT']).resolve()
exclude = {'bin', 'obj', 'artifacts', '.tools', '.public-video-cache', '.git'}
with zipfile.ZipFile(out, 'w', compression=zipfile.ZIP_DEFLATED) as zf:
    for path in root.rglob('*'):
        rel = path.relative_to(root)
        parts = rel.parts
        if parts and parts[0] in exclude:
            continue
        if path.is_file():
            zf.write(path, rel.as_posix())
PY2
fi

echo "Created $ZIP_OUT"
