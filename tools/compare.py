#!/usr/bin/env python3
"""实机截图与目标概念图的差距量化工具。

本项目在无人工观察的环境中开发，需要一种客观手段判断「当前画面离目标还有多远」。
本工具输出两类结果：

1. 一张并排对比图（左：目标，右：实机），便于人工快速扫一眼。
2. 一份中文文字报告，量化亮度、对比度、饱和度、色温、影调分布与主色差异，
   并给出可执行的调整方向。

用法：
    python3 tools/compare.py --target docs/concepts/images/maner-concept-A-control-cabin.jpg \\
                             --actual screenshots/shot_00_t1.00s.png \\
                             --out artifacts/compare/A-vs-current.png
"""

from __future__ import annotations

import argparse
import colorsys
import os
from dataclasses import dataclass

import numpy as np
from PIL import Image, ImageDraw

ANALYSIS_WIDTH = 512


@dataclass
class Metrics:
    luma_mean: float
    luma_std: float
    saturation_mean: float
    warm_bias: float
    shadow_ratio: float
    midtone_ratio: float
    highlight_ratio: float
    histogram: np.ndarray
    dominant: list[tuple[tuple[int, int, int], float]]


def load_rgb(path: str) -> np.ndarray:
    with Image.open(path) as im:
        im = im.convert("RGB")
        w, h = im.size
        scale = ANALYSIS_WIDTH / w
        im = im.resize((ANALYSIS_WIDTH, max(1, round(h * scale))), Image.LANCZOS)
        return np.asarray(im, dtype=np.float32) / 255.0


def compute(rgb: np.ndarray) -> Metrics:
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b

    cmax = rgb.max(axis=-1)
    cmin = rgb.min(axis=-1)
    saturation = np.where(cmax > 1e-6, (cmax - cmin) / np.maximum(cmax, 1e-6), 0.0)

    hist, _ = np.histogram(luma, bins=16, range=(0.0, 1.0))
    hist = hist.astype(np.float64) / luma.size

    quantized = (rgb * 7).round().astype(np.int32)
    keys = quantized[..., 0] * 64 + quantized[..., 1] * 8 + quantized[..., 2]
    counts = np.bincount(keys.ravel(), minlength=512)
    order = np.argsort(counts)[::-1][:5]
    dominant = []
    for key in order:
        if counts[key] == 0:
            continue
        qr, rem = divmod(int(key), 64)
        qg, qb = divmod(rem, 8)
        dominant.append((
            (round(qr * 255 / 7), round(qg * 255 / 7), round(qb * 255 / 7)),
            counts[key] / keys.size,
        ))

    return Metrics(
        luma_mean=float(luma.mean()),
        luma_std=float(luma.std()),
        saturation_mean=float(saturation.mean()),
        warm_bias=float((r - b).mean()),
        shadow_ratio=float((luma < 0.25).mean()),
        midtone_ratio=float(((luma >= 0.25) & (luma < 0.65)).mean()),
        highlight_ratio=float((luma >= 0.65).mean()),
        histogram=hist,
        dominant=dominant,
    )


def pct_delta(actual: float, target: float) -> str:
    if abs(target) < 1e-6:
        return "目标接近 0，不做百分比比较"
    delta = (actual - target) / abs(target) * 100.0
    direction = "高" if delta > 0 else "低"
    return f"{direction} {abs(delta):.0f}%"


def swatch_text(dominant: list[tuple[tuple[int, int, int], float]]) -> str:
    return "  ".join(f"#{r:02X}{g:02X}{b:02X}({share * 100:.0f}%)" for (r, g, b), share in dominant)


def hue_name(rgb: tuple[int, int, int]) -> str:
    h, _, _ = colorsys.rgb_to_hsv(*(c / 255 for c in rgb))
    deg = h * 360
    for lo, hi, name in [
        (0, 20, "红"), (20, 45, "橙"), (45, 70, "黄"), (70, 160, "绿"),
        (160, 200, "青"), (200, 260, "蓝"), (260, 320, "紫"), (320, 361, "品红"),
    ]:
        if lo <= deg < hi:
            return name
    return "灰"


