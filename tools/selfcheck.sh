#!/usr/bin/env bash
# 在虚拟显示环境中运行 Linux 构建，按时间点自动截图并输出帧时间统计。
#
#   tools/selfcheck.sh                        使用默认截图时间点
#   tools/selfcheck.sh 1.0,3.0,6.0            自定义截图时间点（秒）
#   MANER_ORBIT=0 tools/selfcheck.sh          关闭相机环绕
#   MANER_RES=2560x1440 tools/selfcheck.sh    自定义分辨率

source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

SHOTS="${1:-1.0,2.5,4.0}"
RES="${MANER_RES:-1920x1080}"
WIDTH="${RES%x*}"
HEIGHT="${RES#*x}"
ORBIT="${MANER_ORBIT:-1}"
TIMEOUT_SECONDS="${MANER_TIMEOUT:-180}"
PLAYER="$MANER_BUILD_DIR/linux/MANER"
LOG="$MANER_LOG_DIR/selfcheck.log"

if [[ ! -x "$PLAYER" ]]; then
  echo "找不到 Linux 构建: $PLAYER" >&2
  echo "先运行: tools/build.sh linux" >&2
  exit 1
fi

rm -rf "$MANER_SHOT_DIR"
mkdir -p "$MANER_SHOT_DIR"

orbit_flag=()
[[ "$ORBIT" == "1" ]] && orbit_flag=(-manerOrbit)

echo "== 运行自检 分辨率=$RES 截图点=$SHOTS 环绕=$ORBIT"
set +e
timeout "$TIMEOUT_SECONDS" xvfb-run -a -s "-screen 0 ${WIDTH}x${HEIGHT}x24" \
  "$PLAYER" \
  -screen-width "$WIDTH" -screen-height "$HEIGHT" -screen-fullscreen 0 \
  -manerShots "$SHOTS" -manerOut "$MANER_SHOT_DIR" "${orbit_flag[@]}" \
  -logFile "$LOG" >/dev/null 2>&1
code=$?
set -e

grep -E '^\[SelfCheck\]' "$LOG" || true

if [[ $code -eq 124 ]]; then
  echo "运行超时（${TIMEOUT_SECONDS}s），日志: $LOG" >&2
  exit 124
fi

shot_count=$(find "$MANER_SHOT_DIR" -name '*.png' | wc -l)
expected=$(awk -F, '{print NF}' <<<"$SHOTS")

echo "截图 $shot_count / $expected 张，输出目录 $MANER_SHOT_DIR"
if [[ "$shot_count" -lt "$expected" ]]; then
  echo "截图数量不足，日志: $LOG" >&2
  exit 1
fi
