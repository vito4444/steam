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

# 指标分成两组，因为它们该对标的东西不一样。
#
# composition（构图组）：衡量画面的信息密度与影调层次。这一组应该对标那些
#   已经被市场验证过的同类画面，因为"一屏里有多少可读的东西""暗部压不压得住"
#   是通用的画面质量问题，与题材无关。
#
# palette（配色组）：衡量色彩倾向。这一组不能盲目对标别人的截图——参考作品
#   的配色服务的是它自己的题材。照抄只会得到一个复制品。所以配色应该对标
#   项目自己定义的配色档案（--profile）。
#
# ---- 两条用这个工具时反复踩过的坑 ----
#
# 一、要量的是玩家看到的那一帧，也就是 artifacts/playtest/ 下真跑出来的截图，
#     不是 artifacts/screenshots/ 里的美术探针。探针场景没有界面，
#     而界面占了实际画面很大一块面积，配色档案的目标值是按完整画面定的。
#     拿探针场景去对配色档案，绿色占比会低到目标的六分之一，
#     照着那个读数调光只会把场景越调越绿。探针场景只看构图组。
#
# 二、有几项和参考图的差距来自美术方向不同，不是缺陷，追平它们会毁掉方向。
#     参考图（IRON NEST）是一间灯火通明的驾驶舱，本作是一间只有三盏灯的暗房。
#     亮部上限、对比度、细节密度这三项要求画面里有大面积的明亮可读结构，
#     暗房设定下不可能同时满足它和"只有光照到的地方才亮"。
#     这三项当作参考读数看趋势，不当作验收线。
COMPOSITION_METRICS = [
    ("luma_mean", "平均亮度", "画面整体明暗", 0.05),
    ("luma_p05", "暗部下限", "暗部是否死黑", 0.04),
    ("luma_p95", "亮部上限", "亮部是否够亮", 0.06),
    ("shadow_ratio", "死黑占比", "亮度<0.06 的像素比例", 0.08),
    ("highlight_ratio", "过曝占比", "亮度>0.90 的像素比例", 0.04),
    ("contrast_rms", "对比度", "亮度标准差", 0.04),
    ("edge_density", "细节密度", "可读结构的丰富程度", 0.06),
]

PALETTE_METRICS = [
    ("saturation_mean", "平均饱和度", "画面色彩浓度", 0.06),
    ("warm_ratio", "暖色占比", "红橙黄区域", 0.08),
    ("green_ratio", "绿色占比", "黄绿到青区域", 0.08),
    ("cool_ratio", "冷色占比", "青到紫蓝区域", 0.08),
]

# 内置配色档案。night-watch 对应方案 A（冷战监听站）：
# 绿色 CRT 是标志色应当主导，暖黄台灯提供必要的色彩对比，冷蓝只做边缘点缀。
# 这套数值是设计目标，不是从任何参考图测出来的。
PROFILES = {
    "night-watch": {
        "description": "方案 A 深夜监听站：绿色 CRT 主导，暖黄台灯对比，冷蓝边缘点缀",
        "saturation_mean": 0.45,
        "warm_ratio": 0.22,
        "green_ratio": 0.38,
        "cool_ratio": 0.10,
    },
}


def _build_rows(stats: FrameStats, metrics, target_lookup):
    rows, gaps = [], []
    for key, label, note, tolerance in metrics:
        target = target_lookup(key)
        if target is None:
            continue
        mine = getattr(stats, key)
        diff = mine - target
        within = abs(diff) <= tolerance
        rows.append({
            "metric": key, "label": label, "note": note,
            "mine": round(mine, 4), "target": round(target, 4),
            "diff": round(diff, 4), "tolerance": tolerance, "ok": within,
        })
        if not within:
            gaps.append({"label": label, "diff": round(diff, 4),
                         "tolerance": tolerance, "severity": round(abs(diff) / tolerance, 2)})
    gaps.sort(key=lambda g: -g["severity"])
    return rows, gaps


def compare(shot: Path, reference: Path | None, profile: dict | None) -> dict:
    a = analyze(shot)
    result = {"shot": asdict(a), "reference": None, "profile": None,
              "composition_rows": [], "composition_gaps": [],
              "palette_rows": [], "palette_gaps": []}

    if reference is not None:
        b = analyze(reference)
        result["reference"] = asdict(b)
        result["composition_rows"], result["composition_gaps"] = _build_rows(
            a, COMPOSITION_METRICS, lambda k: getattr(b, k))

    if profile is not None:
        result["profile"] = profile
        result["palette_rows"], result["palette_gaps"] = _build_rows(
            a, PALETTE_METRICS, lambda k: profile.get(k))

    return result


