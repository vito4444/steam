#!/usr/bin/env bash
# 烘焙程序化 PBR 贴图与材质。改完配方跑一遍就得到新版本，无需外部软件。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

LOG="${LOG_DIR}/bake-materials.log"
echo "烘焙程序化材质 -> Assets/Textures/Procedural 与 Assets/Materials/Procedural"

set +e
run_unity_headless \
    -batchmode -quit \
    -projectPath "${PROJECT_PATH}" \
    -executeMethod Decoder.EditorTools.ProceduralMaterialBaker.BakeAll \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ${STATUS} -ne 0 ]] || ! grep -q "MATERIALS_BAKED" "${LOG}"; then
    echo "烘焙失败 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "烘焙"
    exit 1
fi

grep -m1 "MATERIALS_BAKED" "${LOG}"
ls -la "${PROJECT_PATH}/Assets/Textures/Procedural" 2>/dev/null | head -25
