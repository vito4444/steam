#!/usr/bin/env bash
# 项目 maner 的公共构建环境变量。其他脚本通过 source 引入。
#
# 这里刻意不设置 set -e：本文件也会被交互式 shell 直接 source，
# 在调用者身上打开 errexit 会让后续任何一条返回非零的命令（例如没有命中的 grep）
# 直接终止整串命令。需要严格模式的脚本自己在开头声明。

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
