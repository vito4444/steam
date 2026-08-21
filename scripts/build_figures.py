"""把 `electron . --figures` 出的舞台 PNG 拼成交付用的对比图。

每一格都是**应用自己的渲染引擎**画出来的（同一份规则表、同一条合成路径），
这里只负责裁白边、拼版、写标注 —— 不做任何重绘。

用法：
    npm run figures            # 先出格子（写到 pixelfit/figures/）
    python scripts/build_figures.py <baseline目录> <输出目录>

baseline 目录放 CERE-11 那一版应用出的 `*-base.png`，没有就只出两列。
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FIG = ROOT / "figures"

FONT_DIR = Path("C:/Windows/Fonts")


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    for name in (["msyhbd.ttc", "simhei.ttf"] if bold else ["msyh.ttc", "simhei.ttf"]):
        p = FONT_DIR / name
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def tile(name: str, width: int) -> Image.Image:
    """读一格，透明底压白，等比缩到指定宽度。"""
    im = Image.open(FIG / f"{name}.png").convert("RGBA")
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    h = round(width * im.height / im.width)
    return bg.convert("RGB").resize((width, h), Image.LANCZOS)


def wrap(d: ImageDraw.ImageDraw, text: str, f, max_w: int) -> list[str]:
    """按像素宽度断行。中文没有词边界，逐字量最省事也最准。"""
    out: list[str] = []
    for para in text.splitlines() or [""]:
        line = ""
        for ch in para:
            if d.textlength(line + ch, font=f) > max_w and line:
                out.append(line)
                line = ch
            else:
                line += ch
        out.append(line)
    return out


def sheet(
    out: Path,
    title: str,
    subtitle: str,
    columns: list[str],
    rows: list[tuple[str, list[str]]],
    col_w: int = 430,
) -> None:
    """columns = 列标题；rows = [(行标注, [每列的图名])]"""
    pad = 26
    head_h = 112
    col_head_h = 34
    line_h = 25

    tiles = [[tile(n, col_w) for n in names] for _, names in rows]
    row_h = max(t.height for row in tiles for t in row)

    w = pad * 2 + col_w * len(columns)
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    body = font(15)
    wrapped = [wrap(probe, cap, body, w - pad * 2 - 16) for cap, _ in rows]
    caption_hs = [len(ls) * line_h + 26 for ls in wrapped]

    h = head_h + sum(col_head_h + row_h + c for c in caption_hs) + pad
    img = Image.new("RGB", (w, h), (246, 245, 243))
    d = ImageDraw.Draw(img)

    d.text((pad, 26), title, font=font(28, True), fill=(28, 24, 20))
    d.text((pad, 66), subtitle, font=font(15), fill=(110, 100, 90))

    y = head_h
    for lines, row, ch in zip(wrapped, tiles, caption_hs):
        for i, col in enumerate(columns):
            x = pad + i * col_w
            d.text((x + 10, y + 8), col, font=font(15, True), fill=(70, 62, 54))
        y += col_head_h
        for i, t in enumerate(row):
            x = pad + i * col_w
            img.paste(t, (x, y))
            d.rectangle([x, y, x + col_w - 1, y + row_h - 1], outline=(226, 222, 216))
        y += row_h
        ty = y + 12
        for ln in lines:
            d.text((pad + 8, ty), ln, font=body, fill=(60, 54, 48))
            ty += line_h
        y += ch

    img.save(out)
    print(out, img.size)


def main() -> None:
    baseline = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else FIG
    out_dir.mkdir(parents=True, exist_ok=True)

    if baseline and baseline.exists():
        for f in baseline.glob("*-base.png"):
            (FIG / f.name).write_bytes(f.read_bytes())

    sheet(
        out_dir / "fig-a-occlusion.png",
        "CERE-14 · 遮挡与贴合闭环：三列对比",
        "全部由应用真实渲染（同一份规则表、同一条合成路径），只是把舞台单独导出来拼版。"
        "底图 = CERE-6 写实模特，素材 = CERE-10 的 41 件真实照片素材。",
        ["① CERE-11 现状", "② 本轮·关掉遮挡", "③ 本轮·遮挡全开"],
        [
            (
                "毛衣 + 皮鞋 ——「浮在胸口的一小块」→ 覆盖整个上身、比例正常。①→② 是贴合改按衣长驱动的效果；"
                "②→③ 是身体遮罩把飘在体侧的袖子裁掉（这件是摊平拍的，袖子朝两侧展开，裁掉后偏无袖，见上限说明）。",
                ["sweater-base", "sweater-off", "sweater-on"],
            ),
            (
                "衬衫 + 牛仔夹克 + 围巾 + 船鞋 —— ①里围巾糊在胸口、衬衫垂成裙子、鞋只有脚踝宽；"
                "③里围巾停在下巴以下（`scarf_below_chin`），外套按 z 盖住衬衫，鞋按双脚站距缩放。",
                ["jacket_scarf-base", "jacket_scarf-off", "jacket_scarf-on"],
            ),
            (
                "长裙 + 过膝长靴 + 手提包 —— ①里包比躯干还大、裙子悬在体侧；"
                "③里裙摆按 z 盖住靴筒，裙子只在胯以上裁到轮廓、往下用长羽化放开（硬裁会在裙摆留一条横切线）。",
                ["gown_boots-base", "gown_boots-off", "gown_boots-on"],
            ),
            (
                "针织衫 + 牛仔短裤 + 帆布鞋 + 托特包 —— 日常搭配的常态：③ 里袖子被裁进轮廓，"
                "短裤腰线与上装下摆的前后关系由画序决定（上装 z55 > 下装 z40，默认放下来）。",
                ["knit_shorts-base", "knit_shorts-off", "knit_shorts-on"],
            ),
        ],
    )

    sheet(
        out_dir / "fig-b-tuck.png",
        "CERE-14 · 塞衣角：上装下摆压在下装腰线之上 / 之下",
        "同一套搭配，只改一个开关。默认值按品类推（crop / waist 长度默认塞进去），"
        "用户可逐件改，改动记进 Look 存档。",
        ["放下来（默认）", "塞进腰里"],
        [
            (
                "格纹衬衫 + 牛仔短裤。塞进去时腰线以下的上装被下装挖掉"
                "（规则 `tuck_top_into_bottom`，生效区间从腰线 −0.015 肩宽往下）。",
                ["tuck_out-on", "tuck_in-on"],
            ),
        ],
        col_w = 470,
    )

    sheet(
        out_dir / "fig-c-limits.png",
        "CERE-14 · 效果上限：规则全开也救不回来的几类",
        "这几张不是 bug，是这条路线的天花板。成员要靠它判断要不要花钱走生成式试穿（CERE-8）。",
        ["挂拍 / 姿势对不上", "产品包装照 / 多件同框", "半透明蕾丝 + 侧身素材"],
        [
            (
                "左：卫衣是挂在衣架上拍的，被垂坠拉长拉斜，衣长与宽度两路都失真，"
                "自动解不出来，只能手动拖。\n"
                "中：折叠塑封的衬衫 + 并排两条短裤 + 盘成一圈的皮带 —— 素材本身就不是「一件穿着中的衣服」，"
                "渲染层无解，属于素材质检（CERE-12）。\n"
                "右：蕾丝裙上身是半透明的，与真·抠图欠实无法自动区分；包挂在体侧，与手臂没有互相遮挡。",
                ["limit_hanging-on", "limit_packaged-on", "limit_lace-on"],
            ),
        ],
    )


if __name__ == "__main__":
    main()
