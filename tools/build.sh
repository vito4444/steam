#!/usr/bin/env bash
# Builds the Undertown player. Usage: tools/build.sh [windows|linux|both]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

TARGET="${1:-both}"
STAMP="$(date +%Y%m%d-%H%M%S)"

build_one() {
  local method="$1" name="$2"
  local logfile="$LOG_DIR/build-$name-$STAMP.log"
  echo "[build] $name -> $logfile"
  set +e
  "$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT_PATH" \
    -executeMethod "Undertown.EditorTools.BuildScript.$method" \
    -logFile "$logfile"
  local rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "[build] FAILED rc=$rc"
    grep -nE "error CS|Error building|Exception|BuildFailedException" "$logfile" | head -40 || true
    unity_log_tail "$logfile" 40
    return $rc
  fi
  echo "[build] $name ok"
}

case "$TARGET" in
  windows) build_one BuildWindows windows ;;
  linux)   build_one BuildLinux   linux   ;;
  both)    build_one BuildLinux linux; build_one BuildWindows windows ;;
  *) echo "usage: $0 [windows|linux|both]" >&2; exit 2 ;;
esac

echo "----- artifacts -----"
find "$REPO_ROOT/build" -maxdepth 2 -type f \( -name '*.exe' -o -name '*.x86_64' \) -print0 2>/dev/null |
  while IFS= read -r -d '' f; do printf '%s\n  %s\n' "$f" "$(file -b "$f")"; done
