#!/usr/bin/env bash
# Runs the edit-mode test suite headlessly and reports the result.
#
# Unity's -runTests exits non-zero on failure, but the XML is parsed here anyway so the
# console output names which tests failed rather than making someone open the file.
set -uo pipefail

log() { echo "[test] $(date -u +%H:%M:%S) $*"; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="${UNITY:-/opt/unity/editors/6000.5.8f1/Editor/Unity}"
PLATFORM="${1:-EditMode}"
RESULTS="/tmp/monster-tests-${PLATFORM}.xml"
LOG="/tmp/monster-tests-${PLATFORM}.log"

[[ -x "${UNITY}" ]] || { log "FATAL: Unity not found at ${UNITY}"; exit 1; }

rm -f "${RESULTS}"
log "running ${PLATFORM} tests"
START=$(date +%s)

xvfb-run -a "${UNITY}" \
  -runTests \
  -batchmode \
  -projectPath "${REPO_ROOT}/game" \
  -testPlatform "${PLATFORM}" \
  -testResults "${RESULTS}" \
  -logFile "${LOG}" >/dev/null 2>&1
STATUS=$?

log "unity exited with ${STATUS} after $(( $(date +%s) - START ))s"

if grep -qE 'error CS[0-9]+' "${LOG}"; then
  log "COMPILE ERRORS:"
  grep -oE '[^ ]+\.cs\([0-9]+,[0-9]+\): error CS[0-9]+: .*' "${LOG}" | sort -u | head -25 | sed 's/^/  /'
  exit 2
fi

if [[ ! -f "${RESULTS}" ]]; then
  log "no results file was produced; last 30 lines of the log:"
  tail -30 "${LOG}" | sed 's/^/  /'
  exit 1
fi

python3 - "${RESULTS}" <<'PY'
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
total = int(root.get("total", 0))
passed = int(root.get("passed", 0))
failed = int(root.get("failed", 0))
skipped = int(root.get("skipped", 0))
duration = float(root.get("duration", 0))

print(f"[test] {passed}/{total} passed, {failed} failed, {skipped} skipped, {duration:.1f}s")

if failed:
    print("[test] failures:")
    for case in root.iter("test-case"):
        if case.get("result") == "Failed":
            print(f"  - {case.get('fullname')}")
            message = case.find("failure/message")
            if message is not None and message.text:
                for line in message.text.strip().splitlines()[:6]:
                    print(f"      {line.strip()}")
sys.exit(1 if failed else 0)
PY
