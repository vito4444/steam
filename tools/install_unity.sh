#!/usr/bin/env bash
# Installs Unity 6.3 LTS editor plus the Windows (Mono) build module on a headless Linux host.
# Idempotent: skips steps whose output already exists.
set -euo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.22f1}"
UNITY_CHANGESET="${UNITY_CHANGESET:-1c726e1fb402}"
DL_DIR="${DL_DIR:-$HOME/unitydl}"
EDITOR_ROOT="${EDITOR_ROOT:-$HOME/Unity/Hub/Editor/$UNITY_VERSION}"
UNITY_BIN="$EDITOR_ROOT/Editor/Unity"

log() { printf '\033[1;36m[install-unity]\033[0m %s\n' "$*"; }

if [[ ! -x "$UNITY_BIN" ]]; then
  log "extracting editor to $EDITOR_ROOT"
  mkdir -p "$EDITOR_ROOT"
  tar -xJf "$DL_DIR/Unity-$UNITY_VERSION.tar.xz" -C "$EDITOR_ROOT"
else
  log "editor already present"
fi

WIN_SUPPORT="$EDITOR_ROOT/Editor/Data/PlaybackEngines/WindowsStandaloneSupport"
if [[ ! -d "$WIN_SUPPORT" ]]; then
  log "installing windows-mono module"
  # Unity ships the Windows player support for Linux hosts as an Apple .pkg (xar archive).
  WORK="$DL_DIR/winmono_extract"
  rm -rf "$WORK" && mkdir -p "$WORK"
  ( cd "$WORK" && 7z x -y "$DL_DIR/WinMono.pkg" >/dev/null )
  PAYLOAD="$(find "$WORK" -name 'Payload*' -type f | head -1)"
  [[ -n "$PAYLOAD" ]] || { echo "payload not found inside pkg" >&2; exit 1; }
  mkdir -p "$WORK/payload" && ( cd "$WORK/payload" && 7z x -y "$PAYLOAD" -so 2>/dev/null | cpio -idm --quiet )
  SRC="$(find "$WORK/payload" -maxdepth 6 -type d -name 'WindowsStandaloneSupport' | head -1)"
  [[ -n "$SRC" ]] || { echo "WindowsStandaloneSupport not found in payload" >&2; find "$WORK/payload" -maxdepth 4 -type d >&2; exit 1; }
  mkdir -p "$(dirname "$WIN_SUPPORT")"
  cp -a "$SRC" "$WIN_SUPPORT"
  rm -rf "$WORK"
else
  log "windows-mono module already present"
fi

log "editor:  $UNITY_BIN"
log "modules: $(ls "$EDITOR_ROOT/Editor/Data/PlaybackEngines" 2>/dev/null | tr '\n' ' ')"
"$UNITY_BIN" -version 2>/dev/null || true
