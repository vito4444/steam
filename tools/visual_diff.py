#!/usr/bin/env python3
"""
视觉差距量化工具。

两种用法：

  1. 与目标参考图对比（美术方向差距）
     python3 tools/visual_diff.py 当前截图.png 目标概念图.png --out selfcheck/0001

  2. 与上一版截图对比（视觉回归检测）
     python3 tools/visual_diff.py 新截图.png 旧截图.png --out selfcheck/0001 --regression

产出：
  metrics.json   六项量化指标及其差值
  compare.png    并排对比图，底部叠加指标条
  diff.png       逐像素差异热力图（仅在两图尺寸相同时生成）
  report.md      指标摘要与自动判读

指标说明见 docs/03-tech-feasibility.md 第五节第 2 层。
"""

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

# 判定阈值。超过这些差值时报告会标记为「明显差距」。
THRESHOLDS = {
    "brightness_mean": 0.08,
    "contrast": 0.06,
    "saturation_mean": 0.10,
    "dark_ratio": 0.12,
    "edge_density": 0.04,
    "centroid_shift": 0.10,
}


def load_rgb(path: Path, size=None) -> np.ndarray:
    """读图为 float32 RGB 数组，取值范围 0..1。"""
    img = Image.open(path).convert("RGB")
    if size is not None and img.size != size:
        img = img.resize(size, Image.LANCZOS)
    return np.asarray(img, dtype=np.float32) / 255.0


def to_gray(rgb: np.ndarray) -> np.ndarray:
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def to_hsv_sv(rgb: np.ndarray):
    """只返回 HSV 里的 S 和 V 两个通道，H 对差距判断意义不大。"""
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    return sat, mx


def kmeans_palette(rgb: np.ndarray, k: int = 6, iters: int = 24, seed: int = 0):
    """
    朴素 k-means 提取主色板。为了速度，先把图降采样到最多 20000 个像素。
    返回 [(颜色十六进制, 占比), ...]，按占比降序。
    """
    pixels = rgb.reshape(-1, 3)
    if len(pixels) > 20000:
        rng = np.random.default_rng(seed)
        pixels = pixels[rng.choice(len(pixels), 20000, replace=False)]

    rng = np.random.default_rng(seed)
    centers = pixels[rng.choice(len(pixels), k, replace=False)].copy()

    labels = np.zeros(len(pixels), dtype=np.int32)
    for _ in range(iters):
        d = ((pixels[:, None, :] - centers[None, :, :]) ** 2).sum(axis=2)
        new_labels = d.argmin(axis=1)
        if np.array_equal(new_labels, labels):
            break
        labels = new_labels
        for i in range(k):
            m = labels == i
            if m.any():
                centers[i] = pixels[m].mean(axis=0)

    out = []
    for i in range(k):
        share = float((labels == i).mean())
        c = (centers[i] * 255).clip(0, 255).astype(int)
        out.append({"hex": "#{:02X}{:02X}{:02X}".format(*c), "rgb": c.tolist(), "share": round(share, 4)})
    out.sort(key=lambda x: -x["share"])
    return out


def edge_density(gray: np.ndarray, threshold: float = 0.08) -> float:
    """Sobel 梯度幅值超过阈值的像素占比，用来衡量画面细节量。"""
    kx = np.array([[-1, 0, 1], [-2, 0, 2], [-1, 0, 1]], dtype=np.float32)
    ky = kx.T

    def conv(a, k):
        p = np.pad(a, 1, mode="edge")
        acc = np.zeros_like(a)
        for i in range(3):
            for j in range(3):
                acc += k[i, j] * p[i:i + a.shape[0], j:j + a.shape[1]]
        return acc

    mag = np.hypot(conv(gray, kx), conv(gray, ky))
    return float((mag > threshold).mean())


def luminance_centroid(gray: np.ndarray):
    """亮度加权质心，归一化到 0..1。反映视觉焦点位置。"""
    h, w = gray.shape
    total = gray.sum()
    if total < 1e-6:
        return [0.5, 0.5]
    ys, xs = np.mgrid[0:h, 0:w]
    cx = float((gray * xs).sum() / total / max(w - 1, 1))
    cy = float((gray * ys).sum() / total / max(h - 1, 1))
    return [round(cx, 4), round(cy, 4)]


