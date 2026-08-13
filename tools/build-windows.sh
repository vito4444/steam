#!/usr/bin/env bash
# 从 Linux 交叉构建 Windows x64 可执行文件（Mono 后端）。
#
# 已知限制: Unity 官方不提供 Linux 版的 Windows IL2CPP 构建模块，
# 因此云端只能产出 Mono 后端的 Windows 版本。Mono 后端可以正常发行到 Steam，
# 但正式发行版建议在 Windows 机器上用 IL2CPP 重新构建（启动更快、更难被反编译）。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

OUT_DIR="${1:-${ARTIFACTS}/build/StandaloneWindows64}"
# 默认构建可玩工位场景。传 probe 则构建纯美术探针场景。
TARGET_METHOD="Decoder.EditorTools.BuildScript.BuildStationWindows64"
if [[ "${2:-station}" == "probe" ]]; then
    TARGET_METHOD="Decoder.EditorTools.BuildScript.BuildWindows64"
fi
LOG="${LOG_DIR}/build-windows.log"

echo "构建 Windows x64 -> ${OUT_DIR}"
rm -rf "${OUT_DIR}"
mkdir -p "${OUT_DIR}"

START=$(date +%s)
set +e
run_unity_headless \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -buildTarget Win64 \
    -executeMethod "${TARGET_METHOD}" \
    -buildOutput "${OUT_DIR}" \
    -logFile "${LOG}"
STATUS=$?
set -e
ELAPSED=$(( $(date +%s) - START ))

if [[ ${STATUS} -ne 0 ]] || ! grep -q "BUILD_SUCCESS" "${LOG}"; then
    echo "构建失败 (exit=${STATUS}, ${ELAPSED}s)" >&2
    report_unity_log "${LOG}" "构建"
    exit 1
fi

echo "构建成功，用时 ${ELAPSED}s"
ls -la "${OUT_DIR}"
if [[ -f "${OUT_DIR}/Decoder.exe" ]]; then
    file "${OUT_DIR}/Decoder.exe"
fi
echo "完整日志: ${LOG}"
