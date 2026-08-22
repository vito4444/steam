"""PixelFit photo-to-wardrobe pipeline."""

from .candidate_generation import CandidateMask, generate_candidates
from .contracts import PipelineError
from .editor import EditSession
from .matting import MattingConfig, RefinementResult, refine_cutout
from .pipeline import analyze_image, set_category
from .quality import QualityReport, QualityThresholds, evaluate_cutout
from .segmentation import RembgCascadeBackend, SegmentationBundle

__all__ = [
    "EditSession",
    "CandidateMask",
    "MattingConfig",
    "PipelineError",
    "QualityReport",
    "QualityThresholds",
    "RefinementResult",
    "RembgCascadeBackend",
    "SegmentationBundle",
    "analyze_image",
    "evaluate_cutout",
    "generate_candidates",
    "refine_cutout",
    "set_category",
]
