#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
PROJECT="${PROJECT:-PublicVideoLauncher.csproj}"
OUTPUT_ROOT="${OUTPUT_ROOT:-artifacts/publish}"
SELF_CONTAINED="${SELF_CONTAINED:-false}"
SINGLE_FILE="${SINGLE_FILE:-false}"
VERSION="${VERSION:-1.0.0}"

if [ "$#" -eq 0 ]; then
  RUNTIMES=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)
else
  RUNTIMES=("$@")
fi

for RID in "${RUNTIMES[@]}"; do
  OUTPUT="$OUTPUT_ROOT/$RID"
  ARGS=(publish "$PROJECT" -c "$CONFIGURATION" -r "$RID" -o "$OUTPUT" --self-contained "$SELF_CONTAINED" -p:Version="$VERSION")

  if [ "$SINGLE_FILE" = "true" ]; then
    ARGS+=("-p:PublishSingleFile=true")
  fi

  echo "Publishing $RID -> $OUTPUT"
  dotnet "${ARGS[@]}"
done
