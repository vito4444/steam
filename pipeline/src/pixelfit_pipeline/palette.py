from __future__ import annotations

import numpy as np
from PIL import Image


def _record(rgb: np.ndarray, count: int, total: int) -> dict[str, object]:
    value = tuple(int(channel) for channel in rgb)
    return {
        "rgb": list(value),
        "hex": "#" + "".join(f"{channel:02x}" for channel in value),
        "proportion": count / total,
    }


def dominant_palette(image: Image.Image, max_colors: int = 5) -> list[dict[str, object]]:
    """Return a deterministic visible-pixel palette without modifying the image."""
    if max_colors < 1:
        raise ValueError("max_colors must be positive")
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    pixels = rgba[..., :3][rgba[..., 3] >= 16]
    if not len(pixels):
        return []

    unique, counts = np.unique(pixels, axis=0, return_counts=True)
    if len(unique) <= max_colors:
        order = np.argsort(-counts, kind="stable")
        return [
            _record(unique[index], int(counts[index]), len(pixels)) for index in order
        ]

    sample = pixels
    if len(sample) > 50_000:
        indices = np.linspace(0, len(sample) - 1, 50_000, dtype=int)
        sample = sample[indices]
    strip = Image.fromarray(sample.reshape(1, len(sample), 3), "RGB")
    quantized = strip.quantize(
        colors=max_colors,
        method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE,
    )
    palette = np.asarray(quantized.getpalette(), dtype=np.uint8).reshape(-1, 3)
    color_counts = quantized.getcolors(maxcolors=max_colors) or []
    ordered = sorted(color_counts, key=lambda item: (-item[0], item[1]))
    return [
        _record(palette[index], int(count), len(sample)) for count, index in ordered
    ]
