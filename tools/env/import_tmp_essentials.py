#!/usr/bin/env python3
"""Unpacks TextMeshPro's essential resources into the project.

TMP ships its shaders, settings asset, default font asset and default material inside a
.unitypackage rather than in the package's Runtime folder, so a project that never opened
the editor GUI does not have them and every TMP_Text component fails at runtime.

Unity's own AssetDatabase.ImportPackage is asynchronous: in batch mode with -quit the
editor exits before the import lands, and without -quit it may hang. A .unitypackage is
just a gzipped tar of `<guid>/asset`, `<guid>/asset.meta` and `<guid>/pathname`, so it is
unpacked directly here instead. That is synchronous, verifiable, and preserves the
original GUIDs from the .meta files, which is what makes the internal references between
the settings asset, the font asset and the material resolve.
"""
from __future__ import annotations

import argparse
import sys
import tarfile
from pathlib import Path

DEFAULT_PACKAGE = (
    "/opt/unity/editors/6000.5.8f1/Editor/Data/Resources/PackageManager/BuiltInPackages/"
    "com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage"
)


def unpack(package: Path, assets_root: Path) -> int:
    if not package.exists():
        print(f"[tmp-import] FATAL: package not found at {package}", file=sys.stderr)
        return 1

    written = 0
    skipped = 0

    with tarfile.open(package, "r:gz") as archive:
        entries: dict[str, dict[str, tarfile.TarInfo]] = {}
        for member in archive.getmembers():
            parts = member.name.split("/")
            if len(parts) != 2 or not member.isfile():
                continue
            entries.setdefault(parts[0], {})[parts[1]] = member

        for guid, files in sorted(entries.items()):
            if "pathname" not in files:
                continue

            handle = archive.extractfile(files["pathname"])
            if handle is None:
                continue
            pathname = handle.read().decode("utf-8").splitlines()[0].strip()

            # Everything in this package is under Assets/; anything else would be a
            # package trying to write outside the project and is refused.
            if not pathname.startswith("Assets/") or ".." in pathname:
                print(f"[tmp-import] refusing suspicious path: {pathname}", file=sys.stderr)
                return 1

            target = assets_root.parent / pathname

            if "asset" not in files:
                # A folder entry: create it and keep its .meta so the GUID is stable.
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                payload = archive.extractfile(files["asset"])
                if payload is None:
                    skipped += 1
                    continue
                target.write_bytes(payload.read())
                written += 1

            if "asset.meta" in files:
                meta = archive.extractfile(files["asset.meta"])
                if meta is not None:
                    Path(str(target) + ".meta").write_bytes(meta.read())

    print(f"[tmp-import] wrote {written} asset(s) into {assets_root}, skipped {skipped}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, default=Path(DEFAULT_PACKAGE))
    parser.add_argument("--assets", type=Path, required=True, help="the project's Assets folder")
    args = parser.parse_args()

    if not args.assets.is_dir():
        print(f"[tmp-import] FATAL: {args.assets} is not a directory", file=sys.stderr)
        return 1

    status = unpack(args.package, args.assets)
    if status != 0:
        return status

    # The three files everything else depends on. If any is missing the import produced
    # something unusable and it is better to fail here than at runtime.
    required = [
        "TextMesh Pro/Resources/TMP Settings.asset",
        "TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset",
        "TextMesh Pro/Shaders/TMP_SDF.shader",
    ]
    missing = [r for r in required if not (args.assets / r).exists()]
    if missing:
        print("[tmp-import] FATAL: required resources missing after unpack:", file=sys.stderr)
        for item in missing:
            print(f"  {item}", file=sys.stderr)
        return 1

    print("[tmp-import] verified settings, default font asset and SDF shader are present")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
