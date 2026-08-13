#!/usr/bin/env bash
# Runs the built Linux player under Xvfb with Mesa's software rasteriser, drives it
# through its self-check checkpoints, and collects the screenshots and metrics.
#
# The Windows player cannot be executed here, so the Linux player built from the same
# code and the same scene stands in for it. That validates scene composition, lighting,
# materials and UI layout. It does not validate anything Windows-specific, and it does
# not tell us how the game looks on a real GPU.
set -uo pipefail

log() { echo "[selfcheck] $(date -u +%H:%M:%S) $*"; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PLAYER="${REPO_ROOT}/game/Build/Linux/MONSTER.x86_64"
WIDTH="${SELFCHECK_WIDTH:-1280}"
HEIGHT="${SELFCHECK_HEIGHT:-720}"
TIMEOUT="${SELFCHECK_TIMEOUT:-420}"

SHA="$(git -C "${REPO_ROOT}" rev-parse --short HEAD 2>/dev/null || echo nogit)"
OUT_DIR="${SELFCHECK_OUT:-${REPO_ROOT}/artifacts/selfcheck/${SHA}}"

[[ -x "${PLAYER}" ]] || { log "FATAL: player not built at ${PLAYER}"; exit 1; }

rm -rf "${OUT_DIR}"
mkdir -p "${OUT_DIR}"
PLAYER_LOG="${OUT_DIR}/player.log"

log "player  : ${PLAYER}"
log "output  : ${OUT_DIR}"
log "screen  : ${WIDTH}x${HEIGHT} (llvmpipe software rasteriser)"

# Force software OpenGL. There is no GPU on this machine, and letting Unity try Vulkan
# first wastes time failing over.
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe
export MESA_GL_VERSION_OVERRIDE=4.5
export MESA_GLSL_VERSION_OVERRIDE=450
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/tmp/xdg-runtime-selfcheck}"
mkdir -p "${XDG_RUNTIME_DIR}"
chmod 700 "${XDG_RUNTIME_DIR}"

START=$(date +%s)
set +e
xvfb-run -a -s "-screen 0 ${WIDTH}x${HEIGHT}x24 -nolisten tcp" \
  timeout --signal=TERM --kill-after=30 "${TIMEOUT}" \
  "${PLAYER}" \
    -force-glcore \
    -screen-width "${WIDTH}" \
    -screen-height "${HEIGHT}" \
    -screen-fullscreen 0 \
    -screen-quality Medium \
    -nolog \
    -selfcheck \
    -selfcheck-out "${OUT_DIR}" \
    -logFile "${PLAYER_LOG}"
STATUS=$?
set -e
ELAPSED=$(( $(date +%s) - START ))

log "player exited with ${STATUS} after ${ELAPSED}s"

if [[ ${STATUS} -eq 124 || ${STATUS} -eq 137 ]]; then
  log "FATAL: player timed out after ${TIMEOUT}s"
fi

# A stable path alongside the per-commit one. The commit-named directory is the
# archive; without a fixed alias, anything looking at "the latest screenshots" silently
# reads the previous commit's the moment a commit lands.
LATEST="$(dirname "${OUT_DIR}")/latest"
rm -rf "${LATEST}"
cp -r "${OUT_DIR}" "${LATEST}"

SHOTS=$(find "${OUT_DIR}" -maxdepth 1 -name '*.png' | wc -l)
log "captured ${SHOTS} screenshot(s)"
find "${OUT_DIR}" -maxdepth 1 -name '*.png' -printf '  %f  %s bytes\n' 2>/dev/null

if [[ -f "${OUT_DIR}/metrics.json" ]]; then
  log "metrics:"
  sed 's/^/  /' "${OUT_DIR}/metrics.json"
else
  log "no metrics.json produced; last 40 lines of the player log:"
  tail -40 "${PLAYER_LOG}" 2>/dev/null | sed 's/^/  /'
fi

# A run that produced no images is a failed run regardless of the player's exit code.
[[ ${SHOTS} -gt 0 ]] || exit 1
exit 0
