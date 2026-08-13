#!/usr/bin/env python3
"""Compares a self-check screenshot against the previous run and against its concept art.

Two different comparisons, because they answer two different questions.

Against the previous run it is a pixel diff, and the question is "did anything break".
A material that lost its texture, a light that stopped working, a UI element that drifted
off screen -- these are silent failures that no test catches and that a diff catches
immediately.

Against the concept art it is deliberately *not* a pixel diff, which would be meaningless
between a painted target and a rendered frame. It compares the properties that art
direction is actually made of: value distribution, hue balance, contrast and where the
light sits in the frame. The output is a number for how far apart they are plus a
side-by-side composite to look at.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def load_rgb(path: Path, size: tuple[int, int] | None = None) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if size is not None and image.size != size:
        image = image.resize(size, Image.LANCZOS)
    return np.asarray(image).astype(np.float32) / 255.0


def luminance(rgb: np.ndarray) -> np.ndarray:
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


# --------------------------------------------------------------- regression diff --


def regression_diff(current: Path, previous: Path, out_dir: Path) -> dict:
    """Pixel-level comparison against the previous run of the same checkpoint.

    The raw frames are compared with no denoising, because the capture is bit-exact:
    two consecutive runs of an identical build were measured at a mean absolute
    difference of 0.000000 with zero pixels above the threshold. The self-check fixes
    the frame rate and the warmup frame count, so even the film grain lands identically.

    That matters. It means any non-zero result here is a real change, and blurring the
    frames first -- which an earlier version of this tool did, on the assumption that
    grain would add noise -- would only hide small regressions.
    """
    size = Image.open(current).size

    delta = np.abs(load_rgb(current) - load_rgb(previous, size=size))
    per_pixel = delta.max(axis=2)

    # 2/255 is below what a viewer can see but above encoder noise, so it is a
    # reasonable floor for "this pixel actually changed".
    changed = float((per_pixel > 2.0 / 255.0).mean())

    heat = (np.clip(per_pixel * 6.0, 0.0, 1.0) * 255).astype(np.uint8)
    heatmap = np.zeros((*heat.shape, 3), dtype=np.uint8)
    heatmap[..., 0] = heat
    heatmap[..., 1] = (heat * 0.25).astype(np.uint8)
    Image.fromarray(heatmap).save(out_dir / "diff_vs_previous.png")

    return {
        "mean_abs_difference": round(float(delta.mean()), 5),
        "max_abs_difference": round(float(delta.max()), 5),
        "changed_pixel_fraction": round(changed, 5),
        "heatmap": "diff_vs_previous.png",
    }


# ------------------------------------------------------------- concept art gap --


def art_direction_gap(current: Path, target: Path) -> dict:
    """Structured comparison against the concept art target.

    Everything measured here is a property an art director would actually name, so that
    a bad score points at a specific thing to change rather than at "it looks wrong".
    """
    size = (512, 288)
    render = load_rgb(current, size)
    concept = load_rgb(target, size)

    render_luma = luminance(render)
    concept_luma = luminance(concept)

    def histogram(values: np.ndarray, bins: int = 32) -> np.ndarray:
        hist, _ = np.histogram(values, bins=bins, range=(0.0, 1.0), density=False)
        return hist.astype(np.float32) / max(1, hist.sum())

    # Earth-mover distance between the two tonal distributions: how differently the two
    # images spread their light and dark, independent of where it is in the frame.
    value_emd = float(np.abs(np.cumsum(histogram(render_luma) - histogram(concept_luma))).sum())

    # Mean colour per third of the frame, which is a crude but useful proxy for whether
    # the warm/cool split lands in the same place.
    def thirds(image: np.ndarray) -> list[list[float]]:
        h = image.shape[0]
        return [
            [round(float(c), 4) for c in image[i * h // 3 : (i + 1) * h // 3].reshape(-1, 3).mean(axis=0)]
            for i in range(3)
        ]

    # Where the brightest 5% of the frame sits. In a single-lamp shot this is the light
    # pool, and if it is in a different place than the concept, the composition is wrong.
    def light_centroid(luma: np.ndarray) -> list[float]:
        threshold = np.quantile(luma, 0.95)
        ys, xs = np.nonzero(luma >= threshold)
        if len(xs) == 0:
            return [0.5, 0.5]
        return [round(float(xs.mean() / luma.shape[1]), 4), round(float(ys.mean() / luma.shape[0]), 4)]

    render_centroid = light_centroid(render_luma)
    concept_centroid = light_centroid(concept_luma)
    centroid_distance = float(
        np.hypot(render_centroid[0] - concept_centroid[0], render_centroid[1] - concept_centroid[1])
    )

    render_mean = render.reshape(-1, 3).mean(axis=0)
    concept_mean = concept.reshape(-1, 3).mean(axis=0)

    return {
        "value_distribution_distance": round(value_emd, 4),
        "mean_luma": {
            "render": round(float(render_luma.mean()), 4),
            "concept": round(float(concept_luma.mean()), 4),
        },
        "contrast_stddev": {
            "render": round(float(render_luma.std()), 4),
            "concept": round(float(concept_luma.std()), 4),
        },
        "mean_rgb": {
            "render": [round(float(c), 4) for c in render_mean],
            "concept": [round(float(c), 4) for c in concept_mean],
        },
        "warm_cool_balance": {
            "render": round(float(render_mean[0] - render_mean[2]), 4),
            "concept": round(float(concept_mean[0] - concept_mean[2]), 4),
        },
        "thirds_mean_rgb": {"render": thirds(render), "concept": thirds(concept)},
        "light_pool_centroid": {
            "render": render_centroid,
            "concept": concept_centroid,
            "distance": round(centroid_distance, 4),
        },
    }


# ------------------------------------------------------------------- composite --


def side_by_side(current: Path, target: Path, out_path: Path, labels: tuple[str, str]) -> None:
    width, height = 960, 540
    render = Image.open(current).convert("RGB").resize((width, height), Image.LANCZOS)
    concept = Image.open(target).convert("RGB").resize((width, height), Image.LANCZOS)

    band = 34
    canvas = Image.new("RGB", (width * 2 + 12, height + band), (16, 16, 18))
    canvas.paste(concept, (0, band))
    canvas.paste(render, (width + 12, band))

    draw = ImageDraw.Draw(canvas)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 19)
    except OSError:
        font = ImageFont.load_default()

    draw.text((10, 8), labels[0], fill=(210, 210, 200), font=font)
    draw.text((width + 22, 8), labels[1], fill=(210, 210, 200), font=font)
    canvas.save(out_path)


# ------------------------------------------------------------------------ main --


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", required=True, type=Path, help="screenshot from this run")
    parser.add_argument("--previous", type=Path, help="same checkpoint from the previous run")
    parser.add_argument("--concept", type=Path, help="concept art target for this checkpoint")
    parser.add_argument("--out", required=True, type=Path, help="directory for the report and composites")
    args = parser.parse_args()

    if not args.current.exists():
        print(f"[compare] FATAL: no screenshot at {args.current}", file=sys.stderr)
        return 1

    args.out.mkdir(parents=True, exist_ok=True)
    report: dict = {"current": str(args.current)}

    if args.previous and args.previous.exists():
        report["regression"] = regression_diff(args.current, args.previous, args.out)
        side_by_side(args.current, args.previous, args.out / "compare_vs_previous.png",
                     ("PREVIOUS RUN", "THIS RUN"))
    else:
        report["regression"] = {"skipped": "no previous run to compare against"}

    if args.concept and args.concept.exists():
        report["art_direction"] = art_direction_gap(args.current, args.concept)
        side_by_side(args.current, args.concept, args.out / "compare_vs_concept.png",
                     ("CONCEPT ART TARGET", "CURRENT BUILD"))
    else:
        report["art_direction"] = {"skipped": "no concept art target supplied"}

    (args.out / "comparison.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
