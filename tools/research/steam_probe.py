#!/usr/bin/env python3
"""Probe Steam's public search endpoint for genre-level competitive data.

Only the store's own public search HTML is used, so no API key is required.
Each probe is one tag combination; results are printed as a compact table of
title / release date / price / review summary so they can be pasted into the
market research document.
"""
from __future__ import annotations

import html
import json
import re
import sys
import time
import urllib.parse
import urllib.request

SEARCH = "https://store.steampowered.com/search/results/"

# Tag ids come from https://store.steampowered.com/tagdata/populartags/english
PROBES: list[tuple[str, dict[str, str]]] = [
    ("Co-op horror (3D, recent)", {"tags": "1667,1685", "category1": "998"}),
    ("Survival craft (3D, recent)", {"tags": "1662,1702", "category1": "998"}),
    ("Extraction shooter", {"tags": "1199779", "category1": "998"}),
    ("Action roguelike 3D", {"tags": "42804,3839", "category1": "998"}),
    ("Automation / factory", {"tags": "255534", "category1": "998"}),
    ("Colony sim", {"tags": "220585", "category1": "998"}),
    ("Souls-like", {"tags": "29482", "category1": "998"}),
    ("Creature collector", {"tags": "916648", "category1": "998"}),
    ("Cozy 3D", {"tags": "97376", "category1": "998"}),
    ("Immersive sim", {"tags": "9204", "category1": "998"}),
]

BASE = {
    "os": "win",
    "cc": "us",
    "l": "english",
    "sort_by": "_ASC",  # relevance, which for a bare tag query is popularity
    "filter": "topsellers",
    "infinite": "1",
}

TITLE_RE = re.compile(r'class="title">(.*?)</span>')
DATE_RE = re.compile(r'class="col search_released responsive_secondrow">(.*?)</div>')
PRICE_RE = re.compile(r'(?:discount_final_price|search_price[^"]*)">(.*?)</div>', re.S)
REVIEW_RE = re.compile(r'data-tooltip-html="([^"]*?)"')
APPID_RE = re.compile(r'data-ds-appid="(\d+)"')


def fetch(params: dict[str, str]) -> str:
    q = dict(BASE)
    q.update(params)
    url = SEARCH + "?" + urllib.parse.urlencode(q)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=30) as fh:
        payload = json.loads(fh.read().decode("utf-8", "replace"))
    return payload.get("results_html", "")


def strip(s: str) -> str:
    return html.unescape(re.sub(r"<.*?>", " ", s)).strip()


def main() -> int:
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 12
    for label, params in PROBES:
        try:
            body = fetch(params)
        except Exception as exc:  # network hiccup should not kill the sweep
            print(f"\n### {label}\n  FETCH FAILED: {exc}")
            continue
        rows = re.split(r'(?=<a href="https://store\.steampowered\.com/(?:app|bundle|sub)/)', body)
        print(f"\n### {label}")
        shown = 0
        for row in rows:
            title = TITLE_RE.search(row)
            if not title:
                continue
            appid = APPID_RE.search(row)
            date = DATE_RE.search(row)
            price = PRICE_RE.search(row)
            review = REVIEW_RE.search(row)
            print(
                "  {title:44.44s} | {appid:>8s} | {date:12.12s} | {price:9.9s} | {review}".format(
                    title=html.unescape(title.group(1)),
                    appid=appid.group(1) if appid else "-",
                    date=strip(date.group(1)) if date else "-",
                    price=strip(price.group(1)) if price else "-",
                    review=strip(review.group(1))[:70] if review else "-",
                )
            )
            shown += 1
            if shown >= limit:
                break
        time.sleep(1.0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
