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

# 等待所有 Unity 编辑器进程完全退出。
#
# 场景生成与构建是两个独立的编辑器进程。前一个进程即使已经打印了退出信息，
# 后台仍可能在写 Library 与资源导入结果；此时启动下一个进程去构建，
# 读到的是写了一半的状态，产出的 level0 在运行时报 corrupted 直接崩溃，
# 而构建过程一句警告都不给。表现为间歇性失败，与场景内容无关。
wait_for_unity_exit() {
    local waited=0
    while pgrep -f "${UNITY_ROOT}/Editor/Unity" > /dev/null 2>&1; do
        sleep 1
        waited=$((waited + 1))
        if [[ ${waited} -ge 60 ]]; then
            echo "等待 Unity 退出超时，仍有进程在运行" >&2
            return 1
        fi
    done

    # 进程消失之后再给文件系统一点时间落盘。
    sleep 2
    return 0
}

# 清空 Unity 的资源缓存，强制下一次启动重新导入。
#
# 反复改代码之后 Library 会进入一种不一致的状态：编译和构建都报成功，
# 但产出的 level0 在运行时报 corrupted 直接崩溃。这个失败与场景内容无关，
# 排查时极易把它误判成"刚加的那个东西有问题"——本项目已经因此得出过
# 三个错误结论（同进程构建、着色器进包、内嵌网格过多），全都不是真因。
#
# 判断依据很简单：清掉 Library 重来一次就正常。代价是几十秒的重新导入。
clear_unity_cache() {
    echo "清空 Library 缓存并重新导入"
    rm -rf "${PROJECT_PATH}/Library" "${PROJECT_PATH}/Temp" 2>/dev/null || true
}

# 从 Unity 日志里提取真正的失败原因，避免在几万行日志里翻找。
report_unity_log() {
    local log_file="$1"
    local label="$2"
    echo "--- ${label} 日志关键行 ---"
    grep -nE "^(Compilation failed|Error building|BuildFailedException)|error CS[0-9]+|Exception:|Fatal Error|Aborting batchmode|Licensing.*[Ee]rror" \
        "${log_file}" | head -30 || true
}
