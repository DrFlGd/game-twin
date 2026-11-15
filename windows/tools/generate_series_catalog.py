#!/usr/bin/env python3
"""Regenerate Windows curated-series links from Game Twin's existing Kotlin catalog.

Usage:
    python windows/tools/generate_series_catalog.py
    python windows/tools/generate_series_catalog.py --check

Only URL/title metadata is exported; original guide content is not included.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "app/src/main/java/com/twinlore/app/data/GuideSeriesCatalog.kt"
OUTPUT = ROOT / "windows/GameTwinGuideLibrary.Core/Catalogs/series_catalog.json"
HEAD = re.compile(r"^    (Metroid|Zelda|Pokemon|FireEmblem)\($", re.M)
ENTRY = re.compile(
    r'SeriesEntry\(\s*"([^"]+)"\s*,\s*"([^"]+)"'
    r'(?:\s*,\s*mapUrl\s*=\s*"([^"]+)")?\s*\)'
)


def generate() -> str:
    source = SOURCE.read_text(encoding="utf-8")
    source = re.sub(r"^\s*//[^\n]*(?:\n|$)", "\n", source, flags=re.M)
    heads = list(HEAD.finditer(source))
    if len(heads) != 4:
        raise ValueError(f"Expected four curated series, found {len(heads)}")
    result = []
    for idx, head in enumerate(heads):
        end = heads[idx + 1].start() if idx + 1 < len(heads) else source.index("\n}\n", head.start())
        body = source[head.start():end]
        def attr(name: str) -> str:
            match = re.search(r'\b' + name + r'\s*=\s*"([^"]+)"', body)
            if match is None:
                raise ValueError(f"Missing {name} for {head.group(1)}")
            return match.group(1)
        entries = []
        for entry in ENTRY.finditer(body):
            guide = {"title": entry.group(1), "url": entry.group(2)}
            if entry.group(3):
                guide["mapUrl"] = entry.group(3)
            entries.append(guide)
        if not entries:
            raise ValueError(f"No links extracted for {head.group(1)}")
        result.append({
            "name": attr("displayName"),
            "keyword": attr("keyword"),
            "hubUrl": attr("hubUrl"),
            "entries": entries,
        })
    return json.dumps(result, ensure_ascii=False, indent=2) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    content = generate()
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != content:
            raise SystemExit("Series catalog is stale. Run generate_series_catalog.py and commit the update.")
        print("Series catalog matches the Android source.")
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(content, encoding="utf-8")
        print("Generated", OUTPUT)


if __name__ == "__main__":
    main()
