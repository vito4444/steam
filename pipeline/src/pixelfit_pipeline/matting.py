from __future__ import annotations

from dataclasses import asdict, dataclass

import numpy as np
from PIL import Image
from scipy import ndimage

from .contracts import PipelineError


@dataclass(frozen=True)
class MattingConfig:
    mask_threshold: int = 96
    keep_secondary_ratio: float = 0.02
    max_hole_ratio: float = 0.005
    unknown_radius_ratio: float = 0.006
    min_unknown_radius: int = 3
    max_unknown_radius: int = 12
    max_working_side: int = 1024
    foreground_seed_threshold: int = 245
    background_seed_threshold: int = 8
    decontaminate_radius: int = 8

    def __post_init__(self) -> None:
        if not 0 <= self.mask_threshold <= 255:
            raise ValueError("mask_threshold must be between 0 and 255")
        if not 0 <= self.keep_secondary_ratio <= 1:
            raise ValueError("keep_secondary_ratio must be between 0 and 1")
        if not 0 <= self.max_hole_ratio <= 1:
            raise ValueError("max_hole_ratio must be between 0 and 1")
        if self.min_unknown_radius < 1 or self.max_unknown_radius < self.min_unknown_radius:
            raise ValueError("unknown radius bounds are invalid")
        if self.max_working_side < 32:
            raise ValueError("max_working_side must be at least 32")


@dataclass(frozen=True)
class CleanupStats:
    input_components: int
    retained_components: int
    removed_component_ratio: float
    filled_hole_ratio: float
    closing_applied: bool
    unknown_radius: int

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


@dataclass(frozen=True)
class RefinementResult:
    cutout: Image.Image
    alpha: Image.Image
    trimap: Image.Image
    cleanup: CleanupStats
    method: str
    foreground_method: str
    working_size: tuple[int, int]

    def processing_dict(self) -> dict[str, object]:
        return {
            "method": self.method,
            "foreground_method": self.foreground_method,
            "working_size": list(self.working_size),
            "cleanup": self.cleanup.to_dict(),
        }


def _same_size(source: Image.Image, mask: Image.Image) -> None:
    if source.size != mask.size:
        raise PipelineError(
            "MASK_SIZE_MISMATCH",
            "image and mask dimensions must match",
            {"image_size": list(source.size), "mask_size": list(mask.size)},
        )


def _perimeter(binary: np.ndarray) -> int:
    if not np.any(binary):
        return 0
    return int(np.count_nonzero(binary ^ ndimage.binary_erosion(binary)))


def _clean_topology(
    mask: Image.Image, config: MattingConfig
) -> tuple[np.ndarray, CleanupStats]:
    alpha = np.asarray(mask.convert("L"), dtype=np.uint8)
    binary = alpha >= config.mask_threshold
    labels, count = ndimage.label(binary)
    if count == 0:
        raise PipelineError("SEGMENTATION_EMPTY", "mask has no visible foreground")

    areas = np.bincount(labels.ravel())
    areas[0] = 0
    largest_area = int(areas.max())
    keep = areas >= max(1, int(np.ceil(largest_area * config.keep_secondary_ratio)))
    keep[0] = False
    retained = keep[labels]
    removed = binary & ~retained
    removed_ratio = float(np.count_nonzero(removed) / max(1, np.count_nonzero(binary)))

    holes, hole_count = ndimage.label(~retained)
    filled_pixels = 0
    if hole_count:
        border_labels = np.unique(
            np.concatenate((holes[0], holes[-1], holes[:, 0], holes[:, -1]))
        )
        hole_areas = np.bincount(holes.ravel())
        max_hole_area = max(
            1, int(np.floor(np.count_nonzero(retained) * config.max_hole_ratio))
        )
        fill = hole_areas <= max_hole_area
        fill[border_labels] = False
        fill[0] = False
        pixels = fill[holes]
        filled_pixels = int(np.count_nonzero(pixels))
        retained |= pixels

    closed = ndimage.binary_closing(retained, structure=np.ones((3, 3), dtype=bool))
    closing_applied = _perimeter(closed) < _perimeter(retained) and np.count_nonzero(
        closed ^ retained
    ) <= max(8, int(np.count_nonzero(retained) * 0.01))
    if closing_applied:
        retained = closed

    radius = int(round(min(mask.size) * config.unknown_radius_ratio))
    radius = int(np.clip(radius, config.min_unknown_radius, config.max_unknown_radius))
    retained_labels = ndimage.label(retained)[1]
    stats = CleanupStats(
        input_components=int(count),
        retained_components=int(retained_labels),
        removed_component_ratio=round(removed_ratio, 6),
        filled_hole_ratio=round(
            float(filled_pixels / max(1, np.count_nonzero(retained))), 6
        ),
        closing_applied=bool(closing_applied),
        unknown_radius=radius,
    )
    return retained, stats


def build_trimap(
    mask: Image.Image, config: MattingConfig | None = None
) -> tuple[Image.Image, CleanupStats]:
    selected = config or MattingConfig()
    retained, stats = _clean_topology(mask, selected)
    radius = stats.unknown_radius
    definite_foreground = ndimage.binary_erosion(retained, iterations=radius)
    if not np.any(definite_foreground):
        distance = ndimage.distance_transform_edt(retained)
        definite_foreground = distance >= max(1.0, float(distance.max()) * 0.65)
    possible_foreground = ndimage.binary_dilation(retained, iterations=radius)
    trimap = np.zeros(retained.shape, dtype=np.uint8)
    trimap[possible_foreground] = 128
    trimap[definite_foreground] = 255
    return Image.fromarray(trimap, "L"), stats


