#!/usr/bin/env bash
# Verifies the full headless pipeline on this machine:
#   URP scene generation -> offscreen capture under Xvfb -> Windows x64 player build.
#
# Usage: tools/probe/run_probe.sh [project_dir]
set -uo pipefail

UNITY=/opt/unity/6000.0.81f1/Editor/Unity
PROJECT="${1:-/tmp/HunterProbe}"
export DISPLAY="${DISPLAY:-:1}"
export PROBE_OUT="${PROBE_OUT:-/tmp/probe-out}"
export PROBE_BUILD="${PROBE_BUILD:-/tmp/probe-build}"
export PROBE_TAG="${PROBE_TAG:-probe}"

log() { echo "[$(date +%H:%M:%S)] $*"; }

if ! xdpyinfo -display "$DISPLAY" > /dev/null 2>&1; then
  log "starting Xvfb on $DISPLAY"
  Xvfb "$DISPLAY" -screen 0 1920x1080x24 > /tmp/xvfb.log 2>&1 &
  sleep 3
fi

if [ ! -d "$PROJECT" ]; then
  log "creating project at $PROJECT"
  "$UNITY" -batchmode -nographics -quit -createProject "$PROJECT" -logFile /tmp/probe_create.log
  python3 - "$PROJECT" <<'PY'
import json, sys
p = sys.argv[1] + '/Packages/manifest.json'
m = json.load(open(p))
m['dependencies']['com.unity.render-pipelines.universal'] = '17.0.4'
json.dump(m, open(p, 'w'), indent=2)
PY
  mkdir -p "$PROJECT/Assets/Editor" "$PROJECT/Assets/Scenes" "$PROJECT/Assets/Settings"
  cp "$(dirname "$0")/Probe.cs" "$PROJECT/Assets/Editor/"
fi

run_step() {
  local method="$1" logfile="$2"
  log "running $method"
  timeout 1800 "$UNITY" -batchmode -quit -projectPath "$PROJECT" \
    -buildTarget StandaloneWindows64 -executeMethod "$method" -logFile "$logfile" > /dev/null 2>&1
  local rc=$?
  grep -a -E "PROBE_[A-Z_]+" "$logfile" | tail -3
  log "$method exit=$rc"
  return $rc
}

run_step Probe.Setup        /tmp/probe_setup.log
run_step Probe.Capture      /tmp/probe_capture.log
run_step Probe.BuildWindows /tmp/probe_build.log

log "=== artifacts ==="
ls -la "$PROBE_OUT" 2>/dev/null
file "$PROBE_BUILD"/*.exe 2>/dev/null
