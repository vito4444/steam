#!/usr/bin/env bash
# 变异验证：故意把实现改坏，确认测试会红，再改回来确认恢复绿灯。
#
# 测试全绿只证明"当前实现能通过当前断言"，不证明"断言真的守住了逻辑"。
# 一条写歪的测试可能永远是绿的。这个脚本逐个注入变异，
# 任何一个变异没能让测试变红，都说明那段逻辑实际上没有被守护。
set -euo pipefail
cd "$(dirname "$0")/.."

RUNTIME="unity/Decoder/Assets/Scripts/Signal"
GAMEPLAY="unity/Decoder/Assets/Scripts/Gameplay"
SOURCES="${RUNTIME} ${GAMEPLAY}"

# 每条变异的格式: 描述|文件|原文|替换文
MUTATIONS=(
  "摩尔斯 S 的码型改错|${RUNTIME}/MorseCode.cs|['S'] = \"...\",|['S'] = \"..-\","
  "摩尔斯划的时长从 3 单位改成 2 单位|${RUNTIME}/MorseCode.cs|timeline.Add(new Element(true, Shape(token[i] == Dah ? fist.dahRatio : 1f)));|timeline.Add(new Element(true, Shape(token[i] == Dah ? 2f : 1f)));"
  "词间隔从 7 单位改成 3 单位|${RUNTIME}/MorseCode.cs|ReplaceOrAppendGap(timeline, Shape(fist.wordGapRatio), unit);|ReplaceOrAppendGap(timeline, unit * 3f, unit);"
  "PARIS 单位公式的分子改错|${RUNTIME}/MorseCode.cs|return 1.2f / wordsPerMinute;|return 1.5f / wordsPerMinute;"
  "电码分组长度从 4 改成 3|${RUNTIME}/ChineseTelegraphCode.cs|public const int CodeLength = 4;|public const int CodeLength = 3;"
  "电码表解析的汉字偏移错一位|${RUNTIME}/ChineseTelegraphCode.cs|var character = line[CodeLength];|var character = line[CodeLength - 1];"
  "差频音调的方向反转|${RUNTIME}/SignalSynthesizer.cs|var tone = NominalToneHz + detuneKHz * 700f;|var tone = NominalToneHz - detuneKHz * 700f;"
  "带通响应换成矩形窗|${RUNTIME}/SignalSynthesizer.cs|return 0.5f * (1f + (float)Math.Cos(Math.PI * t));|return 1f - t * 0f;"
  "去掉键控软化，恢复硬开关|${RUNTIME}/SignalSynthesizer.cs|var rampStep = KeyRampSeconds > 0f ? 1f / (KeyRampSeconds * _sampleRate) : 1f;|var rampStep = 1f;"
  "噪声源忽略种子，破坏确定性|${RUNTIME}/NoiseSource.cs|_state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);|_state = 0x9E3779B9u;"
  "带外信号不再截断|${RUNTIME}/SignalSynthesizer.cs|if (d >= BandwidthKHz)\n            {\n                return 0f;\n            }|if (d >= BandwidthKHz * 100f)\n            {\n                return 0f;\n            }"
  "选台改回只看带通响应，不看实际功率|${RUNTIME}/SignalSynthesizer.cs|var level = BandpassResponse(station.FrequencyKHz - TunedKHz) * station.Strength;|var level = BandpassResponse(station.FrequencyKHz - TunedKHz);"
  "相似度换成逐位比对|${GAMEPLAY}/ReportGrader.cs|var distance = LevenshteinDistance(a, b);|var distance = System.Math.Abs(a.Length - b.Length); for (var i = 0; i < System.Math.Min(a.Length, b.Length); i++) { if (a[i] != b[i]) distance++; }"
  "上报等级偏差的方向反转|${GAMEPLAY}/ReportGrader.cs|LevelDelta = (int)submission.Level - (int)expected.correctLevel,|LevelDelta = (int)expected.correctLevel - (int)submission.Level,"
  "频率容差放大二十倍|${GAMEPLAY}/ReportGrader.cs|public const float FrequencyToleranceKHz = 0.5f;|public const float FrequencyToleranceKHz = 10f;"
  "中文电码信号改发汉字而不是数字|${GAMEPLAY}/ShiftDefinition.cs|return ChineseTelegraphCode.ToDigitStream(telegraph.EncodeText(plainText));|return plainText;"
  "第一班主线等级降为例行|${GAMEPLAY}/ShiftLibrary.cs|correctLevel = ThreatLevel.Attention,\n                isPrimary = true,|correctLevel = ThreatLevel.Routine,\n                isPrimary = true,"
  "密码本加密改成减法|${RUNTIME}/OneTimePad.cs|var result = add ? (value + key) % 10 : ((value - key) % 10 + 10) % 10;|var result = add ? ((value - key) % 10 + 10) % 10 : (value + key) % 10;"
  "模 10 改成模 9|${RUNTIME}/OneTimePad.cs|var result = add ? (value + key) % 10 : ((value - key) % 10 + 10) % 10;|var result = add ? (value + key) % 9 : ((value - key) % 9 + 9) % 9;"
  "分隔符也消耗密钥位|${RUNTIME}/OneTimePad.cs|                    builder.Append(c);\n                    continue;|                    builder.Append(c);\n                    keyIndex++;\n                    continue;"
  "密码本页号不参与混合，每页都一样|${RUNTIME}/OneTimePad.cs|var state = unchecked((uint)(bookSeed * 2654435761L + pageNumber * 40503L));|var state = unchecked((uint)(bookSeed * 2654435761L));"
  "报头位数从 3 改成 4|${RUNTIME}/OneTimePad.cs|public const int PageIndicatorDigits = 3;|public const int PageIndicatorDigits = 4;"
  "报头不足时返回 0 而不是 -1|${RUNTIME}/OneTimePad.cs|            if (digits.Length < PageIndicatorDigits)\n            {\n                return -1;\n            }|            if (digits.Length < PageIndicatorDigits)\n            {\n                return 0;\n            }"
  "报头页码不再补零对齐|${RUNTIME}/OneTimePad.cs|return pageNumber.ToString(\"D\" + PageIndicatorDigits) + cipher;|return pageNumber.ToString() + cipher;"
  "漏报的扣分改得和误报一样轻|${GAMEPLAY}/CampaignState.cs|                        case ReportOutcome.Underreported:\n                            score -= 1f;\n                            break;|                        case ReportOutcome.Underreported:\n                            score -= 0.4f;\n                            break;"
  "上报后不再推进班次|${GAMEPLAY}/CampaignState.cs|            history.Add(record);\n            shiftIndex++;|            history.Add(record);"
  "存档解析忽略版本号上限|${GAMEPLAY}/CampaignState.cs|                        if (state.version > CurrentVersion)\n                        {\n                            // 比本体还新的存档不要硬解，字段含义可能已经变了。\n                            return null;\n                        }|                        if (false)\n                        {\n                            return null;\n                        }"
  "存档损坏时抛异常而不是返回 null|${GAMEPLAY}/CampaignState.cs|            if (lines.Length == 0 || lines[0].Trim() != "decoder-save")\n            {\n                return null;\n            }|            if (lines.Length == 0 || lines[0].Trim() != "decoder-save")\n            {\n                return new CampaignState();\n            }"
  "处境评价不再区分好坏|${GAMEPLAY}/CampaignState.cs|            if (standing >= 0.7f)|            if (standing >= -99f)"
  "手法抖动的量纲换算被去掉|${RUNTIME}/OperatorFist.cs|private const float MeanAbsoluteToAmplitude = 2f;|private const float MeanAbsoluteToAmplitude = 1f;"
  "点长改用最小值而不是中位数|${RUNTIME}/OperatorFist.cs|var dit = Median(ditSamples);|var dit = ditSamples[0];"
  "手法距离忽略字符间隔|${RUNTIME}/OperatorFist.cs|return dah * 0.4f + charGap * 0.4f + wordGap * 0.2f;|return dah * 0.8f + wordGap * 0.2f;"
  "样本不足时也硬给结论|${RUNTIME}/OperatorFist.cs|            if (timeline == null || timeline.Count < MinimumElements)\n            {\n                return default;\n            }|            if (timeline == null || timeline.Count < 1)\n            {\n                return default;\n            }"
  "点划分界从两倍挪到一点二倍|${RUNTIME}/OperatorFist.cs|var threshold = downs[0] * 2f;|var threshold = downs[0] * 1.2f;"
  "手法抖动不再影响发报时长|${RUNTIME}/MorseCode.cs|var factor = 1f + noise.NextWhite() * fist.jitter;|var factor = 1f;"
  "第四班的冒充者改回本人的手法|${GAMEPLAY}/ShiftLibrary.cs|fist = M08Impostor,|fist = M08Operator,"
)

restore() {
    git checkout -- ${SOURCES} 2>/dev/null || true
}
trap restore EXIT

if ! git diff --quiet -- ${SOURCES}; then
    echo "工作区在 ${SOURCES} 下有未提交改动，先提交或暂存后再跑变异验证。" >&2
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

    # 变异定义里的 \n 是字面两字符，这里统一还原成真换行，
    # 这样跨行的代码片段也能作为变异目标。
    if ! python3 - "${file}" "${original}" "${replacement}" <<'PY'
import sys
path, original, replacement = sys.argv[1], sys.argv[2], sys.argv[3]
original = original.replace("\\n", "\n")
replacement = replacement.replace("\\n", "\n")
with open(path, encoding="utf-8") as f:
    text = f.read()
if original not in text:
    sys.exit(3)
with open(path, "w", encoding="utf-8") as f:
    f.write(text.replace(original, replacement, 1))
PY
    then
        echo "  [跳过] ${desc}：在 ${file} 里找不到目标代码，变异用例已过期"
        SURVIVED=$((SURVIVED + 1))
        continue
    fi

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
