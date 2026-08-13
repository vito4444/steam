#!/usr/bin/env bash
# Regenerates the scene from source and renders the fixed comparison angles.
#
# Usage: tools/forge_and_shoot.sh <tag> [shot_name]
#   tag       label for the output files, e.g. v3
#   shot_name optional single angle; omit to render all of them
set -uo pipefail

UNITY=/opt/unity/6000.0.81f1/Editor/Unity
PROJECT=/workspace/HunterGame
TAG="${1:-dev}"
SHOT="${2:-}"

export DISPLAY="${DISPLAY:-:1}"
export HUNTER_SHOTS="${HUNTER_SHOTS:-/tmp/hunter-shots}"
export HUNTER_TAG="$TAG"
export HUNTER_SS="${HUNTER_SS:-1}"
[ -n "$SHOT" ] && export HUNTER_SHOT_ONLY="$SHOT"

log() { echo "[$(date +%H:%M:%S)] $*"; }

if ! xdpyinfo -display "$DISPLAY" > /dev/null 2>&1; then
  log "starting Xvfb on $DISPLAY"
  Xvfb "$DISPLAY" -screen 0 1920x1080x24 > /tmp/xvfb.log 2>&1 &
  sleep 3
fi

log "forging scene"
timeout 1800 "$UNITY" -batchmode -quit -projectPath "$PROJECT" -buildTarget StandaloneWindows64 \
  -executeMethod Hunter.EditorTools.SceneForge.ForgeEverything -logFile /tmp/forge.log > /dev/null 2>&1
FORGE_RC=$?
grep -a -E "error CS|FORGE_OK|FORGE_WARN|Shader error" /tmp/forge.log | head -20
if [ $FORGE_RC -ne 0 ]; then log "FORGE FAILED rc=$FORGE_RC"; exit $FORGE_RC; fi

log "capturing shots"
timeout 2400 "$UNITY" -batchmode -quit -projectPath "$PROJECT" -buildTarget StandaloneWindows64 \
  -executeMethod Hunter.EditorTools.ShotDirector.CaptureShots -logFile /tmp/shot.log > /dev/null 2>&1
SHOT_RC=$?
grep -a -E "SHOT_|Shader error|error CS" /tmp/shot.log | head -20
log "capture rc=$SHOT_RC"

ls -la "$HUNTER_SHOTS" 2>/dev/null | grep "$TAG"
