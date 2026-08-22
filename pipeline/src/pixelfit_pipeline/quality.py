from __future__ import annotations

from dataclasses import asdict, dataclass

import numpy as np
from PIL import Image
from scipy import ndimage


@dataclass(frozen=True)
class QualityThresholds:
    min_visible_ratio: float = 0.008
    max_visible_ratio: float = 0.75
    max_isolated_component_ratio: float = 0.003
    balanced_component_ratio: float = 0.02
    max_hole_ratio: float = 0.015
    min_bbox_fill_ratio: float = 0.18
    border_margin: int = 1
    max_contour_roughness: float = 0.05
    max_edge_sharpness_mean: float = 0.58
    min_soft_alpha_ratio: float = 0.003
    max_soft_alpha_ratio: float = 0.30
    min_score: int = 85

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


@dataclass(frozen=True)
class QualityReason:
    code: str
    metric: str
    value: float | bool
    threshold: float | bool
    message: str

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


@dataclass(frozen=True)
class QualityReport:
    score: int
    decision: str
    allowed: bool
    reasons: tuple[QualityReason, ...]
    metrics: dict[str, float | bool | int]
    thresholds: QualityThresholds

    def to_dict(self) -> dict[str, object]:
        return {
            "score": self.score,
            "decision": self.decision,
            "allowed": self.allowed,
            "reasons": [reason.to_dict() for reason in self.reasons],
            "metrics": dict(self.metrics),
            "thresholds": self.thresholds.to_dict(),
        }


def _admission_decision(
    metrics: dict[str, float | bool | int],
    reasons: list[QualityReason],
    thresholds: QualityThresholds,
) -> tuple[str, bool]:
    """Separate a repairable cutout from output that contains no usable subject."""
    codes = {reason.code for reason in reasons}
    severe = bool(codes & {"SUBJECT_TOO_SMALL", "SUBJECT_TOO_LARGE"})
    missing_over_half = (
        "LOW_BBOX_FILL" in codes
        and float(metrics["bbox_fill_ratio"]) < thresholds.min_bbox_fill_ratio / 2
    )
    if severe or missing_over_half:
        return "retry", False
    if reasons:
        return "needs_optimization", True
    return "pass", True


def _perimeter(binary: np.ndarray) -> int:
    if not np.any(binary):
        return 0
    return int(np.count_nonzero(binary ^ ndimage.binary_erosion(binary)))


def _metrics(alpha: np.ndarray, thresholds: QualityThresholds) -> dict[str, float | bool | int]:
    visible = alpha >= 16
    binary = alpha >= 128
    visible_pixels = int(np.count_nonzero(visible))
    binary_pixels = int(np.count_nonzero(binary))
    total_pixels = int(alpha.size)

    labels, component_count = ndimage.label(binary)
    isolated_pixels = 0
    retained = binary.copy()
    if component_count:
        areas = np.bincount(labels.ravel())
        areas[0] = 0
        largest = int(areas.max())
        significant = areas >= max(
            1, int(np.ceil(largest * thresholds.balanced_component_ratio))
        )
        significant[0] = False
        retained = significant[labels]
        isolated_pixels = int(np.count_nonzero(binary & ~retained))

    filled = ndimage.binary_fill_holes(retained)
    hole_pixels = int(np.count_nonzero(filled & ~retained))
    filled_pixels = int(np.count_nonzero(filled))

    ys, xs = np.nonzero(retained)
    if len(xs):
        bbox_area = int((xs.max() - xs.min() + 1) * (ys.max() - ys.min() + 1))
        bbox_fill_ratio = float(np.count_nonzero(retained) / max(1, bbox_area))
    else:
        bbox_fill_ratio = 0.0

    margin = max(1, thresholds.border_margin)
    border_contact = bool(
        np.any(visible[:margin])
        or np.any(visible[-margin:])
        or np.any(visible[:, :margin])
        or np.any(visible[:, -margin:])
    )

    smoothed = ndimage.gaussian_filter(binary.astype(np.float64), 1.5) >= 0.5
    contour_roughness = max(
        0.0, _perimeter(binary) / max(1, _perimeter(smoothed)) - 1.0
    )

    normalized = alpha.astype(np.float64) / 255.0
    gradient_x = ndimage.sobel(normalized, axis=1) / 4.0
    gradient_y = ndimage.sobel(normalized, axis=0) / 4.0
    gradient = np.hypot(gradient_x, gradient_y)
    boundary = (binary ^ ndimage.binary_erosion(binary)) | (
        ndimage.binary_dilation(binary) ^ binary
    )
    edge_sharpness = float(np.mean(gradient[boundary])) if np.any(boundary) else 0.0
    soft_pixels = int(np.count_nonzero((alpha > 0) & (alpha < 255)))

    return {
        "visible_pixels": visible_pixels,
        "visible_ratio": round(visible_pixels / max(1, total_pixels), 6),
        "component_count": int(component_count),
        "isolated_component_ratio": round(
            isolated_pixels / max(1, binary_pixels), 6
        ),
        "hole_ratio": round(hole_pixels / max(1, filled_pixels), 6),
        "bbox_fill_ratio": round(bbox_fill_ratio, 6),
        "border_contact": border_contact,
        "contour_roughness": round(float(contour_roughness), 6),
        "edge_sharpness_mean": round(edge_sharpness, 6),
        "soft_alpha_ratio": round(soft_pixels / max(1, visible_pixels), 6),
    }


