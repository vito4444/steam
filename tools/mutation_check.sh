#!/usr/bin/env bash
# Mutation testing. A green suite only proves the tests ran; this proves they would
# actually catch the bug they claim to guard. Each mutation breaks one specific rule and
# must turn a specific test red.
set -uo pipefail

UNITY=/opt/unity/6000.0.81f1/Editor/Unity
PROJECT=/workspace/HunterGame
export DISPLAY="${DISPLAY:-:1}"

run_tests() {
  timeout 1800 "$UNITY" -batchmode -runTests -projectPath "$PROJECT" \
    -testPlatform EditMode -testResults /tmp/mut_results.xml -logFile /tmp/mut.log > /dev/null 2>&1
  python3 - <<'PY'
import xml.etree.ElementTree as ET, os
p='/tmp/mut_results.xml'
if not os.path.exists(p):
    print("COMPILE_ERROR"); raise SystemExit
r=ET.parse(p).getroot()
failed=[tc.get('name') for tc in r.iter('test-case') if tc.get('result')!='Passed']
print(f"failed={r.get('failed')} names={','.join(failed[:6])}")
PY
}

mutate() {
  local label="$1" file="$2" from="$3" to="$4" expect="$5"
  echo "=============================================="
  echo "MUTATION: $label"
  echo "  expect to break: $expect"
  cp "$file" "$file.bak"
  python3 - "$file" "$from" "$to" <<'PY'
import sys, pathlib
path, frm, to = sys.argv[1], sys.argv[2], sys.argv[3]
p = pathlib.Path(path)
s = p.read_text()
assert frm in s, f"mutation target not found: {frm}"
p.write_text(s.replace(frm, to, 1))
PY
  if [ $? -ne 0 ]; then echo "  SKIPPED (target not found)"; mv "$file.bak" "$file"; return; fi
  run_tests
  mv "$file.bak" "$file"
}

INV=$PROJECT/Assets/Scripts/Gameplay/Items/Inventory.cs
VAL=$PROJECT/Assets/Scripts/Gameplay/Items/LootValuation.cs
RUN=$PROJECT/Assets/Scripts/Gameplay/Run/RunDirector.cs

echo "### baseline"
run_tests

mutate "inventory stops enforcing the weight limit" "$INV" \
  'if (TotalWeight + item.Weight > WeightLimit + 1e-4f) return AddResult.RejectedOverweight;' \
  '' \
  "TryAdd_RejectsItemHeavierThanRemainingCapacity"

mutate "encumbrance penalty removed" "$INV" \
  'if (ratio <= SoftCapRatio) return 1f;' \
  'return 1f;' \
  "SpeedMultiplier_FallsToFloorAtFullLoad, SpeedMultiplier_DecreasesMonotonicallyPastSoftCap"

mutate "swap threshold drops from 25% better to any improvement" "$VAL" \
  'bool clearlyBetter = density > ValueDensity(worst) * 1.25f;' \
  'bool clearlyBetter = density > ValueDensity(worst);' \
  "Appraise_DoesNotSwapOnNearTies"

mutate "death no longer drops the haul" "$RUN" \
  'DropEverything();
            SetPhase(RunPhase.Died);' \
  'SetPhase(RunPhase.Died);' \
  "Kill_LosesEverythingCarried"

mutate "portal can be entered without ringing the bell" "$RUN" \
  'if (Phase != RunPhase.ExtractionWindow) return false;' \
  'if (IsOver) return false;' \
  "EnterPortal_FailsWhenNoWindowIsOpen"

echo "=============================================="
echo "### restored baseline"
run_tests
