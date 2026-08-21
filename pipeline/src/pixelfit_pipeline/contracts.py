from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any


SCHEMA_VERSION = "2.0"
ALLOWED_CATEGORIES = (
    "upper-body",
    "bottoms",
    "outerwear",
    "dress-or-full-body",
    "shoes",
    "bag",
    "accessory",
    "other",
)


class PipelineError(Exception):
    """Expected application-facing failure with a stable wire code."""

    def __init__(
        self,
        code: str,
        message: str,
        details: dict[str, Any] | None = None,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.details = details or {}

    def to_dict(self) -> dict[str, Any]:
        return {
            "code": self.code,
            "message": self.message,
            "details": self.details,
        }


def validate_slug(value: str, field: str = "id") -> str:
    if (
        not isinstance(value, str)
        or not re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,63}", value)
        or ".." in value
    ):
        raise PipelineError(
            "INVALID_REQUEST",
            f"{field} must be a safe lowercase slug",
            {"field": field},
        )
    return value


def read_json(path: str | Path) -> dict[str, Any]:
    candidate = Path(path)
    try:
        payload = json.loads(candidate.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise PipelineError(
            "ASSET_NOT_FOUND", "metadata file was not found", {"path": str(candidate)}
        ) from exc
    except (OSError, json.JSONDecodeError) as exc:
        raise PipelineError(
            "IO_ERROR", "metadata file could not be read", {"path": str(candidate)}
        ) from exc
    if not isinstance(payload, dict):
        raise PipelineError("INVALID_REQUEST", "metadata root must be an object")
    return payload


def write_json(path: str | Path, payload: dict[str, Any]) -> None:
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".tmp")
    try:
        temporary.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        temporary.replace(destination)
    except OSError as exc:
        raise PipelineError(
            "IO_ERROR", "metadata file could not be written", {"path": str(destination)}
        ) from exc
