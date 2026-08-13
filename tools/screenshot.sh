#!/usr/bin/env bash
# Boots the Linux player on a virtual display and captures gameplay frames, so the
# visual state can be inspected without a human at a monitor.
#
# Usage: tools/screenshot.sh [label] [seconds-to-run]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

LABEL="${1:-shot}"
RUNTIME="${2:-25}"
WIDTH=1920
HEIGHT=1080
SHOT_DIR="$ARTIFACT_DIR/screenshots"
PLAYER="$REPO_ROOT/build/linux/Undertown.x86_64"
XDISPLAY=":77"

mkdir -p "$SHOT_DIR"
[[ -x "$PLAYER" ]] || { echo "player not built: $PLAYER (run tools/build.sh linux)" >&2; exit 1; }

cleanup() {
  [[ -n "${PLAYER_PID:-}" ]] && kill "$PLAYER_PID" 2>/dev/null || true
  [[ -n "${XVFB_PID:-}"   ]] && kill "$XVFB_PID"   2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT

Xvfb "$XDISPLAY" -screen 0 "${WIDTH}x${HEIGHT}x24" -nolisten tcp >/dev/null 2>&1 &
XVFB_PID=$!
sleep 2

PLAYER_LOG="$LOG_DIR/player-$LABEL.log"
DISPLAY="$XDISPLAY" "$PLAYER" \
  -screen-fullscreen 0 -screen-width "$WIDTH" -screen-height "$HEIGHT" \
  -logFile "$PLAYER_LOG" &
PLAYER_PID=$!

# The player needs a few seconds to create its window and finish the first frame.
sleep 8
for i in $(seq 1 "$(( RUNTIME / 5 ))"); do
  out="$SHOT_DIR/${LABEL}-$(printf '%02d' "$i").png"
  DISPLAY="$XDISPLAY" import -window root -silent "$out" 2>/dev/null || \
    DISPLAY="$XDISPLAY" xwd -root -silent | convert xwd:- "$out"
  echo "[shot] $out"
  sleep 5
done

echo "----- player log tail -----"
tail -n 25 "$PLAYER_LOG" 2>/dev/null || true
echo "----- captures -----"
ls -la "$SHOT_DIR"
