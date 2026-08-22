from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Callable

import numpy as np
from PIL import Image

from .contracts import SCHEMA_VERSION, PipelineError
from .editor import EditSession
from .matting import refine_cutout
from .pipeline import analyze_image, cutout_subject, set_category
from .quality import evaluate_cutout
from .segmentation import RembgCascadeBackend


class AppBridge:
    """Stateful JSON-lines bridge intended to live beside the desktop app."""

    def __init__(self) -> None:
        self.sessions: dict[str, EditSession] = {}

    @staticmethod
    def _required(request: dict[str, Any], field: str) -> Any:
        if field not in request:
            raise PipelineError(
                "INVALID_REQUEST", f"missing required field: {field}", {"field": field}
            )
        return request[field]

    @staticmethod
    def _open_image(path: str, mode: str) -> Image.Image:
        candidate = Path(path)
        if not candidate.is_file():
            raise PipelineError(
                "ASSET_NOT_FOUND", "image file was not found", {"path": str(candidate)}
            )
        try:
            with Image.open(candidate) as opened:
                opened.load()
                return opened.convert(mode)
        except OSError as exc:
            raise PipelineError(
                "UNSUPPORTED_IMAGE", "image file could not be opened", {"path": str(candidate)}
            ) from exc

    def _session(self, request: dict[str, Any]) -> EditSession:
        session_id = str(self._required(request, "session_id"))
        try:
            return self.sessions[session_id]
        except KeyError as exc:
            raise PipelineError(
                "ASSET_NOT_FOUND", "edit session was not found", {"session_id": session_id}
            ) from exc

    def _dispatch(self, request: dict[str, Any]) -> dict[str, Any]:
        command = self._required(request, "command")
        if command == "ping":
            return {"service": "pixelfit-pipeline", "schema_version": SCHEMA_VERSION}
        if command == "open-session":
            session_id = str(self._required(request, "session_id"))
            source = self._open_image(str(self._required(request, "source_path")), "RGB")
            mask = self._open_image(str(self._required(request, "mask_path")), "L")
            self.sessions[session_id] = EditSession(source, mask)
            return {"session_id": session_id, "width": source.width, "height": source.height}
        if command == "apply":
            session = self._session(request)
            operation = str(self._required(request, "operation"))
            params = request.get("params", {})
            if not isinstance(params, dict):
                raise PipelineError("INVALID_REQUEST", "params must be an object")
            operations: dict[str, Callable[..., None]] = {
                "erase_brush": session.erase_brush,
                "restore_brush": session.restore_brush,
                "fill_polygon": session.fill_polygon,
                "magic_wand": session.magic_wand,
                "feather": session.feather,
                "contract": session.contract,
                "expand": session.expand,
            }
            if operation not in operations:
                raise PipelineError(
                    "INVALID_OPERATION", "unsupported editor operation", {"operation": operation}
                )
            try:
                operations[operation](**params)
            except TypeError as exc:
                raise PipelineError(
                    "INVALID_REQUEST",
                    "editor operation parameters are invalid",
                    {"operation": operation},
                ) from exc
            return {
                "session_id": request["session_id"],
                "action_count": len(session.actions),
                "visible_pixels": int(np.count_nonzero(np.asarray(session.mask))),
            }
        if command == "undo":
            session = self._session(request)
            session.undo()
            return {"action_count": len(session.actions)}
        if command == "reset":
            session = self._session(request)
            session.reset()
            return {"action_count": 0}
        if command == "save-mask":
            session = self._session(request)
            destination = str(self._required(request, "destination"))
            session.save_mask(destination)
            return {"file": destination}
        if command == "export":
            session = self._session(request)
            return session.export(
                str(self._required(request, "destination")),
                revision_log=request.get("revision_log"),
                decontaminate_radius=int(request.get("decontaminate_radius", 8)),
                quarantine_destination=request.get("quarantine_destination"),
                category=request.get("category"),
            )
        if command == "quality-check":
            cutout = self._open_image(
                str(self._required(request, "cutout_path")), "RGBA"
            )
            return evaluate_cutout(cutout, category=request.get("category")).to_dict()
        if command == "refine-cutout":
            cutout = self._open_image(
                str(self._required(request, "cutout_path")), "RGBA"
            )
            refinement = refine_cutout(
                cutout.convert("RGB"),
                cutout.getchannel("A"),
                category=request.get("category"),
            )
            quality = evaluate_cutout(
                refinement.cutout,
                category=request.get("category"),
                source_mask=cutout.getchannel("A"),
            )
            destination = Path(str(self._required(request, "destination")))
            written_file: str | None = None
            quarantine_file: str | None = None
            if quality.allowed:
                destination.parent.mkdir(parents=True, exist_ok=True)
                refinement.cutout.save(destination, format="PNG")
                written_file = str(destination)
            elif request.get("quarantine_destination") is not None:
                quarantine = Path(str(request["quarantine_destination"]))
                quarantine.parent.mkdir(parents=True, exist_ok=True)
                refinement.cutout.save(quarantine, format="PNG")
                quarantine_file = str(quarantine)
            return {
                "file": written_file,
                "quarantine_file": quarantine_file,
                "admission": quality.to_dict(),
                "processing": refinement.processing_dict(),
            }
        if command == "set-category":
            return set_category(
                str(self._required(request, "metadata_path")),
                str(self._required(request, "asset_id")),
                str(self._required(request, "category")),
            )
        if command == "cutout-subject":
            backend = RembgCascadeBackend(str(self._required(request, "models_dir")))
            return cutout_subject(
                str(self._required(request, "image_path")),
                str(self._required(request, "destination")),
                backend,
            )
        if command == "analyze":
            backend = RembgCascadeBackend(str(self._required(request, "models_dir")))
            return analyze_image(
                str(self._required(request, "image_path")),
                str(self._required(request, "import_id")),
                str(self._required(request, "output_dir")),
                backend,
            )
        raise PipelineError(
            "INVALID_OPERATION", "unsupported command", {"command": command}
        )

    def handle(self, request: Any) -> dict[str, Any]:
        try:
            if not isinstance(request, dict):
                raise PipelineError("INVALID_REQUEST", "request must be a JSON object")
            return {"ok": True, "result": self._dispatch(request)}
        except PipelineError as exc:
            return {"ok": False, "error": exc.to_dict()}
        except Exception as exc:
            return {
                "ok": False,
                "error": PipelineError(
                    "INTERNAL_ERROR", "unexpected pipeline failure", {"type": type(exc).__name__}
                ).to_dict(),
            }

    def handle_line(self, line: str) -> str:
        try:
            request = json.loads(line)
        except json.JSONDecodeError as exc:
            response = {
                "ok": False,
                "error": PipelineError(
                    "INVALID_REQUEST",
                    "request is not valid JSON",
                    {"line": exc.lineno, "column": exc.colno},
                ).to_dict(),
            }
        else:
            response = self.handle(request)
        # ensure_ascii=True 是有意的：回程也必须是纯 ASCII，见 main() 的说明。
        return json.dumps(response, ensure_ascii=True, separators=(",", ":"))


def _force_utf8_stdio() -> None:
    """Pin the JSON-lines transport to UTF-8 regardless of the console codepage.

    Frozen on Windows the interpreter picks the ANSI codepage for stdin/stdout,
    so a request carrying a non-ASCII path (e.g. the default Chinese screenshot
    name 屏幕截图 2026-07-30 005149.png) decodes into mojibake and every lookup
    fails with ASSET_NOT_FOUND. Decoding wrongly and then encoding wrongly is
    byte-symmetric, so the echoed path in the error still *looks* correct --
    which is why this went unnoticed. PYTHONUTF8/PYTHONIOENCODING do not help
    once PyInstaller's bootloader has configured the interpreter, so pin it here.
    """
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8", errors="replace")
        except (OSError, ValueError):  # pragma: no cover - detached stream
            pass


def main() -> int:
    _force_utf8_stdio()
    bridge = AppBridge()
    for line in sys.stdin:
        if not line.strip():
            continue
        print(bridge.handle_line(line), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
