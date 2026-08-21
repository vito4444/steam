"""
把 CERE-6 交付的写实模特底图（pixelfit-visual-kit/models）转成应用内置底图包。

CERE-4 自带的是插画娃娃底图，成员明确否掉了。CERE-6 的底图是写实无面模特、
带自己的锚点表（1152×2304，按解剖重新标定，不是 CERE-2 网格 ×6），所以
底图包必须自带 canvas 与 anchors，应用侧不能再假设 768×1536。

用法：python scripts/build_base_pack.py <visual-kit-dir> [--out assets/base]
"""

import argparse
import json
import shutil
from pathlib import Path

BODIES = ["s", "m", "l"]


def build(kit: Path, out: Path) -> None:
    anchors_doc = json.loads((kit / "models" / "anchors.json").read_text("utf-8"))
    canvas = anchors_doc["canvas"]
    anchors = anchors_doc["anchors"]
    tones = anchors_doc["skin_tones"]

    if out.exists():
        shutil.rmtree(out)

    for body in BODIES:
        body_dir = out / body
        body_dir.mkdir(parents=True)

        tone_entries = []
        for tone in tones:
            src = kit / "models" / f"model_{body}_{tone['id']}.png"
            shutil.copyfile(src, body_dir / src.name)
            tone_entries.append({
                "id": tone["id"],
                "name": tone["name"],
                "swatch": tone["base"],
                "layers": [{"file": src.name, "z": 20, "mode": "normal"}],
            })

        under = kit / "models" / f"underlayer_{body}.png"
        shutil.copyfile(under, body_dir / under.name)

        manifest = {
            "pack": "cere6-realistic-model",
            "body": body,
            "canvas": canvas,
            "anchors": anchors,
            "tones": tone_entries,
            # 默认肤色之外的公共层：打底层压在衣物之下，z 用 24.5 让出
            # underlayer(25) 这个衣物槽位
            "layers": [{"file": under.name, "z": 24.5, "mode": "normal"}],
            "hair": {},
        }
        (body_dir / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2), "utf-8"
        )
        print(f"[base] {body}: {len(tone_entries)} tones -> {body_dir}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("kit", type=Path)
    ap.add_argument("--out", type=Path, default=Path("assets/base"))
    args = ap.parse_args()
    build(args.kit, args.out)