def ssim(a: np.ndarray, b: np.ndarray, win: int = 8) -> float:
    """
    分块 SSIM，够用于回归检测。灰度输入。
    分块而非高斯窗是为了避免引入 scipy 依赖。
    """
    c1, c2 = 0.01 ** 2, 0.03 ** 2
    h, w = a.shape
    h, w = h - h % win, w - w % win
    a = a[:h, :w].reshape(h // win, win, w // win, win).transpose(0, 2, 1, 3).reshape(-1, win * win)
    b = b[:h, :w].reshape(h // win, win, w // win, win).transpose(0, 2, 1, 3).reshape(-1, win * win)
    mu_a, mu_b = a.mean(axis=1), b.mean(axis=1)
    va, vb = a.var(axis=1), b.var(axis=1)
    cov = ((a - mu_a[:, None]) * (b - mu_b[:, None])).mean(axis=1)
    s = ((2 * mu_a * mu_b + c1) * (2 * cov + c2)) / ((mu_a ** 2 + mu_b ** 2 + c1) * (va + vb + c2))
    return float(s.mean())


def measure(rgb: np.ndarray) -> dict:
    gray = to_gray(rgb)
    sat, _ = to_hsv_sv(rgb)
    return {
        "brightness_mean": round(float(gray.mean()), 4),
        "brightness_median": round(float(np.median(gray)), 4),
        "dark_ratio": round(float((gray < 0.15).mean()), 4),
        "bright_ratio": round(float((gray > 0.85).mean()), 4),
        "contrast": round(float(gray.std()), 4),
        "saturation_mean": round(float(sat.mean()), 4),
        "saturation_p90": round(float(np.percentile(sat, 90)), 4),
        "edge_density": round(edge_density(gray), 4),
        "centroid": luminance_centroid(gray),
        "palette": kmeans_palette(rgb),
    }


def make_compare(cur_path: Path, tgt_path: Path, out: Path, cur_m: dict, tgt_m: dict,
                 cur_label: str, tgt_label: str):
    """并排对比图，下方一条色板带，便于肉眼直接看配色差距。"""
    a = Image.open(cur_path).convert("RGB")
    b = Image.open(tgt_path).convert("RGB")
    h = 720
    a = a.resize((int(a.width * h / a.height), h), Image.LANCZOS)
    b = b.resize((int(b.width * h / b.height), h), Image.LANCZOS)

    gap, bar, label_h = 16, 56, 34
    W = a.width + gap + b.width
    H = label_h + h + bar
    canvas = Image.new("RGB", (W, H), (18, 18, 20))
    d = ImageDraw.Draw(canvas)

    d.text((6, 10), cur_label, fill=(235, 235, 235))
    d.text((a.width + gap + 6, 10), tgt_label, fill=(235, 235, 235))
    canvas.paste(a, (0, label_h))
    canvas.paste(b, (a.width + gap, label_h))

    def draw_palette(x0, width, palette):
        cursor = x0
        for entry in palette:
            seg = max(1, int(width * entry["share"]))
            d.rectangle([cursor, label_h + h, cursor + seg, H], fill=tuple(entry["rgb"]))
            cursor += seg
        d.rectangle([x0, label_h + h, x0 + width, H], outline=(60, 60, 60))

    draw_palette(0, a.width, cur_m["palette"])
    draw_palette(a.width + gap, b.width, tgt_m["palette"])
    canvas.save(out)


def make_diff(cur: np.ndarray, tgt: np.ndarray, out: Path) -> float:
    """逐像素差异热力图。返回变化像素占比。"""
    d = np.abs(cur - tgt).max(axis=2)
    changed = float((d > 0.04).mean())
    heat = np.zeros((*d.shape, 3), dtype=np.float32)
    base = to_gray(cur) * 0.22
    heat[..., 0] = np.clip(base + d * 2.4, 0, 1)
    heat[..., 1] = np.clip(base + d * 0.5, 0, 1)
    heat[..., 2] = np.clip(base, 0, 1)
    Image.fromarray((heat * 255).astype(np.uint8)).save(out)
    return changed


def main() -> int:
    p = argparse.ArgumentParser(description="量化当前截图与目标图之间的视觉差距")
    p.add_argument("current", type=Path, help="当前成果截图")
    p.add_argument("target", type=Path, help="目标参考图（概念图或上一版截图）")
    p.add_argument("--out", type=Path, required=True, help="输出目录")
    p.add_argument("--regression", action="store_true",
                   help="回归模式：两图应为同一基准点的不同版本，会计算 SSIM 和差异图")
    args = p.parse_args()

    for path in (args.current, args.target):
        if not path.exists():
            print(f"文件不存在：{path}", file=sys.stderr)
            return 1

    args.out.mkdir(parents=True, exist_ok=True)

    cur_img = Image.open(args.current).convert("RGB")
    cur = load_rgb(args.current)
    tgt = load_rgb(args.target, size=cur_img.size)

    cur_m, tgt_m = measure(cur), measure(tgt)

    deltas = {}
    for key in THRESHOLDS:
        if key == "centroid_shift":
            dx = cur_m["centroid"][0] - tgt_m["centroid"][0]
            dy = cur_m["centroid"][1] - tgt_m["centroid"][1]
            deltas[key] = round(float(np.hypot(dx, dy)), 4)
        else:
            deltas[key] = round(cur_m[key] - tgt_m[key], 4)

    result = {
        "current": str(args.current),
        "target": str(args.target),
        "mode": "regression" if args.regression else "art-direction",
        "current_metrics": cur_m,
        "target_metrics": tgt_m,
        "deltas": deltas,
        "thresholds": THRESHOLDS,
    }

    if args.regression:
        result["ssim"] = round(ssim(to_gray(cur), to_gray(tgt)), 4)
        result["changed_pixel_ratio"] = round(make_diff(cur, tgt, args.out / "diff.png"), 4)

    # PIL 内置位图字体不含中文字形，标签一律用英文，避免渲染成方块。
    make_compare(args.current, args.target, args.out / "compare.png", cur_m, tgt_m,
                 f"CURRENT  {args.current.name}", f"TARGET  {args.target.name}")

    (args.out / "metrics.json").write_text(json.dumps(result, ensure_ascii=False, indent=2))

    # ------------------------------------------------------------ 报告
    lines = [
        "# 视觉差距报告",
        "",
        f"- 当前：`{args.current}`",
        f"- 目标：`{args.target}`",
        f"- 模式：{'视觉回归检测' if args.regression else '美术方向差距'}",
        "",
        "## 量化指标",
        "",
        "| 指标 | 当前 | 目标 | 差值 | 阈值 | 判定 |",
        "| --- | --- | --- | --- | --- | --- |",
    ]

    readable = {
        "brightness_mean": "平均亮度",
        "contrast": "对比度（灰度标准差）",
        "saturation_mean": "平均饱和度",
        "dark_ratio": "暗部占比（亮度<0.15）",
        "edge_density": "边缘密度（细节量）",
        "centroid_shift": "视觉重心偏移",
    }

    flagged = []
    for key, thr in THRESHOLDS.items():
        delta = deltas[key]
        over = abs(delta) > thr
        if key == "centroid_shift":
            cur_v = f"({cur_m['centroid'][0]}, {cur_m['centroid'][1]})"
            tgt_v = f"({tgt_m['centroid'][0]}, {tgt_m['centroid'][1]})"
        else:
            cur_v, tgt_v = cur_m[key], tgt_m[key]
        mark = "**明显差距**" if over else "接近"
        if over:
            flagged.append((readable[key], delta, thr))
        lines.append(f"| {readable[key]} | {cur_v} | {tgt_v} | {delta:+} | {thr} | {mark} |")

    if args.regression:
        lines += [
            "",
            f"- 结构相似度 SSIM：**{result['ssim']}**（1.0 表示完全相同）",
            f"- 变化像素占比：**{result['changed_pixel_ratio']}**",
        ]

    lines += ["", "## 主色板对比", "", "| 位次 | 当前 | 占比 | 目标 | 占比 |", "| --- | --- | --- | --- | --- |"]
    for i in range(min(len(cur_m["palette"]), len(tgt_m["palette"]))):
        c, t = cur_m["palette"][i], tgt_m["palette"][i]
        lines.append(f"| {i + 1} | `{c['hex']}` | {c['share']:.1%} | `{t['hex']}` | {t['share']:.1%} |")

    lines += ["", "## 自动判读", ""]
    if not flagged:
        lines.append("六项指标全部在阈值内。仍需肉眼核对 `compare.png`——指标接近不代表画面对味。")
    else:
        lines.append("以下指标超出阈值，需要处理：")
        lines.append("")
        for name, delta, thr in flagged:
            direction = "偏高" if delta > 0 else "偏低"
            lines.append(f"- **{name}** {direction} {abs(delta)}（阈值 {thr}）")

    lines += [
        "",
        "## 下一步",
        "",
        "指标只负责发现问题，定位原因必须看图。打开 `compare.png` 逐条写出具体差距，",
        "格式参考 `docs/03-tech-feasibility.md` 第六节的差距清单写法：",
        "每条要写清「概念图是什么样、成果图是什么样、原因、改法」。",
        "",
    ]

    (args.out / "report.md").write_text("\n".join(lines))

    print(f"输出目录：{args.out}")
    print(f"  compare.png   并排对比图")
    if args.regression:
        print(f"  diff.png      差异热力图（SSIM={result['ssim']}）")
    print(f"  metrics.json  量化指标")
    print(f"  report.md     差距报告（{len(flagged)} 项超阈值）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
