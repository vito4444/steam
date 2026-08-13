#!/usr/bin/env bash
# Shared environment for every Unity automation script in this repo.
UNITY_VERSION="${UNITY_VERSION:-6000.3.22f1}"
UNITY_CHANGESET="${UNITY_CHANGESET:-1c726e1fb402}"
EDITOR_ROOT="${EDITOR_ROOT:-$HOME/Unity/Hub/Editor/$UNITY_VERSION}"
UNITY="${UNITY:-$EDITOR_ROOT/Editor/Unity}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="${PROJECT_PATH:-$REPO_ROOT/Undertown}"
ARTIFACT_DIR="${ARTIFACT_DIR:-$REPO_ROOT/artifacts}"
LOG_DIR="${LOG_DIR:-$ARTIFACT_DIR/logs}"

mkdir -p "$LOG_DIR"

# Unity's headless invocations still expect an X display to exist for some
# subsystems even under -nographics, and the graphical smoke tests need a real one.
export DISPLAY="${DISPLAY:-:1}"

unity_log_tail() {
  local logfile="$1" lines="${2:-40}"
  if [[ -f "$logfile" ]]; then
    echo "----- tail of $logfile -----"
    tail -n "$lines" "$logfile"
  fi
}
