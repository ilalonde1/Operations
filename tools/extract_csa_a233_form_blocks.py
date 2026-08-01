#!/usr/bin/env python3
"""
Extract recoverable Form XObjects from the Vitrium-protected CSA A23.3 PDF.

The PDF's normal text layer is intentionally obfuscated, and full-page rendering
through Poppler produces blank pages. The useful content is still present in
embedded Form XObjects. This script saves each non-watermark form object as an
individual vector PDF, optionally renders it to PNG using pdftoppm, and writes a
CSV/JSON manifest for review.
"""

from __future__ import annotations

import csv
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

from pypdf import PdfReader, PdfWriter
from pypdf._page import PageObject
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject, NullObject


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PDF = ROOT / "docs" / "CSA A23.3 24 Design of Concrete Structures 1.pdf"
OUT_DIR = ROOT / "docs" / "csa-a233-equation-extract"
PDF_DIR = OUT_DIR / "form-pdfs"
PNG_DIR = OUT_DIR / "form-pngs"


def _literal_string_sample(data: bytes, limit: int = 30) -> tuple[int, str]:
    strings = re.findall(rb"\((?:\\.|[^\\)]){1,160}\)", data)
    readable: list[str] = []
    for raw in strings:
        body = raw[1:-1]
        if not any(32 <= b <= 126 for b in body):
            continue
        try:
            text = body.decode("latin1")
        except UnicodeDecodeError:
            text = repr(body)
        text = text.replace("\r", " ").replace("\n", " ").strip()
        if text and "joka" not in text:
            readable.append(text)

    return len(strings), " | ".join(readable[:limit])[:500]


def _looks_like_watermark(name: str, data: bytes) -> bool:
    return name.startswith("/Xi") or b"joka" in data[:5000]


def _extract_one(reader: PdfReader, page_num: int, name: str, ref, obj, index: int) -> dict[str, object]:
    bbox = obj.get("/BBox")
    if bbox is None or len(bbox) < 4:
        width = 612.0
        height = 792.0
    else:
        width = float(bbox[2]) - float(bbox[0])
        height = float(bbox[3]) - float(bbox[1])

    safe_name = name.strip("/").replace("/", "_")
    stem = f"{index:03d}_p{page_num:03d}_{safe_name}"
    out_pdf = PDF_DIR / f"{stem}.pdf"

    writer = PdfWriter()
    new_page = PageObject.create_blank_page(width=width, height=height)
    new_page[NameObject("/Resources")] = DictionaryObject(
        {NameObject("/XObject"): DictionaryObject({NameObject("/X0"): ref})}
    )
    stream = DecodedStreamObject()
    stream.set_data(b"q\n/X0 Do\nQ\n")
    new_page[NameObject("/Contents")] = stream
    writer.add_page(new_page)
    with out_pdf.open("wb") as f:
        writer.write(f)

    data = obj.get_data()
    string_count, sample = _literal_string_sample(data)
    return {
        "index": index,
        "page": page_num,
        "xobject": name,
        "pdf": str(out_pdf.relative_to(ROOT)),
        "png": str((PNG_DIR / f"{stem}-1.png").relative_to(ROOT)),
        "bbox_width": round(width, 3),
        "bbox_height": round(height, 3),
        "stream_bytes": len(data),
        "literal_string_count": string_count,
        "text_sample": sample,
    }


def _render_pngs() -> None:
    pdftoppm = shutil.which("pdftoppm")
    if not pdftoppm:
        print("pdftoppm not found; PDFs were extracted but PNGs were not rendered.", file=sys.stderr)
        return

    for pdf in sorted(PDF_DIR.glob("*.pdf")):
        out_prefix = PNG_DIR / pdf.stem
        subprocess.run(
            [pdftoppm, "-r", "300", "-png", str(pdf), str(out_prefix)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def main() -> int:
    if not SOURCE_PDF.exists():
        print(f"Missing source PDF: {SOURCE_PDF}", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    PDF_DIR.mkdir(parents=True, exist_ok=True)
    PNG_DIR.mkdir(parents=True, exist_ok=True)

    reader = PdfReader(str(SOURCE_PDF))
    seen: set[tuple[int | None, int | None]] = set()
    rows: list[dict[str, object]] = []

    for page_num, page in enumerate(reader.pages, start=1):
        resources = page.get("/Resources") or {}
        xobjects = resources.get("/XObject") or {}
        for name_obj, ref in xobjects.items():
            name = str(name_obj)
            try:
                obj = ref.get_object()
            except Exception:
                continue
            if isinstance(obj, NullObject) or not hasattr(obj, "get"):
                continue
            if obj.get("/Subtype") != "/Form":
                continue
            try:
                data = obj.get_data()
            except Exception:
                continue
            if _looks_like_watermark(name, data):
                continue

            key = (getattr(ref, "idnum", None), getattr(ref, "generation", None))
            if key in seen:
                continue
            seen.add(key)
            rows.append(_extract_one(reader, page_num, name, ref, obj, len(rows) + 1))

    _render_pngs()

    csv_path = OUT_DIR / "manifest.csv"
    json_path = OUT_DIR / "manifest.json"
    fieldnames = [
        "index",
        "page",
        "xobject",
        "pdf",
        "png",
        "bbox_width",
        "bbox_height",
        "stream_bytes",
        "literal_string_count",
        "text_sample",
    ]
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    json_path.write_text(json.dumps(rows, indent=2), encoding="utf-8")

    print(f"Extracted {len(rows)} non-watermark Form XObjects")
    print(f"Manifest: {csv_path}")
    print(f"PDFs:     {PDF_DIR}")
    print(f"PNGs:     {PNG_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
