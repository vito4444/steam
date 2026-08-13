#!/usr/bin/env bash
# Unity 6 LTS headless install for Linux (no GPU, software rendering via Mesa llvmpipe)
set -uo pipefail

UNITY_VERSION="6000.0.81f1"
UNITY_REVISION="6238fec1e98f"
UNITY_ROOT="/opt/unity/${UNITY_VERSION}"
EDITOR_DIR="${UNITY_ROOT}/Editor"
DL_DIR="/tmp/unity-dl"

log() { echo "[$(date +%H:%M:%S)] $*"; }

mkdir -p "${DL_DIR}"
sudo mkdir -p "${EDITOR_DIR}"
sudo chown -R "$(id -u):$(id -g)" /opt/unity

log "=== STEP 1: system dependencies ==="
export DEBIAN_FRONTEND=noninteractive
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends \
  libgtk-3-0 libnss3 libasound2t64 libxtst6 libxss1 libglu1-mesa \
  libgbm1 libnotify4 libsecret-1-0 libcanberra-gtk3-module \
  mesa-utils libgl1-mesa-dri libegl1 libxcursor1 libxrandr2 libxi6 \
  libxinerama1 libfontconfig1 libfreetype6 xz-utils zip unzip \
  clang lld python3-pip imagemagick 2>&1 | tail -3
log "deps done (rc=$?)"

log "=== STEP 2: download editor (~1.5GB) ==="
EDITOR_URL="https://download.unity3d.com/download_unity/${UNITY_REVISION}/LinuxEditorInstaller/Unity-${UNITY_VERSION}.tar.xz"
if [ ! -f "${DL_DIR}/editor.tar.xz" ]; then
  curl -fL --retry 4 --retry-delay 5 -o "${DL_DIR}/editor.tar.xz" "${EDITOR_URL}" \
    -w "editor http=%{http_code} size=%{size_download}\n"
fi
ls -la "${DL_DIR}/editor.tar.xz"

log "=== STEP 3: extract editor ==="
tar -xf "${DL_DIR}/editor.tar.xz" -C "${UNITY_ROOT}"
ls "${EDITOR_DIR}" | head
"${EDITOR_DIR}/Unity" -version 2>/dev/null || true

log "=== STEP 4: windows-mono build module ==="
# API reports a Mac .pkg URL for this module; the Linux editor needs the
# LinuxEditorTargetInstaller tarball, so probe that path first.
for CANDIDATE in \
  "https://download.unity3d.com/download_unity/${UNITY_REVISION}/LinuxEditorTargetInstaller/UnitySetup-Windows-Mono-Support-for-Editor-${UNITY_VERSION}.tar.xz" \
  "https://download.unity3d.com/download_unity/${UNITY_REVISION}/LinuxEditorTargetInstaller/UnitySetup-Windows-Support-for-Editor-${UNITY_VERSION}.tar.xz"
do
  log "probing ${CANDIDATE}"
  CODE=$(curl -sIL -o /dev/null -w "%{http_code}" "${CANDIDATE}")
  log "  -> ${CODE}"
  if [ "${CODE}" = "200" ]; then
    curl -fL --retry 4 --retry-delay 5 -o "${DL_DIR}/win-mono.tar.xz" "${CANDIDATE}" \
      -w "module http=%{http_code} size=%{size_download}\n"
    tar -xf "${DL_DIR}/win-mono.tar.xz" -C "${EDITOR_DIR}"
    log "windows module extracted"
    break
  fi
done

log "=== STEP 5: verify ==="
ls "${EDITOR_DIR}/Data/PlaybackEngines/" 2>/dev/null || log "NO PlaybackEngines dir"
du -sh "${EDITOR_DIR}" 2>/dev/null
log "=== INSTALL SCRIPT COMPLETE ==="
