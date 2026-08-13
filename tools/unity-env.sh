#!/usr/bin/env bash
# decoder 项目的 Unity 环境变量。被 tools/ 下其它脚本 source 引用。
set -euo pipefail

export UNITY_VERSION="${UNITY_VERSION:-6000.0.81f1}"
export UNITY_ROOT="${UNITY_ROOT:-/opt/unity/${UNITY_VERSION}}"
export UNITY_BIN="${UNITY_BIN:-${UNITY_ROOT}/Editor/Unity}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export REPO_ROOT
export PROJECT_PATH="${PROJECT_PATH:-${REPO_ROOT}/unity/Decoder}"
export ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts}"
export LOG_DIR="${LOG_DIR:-${ARTIFACTS}/logs}"

mkdir -p "${LOG_DIR}"

if [[ ! -x "${UNITY_BIN}" ]]; then
    echo "未找到 Unity 可执行文件: ${UNITY_BIN}" >&2
    echo "请先运行 tools/install-unity.sh" >&2
    exit 1
fi

# 在无 X server 的环境下驱动需要渲染上下文的 Unity 命令。
# Mesa llvmpipe 做软件渲染，慢但可用，是本项目在无 GPU 云环境里做画面迭代的前提。
run_unity_with_display() {
    xvfb-run -a -s "-screen 0 1920x1080x24" \
        env LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
        "${UNITY_BIN}" "$@"
}

# 不需要渲染上下文的 Unity 命令（构建、测试、资源导入）走这条，明显更快。
run_unity_headless() {
    "${UNITY_BIN}" -nographics "$@"
}

# 从 Unity 日志里提取真正的失败原因，避免在几万行日志里翻找。
report_unity_log() {
    local log_file="$1"
    local label="$2"
    echo "--- ${label} 日志关键行 ---"
    grep -nE "^(Compilation failed|Error building|BuildFailedException)|error CS[0-9]+|Exception:|Fatal Error|Aborting batchmode|Licensing.*[Ee]rror" \
        "${log_file}" | head -30 || true
}
