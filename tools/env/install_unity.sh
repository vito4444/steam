#!/usr/bin/env bash
# Installs Unity Hub and a Unity Editor with Windows (Mono) build support on a
# headless Linux machine. Safe to re-run: every step is idempotent.
set -uo pipefail

LOG_PREFIX="[install_unity]"
log() { echo "${LOG_PREFIX} $(date -u +%H:%M:%S) $*"; }

export DEBIAN_FRONTEND=noninteractive

# ---------------------------------------------------------------- Unity Hub --
if ! command -v unityhub >/dev/null 2>&1; then
  log "installing Unity Hub from the official apt repository"
  sudo install -d -m 0755 /usr/share/keyrings
  curl -fsSL https://hub.unity3d.com/linux/keys/public \
    | gpg --dearmor \
    | sudo tee /usr/share/keyrings/Unity_Technologies_ApS.gpg >/dev/null
  echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" \
    | sudo tee /etc/apt/sources.list.d/unityhub.list >/dev/null
  sudo apt-get update -qq
  sudo apt-get install -y -qq unityhub
  # Unity Hub is an Electron app; these are its runtime dependencies plus the
  # Mesa software rasteriser the editor falls back to when there is no GPU.
  # Installed one at a time so a package that was renamed or dropped in a newer
  # Ubuntu release cannot abort the whole batch.
  for pkg in libgtk-3-0t64 libnss3 libasound2t64 libgbm1 libxss1 libxtst6 \
             libcanberra-gtk-module xvfb mesa-utils libgl1-mesa-dri \
             libglu1-mesa libncurses6 libncursesw6 mesa-vulkan-drivers; do
    sudo apt-get install -y -qq "${pkg}" >/dev/null 2>&1 \
      || log "optional dependency unavailable, skipping: ${pkg}"
  done
else
  log "Unity Hub already present: $(command -v unityhub)"
fi

command -v unityhub >/dev/null 2>&1 || { log "FATAL: unityhub missing"; exit 1; }

# Hub is an Electron binary and still wants an X display even in headless mode.
hub() { xvfb-run -a unityhub --no-sandbox --headless "$@"; }

INSTALL_PATH="${UNITY_INSTALL_PATH:-/opt/unity/editors}"
sudo install -d -m 0777 "${INSTALL_PATH}"
hub install-path --set "${INSTALL_PATH}" 2>&1 | tail -2

# ------------------------------------------------------------ Editor choice --
# UNITY_VERSION/UNITY_CHANGESET can be pinned from the caller; otherwise pick the
# newest LTS the Hub advertises.
if [[ -z "${UNITY_VERSION:-}" ]]; then
  log "querying available editor releases"
  RELEASES_RAW="$(hub editors --releases 2>/dev/null)"
  echo "${RELEASES_RAW}" | tail -40
  UNITY_VERSION="$(echo "${RELEASES_RAW}" \
    | grep -oE '6000\.[0-9]+\.[0-9]+f1' | sort -V | tail -1)"
fi

if [[ -z "${UNITY_VERSION}" ]]; then
  log "FATAL: could not determine a Unity version to install"
  exit 1
fi
log "target editor: ${UNITY_VERSION}"

if [[ -x "${INSTALL_PATH}/${UNITY_VERSION}/Editor/Unity" ]]; then
  log "editor ${UNITY_VERSION} already installed"
else
  log "installing editor ${UNITY_VERSION} with Windows (Mono) build support"
  if [[ -n "${UNITY_CHANGESET:-}" ]]; then
    hub install --version "${UNITY_VERSION}" --changeset "${UNITY_CHANGESET}" \
      --module windows-mono --childModules 2>&1 | tail -30
  else
    hub install --version "${UNITY_VERSION}" \
      --module windows-mono --childModules 2>&1 | tail -30
  fi
fi

EDITOR="${INSTALL_PATH}/${UNITY_VERSION}/Editor/Unity"
if [[ -x "${EDITOR}" ]]; then
  log "SUCCESS: editor at ${EDITOR}"
  "${EDITOR}" -version 2>/dev/null | tail -1
  echo "${UNITY_VERSION}" > /tmp/unity_version.txt
  echo "${EDITOR}" > /tmp/unity_editor_path.txt
else
  log "FAILURE: editor binary not found at ${EDITOR}"
  ls -la "${INSTALL_PATH}" 2>/dev/null
  exit 1
fi
