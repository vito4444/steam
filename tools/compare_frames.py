#!/usr/bin/env python3
"""画面自检：把自己渲染的截图与目标参考图放在同一套指标下量化对比。

这个工具解决的问题是：在没有 GPU、也没有人眼实时判断的自动化环境里，
如何知道"我做出来的画面离目标还差多远"。它不追求感知级的相似度评分，
而是拆出几个开发者能直接据以调参的维度：

  亮度分布   —— 画面整体偏亮还是偏暗，暗部是否死黑，亮部是否过曝
  色彩温度   —— 冷暖倾向，是否符合"暖黄/绿/冷蓝"三光源的配色目标
  色彩饱和度 —— 画面是灰扑扑还是有强色彩记忆点
  对比度     —— 明暗层次是否拉开
  细节密度   —— 边缘密度，近似衡量"信息密度"，即画面里有多少可读的东西
  色相分布   —— 主导色相的占比，用于判断配色方案是否落地

用法:
  tools/compare_frames.py --shot artifacts/screenshots/probe_front.png \\
                          --reference docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg
  tools/compare_frames.py --manifest artifacts/screenshots/manifest.json
"""

from __future__ import annotations

import argparse
import colorsys
import json
import sys
from dataclasses import dataclass, asdict
from pathlib import Path

import numpy as np
from PIL import Image

ANALYSIS_WIDTH = 512


@dataclass
class FrameStats:
    path: str
    width: int
    height: int
    luma_mean: float
    luma_median: float
    luma_p05: float
    luma_p95: float
    shadow_ratio: float
    highlight_ratio: float
    contrast_rms: float
    saturation_mean: float
    saturation_p90: float
    warm_ratio: float
    cool_ratio: float
    green_ratio: float
    edge_density: float
    dominant_hues: list


def load_gray_rgb(path: Path):
    img = Image.open(path).convert("RGB")
    w, h = img.size
    scale = ANALYSIS_WIDTH / w
    img = img.resize((ANALYSIS_WIDTH, max(1, int(round(h * scale)))), Image.LANCZOS)
    rgb = np.asarray(img, dtype=np.float64) / 255.0
    return rgb, w, h


