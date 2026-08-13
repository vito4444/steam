#!/usr/bin/env bash
# Runs the Unity Test Framework suites headlessly and prints a pass/fail summary.
# Usage: tools/run_tests.sh [editmode|playmode|all]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

MODE="${1:-all}"
STAMP="$(date +%Y%m%d-%H%M%S)"
FAILED=0

run_platform() {
  local platform="$1"
  local logfile="$LOG_DIR/tests-$platform-$STAMP.log"
  local results="$ARTIFACT_DIR/test-results-$platform.xml"
  echo "[tests] $platform -> $results"
  set +e
  "$UNITY" -batchmode -nographics \
    -projectPath "$PROJECT_PATH" \
    -runTests -testPlatform "$platform" \
    -testResults "$results" \
    -logFile "$logfile"
  local rc=$?
  set -e

  if [[ -f "$results" ]]; then
    python3 - "$results" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
total = root.get('total'); passed = root.get('passed')
failed = root.get('failed'); skipped = root.get('skipped')
print(f"[tests] total={total} passed={passed} failed={failed} skipped={skipped}")
for case in root.iter('test-case'):
    if case.get('result') not in ('Passed', 'Skipped'):
        print(f"  FAIL {case.get('fullname')}")
        msg = case.find('.//message')
        if msg is not None and msg.text:
            print('       ' + msg.text.strip().splitlines()[0])
PY
  fi

  if [[ $rc -ne 0 ]]; then
    echo "[tests] $platform rc=$rc"
    grep -nE "error CS|Exception" "$logfile" | head -20 || true
    FAILED=1
  fi
}

case "$MODE" in
  editmode) run_platform EditMode ;;
  playmode) run_platform PlayMode ;;
  all)      run_platform EditMode; run_platform PlayMode ;;
  *) echo "usage: $0 [editmode|playmode|all]" >&2; exit 2 ;;
esac

exit $FAILED
