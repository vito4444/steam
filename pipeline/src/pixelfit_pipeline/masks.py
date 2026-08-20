from __future__ import annotations

import numpy as np
from PIL import Image
from scipy import ndimage

from .contracts import PipelineError


def _same_size(first: Image.Image, second: Image.Image) -> None:
    if first.size != second.size:
        raise PipelineError(
            "MASK_SIZE_MISMATCH",
            "image and mask dimensions must match",
            {"image_size": list(first.size), "mask_size": list(second.size)},
        )


def clean_mask(
    mask: Image.Image,
    threshold: int = 96,
    min_area_ratio: float = 0.001,
    max_hole_ratio: float = 0.0005,
) -> Image.Image:
    """Threshold alpha, remove tiny islands, and fill small enclosed holes."""
    if not 0 <= threshold <= 255:
        raise ValueError("threshold must be between 0 and 255")
    if not 0 <= min_area_ratio <= 1 or not 0 <= max_hole_ratio <= 1:
        raise ValueError("area ratios must be between 0 and 1")

    binary = np.asarray(mask.convert("L"), dtype=np.uint8) >= threshold
    labels, count = ndimage.label(binary)
    if count:
        areas = np.bincount(labels.ravel())
        minimum = max(1, int(np.ceil(binary.size * min_area_ratio)))
        keep = areas >= minimum
        keep[0] = False
        binary = keep[labels]

    holes, hole_count = ndimage.label(~binary)
    if hole_count:
        border_labels = np.unique(
            np.concatenate((holes[0], holes[-1], holes[:, 0], holes[:, -1]))
        )
        areas = np.bincount(holes.ravel())
        maximum = int(np.floor(binary.size * max_hole_ratio))
        fill = areas <= maximum
        fill[border_labels] = False
        fill[0] = False
        binary = binary | fill[holes]
    return Image.fromarray(binary.astype(np.uint8) * 255, "L")


def clip_to_subject(mask: Image.Image, subject_mask: Image.Image) -> Image.Image:
    _same_size(mask, subject_mask)
    garment = np.asarray(mask.convert("L"), dtype=np.uint8)
    subject = np.asarray(subject_mask.convert("L"), dtype=np.uint8)
    return Image.fromarray(np.minimum(garment, subject), "L")


def apply_mask(image: Image.Image, mask: Image.Image) -> Image.Image:
    _same_size(image, mask)
    rgba = image.convert("RGBA")
    rgba.putalpha(mask.convert("L"))
    return rgba


def decontaminate_edges(
    image: Image.Image,
    opaque_threshold: int = 240,
    radius: int = 8,
) -> Image.Image:
    """Replace translucent fringe RGB with nearest opaque interior RGB."""
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = rgba[..., 3]
    opaque = alpha >= opaque_threshold
    fringe = (alpha > 0) & ~opaque
    if radius <= 0 or not np.any(opaque) or not np.any(fringe):
        return Image.fromarray(rgba, "RGBA")
    distances, indices = ndimage.distance_transform_edt(
        ~opaque, return_distances=True, return_indices=True
    )
    replace = fringe & (distances <= radius)
    nearest_y = indices[0][replace]
    nearest_x = indices[1][replace]
    rgba[..., :3][replace] = rgba[nearest_y, nearest_x, :3]
    return Image.fromarray(rgba, "RGBA")


def quality_warnings(
    mask: Image.Image,
    subject_mask: Image.Image | None = None,
) -> list[dict[str, object]]:
    alpha = np.asarray(mask.convert("L"), dtype=np.uint8)
    visible = alpha >= 16
    ratio = float(np.mean(visible))
    warnings: list[dict[str, object]] = []
    if ratio < 0.005:
        warnings.append({"code": "MASK_TOO_SMALL", "visible_ratio": round(ratio, 6)})
    if ratio > 0.8:
        warnings.append({"code": "MASK_TOO_LARGE", "visible_ratio": round(ratio, 6)})
    if np.any(visible[0]) or np.any(visible[-1]) or np.any(visible[:, 0]) or np.any(visible[:, -1]):
        warnings.append({"code": "MASK_TOUCHES_BORDER"})
    soft = (alpha > 0) & (alpha < 255)
    if np.count_nonzero(visible) and np.count_nonzero(soft) / np.count_nonzero(visible) > 0.2:
        warnings.append({"code": "UNCERTAIN_BOUNDARY"})
    if subject_mask is not None:
        _same_size(mask, subject_mask)
        subject = np.asarray(subject_mask.convert("L"), dtype=np.uint8) >= 16
        leak = visible & ~subject
        leak_ratio = float(np.count_nonzero(leak) / max(1, np.count_nonzero(visible)))
        if leak_ratio > 0.01:
            warnings.append(
                {"code": "OUTSIDE_SUBJECT", "outside_ratio": round(leak_ratio, 6)}
            )
    return warnings
