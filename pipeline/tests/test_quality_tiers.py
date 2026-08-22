from __future__ import annotations

import numpy as np
from PIL import Image

from pixelfit_pipeline.quality import evaluate_cutout


def _rgba(alpha: np.ndarray) -> Image.Image:
    pixels = np.zeros((*alpha.shape, 4), dtype=np.uint8)
    pixels[..., :3] = (186, 124, 72)
    pixels[..., 3] = alpha
    return Image.fromarray(pixels, mode="RGBA")


def _present_cutout() -> Image.Image:
    alpha = np.zeros((100, 100), dtype=np.uint8)
    alpha[20:80, 20:80] = 255
    for offset, value in ((1, 204), (2, 153), (3, 102), (4, 51)):
        alpha[20 - offset, 20 - offset : 80 + offset] = value
        alpha[79 + offset, 20 - offset : 80 + offset] = value
        alpha[20 - offset : 80 + offset, 20 - offset] = value
        alpha[20 - offset : 80 + offset, 79 + offset] = value
    return _rgba(alpha)


def _rough_source_mask() -> Image.Image:
    mask = np.zeros((100, 100), dtype=np.uint8)
    for x in range(20, 80):
        top = 15 if x % 4 < 2 else 25
        mask[top:80, x] = 255
    return Image.fromarray(mask, mode="L")


def test_structurally_rough_but_present_candidate_is_importable_for_optimization() -> None:
    """Regression: a cosmetic input-mask warning must not discard a visible garment."""
    report = evaluate_cutout(_present_cutout(), source_mask=_rough_source_mask())

    assert report.decision == "needs_optimization"
    assert report.allowed is True
    assert {reason.code for reason in report.reasons} == {
        "MASK_STRUCTURE_UNRELIABLE"
    }


def test_sparse_missing_subject_requires_retry() -> None:
    """Regression: an almost empty result must never enter the wardrobe."""
    alpha = np.zeros((100, 100), dtype=np.uint8)
    alpha[49:51, 49:51] = 255

    report = evaluate_cutout(_rgba(alpha))

    assert report.decision == "retry"
    assert report.allowed is False
    assert "SUBJECT_TOO_SMALL" in {reason.code for reason in report.reasons}
