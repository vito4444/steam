#!/usr/bin/env bash
# 无头截图。渲染场景里所有 CaptureShot 机位到 artifacts/screenshots/。
# 必须走 Xvfb（不能用 -nographics），否则 Unity 建立不了渲染上下文。
#
# 用法: tools/capture.sh [场景路径] [输出目录] [宽] [高]
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

SCENE="${1:-Assets/Scenes/ArtProbe.unity}"
OUT_DIR="${2:-${ARTIFACTS}/screenshots}"
WIDTH="${3:-1920}"
HEIGHT="${4:-1080}"
LOG="${LOG_DIR}/capture.log"

mkdir -p "${OUT_DIR}"
echo "截图 场景=${SCENE} 输出=${OUT_DIR} 分辨率=${WIDTH}x${HEIGHT}"

set +e
run_unity_with_display \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -executeMethod Decoder.EditorTools.CaptureHarness.Capture \
    -captureScene "${SCENE}" \
    -captureOutput "${OUT_DIR}" \
    -captureWidth "${WIDTH}" \
    -captureHeight "${HEIGHT}" \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ${STATUS} -ne 0 ]] || ! grep -q "CAPTURE_SUCCESS" "${LOG}"; then
    echo "截图失败 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "截图"
    exit 1
fi

grep "CAPTURED" "${LOG}" | sed 's/^/  /'
echo "完成。完整日志: ${LOG}"
