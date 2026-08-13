#!/usr/bin/env bash
# 运行 EditMode 单元测试并把结果摘要打印出来。
#
# 注意: Unity 的 -runTests 不能与 -quit 同用，测试跑完编辑器会自行退出。
# 退出码 0 表示全部通过，2 表示有用例失败，其它值表示编辑器本身出错。
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

PLATFORM="${1:-EditMode}"
LOG="${LOG_DIR}/tests-${PLATFORM}.log"
RESULTS="${ARTIFACTS}/reports/test-results-${PLATFORM}.xml"
mkdir -p "$(dirname "${RESULTS}")"

# 必须先删掉上一次的结果。否则编译失败时 Unity 不会写新文件，
# 脚本会读到上一轮的旧结果，把失败报成全绿——那比没有测试还危险。
rm -f "${RESULTS}"

echo "运行 ${PLATFORM} 测试"
set +e
run_unity_headless \
    -batchmode \
    -projectPath "${PROJECT_PATH}" \
    -runTests \
    -testPlatform "${PLATFORM}" \
    -testResults "${RESULTS}" \
    -logFile "${LOG}"
STATUS=$?
set -e

if [[ ! -f "${RESULTS}" ]]; then
    echo "未生成测试结果文件 (exit=${STATUS})" >&2
    report_unity_log "${LOG}" "测试"
    exit 1
fi

python3 - "${RESULTS}" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
total = int(root.get("total", 0)); passed = int(root.get("passed", 0))
failed = int(root.get("failed", 0)); skipped = int(root.get("skipped", 0))
print(f"  总计 {total}  通过 {passed}  失败 {failed}  跳过 {skipped}  用时 {root.get('duration','?')}s")
if failed:
    print("\n  失败用例：")
    for case in root.iter("test-case"):
        if case.get("result") == "Failed":
            print(f"    {case.get('fullname')}")
            msg = case.find("failure/message")
            if msg is not None and msg.text:
                for line in msg.text.strip().splitlines()[:4]:
                    print(f"      {line}")
    sys.exit(2)
PY

echo "完整日志: ${LOG}"
echo "结果文件: ${RESULTS}"
