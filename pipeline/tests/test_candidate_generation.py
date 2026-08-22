from __future__ import annotations

from PIL import Image, ImageDraw

from pixelfit_pipeline.candidate_generation import (
    classify_scene,
    generate_candidates,
)
from pixelfit_pipeline.pipeline import analyze_image
from pixelfit_pipeline.segmentation import SegmentationBundle


def _mask(
    size: tuple[int, int],
    rectangles: list[tuple[int, int, int, int]],
) -> Image.Image:
    image = Image.new("L", size, 0)
    draw = ImageDraw.Draw(image)
    for rectangle in rectangles:
        draw.rectangle(rectangle, fill=255)
    return image


def _flatlay_masks() -> tuple[Image.Image, dict[str, Image.Image]]:
    size = (100, 120)
    top = (25, 5, 74, 39)
    bottom = (30, 45, 69, 79)
    bag = (5, 40, 21, 69)
    left_shoe = (35, 90, 47, 114)
    right_shoe = (52, 90, 64, 114)
    subject = _mask(size, [top, bottom, bag, left_shoe, right_shoe])
    return subject, {
        "upper": _mask(size, [top]),
        "lower": _mask(size, [bottom]),
        "full": _mask(size, []),
    }


def _worn_masks() -> tuple[Image.Image, dict[str, Image.Image]]:
    size = (100, 160)
    subject = _mask(
        size,
        [
            (40, 5, 59, 29),
            (25, 25, 74, 94),
            (31, 85, 47, 145),
            (52, 85, 68, 145),
            (27, 140, 48, 158),
            (51, 140, 72, 158),
        ],
    )
    return subject, {
        "upper": _mask(size, [(25, 25, 74, 89)]),
        "lower": _mask(size, [(43, 70, 56, 105)]),
        "full": _mask(size, [(30, 75, 69, 104)]),
    }


def test_flatlay_keeps_clothes_and_extracts_bag_and_paired_shoes() -> None:
    """Regression: residual accessories must not disappear behind three cloth channels."""
    subject, garment_masks = _flatlay_masks()

    candidates = generate_candidates(subject, garment_masks)

    assert classify_scene(subject) == "flatlay"
    assert [(item.asset_id, item.category) for item in candidates] == [
        ("upper", "upper-body"),
        ("lower", "bottoms"),
        ("bag", "bag"),
        ("shoes", "shoes"),
    ]
    assert candidates[-1].mask.getbbox() == (35, 90, 65, 115)


def test_worn_scene_maps_outer_layer_skirt_and_partial_shoes() -> None:
    """Regression: a tall person must yield wearable layers instead of flat-lay labels."""
    subject, garment_masks = _worn_masks()

    candidates = generate_candidates(subject, garment_masks)

    assert classify_scene(subject) == "worn"
    assert [
        (item.asset_id, item.category, item.visibility) for item in candidates
    ] == [
        ("outer", "outerwear", "complete"),
        ("bottom", "bottoms", "complete"),
        ("shoes", "shoes", "partial"),
    ]
    assert candidates[-1].mask.getbbox() == (27, 140, 73, 159)


def test_analyze_image_preserves_every_candidate_preview_and_provenance(tmp_path) -> None:
    """Regression: pipeline orchestration must not collapse generated instances to U2Net names."""
    subject, garment_masks = _flatlay_masks()
    source = tmp_path / "flatlay.png"
    Image.new("RGB", subject.size, (188, 146, 103)).save(source)

    class FakeBackend:
        def segment(self, _image: Image.Image) -> SegmentationBundle:
            return SegmentationBundle(
                route="fake-subject->fake-cloth",
                subject_mask=subject,
                garment_masks=garment_masks,
            )

    metadata = analyze_image(source, "flatlay-case", tmp_path / "output", FakeBackend())

    assert [
        (record["asset_id"], record["category"]) for record in metadata["assets"]
    ] == [
        ("upper", "upper-body"),
        ("lower", "bottoms"),
        ("bag", "bag"),
        ("shoes", "shoes"),
    ]
    root = tmp_path / "output" / "flatlay-case"
    for record in metadata["assets"]:
        assert (root / record["preview_file"]).is_file()
        assert record["candidate_source"]
        assert record["scene"] == "flatlay"
        assert record["review_state"] in {"approved", "needs_optimization", "retry"}
