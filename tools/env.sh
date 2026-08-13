#!/usr/bin/env bash
# 项目 maner 的公共构建环境变量。其他脚本通过 source 引入。

set -euo pipefail

MANER_UNITY_VERSION="${MANER_UNITY_VERSION:-6000.3.22f1}"
MANER_UNITY="${MANER_UNITY:-$HOME/Unity/Hub/Editor/$MANER_UNITY_VERSION/Editor/Unity}"
MANER_ROOT="${MANER_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
MANER_PROJECT="$MANER_ROOT/game"
MANER_BUILD_DIR="$MANER_ROOT/build"
MANER_SHOT_DIR="$MANER_ROOT/screenshots"
MANER_LOG_DIR="$MANER_ROOT/artifacts/logs"

export MANER_UNITY_VERSION MANER_UNITY MANER_ROOT MANER_PROJECT MANER_BUILD_DIR MANER_SHOT_DIR MANER_LOG_DIR

mkdir -p "$MANER_LOG_DIR"

if [[ ! -x "$MANER_UNITY" ]]; then
  echo "找不到 Unity 编辑器: $MANER_UNITY" >&2
  echo "安装命令: xvfb-run -a unityhub --headless install --version $MANER_UNITY_VERSION --module windows-mono --childModules" >&2
  exit 1
fi

# Unity 在无显示环境下必须包一层虚拟显示，即使 -nographics 也会用到 X 相关初始化。
maner_unity() {
  xvfb-run -a "$MANER_UNITY" -batchmode -nographics -projectPath "$MANER_PROJECT" "$@"
}
