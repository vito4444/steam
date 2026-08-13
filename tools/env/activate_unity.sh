#!/usr/bin/env bash
# Activates a Unity Personal licence on a headless machine.
#
# Credentials are read from the environment only (UNITY_EMAIL / UNITY_PASSWORD)
# and are never written into the repository. On CI, supply them as secrets.
set -uo pipefail

log() { echo "[activate_unity] $(date -u +%H:%M:%S) $*"; }

UNITY_VERSION="${UNITY_VERSION:-$(cat /tmp/unity_version.txt 2>/dev/null)}"
EDITOR="${UNITY_EDITOR:-/opt/unity/editors/${UNITY_VERSION}/Editor/Unity}"
LICENSE_CLIENT="$(dirname "${EDITOR}")/Data/Resources/Licensing/Client/Unity.Licensing.Client"

[[ -x "${EDITOR}" ]] || { log "FATAL: editor not found at ${EDITOR}"; exit 1; }
[[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]] \
  || { log "FATAL: UNITY_EMAIL / UNITY_PASSWORD not set"; exit 1; }

# Unity writes its licence to a root-owned system directory.
sudo install -d -m 0777 /usr/share/unity3d/Unity 2>/dev/null || true
sudo install -d -m 0777 "${HOME}/.local/share/unity3d/Unity" 2>/dev/null || true

show_state() {
  if [[ -x "${LICENSE_CLIENT}" ]]; then
    "${LICENSE_CLIENT}" --showEntitlements 2>&1 | grep -Ev '^\s*$' | tail -12
  fi
}

log "current entitlements before activation:"
show_state

if [[ -x "${LICENSE_CLIENT}" ]]; then
  log "activating via Unity.Licensing.Client"
  "${LICENSE_CLIENT}" --activate-ulf \
    --username "${UNITY_EMAIL}" --password "${UNITY_PASSWORD}" 2>&1 | tail -20
else
  log "licensing client absent, falling back to the editor's batch activation"
  xvfb-run -a "${EDITOR}" -batchmode -nographics -quit -logFile /dev/stdout \
    -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" 2>&1 | tail -30
fi

log "entitlements after activation:"
show_state

# A Personal entitlement makes the string "Unity Personal" appear in the report.
if show_state | grep -qi "personal\|plus\|pro"; then
  log "SUCCESS: an editor entitlement is present"
else
  log "WARNING: no entitlement detected; batch builds will fail until one exists"
  exit 2
fi
