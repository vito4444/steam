#!/usr/bin/env bash
#
# Measures a captured frame and prints objective numbers for it.
#
# This exists because eyeballing screenshots is unreliable. During the dusk lighting
# work a change that shifted mean brightness by 28% was repeatedly judged "no visible
# difference" by looking at thumbnails, which wasted several build cycles. Numbers do
# not have that failure mode.
#
# Usage:
#   Tools/analyse-frame.sh <image> [label]
#   Tools/analyse-frame.sh --compare <before> <after>
#
# Metrics, and what each one is for:
#   brightness  mean luminance 0-255. Tracks exposure changes between passes.
#   contrast    luminance standard deviation. Low means a flat, washed out image.
#   saturation  mean saturation 0-255. Low means the palette is reading grey.
#   detail      fraction of pixels on an edge. Proxy for how much is going on.
#   coverage    fraction of pixels that are not background. Proxy for density.

set -euo pipefail

CROP="${CROP:-1600x900+0+0}"

metrics() {
  local image="$1"

  # Restrict to the play area: the side panel and build bar are constant and would
  # drag every metric towards their own values.
  local area
  area=$(mktemp /tmp/frame-XXXXXX.png)
  convert "$image" -crop 840x480+120+40 +repage "$area"

  local brightness contrast saturation detail coverage

  brightness=$(convert "$area" -colorspace Gray -format "%[fx:int(mean*255)]" info:)
  contrast=$(convert "$area" -colorspace Gray -format "%[fx:int(standard_deviation*255)]" info:)
  saturation=$(convert "$area" -colorspace HSL -channel G -separate -format "%[fx:int(mean*255)]" info:)

  # Edge density: how much of the frame carries a visible boundary.
  detail=$(convert "$area" -colorspace Gray -edge 1 -threshold 25% -format "%[fx:int(mean*100)]" info:)

  # Coverage: pixels meaningfully brighter than the darkest tone present.
  coverage=$(convert "$area" -colorspace Gray -threshold 18% -format "%[fx:int(mean*100)]" info:)

  rm -f "$area"
  echo "$brightness $contrast $saturation $detail $coverage"
}

if [[ "${1:-}" == "--compare" ]]; then
  before="$2"
  after="$3"

  read -r b1 c1 s1 d1 v1 <<< "$(metrics "$before")"
  read -r b2 c2 s2 d2 v2 <<< "$(metrics "$after")"

  printf '%-12s %8s %8s %8s\n' "metric" "before" "after" "delta"
  printf '%-12s %8s %8s %+8d\n' "brightness" "$b1" "$b2" "$((b2 - b1))"
  printf '%-12s %8s %8s %+8d\n' "contrast" "$c1" "$c2" "$((c2 - c1))"
  printf '%-12s %8s %8s %+8d\n' "saturation" "$s1" "$s2" "$((s2 - s1))"
  printf '%-12s %8s%% %7s%% %+7d\n' "detail" "$d1" "$d2" "$((d2 - d1))"
  printf '%-12s %8s%% %7s%% %+7d\n' "coverage" "$v1" "$v2" "$((v2 - v1))"

  rmse=$(compare -metric RMSE "$before" "$after" null: 2>&1 | grep -oE '\(([0-9.]+)\)' | tr -d '()')
  printf '%-12s %26s\n' "rmse" "$rmse"
  exit 0
fi

image="${1:?usage: analyse-frame.sh <image> [label]}"
label="${2:-$(basename "$image")}"

read -r b c s d v <<< "$(metrics "$image")"

printf '%s\n' "$label"
printf '  brightness %3s   contrast %3s   saturation %3s   detail %2s%%   coverage %2s%%\n' \
  "$b" "$c" "$s" "$d" "$v"
