#!/usr/bin/env bash
# Builds the shippable players: Windows x64, which is the deliverable, and Linux x64,
# which is what the self-check runs on this machine.
#
# Windows is cross-compiled from the Linux editor, which restricts it to the Mono
# scripting backend -- IL2CPP for a Windows target needs a Windows host. MonsterBuild
# forces Mono rather than letting the build fail late with a confusing error.
#
# Unity's batch mode exits 0 even when a build fails, so MonsterBuild calls
# EditorApplication.Exit(1) itself. Trusting the exit code here is only safe because of
# that.
set -uo pipefail

log() { echo "[build] $(date -u +%H:%M:%S) $*"; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="${UNITY:-/opt/unity/editors/6000.5.8f1/Editor/Unity}"
PROJECT="${REPO_ROOT}/game"
LOG="/tmp/build_unity.log"

[[ -x "${UNITY}" ]] || { log "FATAL: Unity not found at ${UNITY}"; exit 1; }

METHOD="${1:-Monster.EditorTools.MonsterBuild.BuildAll}"

log "building via ${METHOD}"
START=$(date +%s)
xvfb-run -a "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${PROJECT}" \
  -executeMethod "${METHOD}" \
  -logFile "${LOG}" >/dev/null 2>&1
STATUS=$?
log "editor exited with ${STATUS} after $(( $(date +%s) - START ))s"

grep -E '^\[(MonsterBuild|MonsterSetup|NightShift)\]' "${LOG}" | sed 's/^/  /'

if [[ ${STATUS} -ne 0 ]]; then
  log "build failed; compiler errors:"
  grep -E 'error CS[0-9]+' "${LOG}" | sort -u | head -20 | sed 's/^/  /'
  exit "${STATUS}"
fi

for player in "${PROJECT}/Build/Windows/MONSTER.exe" "${PROJECT}/Build/Linux/MONSTER.x86_64"; do
  if [[ -f "${player}" ]]; then
    log "$(du -sh "$(dirname "${player}")" | cut -f1)  ${player#"${REPO_ROOT}/"}"
  else
    log "MISSING: ${player#"${REPO_ROOT}/"}"
    STATUS=1
  fi
done

exit "${STATUS}"