def analyze(path: Path) -> FrameStats:
    rgb, orig_w, orig_h = load_gray_rgb(path)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    # Rec.709 亮度
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b

    cmax = rgb.max(axis=2)
    cmin = rgb.min(axis=2)
    delta = cmax - cmin
    saturation = np.where(cmax > 1e-6, delta / np.maximum(cmax, 1e-6), 0.0)

    # 色相（度），仅统计饱和度足够高的像素，避免灰色像素污染色相分布
    hue = np.zeros_like(cmax)
    mask = delta > 1e-6
    rc, gc, bc = r[mask], g[mask], b[mask]
    dm, cm = delta[mask], cmax[mask]
    h = np.zeros_like(dm)
    is_r = cm == rc
    is_g = (cm == gc) & ~is_r
    is_b = ~is_r & ~is_g
    h[is_r] = ((gc[is_r] - bc[is_r]) / dm[is_r]) % 6
    h[is_g] = (bc[is_g] - rc[is_g]) / dm[is_g] + 2
    h[is_b] = (rc[is_b] - gc[is_b]) / dm[is_b] + 4
    hue[mask] = h * 60.0

    colored = saturation > 0.15
    warm = colored & (((hue >= 0) & (hue < 60)) | (hue >= 300))
    green = colored & (hue >= 60) & (hue < 180)
    cool = colored & (hue >= 180) & (hue < 300)

    # 边缘密度：中心差分梯度超过阈值的像素占比，近似"画面里有多少可读结构"
    gy, gx = np.gradient(luma)
    grad = np.hypot(gx, gy)
    edge_density = float((grad > 0.06).mean())

    total = luma.size
    hue_hist = {}
    if colored.any():
        bins = (hue[colored] // 30).astype(int) * 30
        for bucket in np.unique(bins):
            hue_hist[int(bucket)] = float((bins == bucket).sum() / total)
    dominant = sorted(hue_hist.items(), key=lambda kv: -kv[1])[:4]

    return FrameStats(
        path=str(path),
        width=orig_w,
        height=orig_h,
        luma_mean=float(luma.mean()),
        luma_median=float(np.median(luma)),
        luma_p05=float(np.percentile(luma, 5)),
        luma_p95=float(np.percentile(luma, 95)),
        shadow_ratio=float((luma < 0.06).mean()),
        highlight_ratio=float((luma > 0.90).mean()),
        contrast_rms=float(luma.std()),
        saturation_mean=float(saturation.mean()),
        saturation_p90=float(np.percentile(saturation, 90)),
        warm_ratio=float(warm.mean()),
        cool_ratio=float(cool.mean()),
        green_ratio=float(green.mean()),
        edge_density=edge_density,
        dominant_hues=[[int(k), round(v, 4)] for k, v in dominant],
    )


HUE_NAMES = {
    0: "红", 30: "橙", 60: "黄", 90: "黄绿", 120: "绿", 150: "青绿",
    180: "青", 210: "天蓝", 240: "蓝", 270: "紫蓝", 300: "品红", 330: "玫红",
}

# 每个指标的判读方向：差值为正表示"我的画面这项高于参考"
METRICS = [
    ("luma_mean", "平均亮度", "画面整体明暗", 0.05),
    ("luma_p05", "暗部下限", "暗部是否死黑", 0.04),
    ("luma_p95", "亮部上限", "亮部是否够亮", 0.06),
    ("shadow_ratio", "死黑占比", "亮度<0.06 的像素比例", 0.08),
    ("highlight_ratio", "过曝占比", "亮度>0.90 的像素比例", 0.04),
    ("contrast_rms", "对比度", "亮度标准差", 0.04),
    ("saturation_mean", "平均饱和度", "画面色彩浓度", 0.06),
    ("warm_ratio", "暖色占比", "红橙黄区域", 0.08),
    ("green_ratio", "绿色占比", "黄绿到青区域", 0.08),
    ("cool_ratio", "冷色占比", "青到紫蓝区域", 0.08),
    ("edge_density", "细节密度", "可读结构的丰富程度", 0.06),
]


def compare(shot: Path, reference: Path) -> dict:
    a = analyze(shot)
    b = analyze(reference)
    rows = []
    gaps = []
    for key, label, note, tolerance in METRICS:
        mine = getattr(a, key)
        target = getattr(b, key)
        diff = mine - target
        within = abs(diff) <= tolerance
        rows.append({
            "metric": key, "label": label, "note": note,
            "mine": round(mine, 4), "target": round(target, 4),
            "diff": round(diff, 4), "tolerance": tolerance, "ok": within,
        })
        if not within:
            gaps.append((abs(diff) / tolerance, label, diff, tolerance))
    gaps.sort(reverse=True)
    return {"shot": asdict(a), "reference": asdict(b), "rows": rows,
            "gaps": [{"label": g[1], "diff": round(g[2], 4), "tolerance": g[3],
                      "severity": round(g[0], 2)} for g in gaps]}


def print_report(result: dict) -> None:
    shot = result["shot"]
    ref = result["reference"]
    print(f"\n  我的截图: {shot['path']}  ({shot['width']}x{shot['height']})")
    print(f"  目标参考: {ref['path']}  ({ref['width']}x{ref['height']})")
    print()
    print(f"  {'指标':<12} {'我的':>9} {'目标':>9} {'差值':>9} {'容差':>7}  判定   说明")
    print("  " + "-" * 78)
    for row in result["rows"]:
        verdict = " 达标 " if row["ok"] else "偏差大"
        print(f"  {row['label']:<12} {row['mine']:>9.4f} {row['target']:>9.4f} "
              f"{row['diff']:>+9.4f} {row['tolerance']:>7.3f}  {verdict}  {row['note']}")

    def hue_str(entries):
        return "  ".join(f"{HUE_NAMES.get(h, str(h))}:{v:.1%}" for h, v in entries) or "（无显著色相）"

    print()
    print(f"  我的主导色相: {hue_str(shot['dominant_hues'])}")
    print(f"  目标主导色相: {hue_str(ref['dominant_hues'])}")

    gaps = result["gaps"]
    print()
    if not gaps:
        print("  结论: 全部指标在容差内。")
    else:
        print(f"  结论: {len(gaps)} 项超出容差，按严重程度排序：")
        for gap in gaps:
            direction = "高于" if gap["diff"] > 0 else "低于"
            print(f"    - {gap['label']}: {direction}目标 {abs(gap['diff']):.4f}"
                  f"（容差 {gap['tolerance']}，超出 {gap['severity']:.1f} 倍）")


def main() -> int:
    parser = argparse.ArgumentParser(description="画面自检：截图与目标参考图的量化对比")
    parser.add_argument("--shot", type=Path, help="我方渲染的截图")
    parser.add_argument("--reference", type=Path, help="目标参考图")
    parser.add_argument("--manifest", type=Path,
                        help="JSON 清单，格式 [{\"shot\": ..., \"reference\": ...}, ...]")
    parser.add_argument("--json-out", type=Path, help="把完整结果写成 JSON")
    args = parser.parse_args()

    pairs = []
    if args.manifest:
        pairs = [(Path(e["shot"]), Path(e["reference"]))
                 for e in json.loads(args.manifest.read_text())]
    elif args.shot and args.reference:
        pairs = [(args.shot, args.reference)]
    else:
        parser.error("需要 --shot 与 --reference，或者 --manifest")

    results = []
    for shot, reference in pairs:
        for p in (shot, reference):
            if not p.exists():
                print(f"文件不存在: {p}", file=sys.stderr)
                return 2
        result = compare(shot, reference)
        print_report(result)
        results.append(result)

    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(results, ensure_ascii=False, indent=1))
        print(f"\n  完整结果已写入 {args.json_out}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
