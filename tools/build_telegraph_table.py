#!/usr/bin/env python3
"""从 Unicode Unihan 数据库生成中文电码表，供游戏运行时加载。

中文电码是真实存在的历史系统：1871 年启用，四位十进制数字对应一个汉字，
用于中文在电报线路上的传输。方案 A 把它作为中文版的核心解码机制，
所以码表必须用真实数据，不能自己编。

数据来自 Unicode Unihan 数据库的 kMainlandTelegraph 字段
（https://www.unicode.org/Public/UCD/latest/ucd/Unihan.zip），
这是官方权威来源。抽查验证：中=0022、文=2429、电=7193、码=4316、北=0554、京=0079。

输出格式为定长记录，每行 5 个字符：4 位数字码 + 1 个汉字。
定长让运行时可以按偏移直接定位，不需要逐行解析。

用法:
  tools/build_telegraph_table.py --unihan /tmp/Unihan_OtherMappings.txt \\
      --out unity/Decoder/Assets/Resources/telegraph-cn.txt
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

FIELD = "kMainlandTelegraph"


def parse_unihan(path: Path) -> dict[str, str]:
    """返回 {四位码: 汉字}。同码多字时保留码点最小的那个。"""
    by_code: dict[str, tuple[int, str]] = {}
    duplicates = 0

    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("#") or not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 3 or parts[1] != FIELD:
            continue
        codepoint_text, _, code = parts
        code = code.strip()
        if len(code) != 4 or not code.isdigit():
            continue

        codepoint = int(codepoint_text[2:], 16)
        char = chr(codepoint)
        if code in by_code:
            duplicates += 1
            if codepoint >= by_code[code][0]:
                continue
        by_code[code] = (codepoint, char)

    if duplicates:
        print(f"  同码多字 {duplicates} 处，各取码点最小者", file=sys.stderr)
    return {code: entry[1] for code, entry in by_code.items()}


def main() -> int:
    parser = argparse.ArgumentParser(description="生成中文电码表")
    parser.add_argument("--unihan", type=Path, required=True,
                        help="Unihan_OtherMappings.txt 路径")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    table = parse_unihan(args.unihan)
    if not table:
        print("未解析到任何电码，检查输入文件", file=sys.stderr)
        return 1

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8", newline="\n") as f:
        for code in sorted(table):
            f.write(f"{code}{table[code]}\n")

    print(f"  写出 {len(table)} 条电码 -> {args.out}")
    for probe in ("0001", "0022", "2429", "7193", "4316"):
        if probe in table:
            print(f"    校验 {probe} = {table[probe]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
