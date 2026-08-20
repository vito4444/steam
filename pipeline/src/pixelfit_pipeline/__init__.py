"""PixelFit photo-to-wardrobe pipeline."""

from .contracts import PipelineError
from .editor import EditSession
from .matting import MattingConfig, RefinementResult, refine_cutout
from .pipeline import analyze_image, set_category
from .quality import QualityReport, QualityThresholds, evaluate_cutout
from .segmentation import RembgCascadeBackend, SegmentationBundle

__all__ = [
    "EditSession",
    "MattingConfig",
    "PipelineError",
    "QualityReport",
    "QualityThresholds",
    "RefinementResult",
    "RembgCascadeBackend",
    "SegmentationBundle",
    "analyze_image",
    "evaluate_cutout",
    "refine_cutout",
    "set_category",
]
