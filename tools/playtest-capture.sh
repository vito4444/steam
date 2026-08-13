#!/usr/bin/env bash
# 构建 Linux 版并在 Xvfb 下真实运行，抓取运行中的画面。
#
# 为什么要这么做：编辑器截图只渲染场景，不会执行任何运行时逻辑——
# 界面是 Awake 里生成的，接收机是音频线程驱动的，这些在编辑器截图里全都不存在。
# 只有把游戏真跑起来截屏，才能验证"这东西确实能玩"。
#
# 用 Linux 版而不是 Windows 版，是因为云端没有 Windows 也没有 Wine。
# 两者共用同一份 C# 代码和场景，运行时行为一致。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

SCENE="${1:-Assets/Scenes/Station.unity}"
SECONDS_TO_RUN="${2:-22}"
# 传 -playtest 让游戏自动走一遍班次流程，用于验证整条玩法链路。
PLAYTEST_FLAG="${3:--playtest}"
OUT_DIR="${ARTIFACTS}/playtest"
BUILD_DIR="${ARTIFACTS}/build/StandaloneLinux64"
LOG="${LOG_DIR}/playtest.log"
PLAYER_LOG="${LOG_DIR}/player.log"
DISPLAY_NUM=":91"

mkdir -p "${OUT_DIR}"
rm -f "${OUT_DIR}"/*.png

# 场景生成与构建必须分成两个编辑器进程。同进程连着做会让构建读到内存里
# 尚未与磁盘对齐的场景状态，产出的 level0 在运行时报 corrupted 直接崩溃，
# 而构建过程一句警告都不给。
echo "生成可玩场景"
"$(dirname "$0")/build-station-scene.sh" > /dev/null
wait_for_unity_exit

echo "构建 Linux x64"
rm -rf "${BUILD_DIR}"
set +e
run_unity_headless \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -buildTarget Linux64 \
    -executeMethod Decoder.EditorTools.BuildScript.BuildLinux64 \
    -buildOutput "${BUILD_DIR}" \
    -buildScenes "${SCENE}" \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ${STATUS} -ne 0 ]] || ! grep -q "BUILD_SUCCESS" "${LOG}"; then
    echo "构建失败 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "构建"
    exit 1
fi

cleanup() {
    [[ -n "${PLAYER_PID:-}" ]] && kill "${PLAYER_PID}" 2>/dev/null || true
    [[ -n "${XVFB_PID:-}" ]] && kill "${XVFB_PID}" 2>/dev/null || true
    wait 2>/dev/null || true
}
trap cleanup EXIT

echo "启动 Xvfb ${DISPLAY_NUM}"
Xvfb "${DISPLAY_NUM}" -screen 0 1920x1080x24 -nolisten tcp &
XVFB_PID=$!
sleep 2

echo "运行游戏 ${SECONDS_TO_RUN} 秒"
DISPLAY="${DISPLAY_NUM}" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
    "${BUILD_DIR}/Decoder" \
    -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
    ${PLAYTEST_FLAG:-} \
    -logFile "${PLAYER_LOG}" &
PLAYER_PID=$!

# 分几个时间点抓帧：早期确认起得来，中期确认稳定运行，
# 后期确认没有在跑了一会儿之后崩掉或黑屏。
SHOT_INDEX=0
for delay in 5 9 13 17 21; do
    while [[ ${SECONDS_ELAPSED:-0} -lt ${delay} ]]; do
        sleep 1
        SECONDS_ELAPSED=$(( ${SECONDS_ELAPSED:-0} + 1 ))
    done

    if ! kill -0 "${PLAYER_PID}" 2>/dev/null; then
        if grep -q "is corrupted" "${PLAYER_LOG}" 2>/dev/null && [[ "${DECODER_RETRIED:-0}" != "1" ]]; then
            # level0 损坏几乎总是 Library 缓存不一致造成的，与刚改的代码无关。
            # 清掉缓存重跑一次，比在错误的方向上排查半天划算得多。
            echo "检测到 level0 损坏，清空缓存后重试一次" >&2
            cleanup
            trap - EXIT
            clear_unity_cache
            DECODER_RETRIED=1 exec "$0" "$@"
        fi

        echo "游戏进程在第 ${delay} 秒前退出了" >&2
        tail -30 "${PLAYER_LOG}" >&2
        exit 1
    fi

    OUT="${OUT_DIR}/playtest_$(printf '%02d' "${SHOT_INDEX}")_t${delay}s.png"
    ffmpeg -loglevel error -y -f x11grab -video_size 1920x1080 \
        -i "${DISPLAY_NUM}.0" -frames:v 1 "${OUT}"
    echo "  抓帧 ${OUT}"
    SHOT_INDEX=$((SHOT_INDEX + 1))
done

while [[ ${SECONDS_ELAPSED:-0} -lt ${SECONDS_TO_RUN} ]]; do
    sleep 1
    SECONDS_ELAPSED=$(( ${SECONDS_ELAPSED:-0} + 1 ))
done

if ! kill -0 "${PLAYER_PID}" 2>/dev/null; then
    echo "游戏在 ${SECONDS_TO_RUN} 秒内退出了" >&2
    exit 1
fi

echo "游戏持续运行 ${SECONDS_TO_RUN} 秒未退出"

echo "--- 演练日志 ---"
grep -oE "\[PlaytestDriver\].*" "${PLAYER_LOG}" | head -10 || echo "  未运行演练"

echo "--- 播放器日志中的异常 ---"
grep -nE "Exception|Error|error CS|NullReference|Fatal" "${PLAYER_LOG}" \
    | grep -viE "ALSA|audio device|AudioDevice|snd_|No such file or directory.*alsa" \
    | head -15 || echo "  无异常"

ls -la "${OUT_DIR}"