def evaluate_cutout(
    cutout: Image.Image,
    category: str | None = None,
    thresholds: QualityThresholds | None = None,
    source_mask: Image.Image | None = None,
) -> QualityReport:
    del category  # Stable extension point for category-specific thresholds.
    selected = thresholds or QualityThresholds()
    alpha = np.asarray(cutout.convert("RGBA"), dtype=np.uint8)[..., 3]
    metrics = _metrics(alpha, selected)
    if source_mask is not None:
        if source_mask.size != cutout.size:
            raise ValueError("source_mask and cutout dimensions must match")
        input_metrics = _metrics(
            np.asarray(source_mask.convert("L"), dtype=np.uint8), selected
        )
        metrics.update(
            {
                "input_contour_roughness": input_metrics["contour_roughness"],
                "input_isolated_component_ratio": input_metrics[
                    "isolated_component_ratio"
                ],
                "input_hole_ratio": input_metrics["hole_ratio"],
            }
        )
    reasons: list[QualityReason] = []

    def reject(
        code: str,
        metric: str,
        threshold: float | bool,
        message: str,
    ) -> None:
        reasons.append(
            QualityReason(code, metric, metrics[metric], threshold, message)
        )

    visible_ratio = float(metrics["visible_ratio"])
    if visible_ratio < selected.min_visible_ratio:
        reject(
            "SUBJECT_TOO_SMALL",
            "visible_ratio",
            selected.min_visible_ratio,
            "visible garment area is too small for a usable wardrobe asset",
        )
    if visible_ratio > selected.max_visible_ratio:
        reject(
            "SUBJECT_TOO_LARGE",
            "visible_ratio",
            selected.max_visible_ratio,
            "foreground covers too much of the source and likely includes background",
        )
    if float(metrics["isolated_component_ratio"]) > selected.max_isolated_component_ratio:
        reject(
            "ISOLATED_COMPONENTS",
            "isolated_component_ratio",
            selected.max_isolated_component_ratio,
            "disconnected alpha debris exceeds the admission limit",
        )
    if float(metrics["hole_ratio"]) > selected.max_hole_ratio:
        reject(
            "EXCESSIVE_HOLES",
            "hole_ratio",
            selected.max_hole_ratio,
            "enclosed transparent holes indicate a broken garment mask",
        )
    if float(metrics["bbox_fill_ratio"]) < selected.min_bbox_fill_ratio:
        reject(
            "LOW_BBOX_FILL",
            "bbox_fill_ratio",
            selected.min_bbox_fill_ratio,
            "garment occupancy inside its bounding box is implausibly sparse",
        )
    if bool(metrics["border_contact"]):
        reject(
            "SUBJECT_TRUNCATED",
            "border_contact",
            False,
            "foreground reaches the source edge and may be truncated",
        )
    if float(metrics["contour_roughness"]) > selected.max_contour_roughness:
        reject(
            "JAGGED_EDGE",
            "contour_roughness",
            selected.max_contour_roughness,
            "high-frequency contour variation exceeds the jaggedness limit",
        )
    if (
        source_mask is not None
        and float(metrics["input_contour_roughness"])
        > selected.max_contour_roughness
    ):
        reject(
            "MASK_STRUCTURE_UNRELIABLE",
            "input_contour_roughness",
            selected.max_contour_roughness,
            "input mask has structural bites that edge smoothing cannot prove complete",
        )
    if float(metrics["edge_sharpness_mean"]) > selected.max_edge_sharpness_mean:
        reject(
            "EDGE_TOO_SHARP",
            "edge_sharpness_mean",
            selected.max_edge_sharpness_mean,
            "alpha changes too abruptly at the garment edge",
        )
    soft_ratio = float(metrics["soft_alpha_ratio"])
    if soft_ratio < selected.min_soft_alpha_ratio:
        reject(
            "ALPHA_TRANSITION_MISSING",
            "soft_alpha_ratio",
            selected.min_soft_alpha_ratio,
            "alpha has no usable semi-transparent transition band",
        )
    if soft_ratio > selected.max_soft_alpha_ratio:
        reject(
            "ALPHA_TRANSITION_EXCESSIVE",
            "soft_alpha_ratio",
            selected.max_soft_alpha_ratio,
            "too much of the subject is semi-transparent",
        )

    codes = {reason.code for reason in reasons}
    score = 100
    if codes & {"JAGGED_EDGE", "EDGE_TOO_SHARP", "MASK_STRUCTURE_UNRELIABLE"}:
        score -= 25
    if "ISOLATED_COMPONENTS" in codes:
        score -= 20
    if codes & {
        "SUBJECT_TOO_SMALL",
        "SUBJECT_TOO_LARGE",
        "EXCESSIVE_HOLES",
        "LOW_BBOX_FILL",
        "SUBJECT_TRUNCATED",
    }:
        score -= 35
    if codes & {"ALPHA_TRANSITION_MISSING", "ALPHA_TRANSITION_EXCESSIVE"}:
        score -= 20
    score = int(np.clip(score, 0, 100))
    decision, allowed = _admission_decision(metrics, reasons, selected)
    return QualityReport(
        score=score,
        decision=decision,
        allowed=allowed,
        reasons=tuple(reasons),
        metrics=metrics,
        thresholds=selected,
    )