def _working_crop(
    source: Image.Image,
    trimap: Image.Image,
    max_side: int,
) -> tuple[Image.Image, Image.Image, tuple[int, int, int, int], tuple[int, int]]:
    active = np.asarray(trimap, dtype=np.uint8) > 0
    ys, xs = np.nonzero(active)
    if not len(xs):
        raise PipelineError("SEGMENTATION_EMPTY", "trimap has no foreground")
    padding = 4
    box = (
        max(0, int(xs.min()) - padding),
        max(0, int(ys.min()) - padding),
        min(source.width, int(xs.max()) + padding + 1),
        min(source.height, int(ys.max()) + padding + 1),
    )
    image_crop = source.convert("RGB").crop(box)
    trimap_crop = trimap.crop(box)
    original_size = image_crop.size
    scale = min(1.0, max_side / max(original_size))
    if scale < 1.0:
        working_size = (
            max(2, int(round(original_size[0] * scale))),
            max(2, int(round(original_size[1] * scale))),
        )
        image_crop = image_crop.resize(working_size, Image.Resampling.LANCZOS)
        trimap_crop = trimap_crop.resize(working_size, Image.Resampling.NEAREST)
    return image_crop, trimap_crop, box, original_size


def refine_cutout(
    source: Image.Image,
    mask: Image.Image,
    config: MattingConfig | None = None,
    category: str | None = None,
) -> RefinementResult:
    del category  # Reserved for category-specific trimap policy without changing the API.
    selected = config or MattingConfig()
    _same_size(source, mask)
    trimap, cleanup = build_trimap(mask, selected)
    image_crop, trimap_crop, box, original_size = _working_crop(
        source, trimap, selected.max_working_side
    )
    image_array = np.asarray(image_crop, dtype=np.float64) / 255.0
    trimap_array = np.asarray(trimap_crop, dtype=np.float64) / 255.0
    trimap_array[trimap_array <= selected.background_seed_threshold / 255.0] = 0.0
    trimap_array[trimap_array >= selected.foreground_seed_threshold / 255.0] = 1.0

    try:
        from pymatting import estimate_alpha_cf, estimate_foreground_cf
    except ImportError as exc:
        raise PipelineError(
            "MATTING_UNAVAILABLE",
            "PyMatting closed-form runtime is not installed",
            {"install": "pymatting==1.1.15"},
        ) from exc

    try:
        alpha_working = estimate_alpha_cf(
            image_array,
            trimap_array,
            laplacian_kwargs={"epsilon": 1e-6},
            cg_kwargs={"maxiter": 1000},
        )
        alpha_working[trimap_array == 0.0] = 0.0
        alpha_working[trimap_array == 1.0] = 1.0
        unknown = (trimap_array > 0.0) & (trimap_array < 1.0)
        if np.any(unknown):
            stabilized = ndimage.gaussian_filter(alpha_working, sigma=1.2)
            alpha_working[unknown] = stabilized[unknown]
            alpha_working[trimap_array == 0.0] = 0.0
            alpha_working[trimap_array == 1.0] = 1.0
    except Exception as exc:
        raise PipelineError(
            "MATTING_FAILED",
            "closed-form alpha matting failed",
            {"type": type(exc).__name__},
        ) from exc

    foreground_method = "closed-form"
    if float(np.max(np.std(image_array, axis=(0, 1)))) < 1e-8:
        foreground_working = image_array
        foreground_method = "source-rgb-flat-image"
    else:
        try:
            foreground_working = estimate_foreground_cf(
                image_array,
                alpha_working,
                rtol=1e-4,
                cg_kwargs={"maxiter": 500},
            )
        except Exception as exc:
            foreground_working = image_array
            foreground_method = f"source-rgb-fallback:{type(exc).__name__}"

    alpha_image = Image.fromarray(
        np.clip(np.rint(alpha_working * 255.0), 0, 255).astype(np.uint8), "L"
    )
    foreground_image = Image.fromarray(
        np.clip(np.rint(foreground_working * 255.0), 0, 255).astype(np.uint8), "RGB"
    )
    if alpha_image.size != original_size:
        alpha_image = alpha_image.resize(original_size, Image.Resampling.LANCZOS)
        foreground_image = foreground_image.resize(original_size, Image.Resampling.LANCZOS)

    original_trimap = np.asarray(trimap.crop(box), dtype=np.uint8)
    alpha_crop = np.asarray(alpha_image, dtype=np.uint8).copy()
    alpha_crop[original_trimap == 0] = 0
    alpha_crop[original_trimap == 255] = 255

    source_array = np.asarray(source.convert("RGB"), dtype=np.uint8).copy()
    crop_rgb = source_array[box[1] : box[3], box[0] : box[2]].copy()
    estimated_rgb = np.asarray(foreground_image, dtype=np.uint8)
    partial = (alpha_crop > 0) & (alpha_crop < 255)
    if selected.decontaminate_radius > 0:
        opaque = alpha_crop >= selected.foreground_seed_threshold
        distance = ndimage.distance_transform_edt(~opaque)
        partial &= distance <= selected.decontaminate_radius
    crop_rgb[partial] = estimated_rgb[partial]
    source_array[box[1] : box[3], box[0] : box[2]] = crop_rgb

    full_alpha = np.zeros((source.height, source.width), dtype=np.uint8)
    full_alpha[box[1] : box[3], box[0] : box[2]] = alpha_crop
    rgba = np.dstack((source_array, full_alpha))
    return RefinementResult(
        cutout=Image.fromarray(rgba, "RGBA"),
        alpha=Image.fromarray(full_alpha, "L"),
        trimap=trimap,
        cleanup=cleanup,
        method="closed-form",
        foreground_method=foreground_method,
        working_size=image_crop.size,
    )
