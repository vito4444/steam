from __future__ import annotations

import hashlib
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageOps, UnidentifiedImageError

from .candidate_generation import generate_candidates
from .contracts import (
    ALLOWED_CATEGORIES,
    SCHEMA_VERSION,
    PipelineError,
    read_json,
    validate_slug,
    write_json,
)
from .masks import clean_mask, clip_to_subject
from .matting import refine_cutout
from .palette import dominant_palette
from .quality import evaluate_cutout
from .segmentation import SegmentationBackend


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _load_image(path: Path) -> Image.Image:
    if not path.is_file():
        raise PipelineError(
            "ASSET_NOT_FOUND", "source image was not found", {"path": str(path)}
        )
    try:
        with Image.open(path) as opened:
            opened.load()
            image = ImageOps.exif_transpose(opened).convert("RGB")
    except (UnidentifiedImageError, OSError, Image.DecompressionBombError) as exc:
        raise PipelineError(
            "UNSUPPORTED_IMAGE", "source is not a readable image", {"path": str(path)}
        ) from exc
    if image.width < 2 or image.height < 2:
        raise PipelineError("UNSUPPORTED_IMAGE", "source image is too small")
    return image


def analyze_image(
    image_path: str | Path,
    import_id: str,
    output_dir: str | Path,
    backend: SegmentationBackend,
) -> dict[str, Any]:
    validate_slug(import_id, "import_id")
    source_path = Path(image_path).resolve()
    image = _load_image(source_path)
    output_root = Path(output_dir).resolve()
    root = (output_root / import_id).resolve()
    if root.parent != output_root:
        raise PipelineError("INVALID_REQUEST", "import_id escapes output directory")
    masks_dir = root / "masks"
    assets_dir = root / "assets"
    quarantine_dir = root / "quarantine"
    revisions_dir = root / "revisions"
    for directory in (masks_dir, assets_dir, quarantine_dir, revisions_dir):
        directory.mkdir(parents=True, exist_ok=True)

    normalized_source = root / "source.png"
    image.save(normalized_source, format="PNG")
    bundle = backend.segment(image)
    if bundle.subject_mask.size != image.size:
        raise PipelineError("MASK_SIZE_MISMATCH", "subject mask differs from source image")
    subject = clean_mask(bundle.subject_mask)
    records: list[dict[str, Any]] = []
    for candidate in generate_candidates(subject, bundle.garment_masks):
        asset_id = validate_slug(candidate.asset_id, "mask_name")
        raw_mask = candidate.mask
        if raw_mask.size != image.size:
            raise PipelineError(
                "MASK_SIZE_MISMATCH",
                "garment mask differs from source image",
                {"mask": candidate.asset_id},
            )
        clipped = clip_to_subject(raw_mask, subject)
        try:
            refinement = refine_cutout(image, clipped)
        except PipelineError as exc:
            if exc.code == "SEGMENTATION_EMPTY":
                continue
            raise
        quality = evaluate_cutout(
            refinement.cutout,
            category=candidate.category,
            source_mask=clipped,
        )
        bbox = refinement.alpha.getbbox()
        if bbox is None:
            continue
        mask_path = masks_dir / f"{asset_id}-auto.png"
        working_path = masks_dir / f"{asset_id}-working.png"
        refinement.alpha.save(mask_path)
        refinement.alpha.save(working_path)
        if quality.allowed:
            output_path = assets_dir / f"{asset_id}-auto.png"
            auto_file = output_path.relative_to(root).as_posix()
            quarantine_file = None
        else:
            output_path = quarantine_dir / f"{asset_id}-candidate.png"
            auto_file = None
            quarantine_file = output_path.relative_to(root).as_posix()
        refinement.cutout.save(output_path, format="PNG")
        preview_file = output_path.relative_to(root).as_posix()
        category = candidate.category
        palette = dominant_palette(refinement.cutout, max_colors=5)
        admission = {
            "allowed": quality.allowed,
            "decision": quality.decision,
            "score": quality.score,
            "reasons": [reason.code for reason in quality.reasons],
        }
        records.append(
            {
                "asset_id": asset_id,
                "mask_name": candidate.asset_id,
                "category": category,
                "category_source": candidate.source,
                "candidate_source": candidate.source,
                "scene": candidate.scene,
                "visibility": candidate.visibility,
                "tags": [category, *[f"color:{entry['hex']}" for entry in palette[:3]]],
                "palette": palette,
                "source_bbox": list(bbox),
                "pixel_width": image.width,
                "pixel_height": image.height,
                "visible_ratio": quality.metrics["visible_ratio"],
                "review_state": {
                    "pass": "approved",
                    "needs_optimization": "needs_optimization",
                    "retry": "retry",
                }[quality.decision],
                "warnings": [reason.to_dict() for reason in quality.reasons],
                "quality": quality.to_dict(),
                "admission": admission,
                "processing": refinement.processing_dict(),
                "mask_file": mask_path.relative_to(root).as_posix(),
                "working_mask_file": working_path.relative_to(root).as_posix(),
                "preview_file": preview_file,
                "auto_file": auto_file,
                "quarantine_file": quarantine_file,
                "final_file": None,
                "revision_file": (revisions_dir / f"{asset_id}.jsonl")
                .relative_to(root)
                .as_posix(),
            }
        )
    if not records:
        raise PipelineError(
            "SEGMENTATION_EMPTY",
            "automatic segmentation produced no usable garment masks",
            {"route": bundle.route, "warnings": bundle.warnings},
        )

    metadata: dict[str, Any] = {
        "schema_version": SCHEMA_VERSION,
        "import_id": import_id,
        "created_at": datetime.now(timezone.utc).isoformat(),
        "source": {
            "original_filename": source_path.name,
            "sha256": _file_sha256(source_path),
            "width": image.width,
            "height": image.height,
            "file": "source.png",
        },
        "route": bundle.route,
        "model_info": bundle.model_info,
        "warnings": bundle.warnings,
        "assets": records,
    }
    write_json(root / "metadata.json", metadata)
    return metadata


