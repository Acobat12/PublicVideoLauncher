#!/usr/bin/env bash
set -euo pipefail

OUTPUT_FILE="${1:-artifacts/SHA256SUMS.txt}"
shift || true

if [ "$#" -eq 0 ]; then
  TARGETS=(artifacts/releases artifacts/source)
else
  TARGETS=("$@")
fi

mkdir -p "$(dirname "$OUTPUT_FILE")"
: > "$OUTPUT_FILE"

hash_file() {
  local file="$1"
  local rel
  rel="${file#./}"

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk -v rel="$rel" '{print tolower($1) "  " rel}' >> "$OUTPUT_FILE"
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk -v rel="$rel" '{print tolower($1) "  " rel}' >> "$OUTPUT_FILE"
  else
    python3 - "$file" "$rel" "$OUTPUT_FILE" <<'PY2'
import hashlib, pathlib, sys
file_path = pathlib.Path(sys.argv[1])
rel = sys.argv[2]
out = pathlib.Path(sys.argv[3])
digest = hashlib.sha256(file_path.read_bytes()).hexdigest()
with out.open('a', encoding='utf-8') as f:
    f.write(f"{digest}  {rel}\n")
PY2
  fi
}

for target in "${TARGETS[@]}"; do
  [ -e "$target" ] || continue
  while IFS= read -r -d '' file; do
    case "$(basename "$file")" in
      SHA256SUMS.txt) continue ;;
    esac
    hash_file "$file"
  done < <(find "$target" -type f -print0 | sort -z)
done

echo "Wrote $OUTPUT_FILE"