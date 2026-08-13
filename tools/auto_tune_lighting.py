#!/usr/bin/env python3
"""光照自动配平：让画面自检的结果直接驱动参数调整，闭合"运行 → 自检 → 调参 → 再运行"的循环。

手工二分调光在无 GPU 环境里非常慢——每改一个数值都要重建场景、软件渲染、再看图。
这个脚本把这件事自动化：它读画面自检产出的偏差，按启发式规则改写
unity/Decoder/Assets/Config/lighting-probe.json，重跑一轮，记录评分，
迭代若干轮后把评分最好的那组参数写回配置。

评分 = 各项偏差除以各自容差后的加权和，越小越好。脚本只改光照与自发光强度，
不改几何、材质和机位——那些属于设计决策，不该由自动配平代劳。

用法:
  tools/auto_tune_lighting.py --iterations 6 --profile night-watch \\
      --reference docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg
"""

from __future__ import annotations

import argparse
import copy
import json
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONFIG = REPO / "unity/Decoder/Assets/Config/lighting-probe.json"
SHOT = REPO / "artifacts/screenshots/probe_front.png"
TUNE_DIR = REPO / "artifacts/tuning"

sys.path.insert(0, str(REPO / "tools"))
from compare_frames import PROFILES, compare  # noqa: E402

# 每个偏差指标对应哪些参数、往哪个方向推。
# gain 是把"超出容差的倍数"换算成"参数相对变化量"的系数，保守取值避免震荡。
RULES = {
    "绿色占比": [("lights.fillCrtGreen.intensity", 0.28), ("emission.crtScreen", 0.20)],
    "暖色占比": [("lights.keyLampWarm.intensity", 0.16),
                 ("lights.bounceDeskWarm.intensity", 0.30),
                 ("lights.practicalNeon.intensity", 0.20)],
    "冷色占比": [("lights.rimColdWindow.intensity", 0.28)],
    "平均饱和度": [("emission.crtScreen", 0.12), ("emission.neonLamp", 0.12),
                   ("emission.indicatorLamp", 0.14)],
    "平均亮度": [("lights.keyLampWarm.intensity", 0.12),
                 ("lights.fillCrtGreen.intensity", 0.12),
                 ("lights.rimColdWindow.intensity", 0.12),
                 ("lights.bounceDeskWarm.intensity", 0.12)],
    "死黑占比": [("lights.keyLampWarm.range", 0.10),
                 ("lights.fillCrtGreen.range", 0.10),
                 ("lights.rimColdWindow.range", 0.10),
                 ("lights.bounceDeskWarm.range", 0.10),
                 ("lights.practicalNeon.range", 0.10)],
    "暗部下限": [("ambient", 0.35)],
    "亮部上限": [("emission.crtScreen", 0.16), ("emission.neonLamp", 0.14),
                 ("emission.dialStrip", 0.12)],
}

# 参数硬边界，防止自动配平把数值推到物理上没有意义的区间。
BOUNDS = {
    "intensity": (0.05, 24.0),
    "range": (0.4, 8.0),
    "spotAngle": (12.0, 160.0),
    "emission": (0.02, 12.0),
}

# 目标函数权重。两项刻意设为 0：
#
#   细节密度 —— 由几何数量和材质贴图决定。八轮配平实测这一项在 0.050 附近纹丝不动，
#               无论怎么调光都改不了。想提升只能加几何或上贴图。
#   死黑占比 —— 由构图决定。参考图里近 40% 的死黑来自舱壁与管道的自阴影，
#               而探针场景的仪表墙是一整块正对光源的平面，没有可以投影的结构。
#               把它留在目标函数里，只会让配平不断压暗全场去追一个够不着的数字。
#
# 把够不着的指标移出目标函数，配平才能专注在它真正能改善的维度上。
WEIGHTS = {
    "死黑占比": 0.0,
    "细节密度": 0.0,
    "绿色占比": 1.2,
    "暖色占比": 1.2,
    "平均饱和度": 1.1,
}


def bound_for(path: str) -> tuple[float, float]:
    if path.startswith("emission."):
        return BOUNDS["emission"]
    for key in ("intensity", "range", "spotAngle"):
        if path.endswith(key):
            return BOUNDS[key]
    return (0.0001, 100.0)


