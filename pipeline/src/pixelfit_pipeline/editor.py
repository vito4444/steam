from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Sequence

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

from .contracts import PipelineError
from .matting import MattingConfig, refine_cutout
from .quality import evaluate_cutout


Point = tuple[int, int] | list[int]


class EditSession:
    """Non-destructive source-space alpha editor suitable for a thin UI shell."""

    def __init__(self, source: Image.Image, baseline_mask: Image.Image) -> None:
        if source.size != baseline_mask.size:
            raise PipelineError("MASK_SIZE_MISMATCH", "source and baseline mask differ")
        self.source = source.convert("RGB").copy()
        self.baseline = baseline_mask.convert("L").copy()
        self._working = np.asarray(self.baseline, dtype=np.uint8).copy()
        self._history: list[np.ndarray] = []
        self._actions: list[dict[str, Any]] = []

    @property
    def mask(self) -> Image.Image:
        return Image.fromarray(self._working.copy(), "L")

    @property
    def actions(self) -> list[dict[str, Any]]:
        return [dict(item) for item in self._actions]

    def _begin(self) -> None:
        self._history.append(self._working.copy())

    def _record(self, op: str, **params: Any) -> None:
        self._actions.append({"op": op, **params})

    def _brush(self, points: Sequence[Point], radius: int) -> np.ndarray:
        if radius < 1 or not points:
            raise PipelineError("INVALID_OPERATION", "brush requires points and radius >= 1")
        normalized = [(int(point[0]), int(point[1])) for point in points]
        overlay = Image.new("L", self.source.size, 0)
        draw = ImageDraw.Draw(overlay)
        if len(normalized) > 1:
            draw.line(normalized, fill=255, width=radius * 2 + 1, joint="curve")
        for x, y in normalized:
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)
        return np.asarray(overlay, dtype=np.uint8) > 0

    def erase_brush(self, points: Sequence[Point], radius: int) -> None:
        selection = self._brush(points, radius)
        self._begin()
        self._working[selection] = 0
        self._record("erase_brush", points=[list(point) for point in points], radius=radius)

    def restore_brush(
        self,
        points: Sequence[Point],
        radius: int,
        source: str = "baseline",
    ) -> None:
        if source not in {"baseline", "opaque"}:
            raise PipelineError("INVALID_OPERATION", "restore source must be baseline or opaque")
        selection = self._brush(points, radius)
        self._begin()
        if source == "baseline":
            baseline = np.asarray(self.baseline, dtype=np.uint8)
            self._working[selection] = baseline[selection]
        else:
            self._working[selection] = 255
        self._record(
            "restore_brush",
            points=[list(point) for point in points],
            radius=radius,
            source=source,
        )

    def fill_polygon(
        self,
        points: Sequence[Point],
        mode: str,
        alpha: int = 255,
    ) -> None:
        if mode not in {"erase", "restore"} or len(points) < 3 or not 0 <= alpha <= 255:
            raise PipelineError("INVALID_OPERATION", "invalid polygon operation")
        overlay = Image.new("L", self.source.size, 0)
        normalized = [(int(point[0]), int(point[1])) for point in points]
        ImageDraw.Draw(overlay).polygon(normalized, fill=255)
        selection = np.asarray(overlay, dtype=np.uint8) > 0
        self._begin()
        self._working[selection] = 0 if mode == "erase" else alpha
        self._record(
            "fill_polygon", points=[list(point) for point in points], mode=mode, alpha=alpha
        )

    def magic_wand(
        self,
        x: int,
        y: int,
        tolerance: int,
        mode: str = "erase",
        source: str = "opaque",
    ) -> None:
        if not 0 <= x < self.source.width or not 0 <= y < self.source.height:
            raise PipelineError("INVALID_OPERATION", "magic wand seed is outside the image")
        if not 0 <= tolerance <= 441 or mode not in {"erase", "restore"}:
            raise PipelineError("INVALID_OPERATION", "invalid magic wand operation")
        rgb = np.asarray(self.source, dtype=np.int32)
        seed = rgb[y, x]
        delta = rgb - seed
        candidate = np.sum(delta * delta, axis=2, dtype=np.int64) <= tolerance * tolerance
        labels, _ = ndimage.label(candidate)
        selection = labels == labels[y, x]
        self._begin()
        if mode == "erase":
            self._working[selection] = 0
        elif source == "baseline":
            baseline = np.asarray(self.baseline, dtype=np.uint8)
            self._working[selection] = baseline[selection]
        elif source == "opaque":
            self._working[selection] = 255
        else:
            self._history.pop()
            raise PipelineError("INVALID_OPERATION", "invalid magic wand restore source")
        self._record(
            "magic_wand",
            x=x,
            y=y,
            tolerance=tolerance,
            mode=mode,
            source=source,
        )

    def feather(self, radius: int) -> None:
        if radius < 1 or radius > 64:
            raise PipelineError("INVALID_OPERATION", "feather radius must be 1..64")
        self._begin()
        blurred = Image.fromarray(self._working, "L").filter(ImageFilter.GaussianBlur(radius))
        self._working = np.asarray(blurred, dtype=np.uint8).copy()
        self._record("feather", radius=radius)

    def contract(self, pixels: int) -> None:
        if pixels < 1 or pixels > 64:
            raise PipelineError("INVALID_OPERATION", "contract pixels must be 1..64")
        self._begin()
        self._working = ndimage.grey_erosion(
            self._working, size=(pixels * 2 + 1, pixels * 2 + 1)
        ).astype(np.uint8)
        self._record("contract", pixels=pixels)

    def expand(self, pixels: int) -> None:
        if pixels < 1 or pixels > 64:
            raise PipelineError("INVALID_OPERATION", "expand pixels must be 1..64")
        self._begin()
        self._working = ndimage.grey_dilation(
            self._working, size=(pixels * 2 + 1, pixels * 2 + 1)
        ).astype(np.uint8)
        self._record("expand", pixels=pixels)

    def undo(self) -> None:
        if not self._history:
            raise PipelineError("INVALID_OPERATION", "there is no edit to undo")
        self._working = self._history.pop()
        if self._actions:
            self._actions.pop()

    def reset(self) -> None:
        self._working = np.asarray(self.baseline, dtype=np.uint8).copy()
        self._history.clear()
        self._actions.clear()

    def save_mask(self, destination: str | Path) -> None:
        path = Path(destination)
        path.parent.mkdir(parents=True, exist_ok=True)
        self.mask.save(path)

    def export(
        self,
        destination: str | Path,
        revision_log: str | Path | None = None,
        decontaminate_radius: int = 8,
        quarantine_destination: str | Path | None = None,
        category: str | None = None,
    ) -> dict[str, Any]:
        path = Path(destination)
        refinement = refine_cutout(
            self.source,
            self.mask,
            MattingConfig(decontaminate_radius=decontaminate_radius),
            category=category,
        )
        quality = evaluate_cutout(
            refinement.cutout, category=category, source_mask=self.mask
        )
        written_file: str | None = None
        quarantine_file: str | None = None
        if quality.allowed:
            path.parent.mkdir(parents=True, exist_ok=True)
            refinement.cutout.save(path, format="PNG")
            written_file = str(path)
        elif quarantine_destination is not None:
            quarantine_path = Path(quarantine_destination)
            quarantine_path.parent.mkdir(parents=True, exist_ok=True)
            refinement.cutout.save(quarantine_path, format="PNG")
            quarantine_file = str(quarantine_path)
        result = {
            "file": written_file,
            "quarantine_file": quarantine_file,
            "width": refinement.cutout.width,
            "height": refinement.cutout.height,
            "action_count": len(self._actions),
            "admission": quality.to_dict(),
            "processing": refinement.processing_dict(),
        }
        if revision_log is not None:
            log_path = Path(revision_log)
            log_path.parent.mkdir(parents=True, exist_ok=True)
            with log_path.open("a", encoding="utf-8", newline="\n") as stream:
                stream.write(
                    json.dumps(
                        {
                            "output": written_file,
                            "quarantine_output": quarantine_file,
                            "actions": self.actions,
                            "admission": quality.to_dict(),
                        },
                        ensure_ascii=False,
                    )
                    + "\n"
                )
        return result
