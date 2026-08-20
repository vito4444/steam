from __future__ import annotations

import hashlib
import os
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Protocol

import numpy as np
from PIL import Image
from scipy import ndimage

from .contracts import PipelineError


MODEL_FILES = {
    "birefnet-general-lite": "birefnet-general-lite.onnx",
    "u2net_cloth_seg": "u2net_cloth_seg.onnx",
}
MODEL_MD5 = {
    "birefnet-general-lite": "4fab47adc4ff364be1713e97b7e66334",
    "u2net_cloth_seg": "2434d1f3cb744e0e49386c906e5a08bb",
}


@dataclass
class SegmentationBundle:
    route: str
    subject_mask: Image.Image
    garment_masks: dict[str, Image.Image]
    model_info: dict[str, Any] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)


class SegmentationBackend(Protocol):
    def segment(self, image: Image.Image) -> SegmentationBundle:
        ...


def subject_bbox(
    mask: Image.Image,
    padding_ratio: float = 0.08,
    threshold: int = 96,
) -> tuple[int, int, int, int] | None:
    if padding_ratio < 0:
        raise ValueError("padding_ratio must be non-negative")
    binary = np.asarray(mask.convert("L"), dtype=np.uint8) >= threshold
    labels, count = ndimage.label(binary)
    if count == 0:
        return None
    sizes = np.bincount(labels.ravel())
    sizes[0] = 0
    largest = int(sizes.argmax())
    ys, xs = np.nonzero(labels == largest)
    left, top = int(xs.min()), int(ys.min())
    right, bottom = int(xs.max()) + 1, int(ys.max()) + 1
    padding = int(round(max(right - left, bottom - top) * padding_ratio))
    return (
        max(0, left - padding),
        max(0, top - padding),
        min(mask.width, right + padding),
        min(mask.height, bottom + padding),
    )


def restore_crop_masks(
    masks: dict[str, Image.Image],
    bbox: tuple[int, int, int, int],
    source_size: tuple[int, int],
) -> dict[str, Image.Image]:
    expected = (bbox[2] - bbox[0], bbox[3] - bbox[1])
    restored: dict[str, Image.Image] = {}
    for name, mask in masks.items():
        if mask.size != expected:
            raise PipelineError(
                "MASK_SIZE_MISMATCH",
                "cropped segmentation mask has unexpected dimensions",
                {"mask": name, "expected": list(expected), "actual": list(mask.size)},
            )
        canvas = Image.new("L", source_size, 0)
        canvas.paste(mask.convert("L"), bbox[:2])
        restored[name] = canvas
    return restored


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


class RembgCascadeBackend:
    """Offline CPU adapter for BiRefNet subject crop -> U2Net cloth."""

    def __init__(self, models_dir: str | Path, padding_ratio: float = 0.08) -> None:
        self.models_dir = Path(models_dir).resolve()
        self.padding_ratio = padding_ratio
        self._subject_session: Any | None = None
        self._cloth_session: Any | None = None
        self._session_seconds = 0.0

    def _model_path(self, route: str) -> Path:
        filename = MODEL_FILES[route]
        candidates = (
            self.models_dir / filename,
            self.models_dir / "models" / route / filename,
            self.models_dir / "models" / route / f"{route}.onnx",
        )
        for candidate in candidates:
            if candidate.is_file():
                return candidate
        raise PipelineError(
            "MODEL_MISSING",
            f"required offline model is missing: {route}",
            {"route": route, "expected_paths": [str(path) for path in candidates]},
        )

    def _validate_model(self, path: Path, route: str) -> None:
        digest = hashlib.md5(usedforsecurity=False)
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        actual = digest.hexdigest()
        expected = MODEL_MD5[route]
        if actual != expected:
            raise PipelineError(
                "MODEL_INTEGRITY_FAILED",
                f"offline model checksum does not match the pinned rembg 2.0.80 artifact: {route}",
                {"route": route, "expected_md5": expected, "actual_md5": actual},
            )

    def _ensure_sessions(self) -> tuple[Path, Path]:
        subject_path = self._model_path("birefnet-general-lite")
        cloth_path = self._model_path("u2net_cloth_seg")
        self._validate_model(subject_path, "birefnet-general-lite")
        self._validate_model(cloth_path, "u2net_cloth_seg")
        if self._subject_session is None or self._cloth_session is None:
            try:
                from rembg import new_session
            except ImportError as exc:
                raise PipelineError(
                    "MODEL_MISSING",
                    f"rembg CPU runtime is not installed: {exc}",
                    {"install": "requirements-models.txt", "import_error": str(exc)},
                ) from exc
            os.environ["U2NET_HOME"] = str(self.models_dir)
            started = time.perf_counter()
            self._subject_session = new_session(
                "birefnet-general-lite", providers=["CPUExecutionProvider"]
            )
            self._cloth_session = new_session(
                "u2net_cloth_seg", providers=["CPUExecutionProvider"]
            )
            self._session_seconds = time.perf_counter() - started
        return subject_path, cloth_path

    @staticmethod
    def _predict(session: Any, image: Image.Image, names: list[str]) -> dict[str, Image.Image]:
        raw_masks = list(session.predict(image.convert("RGB")))
        masks: dict[str, Image.Image] = {}
        for index, raw in enumerate(raw_masks):
            name = names[index] if index < len(names) else f"mask-{index}"
            mask = raw.convert("L")
            if mask.size == image.size and np.any(np.asarray(mask, dtype=np.uint8) >= 96):
                masks[name] = mask
        return masks

    def segment(self, image: Image.Image) -> SegmentationBundle:
        subject_path, cloth_path = self._ensure_sessions()
        started = time.perf_counter()
        subject_masks = self._predict(self._subject_session, image, ["person"])
        subject = subject_masks.get("person", Image.new("L", image.size, 0))
        bbox = subject_bbox(subject, padding_ratio=self.padding_ratio)
        if bbox is None:
            return SegmentationBundle(
                route="birefnet-general-lite->u2net_cloth_seg",
                subject_mask=subject,
                garment_masks={},
                warnings=["subject mask is empty"],
            )
        crop = image.crop(bbox)
        cropped_masks = self._predict(
            self._cloth_session, crop, ["upper", "lower", "full"]
        )
        garments = restore_crop_masks(cropped_masks, bbox, image.size)
        elapsed = time.perf_counter() - started
        return SegmentationBundle(
            route="birefnet-general-lite->u2net_cloth_seg",
            subject_mask=subject,
            garment_masks=garments,
            model_info={
                "provider": "CPUExecutionProvider",
                "session_seconds": round(self._session_seconds, 4),
                "inference_seconds": round(elapsed, 4),
                "subject": {
                    "route": "birefnet-general-lite",
                    "bytes": subject_path.stat().st_size,
                    "sha256": _sha256(subject_path),
                },
                "cloth": {
                    "route": "u2net_cloth_seg",
                    "bytes": cloth_path.stat().st_size,
                    "sha256": _sha256(cloth_path),
                },
            },
            warnings=[f"subject crop: {bbox}"],
        )
