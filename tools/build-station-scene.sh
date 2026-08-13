#!/usr/bin/env bash
# 生成可玩的工位场景。几何与探针场景共用同一套生成代码，
# 额外接上接收机、交互控件、玩家视角与界面。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

LOG="${LOG_DIR}/station-scene.log"
echo "生成可玩场景 -> Assets/Scenes/Station.unity"

set +e
run_unity_headless \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -executeMethod Decoder.EditorTools.ProbeSceneBuilder.BuildPlayable \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ${STATUS} -ne 0 ]] || ! grep -q "STATION_SCENE_BUILT" "${LOG}"; then
    echo "场景生成失败 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "场景生成"
    exit 1
fi

grep -m1 "STATION_SCENE_BUILT" "${LOG}"
echo "完整日志: ${LOG}"
