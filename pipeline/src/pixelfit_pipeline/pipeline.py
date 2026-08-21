from __future__ import annotations

import hashlib
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageOps, UnidentifiedImageError

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


CATEGORY_MAP = {
    "upper": "upper-body",
    "lower": "bottoms",
    "full": "dress-or-full-body",
}


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
    for channel, raw_mask in bundle.garment_masks.items():
        asset_id = validate_slug(channel, "mask_name")
        if raw_mask.size != image.size:
            raise PipelineError(
                "MASK_SIZE_MISMATCH",
                "garment mask differs from source image",
                {"mask": channel},
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
            category=CATEGORY_MAP.get(channel),
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
        category = CATEGORY_MAP.get(channel, "other")
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
                "mask_name": channel,
                "category": category,
                "category_source": "u2net-cloth-coarse-class",
                "tags": [category, *[f"color:{entry['hex']}" for entry in palette[:3]]],
                "palette": palette,
                "source_bbox": list(bbox),
                "pixel_width": image.width,
                "pixel_height": image.height,
                "visible_ratio": quality.metrics["visible_ratio"],
                "review_state": (
                    "approved" if quality.allowed else "needs_manual_repair"
                ),
                "warnings": [reason.to_dict() for reason in quality.reasons],
                "quality": quality.to_dict(),
                "admission": admission,
                "processing": refinement.processing_dict(),
                "mask_file": mask_path.relative_to(root).as_posix(),
                "working_mask_file": working_path.relative_to(root).as_posix(),
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
