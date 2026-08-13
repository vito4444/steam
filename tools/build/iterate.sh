#!/usr/bin/env bash
# One iteration of the visual development loop: regenerate the scene from code,
# build the Linux player, run it headless, capture screenshots and metrics.
#
# Scene generation and the build share a single editor session because importing the
# asset database is the slow part and doing it once roughly halves the cycle.
set -uo pipefail

log() { echo "[iterate] $(date -u +%H:%M:%S) $*"; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="${UNITY:-/opt/unity/editors/6000.5.8f1/Editor/Unity}"
PROJECT="${REPO_ROOT}/game"
LOG="/tmp/iterate_unity.log"

[[ -x "${UNITY}" ]] || { log "FATAL: Unity not found at ${UNITY}"; exit 1; }

log "regenerating scene and building the Linux player"
START=$(date +%s)
xvfb-run -a "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${PROJECT}" \
  -executeMethod Monster.EditorTools.MonsterIterate.RebuildSceneAndBuildLinux \
  -logFile "${LOG}" >/dev/null 2>&1
STATUS=$?
log "editor exited with ${STATUS} after $(( $(date +%s) - START ))s"

grep -E '^\[(MonsterBuild|MonsterSetup|NightShift|MonsterIterate)\]' "${LOG}" | sed 's/^/  /'

if [[ ${STATUS} -ne 0 ]]; then
  log "build failed; compiler errors:"
  grep -E 'error CS[0-9]+' "${LOG}" | sort -u | head -20 | sed 's/^/  /'
  exit "${STATUS}"
fi

exec "${REPO_ROOT}/tools/selfcheck/run_selfcheck.sh"
