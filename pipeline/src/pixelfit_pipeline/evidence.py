from __future__ import annotations

from PIL import Image, ImageDraw, ImageFont, ImageOps


def checkerboard(size: tuple[int, int], tile: int = 20) -> Image.Image:
    background = Image.new("RGBA", size, (246, 246, 246, 255))
    draw = ImageDraw.Draw(background)
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle(
                    (x, y, min(x + tile, size[0]), min(y + tile, size[1])),
                    fill=(224, 226, 230, 255),
                )
    return background


def _fit(image: Image.Image, size: tuple[int, int], transparent: bool) -> Image.Image:
    prepared = ImageOps.contain(image.convert("RGBA"), size, Image.Resampling.LANCZOS)
    if transparent:
        canvas = checkerboard(size)
        offset = ((size[0] - prepared.width) // 2, (size[1] - prepared.height) // 2)
        canvas.alpha_composite(prepared, offset)
        return canvas
    canvas = Image.new("RGBA", size, "white")
    offset = ((size[0] - prepared.width) // 2, (size[1] - prepared.height) // 2)
    canvas.alpha_composite(prepared, offset)
    return canvas
def render_comparison(
    original: Image.Image,
    automatic: Image.Image,
    repaired: Image.Image,
    title: str,
    attribution: str,
) -> Image.Image:
    """Render a three-panel original -> automatic -> repaired evidence sheet."""
    panel = (420, 560)
    margin = 24
    header = 68
    footer = 46
    width = panel[0] * 3 + margin * 4
    height = header + panel[1] + footer
    canvas = Image.new("RGB", (width, height), (250, 250, 251))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    draw.text((margin, 16), title, fill=(25, 28, 33), font=font)
    labels = ("Original photo", "Automatic pre-cut", "After manual repair")
    images = (
        _fit(original, panel, False),
        _fit(automatic, panel, True),
        _fit(repaired, panel, True),
    )
    for index, (label, image) in enumerate(zip(labels, images)):
        x = margin + index * (panel[0] + margin)
        canvas.paste(image.convert("RGB"), (x, header))
        draw.text((x, header - 20), label, fill=(55, 58, 64), font=font)
    draw.text((margin, height - 26), attribution, fill=(80, 83, 89), font=font)
    return canvas
