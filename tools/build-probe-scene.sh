#!/usr/bin/env bash
# 程序化重建美术探针场景。场景内容完全由 ProbeSceneBuilder.cs 决定，
# 因此每次改完参数跑一遍这个脚本就能得到可复现的新版本。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

LOG="${LOG_DIR}/probe-scene.log"
echo "重建探针场景 -> Assets/Scenes/ArtProbe.unity"

set +e
run_unity_headless \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -executeMethod Decoder.EditorTools.ProbeSceneBuilder.Build \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ${STATUS} -ne 0 ]] || ! grep -q "PROBE_SCENE_BUILT" "${LOG}"; then
    echo "场景生成失败 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "场景生成"
    exit 1
fi

grep -m1 "PROBE_SCENE_BUILT" "${LOG}"
echo "完成。完整日志: ${LOG}"
