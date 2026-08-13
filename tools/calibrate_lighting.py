#!/usr/bin/env python3
"""照明标定：在同一份构建里扫描光强组合，挑出影调分布最接近概念图的那一组。

本项目的实机画面只能通过无头截图观察，人眼调参这条路不存在。这个脚本把
「调参 → 截图 → 与概念图比对」变成一次可重复执行的搜索：对每组参数跑一次
构建产物，量化平均亮度、暗部占比、对比度与色温，按加权距离排序。

用法：
    python3 tools/calibrate_lighting.py --target docs/concepts/images/xxx.jpg
"""

from __future__ import annotations

import argparse
import itertools
import os
import shutil
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLAYER = os.path.join(ROOT, "build", "linux", "MANER")


def measure(path: str) -> dict[str, float]:
    with Image.open(path) as im:
        rgb = np.asarray(im.convert("RGB"), dtype=np.float32) / 255.0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b
    cmax = rgb.max(axis=-1)
    cmin = rgb.min(axis=-1)
    sat = np.where(cmax > 1e-6, (cmax - cmin) / np.maximum(cmax, 1e-6), 0.0)
    return {
        "luma": float(luma.mean()),
        "contrast": float(luma.std()),
        "sat": float(sat.mean()),
        "shadow": float((luma < 0.25).mean()),
        "mid": float(((luma >= 0.25) & (luma < 0.65)).mean()),
        "high": float((luma >= 0.65).mean()),
        "warm": float((r - b).mean()),
    }


def run_once(args_extra: list[str], width: int, height: int, timeout: int = 150) -> str | None:
    outdir = tempfile.mkdtemp(prefix="maner_cal_")
    cmd = [
        "xvfb-run", "-a", "-s", f"-screen 0 {width}x{height}x24",
        PLAYER,
        "-screen-width", str(width), "-screen-height", str(height), "-screen-fullscreen", "0",
        "-manerShots", "1.4", "-manerOut", outdir,
        "-logFile", os.path.join(outdir, "run.log"),
    ] + args_extra
    try:
        subprocess.run(cmd, timeout=timeout, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    except subprocess.TimeoutExpired:
        shutil.rmtree(outdir, ignore_errors=True)
        return None

    shots = [f for f in os.listdir(outdir) if f.endswith(".png")]
    if not shots:
        shutil.rmtree(outdir, ignore_errors=True)
        return None
    return os.path.join(outdir, shots[0])


def score(actual: dict[str, float], target: dict[str, float]) -> float:
    """加权距离。影调分布比绝对亮度更重要，因为它决定画面的气质。"""
    weights = {"luma": 2.2, "contrast": 1.6, "shadow": 2.6, "mid": 1.0, "high": 1.2, "warm": 0.9, "sat": 0.5}
    total = 0.0
    for key, w in weights.items():
        total += w * abs(actual[key] - target[key])
    return total


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", required=True)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--key", default="0.35,0.6,0.9,1.3")
    parser.add_argument("--bulb", default="4,10,22,45")
    parser.add_argument("--exposure", default="-0.3,0.1")
    args = parser.parse_args()

    if not os.path.exists(PLAYER):
        print(f"找不到构建产物 {PLAYER}，先运行 tools/build.sh linux", file=sys.stderr)
        return 1

    target = measure(args.target)
    print("目标影调:", {k: round(v, 4) for k, v in target.items()})
    print()

    keys = [float(x) for x in args.key.split(",")]
    bulbs = [float(x) for x in args.bulb.split(",")]
    exposures = [float(x) for x in args.exposure.split(",")]

    results = []
    for k, bulb, ev in itertools.product(keys, bulbs, exposures):
        extra = [
            "-manerKey", str(k),
            "-manerFill", str(round(k * 0.16, 4)),
            "-manerBulb", str(bulb),
            "-manerWash", str(round(bulb * 0.6, 3)),
            "-manerExposure", str(ev),
        ]
        shot = run_once(extra, args.width, args.height)
        if shot is None:
            print(f"key={k} bulb={bulb} ev={ev} -> 运行失败")
            continue

        m = measure(shot)
        s = score(m, target)
        results.append((s, k, bulb, ev, m))
        print(f"key={k:<5} bulb={bulb:<5} ev={ev:<5} | "
              f"亮度 {m['luma']:.3f} 暗部 {m['shadow']:.3f} 对比 {m['contrast']:.3f} "
              f"高光 {m['high']:.3f} 暖色 {m['warm']:+.3f} | 距离 {s:.3f}")
        shutil.rmtree(os.path.dirname(shot), ignore_errors=True)

    if not results:
        print("没有任何一次运行成功", file=sys.stderr)
        return 1

    results.sort(key=lambda x: x[0])
    best = results[0]
    print()
    print(f"最优组合: key={best[1]} fill={round(best[1] * 0.16, 4)} bulb={best[2]} "
          f"wash={round(best[2] * 0.6, 3)} exposure={best[3]}  距离={best[0]:.3f}")
    print("对应影调:", {k: round(v, 4) for k, v in best[4].items()})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