def build_report(target: Metrics, actual: Metrics) -> str:
    lines: list[str] = []
    lines.append("画面差距报告（左目标 / 右实机）")
    lines.append("=" * 60)
    lines.append(f"{'指标':<14}{'目标':>10}{'实机':>10}   差距")
    lines.append("-" * 60)

    rows = [
        ("平均亮度", target.luma_mean, actual.luma_mean),
        ("对比度(标准差)", target.luma_std, actual.luma_std),
        ("平均饱和度", target.saturation_mean, actual.saturation_mean),
        ("暗部占比", target.shadow_ratio, actual.shadow_ratio),
        ("中间调占比", target.midtone_ratio, actual.midtone_ratio),
        ("高光占比", target.highlight_ratio, actual.highlight_ratio),
    ]
    for name, t, a in rows:
        lines.append(f"{name:<14}{t:>10.3f}{a:>10.3f}   {pct_delta(a, t)}")

    lines.append(f"{'暖色偏向(R-B)':<14}{target.warm_bias:>10.3f}{actual.warm_bias:>10.3f}   "
                 f"{'实机更冷' if actual.warm_bias < target.warm_bias else '实机更暖'}")

    hist_l1 = float(np.abs(target.histogram - actual.histogram).sum())
    lines.append("-" * 60)
    lines.append(f"亮度直方图 L1 距离: {hist_l1:.3f}（0 完全一致，2 完全不重叠）")
    lines.append(f"目标主色: {swatch_text(target.dominant)}")
    lines.append(f"实机主色: {swatch_text(actual.dominant)}")

    lines.append("")
    lines.append("可执行的调整方向")
    lines.append("-" * 60)
    suggestions: list[str] = []

    if actual.luma_mean > target.luma_mean * 1.15:
        suggestions.append("整体过亮：降低环境光强度与曝光，或加深天空/背景基色。")
    elif actual.luma_mean < target.luma_mean * 0.85:
        suggestions.append("整体过暗：提高主光强度或环境光，检查是否缺少补光。")

    if actual.luma_std < target.luma_std * 0.85:
        suggestions.append("对比度不足：画面偏平。加强主光与阴影的比值，或引入局部强光源制造亮暗分离。")
    elif actual.luma_std > target.luma_std * 1.2:
        if actual.shadow_ratio >= target.shadow_ratio:
            suggestions.append("对比度过强：暗部可能死黑、高光可能过曝。补一层环境光或降低主光强度。")
        else:
            suggestions.append("亮度跨度过大但暗部不足：画面缺少统一的影调基底。应收窄高光范围并压低整体基调，而不是继续提亮暗部。")

    if actual.saturation_mean < target.saturation_mean * 0.8:
        suggestions.append("饱和度不足：材质基色过灰。提高关键物件的基色饱和度，或加入色调分级。")
    elif actual.saturation_mean > target.saturation_mean * 1.25:
        suggestions.append("饱和度过高：容易显得廉价。收敛调色板，向有限色系靠拢。")

    if actual.warm_bias < target.warm_bias - 0.03:
        suggestions.append("色温偏冷：目标画面更暖。调整主光颜色向橙黄偏移，或增加暖色实用光源。")
    elif actual.warm_bias > target.warm_bias + 0.03:
        suggestions.append("色温偏暖：目标画面更冷。降低主光的红色分量，或增加冷色环境光。")

    if actual.shadow_ratio < target.shadow_ratio * 0.7:
        suggestions.append("暗部不够：目标画面有大面积暗区。收缩光照覆盖范围，让画面留出真正的黑。")
    if actual.highlight_ratio < target.highlight_ratio * 0.6:
        suggestions.append("缺少高光：目标画面有明确的亮点。加入自发光元素或镜面高光强的材质。")

    target_hues = {hue_name(c) for c, share in target.dominant[:3]}
    actual_hues = {hue_name(c) for c, share in actual.dominant[:3]}
    missing = target_hues - actual_hues
    if missing:
        suggestions.append(f"主色缺失：目标画面的主导色系包含 {'、'.join(sorted(missing))}，实机没有对应色块。")

    if not suggestions:
        suggestions.append("各项指标均在目标的可接受区间内，可以进入下一层细节打磨。")

    for i, s in enumerate(suggestions, 1):
        lines.append(f"{i}. {s}")

    return "\n".join(lines)


def make_side_by_side(target_path: str, actual_path: str, out_path: str) -> None:
    with Image.open(target_path) as t, Image.open(actual_path) as a:
        t = t.convert("RGB")
        a = a.convert("RGB")
        height = 720
        t = t.resize((round(t.width * height / t.height), height), Image.LANCZOS)
        a = a.resize((round(a.width * height / a.height), height), Image.LANCZOS)

        label_h = 34
        canvas = Image.new("RGB", (t.width + a.width + 12, height + label_h), (18, 18, 20))
        canvas.paste(t, (0, label_h))
        canvas.paste(a, (t.width + 12, label_h))

        draw = ImageDraw.Draw(canvas)
        draw.text((8, 9), "TARGET  (concept)", fill=(235, 235, 235))
        draw.text((t.width + 20, 9), "ACTUAL  (in-engine)", fill=(120, 220, 160))

        os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
        canvas.save(out_path, quality=92)


def main() -> int:
    parser = argparse.ArgumentParser(description="对比实机截图与目标概念图")
    parser.add_argument("--target", required=True, help="目标参考图路径")
    parser.add_argument("--actual", required=True, help="实机截图路径")
    parser.add_argument("--out", default="artifacts/compare/comparison.png", help="并排对比图输出路径")
    parser.add_argument("--report", default=None, help="文字报告输出路径，缺省只打印到标准输出")
    args = parser.parse_args()

    target_metrics = compute(load_rgb(args.target))
    actual_metrics = compute(load_rgb(args.actual))
    report = build_report(target_metrics, actual_metrics)

    make_side_by_side(args.target, args.actual, args.out)
    print(report)
    print(f"\n并排对比图: {args.out}")

    if args.report:
        os.makedirs(os.path.dirname(args.report) or ".", exist_ok=True)
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(report + "\n")
        print(f"文字报告: {args.report}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