def get_path(cfg: dict, path: str):
    node = cfg
    for part in path.split("."):
        node = node[part]
    return node


def set_path(cfg: dict, path: str, value: float) -> None:
    parts = path.split(".")
    node = cfg
    for part in parts[:-1]:
        node = node[part]
    node[parts[-1]] = round(value, 5)


def apply_delta(cfg: dict, path: str, relative: float) -> None:
    if path == "ambient":
        for key in ("ambientSky", "ambientEquator", "ambientGround"):
            cfg[key] = [round(max(0.0, v * (1.0 + relative)), 6) for v in cfg[key]]
        return
    lo, hi = bound_for(path)
    current = float(get_path(cfg, path))
    set_path(cfg, path, min(hi, max(lo, current * (1.0 + relative))))


def run(cmd: list[str]) -> None:
    result = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stdout + result.stderr)
        raise SystemExit(f"命令失败: {' '.join(cmd)}")


def evaluate(reference: Path | None, profile: dict | None) -> tuple[float, dict]:
    result = compare(SHOT, reference, profile)
    score = 0.0
    for gap in result["composition_gaps"] + result["palette_gaps"]:
        score += gap["severity"] * WEIGHTS.get(gap["label"], 1.0)
    return score, result


def summarize(result: dict) -> str:
    gaps = result["composition_gaps"] + result["palette_gaps"]
    if not gaps:
        return "全部达标"
    return "  ".join(f"{g['label']}{g['diff']:+.3f}" for g in gaps[:4])


def main() -> int:
    parser = argparse.ArgumentParser(description="按画面自检结果自动配平光照参数")
    parser.add_argument("--iterations", type=int, default=6)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--profile", default="night-watch")
    parser.add_argument("--keep-frames", action="store_true", help="保留每轮截图")
    args = parser.parse_args()

    profile = PROFILES.get(args.profile)
    if args.profile and profile is None:
        print(f"未知配色档案: {args.profile}", file=sys.stderr)
        return 2

    TUNE_DIR.mkdir(parents=True, exist_ok=True)
    cfg = json.loads(CONFIG.read_text())
    best_cfg, best_score, history = copy.deepcopy(cfg), float("inf"), []

    for i in range(args.iterations):
        CONFIG.write_text(json.dumps(cfg, ensure_ascii=False, indent=2) + "\n")
        run(["./tools/build-probe-scene.sh"])
        run(["./tools/capture.sh"])

        score, result = evaluate(args.reference, profile)
        history.append({"iteration": i, "score": round(score, 3),
                        "config": copy.deepcopy(cfg), "summary": summarize(result)})
        marker = ""
        if score < best_score:
            best_score, best_cfg = score, copy.deepcopy(cfg)
            marker = "  <- 当前最优"
        print(f"  第 {i + 1}/{args.iterations} 轮  评分 {score:7.3f}   {summarize(result)}{marker}")

        if args.keep_frames:
            shutil.copy(SHOT, TUNE_DIR / f"iter_{i:02d}.png")
        if score == 0.0:
            print("  已全部达标，提前结束。")
            break

        # 按偏差方向推参数：偏差为正表示"我的画面这项偏高"，需要把对应参数往下调。
        for gap in result["composition_gaps"] + result["palette_gaps"]:
            for path, gain in RULES.get(gap["label"], []):
                step = max(-0.35, min(0.35, -gap["severity"] * gain * 0.5
                                      * (1 if gap["diff"] > 0 else -1)))
                apply_delta(cfg, path, step)

    CONFIG.write_text(json.dumps(best_cfg, ensure_ascii=False, indent=2) + "\n")
    (TUNE_DIR / "history.json").write_text(json.dumps(history, ensure_ascii=False, indent=1))

    print(f"\n  最优评分 {best_score:.3f}，已写回 {CONFIG.relative_to(REPO)}")
    print(f"  迭代记录: {(TUNE_DIR / 'history.json').relative_to(REPO)}")
    print("  用最优参数重建场景并截图...")
    run(["./tools/build-probe-scene.sh"])
    run(["./tools/capture.sh"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
