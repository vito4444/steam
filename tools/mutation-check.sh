#!/usr/bin/env bash
# 变异验证：故意把实现改坏，确认测试会红，再改回来确认恢复绿灯。
#
# 测试全绿只证明"当前实现能通过当前断言"，不证明"断言真的守住了逻辑"。
# 一条写歪的测试可能永远是绿的。这个脚本逐个注入变异，
# 任何一个变异没能让测试变红，都说明那段逻辑实际上没有被守护。
set -euo pipefail
cd "$(dirname "$0")/.."

RUNTIME="unity/Decoder/Assets/Scripts/Signal"

# 每条变异的格式: 描述|文件|原文|替换文
MUTATIONS=(
  "摩尔斯 S 的码型改错|${RUNTIME}/MorseCode.cs|['S'] = \"...\",|['S'] = \"..-\","
  "摩尔斯划的时长从 3 单位改成 2 单位|${RUNTIME}/MorseCode.cs|var length = token[i] == Dah ? 3f : 1f;|var length = token[i] == Dah ? 2f : 1f;"
  "词间隔从 7 单位改成 3 单位|${RUNTIME}/MorseCode.cs|ReplaceOrAppendGap(timeline, unit * 7f, unit);|ReplaceOrAppendGap(timeline, unit * 3f, unit);"
  "PARIS 单位公式的分子改错|${RUNTIME}/MorseCode.cs|return 1.2f / wordsPerMinute;|return 1.5f / wordsPerMinute;"
  "电码分组长度从 4 改成 3|${RUNTIME}/ChineseTelegraphCode.cs|public const int CodeLength = 4;|public const int CodeLength = 3;"
  "电码表解析的汉字偏移错一位|${RUNTIME}/ChineseTelegraphCode.cs|var character = line[CodeLength];|var character = line[CodeLength - 1];"
)

restore() {
    git checkout -- "${RUNTIME}" 2>/dev/null || true
}
trap restore EXIT

if ! git diff --quiet -- "${RUNTIME}"; then
    echo "工作区在 ${RUNTIME} 下有未提交改动，先提交或暂存后再跑变异验证。" >&2
    exit 1
fi

echo "基线：确认当前测试是绿的"
if ! ./tools/run-tests.sh EditMode > /tmp/mutation-baseline.log 2>&1; then
    echo "基线测试就没通过，先修好再跑变异验证。" >&2
    tail -20 /tmp/mutation-baseline.log >&2
    exit 1
fi
grep -m1 "总计" /tmp/mutation-baseline.log

SURVIVED=0
KILLED=0

for entry in "${MUTATIONS[@]}"; do
    IFS='|' read -r desc file original replacement <<< "${entry}"

    if ! grep -qF -- "${original}" "${file}"; then
        echo "  [跳过] ${desc}：在 ${file} 里找不到目标代码，变异用例已过期"
        SURVIVED=$((SURVIVED + 1))
        continue
    fi

    python3 - "${file}" "${original}" "${replacement}" <<'PY'
import sys
path, original, replacement = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, encoding="utf-8") as f:
    text = f.read()
with open(path, "w", encoding="utf-8") as f:
    f.write(text.replace(original, replacement, 1))
PY

    if ./tools/run-tests.sh EditMode > /tmp/mutation-run.log 2>&1; then
        echo "  [存活] ${desc} —— 测试仍然全绿，这段逻辑没有被守护"
        SURVIVED=$((SURVIVED + 1))
    else
        echo "  [杀死] ${desc} —— $(grep -m1 '总计' /tmp/mutation-run.log | tr -s ' ')"
        KILLED=$((KILLED + 1))
    fi

    git checkout -- "${file}"
done

echo
echo "变异验证结果：杀死 ${KILLED}，存活 ${SURVIVED}"

echo "恢复后复查：确认测试重新变绿"
./tools/run-tests.sh EditMode > /tmp/mutation-final.log 2>&1
grep -m1 "总计" /tmp/mutation-final.log

if [[ ${SURVIVED} -gt 0 ]]; then
    echo "有变异存活，说明测试没有覆盖住对应逻辑。" >&2
    exit 1
fi
echo "全部变异都被测试杀死。"
