#!/usr/bin/env bash
#
# One-shot self-test for project worker.
#
# Runs the unit suite, then simulates both factory scenarios headlessly, capturing
# screenshots and CPU-side metrics. Requires only the .NET SDK: no GPU, no display
# server and no Unity licence, which is what makes it usable on a build machine and
# in CI.
#
# Usage:
#   Tools/selftest.sh [output-tag]
#
# Output lands in Artifacts/selftest/<tag>/ with one directory per scenario.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

TAG="${1:-$(git rev-parse --short HEAD 2>/dev/null || echo local)}"
OUT_ROOT="Artifacts/selftest/${TAG}"

# Simulation ticks per run. 12000 is ten in-game days at the current day length,
# long enough for the standing contracts to cycle several times.
TICKS="${WORKER_SELFTEST_TICKS:-12000}"
INTERVAL="${WORKER_SELFTEST_INTERVAL:-2000}"

# Budget for one simulation tick. The sim runs at 20 Hz, so 2000 microseconds is
# four percent of a tick's real-time budget; anything above this means the model
# has grown too expensive to leave headroom for rendering on a real machine.
TICK_P95_BUDGET_US="${WORKER_TICK_P95_BUDGET_US:-2000}"

echo "=== worker self-test: ${TAG} ==="
echo

echo "--- unit tests ---"
dotnet test Tools/CoreTests/Worker.Core.Tests.csproj --nologo -v minimal

echo
echo "--- scenario: starter ---"
dotnet run --project Tools/Preview -c Release -- \
  --scenario starter --seed 3 --workers 4 \
  --ticks "$TICKS" --interval "$INTERVAL" \
  --out "${OUT_ROOT}/starter"

echo
echo "--- scenario: automated ---"
dotnet run --project Tools/Preview -c Release -- \
  --scenario automated --seed 7 --workers 6 \
  --ticks "$TICKS" --interval "$INTERVAL" \
  --out "${OUT_ROOT}/automated"

echo
echo "--- budget check ---"

fail=0
for scenario in starter automated; do
  metrics="${OUT_ROOT}/${scenario}/metrics.json"
  if [[ ! -f "$metrics" ]]; then
    echo "FAIL ${scenario}: metrics.json missing"
    fail=1
    continue
  fi

  p95=$(grep -o '"tickMicrosP95": [0-9]*' "$metrics" | grep -o '[0-9]*$')
  shipped=$(grep -o '"unitsShipped": [0-9]*' "$metrics" | grep -o '[0-9]*$')
  idle=$(grep -o '"idleWorkerRatioPercent": [0-9]*' "$metrics" | grep -o '[0-9]*$')

  if (( p95 > TICK_P95_BUDGET_US )); then
    echo "FAIL ${scenario}: tick p95 ${p95}us exceeds budget ${TICK_P95_BUDGET_US}us"
    fail=1
  fi

  # A factory that ships nothing has stalled, whatever the tests say.
  if (( shipped == 0 )); then
    echo "FAIL ${scenario}: nothing was shipped, the line is stalled"
    fail=1
  fi

  # Near-total idleness means the scheduler stopped handing out work.
  if (( idle > 60 )); then
    echo "FAIL ${scenario}: workers idle ${idle}% of samples, scheduler may be deadlocked"
    fail=1
  fi

  echo "  ${scenario}: p95 ${p95}us, shipped ${shipped}, idle ${idle}%"
done

echo
echo "frames written to ${OUT_ROOT}"
find "${OUT_ROOT}" -name '*.png' | wc -l | xargs echo "  screenshots:"

if (( fail != 0 )); then
  echo
  echo "SELF-TEST FAILED"
  exit 1
fi

echo
echo "SELF-TEST PASSED"