def _print_table(title: str, subtitle: str, rows: list, gaps: list) -> None:
    if not rows:
        return
    print()
    print(f"  【{title}】{subtitle}")
    print(f"  {'指标':<12} {'我的':>9} {'目标':>9} {'差值':>9} {'容差':>7}  判定   说明")
    print("  " + "-" * 78)
    for row in rows:
        verdict = " 达标 " if row["ok"] else "偏差大"
        print(f"  {row['label']:<12} {row['mine']:>9.4f} {row['target']:>9.4f} "
              f"{row['diff']:>+9.4f} {row['tolerance']:>7.3f}  {verdict}  {row['note']}")
    if not gaps:
        print("  → 全部在容差内。")
    else:
        for gap in gaps:
            direction = "高于" if gap["diff"] > 0 else "低于"
            print(f"  → {gap['label']}: {direction}目标 {abs(gap['diff']):.4f}"
                  f"（容差 {gap['tolerance']}，超出 {gap['severity']:.1f} 倍）")


def print_report(result: dict) -> None:
    shot = result["shot"]
    print(f"\n  我的截图: {shot['path']}  ({shot['width']}x{shot['height']})")
    if result["reference"]:
        ref = result["reference"]
        print(f"  构图对标: {ref['path']}  ({ref['width']}x{ref['height']})")
    if result["profile"]:
        print(f"  配色档案: {result['profile'].get('description', '（无描述）')}")

    _print_table("构图与影调", "对标参考截图，衡量信息密度与明暗层次",
                 result["composition_rows"], result["composition_gaps"])
    _print_table("配色", "对标项目自定义档案，不照抄参考图的色彩倾向",
                 result["palette_rows"], result["palette_gaps"])

    def hue_str(entries):
        return "  ".join(f"{HUE_NAMES.get(h, str(h))}:{v:.1%}" for h, v in entries) or "（无显著色相）"

    print()
    print(f"  我的主导色相: {hue_str(shot['dominant_hues'])}")
    if result["reference"]:
        print(f"  参考主导色相: {hue_str(result['reference']['dominant_hues'])}  "
              f"（仅供参考，配色不以此为目标）")

    total = len(result["composition_gaps"]) + len(result["palette_gaps"])
    print()
    print("  结论: 全部指标在容差内。" if total == 0
          else f"  结论: {len(result['composition_gaps'])} 项构图偏差 + "
               f"{len(result['palette_gaps'])} 项配色偏差。")


def main() -> int:
    parser = argparse.ArgumentParser(description="画面自检：截图与构图参考、配色档案的量化对比")
    parser.add_argument("--shot", type=Path, help="我方渲染的截图")
    parser.add_argument("--reference", type=Path, help="构图与影调的对标参考图")
    parser.add_argument("--profile", help=f"配色档案名，内置: {', '.join(PROFILES)}；也可传 JSON 文件路径")
    parser.add_argument("--manifest", type=Path,
                        help="JSON 清单，元素格式 {\"shot\":..., \"reference\":..., \"profile\":...}")
    parser.add_argument("--json-out", type=Path, help="把完整结果写成 JSON")
    parser.add_argument("--list-profiles", action="store_true", help="列出内置配色档案后退出")
    args = parser.parse_args()

    if args.list_profiles:
        for name, prof in PROFILES.items():
            print(f"{name}: {prof['description']}")
            for k, v in prof.items():
                if k != "description":
                    print(f"    {k} = {v}")
        return 0

    def resolve_profile(value):
        if not value:
            return None
        if value in PROFILES:
            return PROFILES[value]
        path = Path(value)
        if path.exists():
            return json.loads(path.read_text())
        print(f"未知的配色档案: {value}", file=sys.stderr)
        raise SystemExit(2)

    jobs = []
    if args.manifest:
        for entry in json.loads(args.manifest.read_text()):
            jobs.append((Path(entry["shot"]),
                         Path(entry["reference"]) if entry.get("reference") else None,
                         resolve_profile(entry.get("profile"))))
    elif args.shot:
        jobs.append((args.shot,
                     args.reference,
                     resolve_profile(args.profile)))
    else:
        parser.error("需要 --shot（可选配 --reference / --profile），或者 --manifest")

    results = []
    for shot, reference, profile in jobs:
        if reference is None and profile is None:
            print("至少要给出 --reference 或 --profile 之一", file=sys.stderr)
            return 2
        for p in (shot, reference):
            if p is not None and not p.exists():
                print(f"文件不存在: {p}", file=sys.stderr)
                return 2
        result = compare(shot, reference, profile)
        print_report(result)
        results.append(result)

    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(results, ensure_ascii=False, indent=1))
        print(f"\n  完整结果已写入 {args.json_out}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
