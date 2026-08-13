#!/usr/bin/env bash
#
# Builds the Unity player and runs it unattended, capturing screenshots from the real
# renderer.
#
# This is the other half of the self-test. Tools/selftest.sh needs nothing but the .NET
# SDK and draws the world with its own rasteriser, which makes it fast and always
# available; this script is slower and needs a licensed Editor, but it is the only thing
# that proves what the shipping renderer actually puts on screen.
#
# Usage:
#   Tools/unity-selftest.sh [scenario] [output-tag]
#
#     scenario  starter | automated   (default: automated)
#
# Requires:
#   UNITY_EDITOR  path to the Unity executable
#                 (default: $HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Unity)
#   An X server. One is started on DISPLAY_NUM if nothing is listening there.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

SCENARIO="${1:-automated}"
TAG="${2:-$(git rev-parse --short HEAD 2>/dev/null || echo local)}"

UNITY_EDITOR="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Unity}"
DISPLAY_NUM="${DISPLAY_NUM:-:99}"
SCREEN_WIDTH="${SCREEN_WIDTH:-1600}"
SCREEN_HEIGHT="${SCREEN_HEIGHT:-900}"

CAPTURE_COUNT="${CAPTURE_COUNT:-6}"
CAPTURE_INTERVAL="${CAPTURE_INTERVAL:-4}"
RUN_SECONDS="${RUN_SECONDS:-40}"
SPEED="${SPEED:-8}"
WORKERS="${WORKERS:-6}"
SEED="${SEED:-7}"

OUT_DIR="Artifacts/unity-selftest/${TAG}/${SCENARIO}"
BUILD_DIR="Artifacts/build/Linux64-selftest"

if [[ ! -x "$UNITY_EDITOR" ]]; then
  echo "Unity editor not found at $UNITY_EDITOR"
  echo "Set UNITY_EDITOR to the Unity executable."
  exit 2
fi

echo "=== unity self-test: ${SCENARIO} @ ${TAG} ==="

echo
echo "--- build ---"
"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$REPO_ROOT" \
  -buildTarget Linux64 \
  -executeMethod Worker.Editor.BuildPipelineEntry.BuildLinux64SelfTest \
  -logFile /tmp/worker-unity-build.log

echo "  built $(du -sh "$BUILD_DIR" | cut -f1)"

# Bring up a virtual display only if one is not already running. The player needs a
# real GL context even though nobody is watching; llvmpipe provides it in software.
if ! DISPLAY="$DISPLAY_NUM" xdpyinfo >/dev/null 2>&1; then
  echo
  echo "--- starting Xvfb on ${DISPLAY_NUM} ---"
  Xvfb "$DISPLAY_NUM" -screen 0 "${SCREEN_WIDTH}x$((SCREEN_HEIGHT + 100))x24" -nolisten tcp &
  XVFB_PID=$!
  trap 'kill $XVFB_PID 2>/dev/null || true' EXIT
  sleep 3
fi

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

echo
echo "--- capture ---"

SCENARIO_FLAG=""
if [[ "$SCENARIO" == "automated" ]]; then
  SCENARIO_FLAG="--automated"
fi

(
  cd "$BUILD_DIR"
  DISPLAY="$DISPLAY_NUM" \
  LIBGL_ALWAYS_SOFTWARE=1 \
  GALLIUM_DRIVER=llvmpipe \
  ./Worker.x86_64 \
    -screen-width "$SCREEN_WIDTH" -screen-height "$SCREEN_HEIGHT" -screen-fullscreen 0 \
    -force-glcore \
    $SCENARIO_FLAG --seed "$SEED" --workers "$WORKERS" --speed "$SPEED" --hud \
    --capture-dir "$REPO_ROOT/$OUT_DIR" \
    --capture-interval "$CAPTURE_INTERVAL" \
    --capture-count "$CAPTURE_COUNT" \
    --run-seconds "$RUN_SECONDS" \
    -logFile /tmp/worker-unity-player.log
)

echo
echo "--- results ---"

METRICS="${OUT_DIR}/unity-metrics.json"
if [[ ! -f "$METRICS" ]]; then
  echo "FAIL: the player exited without writing metrics"
  tail -30 /tmp/worker-unity-player.log || true
  exit 1
fi

cat "$METRICS"

shots=$(find "$OUT_DIR" -name '*.png' | wc -l)
tick=$(grep -o '"tick": [0-9]*' "$METRICS" | grep -o '[0-9]*$')
crafts=$(grep -o '"craftsCompleted": [0-9]*' "$METRICS" | grep -o '[0-9]*$')

echo
echo "  screenshots: ${shots}"

fail=0

if (( shots < CAPTURE_COUNT )); then
  echo "FAIL: expected ${CAPTURE_COUNT} screenshots, got ${shots}"
  fail=1
fi

# A player whose simulation never advanced would still produce screenshots, and they
# would all be identical. This is exactly the runInBackground bug that focus loss caused
# under a headless X server, so it is worth asserting explicitly.
if (( tick < 100 )); then
  echo "FAIL: simulation only reached tick ${tick}; the player is probably paused"
  fail=1
fi

if (( crafts == 0 )); then
  echo "FAIL: nothing was produced, the factory is stalled"
  fail=1
fi

if (( fail != 0 )); then
  echo
  echo "UNITY SELF-TEST FAILED"
  exit 1
fi

echo
echo "UNITY SELF-TEST PASSED"
