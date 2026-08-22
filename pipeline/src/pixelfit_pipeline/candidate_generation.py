from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Literal

import numpy as np
from PIL import Image
from scipy import ndimage


SceneKind = Literal["flatlay", "worn"]
Visibility = Literal["complete", "partial"]


@dataclass(frozen=True)
class CandidateMask:
    asset_id: str
    category: str
    mask: Image.Image
    source: str
    scene: SceneKind
    visibility: Visibility = "complete"


def _binary(mask: Image.Image | np.ndarray) -> np.ndarray:
    if isinstance(mask, Image.Image):
        return np.asarray(mask.convert("L"), dtype=np.uint8) >= 128
    return np.asarray(mask, dtype=bool)


def _mask_image(binary: np.ndarray) -> Image.Image:
    return Image.fromarray(binary.astype(np.uint8) * 255, mode="L")


def _component_bounds(binary: np.ndarray) -> tuple[int, int, int, int] | None:
    ys, xs = np.nonzero(binary)
    if not len(xs):
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def _largest_component(binary: np.ndarray) -> np.ndarray:
    labels, count = ndimage.label(binary)
    if count == 0:
        return np.zeros_like(binary, dtype=bool)
    areas = np.bincount(labels.ravel())
    areas[0] = 0
    return labels == int(areas.argmax())


def classify_scene(subject_mask: Image.Image | np.ndarray) -> SceneKind:
    subject = _binary(subject_mask)
    largest = _largest_component(subject)
    bounds = _component_bounds(largest)
    if bounds is None:
        return "flatlay"
    left, top, right, bottom = bounds
    aspect = (bottom - top) / max(1, right - left)
    reaches_lower_frame = bottom >= subject.shape[0] * 0.8
    return "worn" if aspect >= 1.6 and reaches_lower_frame else "flatlay"


def significant_components(
    mask: np.ndarray,
    min_ratio: float = 0.004,
) -> list[np.ndarray]:
    labels, count = ndimage.label(mask)
    minimum = max(1, int(np.ceil(mask.size * min_ratio)))
    components = [
        labels == index
        for index in range(1, count + 1)
        if int(np.count_nonzero(labels == index)) >= minimum
    ]
    return sorted(components, key=np.count_nonzero, reverse=True)


def _usable(mask: np.ndarray, min_ratio: float = 0.003) -> bool:
    return int(np.count_nonzero(mask)) >= max(1, int(np.ceil(mask.size * min_ratio)))


def _intersection_over_union(left: np.ndarray, right: np.ndarray) -> float:
    union = int(np.count_nonzero(left | right))
    if union == 0:
        return 0.0
    return int(np.count_nonzero(left & right)) / union


def _worn_bottom_score(
    mask: np.ndarray,
    subject: np.ndarray,
    upper: np.ndarray,
) -> float:
    """Prefer substantial lower-body coverage without duplicating the upper layer."""
    clipped = mask & subject
    area = int(np.count_nonzero(clipped))
    if not _usable(clipped):
        return float("-inf")
    bounds = _component_bounds(_largest_component(subject))
    if bounds is None:
        return float("-inf")
    _, top, _, bottom = bounds
    lower_start = int(round(top + (bottom - top) * 0.45))
    lower_pixels = int(np.count_nonzero(clipped[lower_start:, :]))
    lower_fraction = lower_pixels / max(1, area)
    subject_area = max(1, int(np.count_nonzero(subject)))
    usable_area = min(1.0, area / (subject_area * 0.30))
    upper_overlap = _intersection_over_union(clipped, upper & subject)
    return lower_fraction * 0.55 + usable_area * 0.35 - upper_overlap * 0.75