def cutout_subject(
    image_path: str | Path,
    destination: str | Path,
    backend: Any,
    margin_ratio: float = 0.04,
) -> dict[str, Any]:
    """Cut the person out of a photo and write a trimmed transparent PNG.

    This is the model-base import path (CERE-28): the member picks a photo and
    the app has to end up with a transparent full-body figure without anyone
    opening an image editor. Unlike the wardrobe import there is no quality
    gate here -- a base image the member chose on purpose is theirs to keep, and
    the anchors are derived from whatever silhouette comes out.
    """
    source_path = Path(image_path).resolve()
    image = _load_image(source_path)
    raw_mask = backend.segment_subject(image)
    mask = clean_mask(raw_mask)
    refinement = refine_cutout(image, mask)
    cutout = refinement.cutout

    alpha = np.asarray(cutout.getchannel("A"), dtype=np.uint8)
    rows = np.nonzero(alpha.max(axis=1) >= 16)[0]
    columns = np.nonzero(alpha.max(axis=0) >= 16)[0]
    if rows.size == 0 or columns.size == 0:
        raise PipelineError(
            "SUBJECT_NOT_FOUND",
            "no person was found in this photo",
            {"path": str(source_path)},
        )

    margin = int(round(max(cutout.width, cutout.height) * max(margin_ratio, 0.0)))
    left = max(0, int(columns[0]) - margin)
    top = max(0, int(rows[0]) - margin)
    right = min(cutout.width, int(columns[-1]) + 1 + margin)
    bottom = min(cutout.height, int(rows[-1]) + 1 + margin)
    trimmed = cutout.crop((left, top, right, bottom))

    target = Path(destination)
    target.parent.mkdir(parents=True, exist_ok=True)
    trimmed.save(target, format="PNG")
    visible = int(np.count_nonzero(np.asarray(trimmed.getchannel("A"), dtype=np.uint8) >= 16))
    return {
        "file": str(target),
        "width": trimmed.width,
        "height": trimmed.height,
        "visible_pixels": visible,
        "visible_ratio": round(visible / float(trimmed.width * trimmed.height), 6),
        "source": {
            "original_filename": source_path.name,
            "width": image.width,
            "height": image.height,
        },
    }


def set_category(
    metadata_path: str | Path,
    asset_id: str,
    category: str,
) -> dict[str, Any]:
    validate_slug(asset_id, "asset_id")
    if category not in ALLOWED_CATEGORIES:
        raise PipelineError(
            "INVALID_REQUEST",
            "unsupported category",
            {"category": category, "allowed": list(ALLOWED_CATEGORIES)},
        )
    metadata = read_json(metadata_path)
    for asset in metadata.get("assets", []):
        if asset.get("asset_id") == asset_id:
            asset["category"] = category
            asset["category_source"] = "user-override"
            tags = [
                tag
                for tag in asset.get("tags", [])
                if isinstance(tag, str) and tag.startswith("color:")
            ]
            asset["tags"] = [category, *tags]
            write_json(metadata_path, metadata)
            return metadata
    raise PipelineError(
        "ASSET_NOT_FOUND", "asset was not found in metadata", {"asset_id": asset_id}
    )
