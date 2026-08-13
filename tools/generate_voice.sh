#!/usr/bin/env bash
# 生成电话通话的占位语音。
#
# 流程是「espeak-ng 合成 -> ffmpeg 走一遍无线电链路」。加这层处理不只是为了氛围：
# 300–3000 Hz 带通、削波失真与底噪能大幅掩盖合成语音的机械感，
# 让占位语音在替换成真人配音之前也能听。
#
# 输出到 game/Assets/Resources/Voice/，运行时用 Resources.Load 取。

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$ROOT/game/Assets/Resources/Voice"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

for tool in espeak-ng ffmpeg; do
  command -v "$tool" >/dev/null || { echo "缺少 $tool" >&2; exit 1; }
done

mkdir -p "$OUT_DIR"

# 通话本：文件名|语速|音高|文本。语速与音高的差异用来区分说话人。
LINES=(
  "crew_ready|142|38|调度室，三班组十二人已经在罐笼口了，等你放我们下去。"
  "command_pressure|158|22|调度，上级看着今班的产量呢，别在地面上磨蹭。"
  "crew_power_lost|172|55|调度！这边灯全灭了，罐笼卡在半道上不动了，你那边什么情况？"
  "crew_calm|132|40|行，我们不动，等你的信号。"
  "crew_panic|182|60|什么叫自己想办法？这里离底板还有八百米啊！"
  "unknown_whisper|96|8|喂。你们那边，是不是还有一个人在下面。"
)

# 无线电链路：带通模拟话务频响，削波制造过载失真，底噪补上静电感。
RADIO_FILTER="highpass=f=320,lowpass=f=2900,\
acompressor=threshold=0.12:ratio=6:attack=5:release=90,\
aeval='clip(val(0)*2.1,-0.85,0.85)':c=same,\
highpass=f=300,lowpass=f=3000,\
volume=1.35"

for entry in "${LINES[@]}"; do
  IFS='|' read -r name speed pitch text <<<"$entry"
  raw="$TMP_DIR/$name.raw.wav"
  noise="$TMP_DIR/$name.noise.wav"
  out="$OUT_DIR/$name.ogg"

  espeak-ng -v cmn -s "$speed" -p "$pitch" -a 170 -w "$raw" "$text"

  duration=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$raw")
  ffmpeg -v error -y -f lavfi -i "anoisesrc=d=$duration:c=pink:r=22050:a=0.06" \
    -ar 22050 -ac 1 "$noise"

  ffmpeg -v error -y -i "$raw" -i "$noise" \
    -filter_complex "[0:a]${RADIO_FILTER}[v];[v][1:a]amix=inputs=2:duration=first:weights=1 0.55[m];[m]alimiter=limit=0.92[o]" \
    -map "[o]" -ar 22050 -ac 1 -c:a libvorbis -q:a 3 "$out"

  printf "%-20s %5.2fs  %6d B\n" "$name" "$duration" "$(stat -c%s "$out")"
done

echo "语音已输出到 $OUT_DIR"