def _flatlay_candidates(
    subject: np.ndarray,
    cloth: Mapping[str, np.ndarray],
) -> list[CandidateMask]:
    candidates: list[CandidateMask] = []
    upper = cloth.get("upper", np.zeros_like(subject))
    lower = cloth.get("lower", np.zeros_like(subject))
    if _usable(upper):
        candidates.append(
            CandidateMask(
                "upper",
                "upper-body",
                _mask_image(upper),
                "u2net-cloth:upper",
                "flatlay",
            )
        )
    if _usable(lower):
        candidates.append(
            CandidateMask(
                "lower",
                "bottoms",
                _mask_image(lower),
                "u2net-cloth:lower",
                "flatlay",
            )
        )

    cloth_union = np.zeros_like(subject)
    for mask in cloth.values():
        cloth_union |= mask
    residual = subject & ~ndimage.binary_dilation(cloth_union, iterations=3)
    components = significant_components(residual)
    height, width = subject.shape

    shoe_components: list[np.ndarray] = []
    accessory_components: list[np.ndarray] = []
    for component in components:
        bounds = _component_bounds(component)
        if bounds is None:
            continue
        _, top, _, bottom = bounds
        center_y = (top + bottom) / 2
        if top >= height * 0.72 or center_y >= height * 0.80:
            shoe_components.append(component)
        else:
            accessory_components.append(component)

    side_accessories = []
    for component in accessory_components:
        bounds = _component_bounds(component)
        if bounds is None:
            continue
        left, _, right, _ = bounds
        center_x = (left + right) / 2
        if center_x <= width * 0.38 or center_x >= width * 0.62:
            side_accessories.append(component)
    if side_accessories:
        bag = max(side_accessories, key=np.count_nonzero)
        candidates.append(
            CandidateMask(
                "bag",
                "bag",
                _mask_image(bag),
                "birefnet-subject-residual",
                "flatlay",
            )
        )
    if shoe_components:
        shoes = np.logical_or.reduce(shoe_components)
        candidates.append(
            CandidateMask(
                "shoes",
                "shoes",
                _mask_image(shoes),
                "birefnet-subject-residual",
                "flatlay",
            )
        )
    return candidates


def _worn_candidates(
    subject: np.ndarray,
    cloth: Mapping[str, np.ndarray],
) -> list[CandidateMask]:
    candidates: list[CandidateMask] = []
    upper = cloth.get("upper", np.zeros_like(subject))
    if _usable(upper):
        candidates.append(
            CandidateMask(
                "outer",
                "outerwear",
                _mask_image(upper),
                "u2net-cloth:upper",
                "worn",
            )
        )

    full = cloth.get("full", np.zeros_like(subject))
    lower = cloth.get("lower", np.zeros_like(subject))
    bottom_options = [
        (_worn_bottom_score(full, subject, upper), "full", full),
        (_worn_bottom_score(lower, subject, upper), "lower", lower),
    ]
    bottom_score, bottom_source, bottom = max(bottom_options, key=lambda item: item[0])
    if np.isfinite(bottom_score):
        candidates.append(
            CandidateMask(
                "bottom",
                "bottoms",
                _mask_image(bottom),
                f"u2net-cloth:{bottom_source}",
                "worn",
            )
        )

    bounds = _component_bounds(_largest_component(subject))
    if bounds is not None:
        _, top, _, bottom_edge = bounds
        band_start = int(round(top + (bottom_edge - top) * 0.875))
        shoes = subject.copy()
        shoes[:band_start, :] = False
        if _usable(shoes, min_ratio=0.001):
            candidates.append(
                CandidateMask(
                    "shoes",
                    "shoes",
                    _mask_image(shoes),
                    "birefnet-subject-bottom-band",
                    "worn",
                    "partial",
                )
            )
    return candidates


def generate_candidates(
    subject_mask: Image.Image,
    garment_masks: Mapping[str, Image.Image],
) -> list[CandidateMask]:
    subject = _binary(subject_mask)
    cloth = {name: _binary(mask) for name, mask in garment_masks.items()}
    expected_shape = subject.shape
    if any(mask.shape != expected_shape for mask in cloth.values()):
        raise ValueError("subject and garment masks must have matching dimensions")
    if classify_scene(subject) == "worn":
        return _worn_candidates(subject, cloth)
    return _flatlay_candidates(subject, cloth)
