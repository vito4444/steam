#!/usr/bin/env bash
#
# Drives the running game through a scripted interaction and captures the result.
#
# The automated capture in HeadlessCapture proves the renderer works, but it can only
# photograph a factory running on its own. This script uses xdotool to actually click
# things, which is the only way to get a screenshot of the interface responding: a
# selected worker, a placement preview, a demolition target.
#
# Usage:
#   Tools/capture-demo.sh <output-dir> [x y] [x y] ...
#
# Each coordinate pair is clicked and photographed in turn.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

OUT_DIR="${1:-Artifacts/demo}"
shift || true

export DISPLAY="${DISPLAY_NUM:-:99}"
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe

BUILD_DIR="Artifacts/build/Linux64-selftest"
mkdir -p "$OUT_DIR"

pkill -f "Worker.x86_64" 2>/dev/null || true
sleep 2

echo "starting player"
(
  cd "$BUILD_DIR"
  ./Worker.x86_64 -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -force-glcore \
    --automated --seed 7 --workers 6 --speed 6 \
    -logFile /tmp/worker-demo-player.log &
  echo $! > /tmp/worker-demo.pid
)

# Let the factory build up some state before photographing it; an empty factory is a
# poor advertisement for a factory game.
echo "warming up"
sleep 40

WINDOW=$(xdotool search --name "^Worker$" | head -1)
if [[ -z "$WINDOW" ]]; then
  echo "player window never appeared"
  tail -20 /tmp/worker-demo-player.log || true
  exit 1
fi

echo "window ${WINDOW}"
xdotool windowactivate "$WINDOW" 2>/dev/null || true
xdotool windowfocus "$WINDOW" 2>/dev/null || true
sleep 2

# Pause so that clicking hits what the screenshot shows: at 6x speed a worker moves
# several tiles between the click and the capture.
xdotool key --window "$WINDOW" space
sleep 1

shot() {
  local name="$1"
  sleep 1
  scrot -o "${OUT_DIR}/${name}.png"
  echo "  captured ${name}"
}

shot "01-paused"

index=2
while [[ $# -ge 2 ]]; do
  x="$1"; y="$2"; shift 2
  echo "clicking ${x},${y}"
  xdotool mousemove "$x" "$y"
  sleep 1
  xdotool click 1
  shot "$(printf '%02d' $index)-click-${x}-${y}"
  index=$((index + 1))
done

# Build mode last, so the placement preview is the final state on screen. Hotkey 3 is
# the sawbench: a 2x2 footprint shows the legality tint better than a single belt tile.
echo "build preview"
xdotool key --window "$WINDOW" 3
sleep 1
xdotool mousemove 1000 700
shot "$(printf '%02d' $index)-build-preview-valid"
index=$((index + 1))

# Over an occupied tile the same preview must turn red rather than silently refusing
# the click later.
xdotool mousemove 516 259
shot "$(printf '%02d' $index)-build-preview-blocked"

echo "done, output in ${OUT_DIR}"
