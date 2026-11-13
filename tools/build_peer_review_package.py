#!/usr/bin/env python3
r"""Lay out a KOR peer-review package the way a reviewing engineer reads it: two PDFs, the 3D viewer,
the models and the results. No markdown ships.

WHY THIS EXISTS
    The 01819-01 package went out as 225 files, the reading matter in ten .md files spread over five
    folders. Ian, 12 Nov 2026: "it's full of md files and it's not intuitive". The content was right;
    the shape was not. This script takes the frozen package as its only input, never edits it, and
    writes:

        <name>/01 Review letter and findings.pdf   letter, intro, ranked register with its pictures,
                                                    load path, transfer results
        <name>/02 3D model viewer.html              the viewer, renamed, unchanged
        <name>/03 Models/ETABS, 03 Models/SAFE      .e2k; .f2k with their assumptions and provenance
        <name>/04 Results/ETABS, 04 Results/SAFE    the result .csv files
        <name>/05 Appendices.pdf                    lettered appendices A-H
        <name>/05 Appendix A - answer key.csv       the 2,355-row key, a file an engineer filters

ROUTE (prior art, not new machinery)
    markdown -> HTML fragment: pandoc (gfm), the converter tools/Format-BdDocx.ps1 already uses.
    HTML -> PDF: tools/Format-BdWebPdf.ps1 (headless Edge, fresh file name per render, waits for the
    output to refresh). Styling: the tokens of tools/BdDocTemplate (slate #3F5364, accent, Segoe UI,
    mono kickers, editorial tables) and the Kor Structural logo and office line from
    docs/email-signatures. Two renders per PDF: the first finds the page of every heading from the
    contents links; the second writes those page numbers into the contents. Bookmarks and metadata
    are then set with PyMuPDF.

WHAT IT CHECKS (on the PDFs it wrote, read back as text) - and what it does not
    checks:  every numeric token of every source text (after the file-name remap) occurs in the PDF at
             least as often as in its sources; every register picture is embedded in PDF 01 (count of
             image placements); the contents page numbers equal the pages the links land on; the
             register entry's heading line equals the register CSV's severity/title/storey/grid/sheets;
             no ".md", no "safe/", "etabs/" or "README" left in the PDFs; the copied files hash equal
             to their sources.
    does NOT check: that a sentence reads the same (only numbers are compared, and only as a
             multiset - a number moved to the wrong row would pass); that a table kept its columns on
             the page (LOOK at the pages for that: wide tables clip silently in Edge); the viewer's own
             pin texts, which still name the old file names.
    same-class fault it would miss: a remap that turns one file reference into the wrong appendix
             letter keeps every number and passes.

USAGE
    python -I build_peer_review_package.py --src <frozen package dir> --out <new package dir>
"""
from __future__ import annotations

import argparse
import collections
import csv
import hashlib
import html
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

import fitz  # PyMuPDF
from bs4 import BeautifulSoup, NavigableString

OPS = Path(__file__).resolve().parents[1]
WEBPDF = OPS / "tools" / "Format-BdWebPdf.ps1"
LOGO = OPS / "docs" / "email-signatures" / "korlogo.png"

PROJECT = "Easthill, 2480 & 2470 Renfrew St"
JOB = "01819-01"
RUNNING_HEAD = f"{PROJECT} ({JOB}) \u00b7 Structural peer review"


APPX = {
    "A": "Answer key: every value in WHM's set, as drawn",
    "B": "Early comment list",
    "C": "Tower columns TC1-TC26: gravity hand check (preliminary)",
    "D": "The design at a glance",
    "E": "Model basis, results and assumptions",
    "F": "SAFE storey notes",
    "G": "P1/GF: the 11 restrained points with no reaction",
    "H": "SAFE plans: the loads not applied at P2 and P3",
}

# ---------------------------------------------------------------- file-name remap (old -> new)
# Applied to every source text before conversion. Ordered: the first match wins for overlapping text.
REMAP_EXACT = [
    # sentences that only made sense in the folder layout
    ("; it added F26 and F27 (`COORDINATION-SWEEP.md`, its own page).", "; it added F26 and F27."),
    (" The same rows are in `FINDINGS-REGISTER.csv`.", ""),
    (" Click a picture to open it full size.", " Each picture is embedded at full resolution: zoom in to read it."),
    ("the checks made are listed in the README.", "the checks made are listed in Section 2 of OF_LETTER."),
    ("(the full table, with element counts, is `design-at-a-glance.csv` beside this page)",
     "(the full table, with element counts, is \"Per storey, as computed\" at the end of this appendix)"),
    ("Pictures: pictures/beyond-300mm/<storey>-overview.png and <storey>-<member>.png.", "Pictures: Appendix H."),
    ("ANSWER-KEY.md MISSING (", "Appendix A, missing values ("),
    ("(README, 'Slab uses')", "(Appendix E, 'Slab uses')"),
    ("and the README's\nslab-use table below", "and the\nslab-use table below"),
    ("The SAFE files and this README give", "The SAFE files and this appendix give"),
    ("README (base reactions)", "Appendix E (base reactions)"),
    ('see README "Materials"', 'see Appendix E "Materials"'),
    ('against the one rule under "What was checked" above.', 'against the one rule under "What was checked, and how" (Section 2 of OF_LETTER).'),
    ("Data: `gravity-tower-columns.csv` (this folder), one row per column per level.",
     "Data: the table \"Every column-level\" at the end of this appendix, one row per column per level."),
    ("the package's `safe/` folder (", "the package's SAFE models and results ("),
    ("from the model in `etabs/`.", "from the model in `03 Models/ETABS/`."),
    ("the model file in `etabs/`", "the model file in `03 Models/ETABS/`"),
    ("`P2-overview.png`, `P2-KW61.png`", "Appendix H, Figures H1 and H2"),
    ("`P3-overview.png`, `P3-KW6.png`", "Appendix H, Figures H3 and H4"),
    ("Pictures in `safe/pictures/beyond-300mm/` show", "Pictures in Appendix H show"),
    ("pictures in `safe/pictures/beyond-300mm/`", "pictures in Appendix H"),
    ("(each listed in safe-loads.csv with why)", "(each listed in Appendix F5 with why)"),
    ("The 14 transfer storeys are the storeys of safe-transfers.csv:", "The 14 transfer storeys are the storeys of Appendix F3:"),
    ("COORDINATION-SWEEP.md (architect against structure on 37 of 38 storeys)",
     "our coordination sweep (architect against structure on 37 of 38 storeys)"),
]
REMAP_RE = [
    (r"`?(?:\.\./)?etabs/01819-01 Renfrew - ETABS model\.e2k`?", "`03 Models/ETABS/01819-01 Renfrew - ETABS model.e2k`"),
    (r"`?(?:\.\./)?etabs/ETABS-loads-onto-transfer-storeys\.csv`?", "`04 Results/ETABS/ETABS-loads-onto-transfer-storeys.csv`"),
    (r"`?(?:\.\./)?etabs/ETABS-base-reactions\.csv`?", "`04 Results/ETABS/ETABS-base-reactions.csv`"),
    (r"`?safe/SAFE-RESULTS\.txt`?", "Appendix F1"),
    (r"SAFE-RESULTS\.txt", "Appendix F1"),
    (r"`?safe/safe-set\.txt`?", "Appendix F2"),
    (r"`?safe/safe-transfers\.csv`?", "Appendix F3"),
    (r"`?safe/safe-levels\.csv`?", "Appendix F4"),
    (r"`?safe/safe-loads\.csv`?", "Appendix F5"),
    (r"`?(?:safe/)?P1_GF-unattached-supports\.md`?", "Appendix G"),
    (r"safe/models/", "03 Models/SAFE/"),
    (r"(?<![\w/.])models/", "03 Models/SAFE/"),
    (r"safe/results/", "04 Results/SAFE/"),
    (r"(?<![\w/.])results/", "04 Results/SAFE/"),
    (r"`?GRAVITY-CHECK\.md`?", "Appendix C"),
    (r"`?gravity-tower-columns\.csv`?", "Appendix C, table \"Every column-level\""),
    (r"`?ANSWER-KEY\.md`?", "Appendix A"),
    (r"`answer-key\.csv`", "`05 Appendix A - answer key.csv`"),
    (r"`?EARLY-COMMENTS\.md`?(?= [AB]\d)", "Appendix B,"),
    (r"`?EARLY-COMMENTS\.md`?", "Appendix B"),
    (r"`?DESIGN-AT-A-GLANCE\.md`?", "Appendix D"),
    (r"`?TRANSFER-RESULTS\.md`?", "Section 5 of OF_LETTER"),
    (r"`?LOAD-PATH-SUMMARY\.md`?", "Section 4 of OF_LETTER"),
    (r"`?FINDINGS-REGISTER\.(?:csv|md)`?", "the findings register (Section 3 of OF_LETTER)"),
    (r"`01819-01 Renfrew - 3D model viewer\.html`", "`02 3D model viewer.html`"),
]


def remap(text: str, doc: str = "01") -> str:
    """doc '01': a section reference points inside the same PDF; '05': it names PDF 01."""
    for old, new in REMAP_EXACT:
        text = text.replace(old, new)
    for pat, new in REMAP_RE:
        text = re.sub(pat, new, text)
    return text.replace(" of OF_LETTER", "" if doc == "01" else " of 01 Review letter and findings")


# ---------------------------------------------------------------- helpers
def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def read(p: Path) -> str:
    return p.read_text(encoding="utf-8")


def pandoc(md: str, prefix: str) -> str:
    r = subprocess.run(["pandoc", "-f", "gfm", "-t", "html5", "--wrap=none", f"--id-prefix={prefix}-"],
                       input=md.encode("utf-8"), capture_output=True, check=True)
    return r.stdout.decode("utf-8")


def strip_h1(md: str) -> str:
    """Drop the source's own H1 line(s): the section title is ours."""
    return "\n".join(l for l in md.splitlines() if not re.match(r"^# ", l))


def md_sections(md: str) -> dict[str, str]:
    """Split a markdown file at its H2 headings: {heading text: body including the heading}."""
    out, cur, buf = {}, "_head", []
    for line in md.splitlines():
        m = re.match(r"^## (.+)$", line)
        if m:
            out[cur] = "\n".join(buf)
            cur, buf = m.group(1).strip(), [line]
        else:
            buf.append(line)
    out[cur] = "\n".join(buf)
    return out


def is_num(s: str) -> bool:
    return bool(re.fullmatch(r"[-+]?[\d,]*\.?\d+(e[-+]?\d+)?", s.strip(), re.I))


UNITS = [("_kNm", "kNm"), ("_kN", "kN"), ("_mm2", "mm\u00b2"), ("_m2", "m\u00b2"), ("_ft2", "ft\u00b2"),
         ("_m3", "m\u00b3"), ("_mm", "mm"), ("_MPa", "MPa"), ("_ft", "ft"), ("_in", "in"), ("_m", "m"),
         ("_pct_floor", "% of floor")]


def label(col: str) -> str:
    """A data column name as a table heading: words, with the unit in brackets."""
    for suf, unit in UNITS:
        if col.endswith(suf):
            return f"{col[:-len(suf)].replace('_', ' ')} ({unit})"
    return col.replace("_", " ")


def csv_table(path: Path, cols: list[str] | None = None, rename: dict | None = None,
              extra: dict | None = None, cls: str = "data") -> str:
    rows = list(csv.DictReader(open(path, encoding="utf-8", newline="")))
    cols = cols or [c for c in rows[0].keys() if any(r[c].strip() for r in rows)]  # an all-empty column says nothing
    numeric = {c: all(is_num(r[c]) or r[c] == "" for r in rows) and any(r[c] for r in rows) for c in cols}
    h = [f'<table class="{cls}"><thead><tr>']
    for c in cols:
        lab = (rename or {}).get(c, label(c))
        h.append(f'<th class="{"num" if numeric[c] else ""}">{html.escape(lab)}</th>')
    h.append("</tr></thead><tbody>")
    for r in rows:
        h.append("<tr>")
        for c in cols:
            v = (extra or {}).get(c, lambda row: row[c])(r)
            over = c.startswith("utilisation") and is_num(v) and float(v.replace(",", "")) > 1.0
            k = ("num" if numeric[c] else "txt") + (" over" if over else "") + (" nw" if len(v) <= 34 else "")
            h.append(f'<td class="{k}">{html.escape(v)}</td>')
        h.append("</tr>")
    h.append("</tbody></table>")
    return "".join(h)


def csv_cells(path: Path) -> str:
    """The data cells of a CSV, one per line: the text whose numbers its table must carry."""
    rows = list(csv.reader(open(path, encoding="utf-8", newline="")))[1:]
    return "\n".join(c for r in rows for c in r)


def pre_block(text: str) -> str:
    return f'<pre class="plain">{html.escape(text)}</pre>'


# ---------------------------------------------------------------- HTML post-processing
SEV_CLASS = {"CRITICAL": "crit", "SIGNIFICANT": "sig", "MINOR": "minor"}


def pill(sev: str) -> str:
    return f'<span class="pill pill--{SEV_CLASS[sev]}">{sev.title()}</span>'


def widen_tables(soup: BeautifulSoup, min_cols: int = 8) -> None:
    """Tables of min_cols or more columns go on a landscape page (Edge clips wide tables silently)."""
    for t in soup.find_all("table"):
        if t.find_parent(class_="wide"):
            continue
        first = t.find("tr")
        n = len(first.find_all(["th", "td"])) if first else 0
        if n >= min_cols:
            wrap = soup.new_tag("div", attrs={"class": "wide"})
            lead = []
            prev = t.find_previous_sibling()
            while prev is not None and prev.name in ("p", "h2", "h3", "h4") and len(lead) < 3:
                lead.insert(0, prev)
                if prev.name.startswith("h"):
                    break
                prev = prev.find_previous_sibling()
            if not (lead and lead[0].name.startswith("h")):
                lead = []  # only a heading's own lead-in travels with the table
            t.wrap(wrap)
            for el in reversed(lead):
                wrap.insert(0, el.extract())


def picture_tables_to_figures(soup: BeautifulSoup, pic_dir: Path, used: list) -> None:
    """A markdown table of pictures (captions in the header row, images in the body) becomes figures."""
    for t in soup.find_all("table"):
        imgs = t.find_all("img")
        if not imgs:
            continue
        caps = [th.get_text(" ", strip=True) for th in t.find_all("th")]
        cells = [td for td in t.find_all("td")]
        grid = soup.new_tag("div", attrs={"class": f"figs n{len(cells)}"})
        for cap, td in zip(caps, cells):
            img = td.find("img")
            name = Path(img["src"]).name
            src = pic_dir / name
            if not src.exists():
                raise SystemExit(f"picture missing: {src}")
            used.append(name)
            fig = soup.new_tag("figure")
            im = soup.new_tag("img", attrs={"src": src.as_uri(), "alt": cap})
            fig.append(im)
            fc = soup.new_tag("figcaption")
            b = soup.new_tag("b")
            b.string = "Figure " + Path(name).stem.split("-")[0] + "-" + Path(name).stem.split("-")[1] + ". "
            fc.append(b)
            fc.append(NavigableString(cap))
            fig.append(fc)
            grid.append(fig)
        t.replace_with(grid)


class Doc:
    """Collect body HTML with numbered headings; produce contents entries."""

    def __init__(self, prefix: str):
        self.parts: list[str] = []
        self.toc: list[tuple[int, str, str]] = []  # (level, id, text)
        self.prefix = prefix

    def section(self, num: str, title: str, body_html: str, anchor: str, number_h2: bool = True,
                kicker: str = "", first: bool = False) -> None:
        soup = BeautifulSoup(body_html, "html.parser")
        sub = 0
        for h in soup.find_all(["h2"]):
            if "finding" in (h.get("class") or []):
                self.toc.append((3, h["id"], h.get("data-toc")))
                continue
            if number_h2:
                sub += 1
                label = f"{num}.{sub}"
                txt = re.sub(r"^\d+\.\s+", "", h.get_text(" ", strip=True))
                h.clear()
                s = soup.new_tag("span", attrs={"class": "hnum"})
                s.string = label
                h.append(s)
                h.append(NavigableString(" " + txt))
                self.toc.append((2, h["id"], f"{label}\u2003{txt}"))
            else:
                self.toc.append((2, h["id"], h.get_text(" ", strip=True)))
        # h3 stay unnumbered; h4+ are rare
        self.toc.insert(len(self.toc) - sum(1 for _ in soup.find_all("h2")),
                        (1, anchor, f"{num}\u2003{title}"))
        cls = "sec first" if first else "sec"
        k = f'<p class="kicker">{html.escape(kicker)}</p>' if kicker else ""
        self.parts.append(f'<section class="{cls}" id="{anchor}">{k}<h1><span class="hnum">{num}</span> '
                          f'{html.escape(title)}</h1>{soup}</section>')


def toc_html(toc, pages: dict | None) -> str:
    out = ['<nav class="toc"><p class="toc__title">Contents</p><ol>']
    for lvl, ident, text in toc:
        pg = str(pages.get(ident, "")) if pages else "000"
        out.append(f'<li class="l{lvl}"><a href="#{ident}"><span class="t">{html.escape(text)}</span>'
                   f'<span class="dots"></span><span class="pg">{pg}</span></a></li>')
    out.append("</ol></nav>")
    return "".join(out)


# ---------------------------------------------------------------- page shell
def css() -> str:
    return r"""
:root{--ink:#1C2530;--ink-soft:#55636F;--ink-faint:#8A97A2;--line:#E5E4E1;--line-strong:#D3D2CE;
--surface-2:#F4F2F0;--slate:#3F5364;--slate-deep:#2C3A47;--accent:#F15D3A;--accent-ink:#C6360F;
--crit:#C0392B;--crit-bg:#FBE9E7;--warn:#A9741A;--warn-bg:#FBF1DE;--blue:#3E7CB1;--blue-bg:#E8F0F7;
--sans:"Segoe UI",system-ui,Arial,sans-serif;--mono:"Cascadia Code",Consolas,monospace}
@page{size:Letter;margin:20mm 17mm 17mm;
 @top-left{content:"RUNHEAD";font:7.5pt var(--sans);color:#55636F;vertical-align:bottom;padding-bottom:3mm}
 @top-right{content:"Kor Structural";font:600 7.5pt var(--sans);color:#C6360F;vertical-align:bottom;padding-bottom:3mm}
 @bottom-left{content:"Confidential: another firm's design";font:7pt var(--sans);color:#8A97A2;vertical-align:top;padding-top:3mm}
 @bottom-right{content:"DOCNAME \2003 Page " counter(page) " of " counter(pages);font:7.5pt var(--sans);color:#55636F;vertical-align:top;padding-top:3mm}}
@page:first{@top-left{content:none}@top-right{content:none}}
@page wide{size:Letter landscape;margin:18mm 15mm 15mm}
*{box-sizing:border-box}
html,body{background:#fff}
body{margin:0;color:var(--ink);font-family:var(--sans);font-size:9.4pt;line-height:1.45;
 -webkit-print-color-adjust:exact;print-color-adjust:exact}
.cover{min-height:228mm;display:flex;flex-direction:column}
.cover .logo{width:62mm;margin:2mm 0 16mm}
.cover .band{background:linear-gradient(180deg,var(--slate-deep),var(--slate));color:#EAF0F5;
 border-bottom:3px solid var(--accent);padding:10mm 9mm 9mm;border-radius:2px}
.cover .eyebrow{font-family:var(--mono);font-size:7.6pt;letter-spacing:.2em;text-transform:uppercase;color:#FFB59F;margin:0 0 4mm}
.cover h1{font-size:23pt;line-height:1.12;margin:0 0 3mm;color:#fff;font-weight:700;letter-spacing:-.01em}
.cover .lede{font-size:11pt;color:#CBD8E2;margin:0}
.cover .band strong{color:#fff}
.cover table.facts{margin:9mm 0 0;border-collapse:collapse;width:100%;font-size:9.4pt}
.cover table.facts th{width:34mm;text-align:left;font:600 7pt var(--mono);letter-spacing:.12em;text-transform:uppercase;
 color:var(--ink-faint);padding:2.2mm 0;border-bottom:1px solid var(--line);vertical-align:top;background:none}
.cover table.facts td{padding:2.2mm 0;border-bottom:1px solid var(--line);background:none}
.cover .spacer{flex:1}
.cover .office{font-size:7.6pt;color:var(--ink-soft);border-top:2px solid var(--accent);padding-top:3mm;margin-top:8mm}
.cover .office b{color:#373435}
.pkg{margin-top:8mm}
.pkg table{width:100%}
.toc{break-before:page}
.toc__title,.kicker{font-family:var(--mono);font-size:7.4pt;letter-spacing:.18em;text-transform:uppercase;color:var(--accent-ink);margin:0 0 3mm;font-weight:600}
.toc ol{list-style:none;margin:0;padding:0}
.toc li a{display:flex;align-items:baseline;gap:2mm;color:var(--ink);text-decoration:none;padding:.9mm 0}
.toc li .dots{flex:1;border-bottom:1px dotted var(--line-strong);transform:translateY(-1mm)}
.toc li .pg{font-variant-numeric:tabular-nums;min-width:7mm;text-align:right;color:var(--ink-soft)}
.toc li.l1{margin-top:2.6mm;font-weight:650;font-size:10pt}
.toc li.l2{padding-left:8mm;font-size:8.8pt}
.toc li.l3{padding-left:8mm;font-size:8.2pt;color:var(--ink-soft)}
.toc li.l3 a{padding:.35mm 0}
section.sec{break-before:page}
h1{font-size:17pt;line-height:1.15;margin:0 0 5mm;color:var(--slate-deep);font-weight:700;letter-spacing:-.01em;
 padding-bottom:2.5mm;border-bottom:2px solid var(--accent);break-after:avoid}
h2{font-size:12pt;margin:7mm 0 2.5mm;color:var(--slate-deep);font-weight:650;break-after:avoid}
h3{font-size:10pt;margin:5mm 0 2mm;color:var(--slate);font-weight:650;break-after:avoid}
h4{font-size:9.4pt;margin:4mm 0 1.5mm;break-after:avoid}
.hnum{color:var(--accent-ink);font-variant-numeric:tabular-nums;margin-right:1.5mm}
p{margin:0 0 2.4mm}
ul,ol{margin:0 0 2.6mm;padding-left:5.5mm}
li{margin:0 0 1.2mm}
li::marker{color:var(--accent)}
strong{font-weight:650}
code{font-family:var(--mono);font-size:.86em;color:#2C3A47;background:#F4F2F0;padding:0 .25em;border-radius:2px}
a{color:var(--accent-ink);text-decoration:none}
hr{border:0;border-top:1px solid var(--line);margin:4mm 0}
table{border-collapse:collapse;width:100%;margin:2mm 0 4mm;font-size:8pt;line-height:1.32}
thead{display:table-header-group}
tr,li,figure{break-inside:avoid}
th{background:var(--slate);color:#fff;text-align:left;font-weight:600;font-size:7.4pt;padding:1.4mm 1.6mm;vertical-align:bottom}
td{padding:1.2mm 1.6mm;border-bottom:1px solid var(--line);vertical-align:top}
tbody tr:nth-child(even) td{background:#F8F7F5}
td.num,th.num,td[style*="right"],th[style*="right"]{text-align:right;font-variant-numeric:tabular-nums}
.wide{page:wide}
.wide table{font-size:7.2pt}
.wide th{font-size:6.8pt}
table.data{font-size:7pt}
table.data th{font-size:6.6pt}
table.data td{padding:.7mm 1.2mm}
.wide table.data{font-size:6.6pt}
pre.plain{font-family:var(--mono);font-size:6.7pt;line-height:1.36;white-space:pre-wrap;word-break:break-word;
 background:#F8F7F5;border-left:2px solid var(--line-strong);padding:2.5mm 3mm;margin:0 0 4mm}
.pill{display:inline-block;font:700 6.8pt var(--sans);letter-spacing:.06em;text-transform:uppercase;padding:.5mm 2mm;border-radius:8px;color:#fff;vertical-align:1px}
.pill--crit{background:var(--crit)}.pill--sig{background:#C98A16}.pill--minor{background:var(--blue)}
h2.finding{font-size:11.5pt;margin:0 0 2mm;padding-top:1mm;break-before:auto}
h2.finding .fid{font-family:var(--mono);color:var(--accent-ink);margin-right:2mm}
h2.finding .pill{margin-right:2mm}
.entry{border-top:2px solid var(--slate);padding-top:3mm;margin-top:7mm}
.entry.first{margin-top:4mm}
table.meta{font-size:8pt;margin:0 0 3mm;break-inside:avoid;break-after:avoid}
.entry>h2{break-after:avoid}
td.over{color:var(--crit);font-weight:700}
td.nw{white-space:nowrap}
table.data td.txt{padding-left:2.4mm}
table.meta th{background:#EEF1F4;color:var(--slate-deep);width:24mm;font-size:7pt;letter-spacing:.06em;text-transform:uppercase;vertical-align:top;padding:1.2mm 1.6mm}
table.meta td{background:#fff !important}
table.register td:first-child a{font-family:var(--mono);font-weight:600}
.figs{display:flex;flex-wrap:wrap;gap:4mm;margin:3mm 0 4mm}
.figs figure{margin:0;flex:1 1 0;min-width:0;break-inside:avoid}
.figs.n1 figure{flex:0 1 82%}
.figs img{width:100%;max-height:92mm;object-fit:contain;border:1px solid var(--line-strong);background:#fff;display:block}
.figs figcaption,figure.single figcaption{font-size:7.6pt;color:var(--ink-soft);margin-top:1.2mm;line-height:1.3}
.figs figcaption b,figure.single figcaption b{color:var(--ink)}
figure.single{margin:3mm 0 5mm;break-inside:avoid}
figure.single img{width:100%;max-height:200mm;object-fit:contain;border:1px solid var(--line-strong)}
.box{border:1px solid var(--line-strong);border-left:3px solid var(--accent);background:#FBFAF9;padding:2.5mm 3.5mm;margin:0 0 4mm;break-inside:avoid}
.box p:last-child{margin:0}
.box--warn{border-left-color:var(--warn);background:var(--warn-bg)}
"""


def page(title: str, docname: str, body: str) -> str:
    c = css().replace("RUNHEAD", RUNNING_HEAD.replace('"', '\\"')).replace("DOCNAME", docname)
    return (f'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>{html.escape(title)}</title>'
            f"<style>{c}</style></head><body>{body}</body></html>")


def cover(title: str, eyebrow: str, lede: str, facts: list[tuple[str, str]], extra: str = "") -> str:
    rows = "".join(f"<tr><th>{html.escape(k)}</th><td>{v}</td></tr>" for k, v in facts)
    return (f'<div class="cover"><img class="logo" src="{LOGO.as_uri()}" alt="Kor Structural">'
            f'<div class="band"><p class="eyebrow">{html.escape(eyebrow)}</p><h1>{html.escape(title)}</h1>'
            f'<p class="lede">{lede}</p></div><table class="facts">{rows}</table>{extra}<div class="spacer"></div>'
            f'<div class="office"><b>Kor Structural</b> \u00b7 Suite 501 \u2013 510 Burrard Street, Vancouver, BC V6C 3A8 '
            f'\u00b7 604.685.9533 \u00b7 korstructural.com</div></div>')


# ---------------------------------------------------------------- render + verify
def render(html_text: str, build: Path, stem: str, out_pdf: Path) -> None:
    hp = build / f"{stem}.html"
    hp.write_text(html_text, encoding="utf-8")
    if out_pdf.exists():
        out_pdf.unlink()  # Edge can leave an old PDF in place silently
    r = subprocess.run(["pwsh", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(WEBPDF),
                        "-Html", str(hp), "-Pdf", str(out_pdf)], capture_output=True, text=True)
    if r.returncode != 0 or not out_pdf.exists():
        raise SystemExit(f"render failed for {stem}: {r.stdout}\n{r.stderr}")
    prod = fitz.open(out_pdf).metadata.get("producer", "")
    if not prod.startswith("Skia/PDF"):
        raise SystemExit(f"{out_pdf.name}: producer {prod!r}, not Edge")


def link_pages(pdf: Path, toc_ids: list[str]) -> dict[str, int]:
    """Page (1-based) each contents link lands on, read off the PDF's link annotations."""
    d = fitz.open(pdf)
    found = {}
    for p in d:
        for ln in p.get_links():
            nd = ln.get("nameddest") or ""
            if nd in toc_ids and nd not in found and ln.get("page", -1) >= 0:
                found[nd] = ln["page"] + 1
    return found


def build_pdf(title: str, docname: str, cover_html: str, doc: Doc, build: Path, stem: str, out_pdf: Path,
              meta_subject: str) -> dict:
    ids = [i for _, i, _ in doc.toc]
    body = lambda pages: cover_html + toc_html(doc.toc, pages) + "".join(doc.parts)
    render(page(title, docname, body(None)), build, stem + "-pass1", out_pdf)
    pages = link_pages(out_pdf, ids)
    missing = [i for i in ids if i not in pages]
    if missing:
        raise SystemExit(f"{docname}: contents links with no destination: {missing[:5]}")
    n1 = fitz.open(out_pdf).page_count
    render(page(title, docname, body(pages)), build, stem + "-pass2", out_pdf)
    pages2 = link_pages(out_pdf, ids)
    if pages2 != pages or fitz.open(out_pdf).page_count != n1:
        raise SystemExit(f"{docname}: pagination moved between passes")
    d = fitz.open(out_pdf)
    marks, last = [], 0
    for lvl, i, text in doc.toc:
        lvl = 2 if lvl == 3 else lvl  # a finding sits directly under its section (no 3.x headings there)
        lvl = min(lvl, last + 1)
        marks.append([lvl, re.sub(r"\s+", " ", text), pages[i]])
        last = lvl
    d.set_toc(marks)
    d.set_metadata({"title": title, "author": "Kor Structural", "subject": meta_subject,
                    "creator": "Kor Structural", "producer": d.metadata.get("producer", "")})
    tmp = out_pdf.with_suffix(".tmp.pdf")
    d.save(tmp, garbage=3, deflate=True)
    d.close()
    tmp.replace(out_pdf)
    return pages


NUM_RE = re.compile(r"(?<![\w.])[-+]?\d(?:[\d,]*\d)?(?:\.\d+)?")


def numbers(text: str) -> collections.Counter:
    return collections.Counter(m.group(0).lstrip("+") for m in NUM_RE.finditer(text))


def pdf_text(pdf: Path) -> str:
    return "\n".join(p.get_text() for p in fitz.open(pdf))


def check_numbers(name: str, sources: list[str], pdf: Path) -> list[str]:
    want = collections.Counter()
    for s in sources:
        # markdown/html markup carries no numbers of its own except picture widths and anchors
        s = re.sub(r'<img[^>]*>|<a href="[^"]*">|\]\(#[^)]*\)|\]\([^)]*\.png\)', " ", s)
        want += numbers(s)
    have = numbers(pdf_text(pdf))
    short = [f"{k} (source {v}, pdf {have.get(k, 0)})" for k, v in want.items() if have.get(k, 0) < v]
    print(f"NUMBERS {name}: {sum(want.values())} numeric tokens in the sources, {len(want)} distinct; "
          f"short in the PDF: {len(short)}")
    return short


# ---------------------------------------------------------------- the build
def main() -> int:
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--src", type=Path, help="an assembled package (the shape RUN-ALL.sh writes)")
    g.add_argument("--assemble", type=Path, help="an INPUTS.env: run the job's RUN-ALL.sh on it first, then lay out "
                                                 "the package it writes (its PKG)")
    ap.add_argument("--runall", type=Path, help="the assembly runner (default: RUN-ALL.sh beside the INPUTS.env)")
    ap.add_argument("--out", required=True, type=Path, help="the laid-out package folder")
    ap.add_argument("--replace", action="store_true", help="delete --out first if it exists (it is a work product)")
    ap.add_argument("--build", type=Path, help="working folder for the HTML (default: <out>/../_build)")
    a = ap.parse_args()
    if a.assemble:
        env = a.assemble.resolve()
        runall = (a.runall or env.parent / "RUN-ALL.sh").resolve()
        print(f"ASSEMBLE: bash {runall} {env}", flush=True)
        if subprocess.run(["bash", str(runall), str(env)]).returncode != 0:
            raise SystemExit("assembly refused: the layout is not built")
        pkg = subprocess.run(["bash", "-c", 'source "$1"; echo "$PKG"', "_", str(env)],
                             capture_output=True, text=True, check=True).stdout.strip()
        src = Path(pkg).resolve()
    else:
        src = a.src.resolve()
    out = a.out.resolve()
    build = a.build or out.parent / "_build"
    if out.exists():
        if not a.replace:
            raise SystemExit(f"{out} exists: pass --replace to rebuild it")
        shutil.rmtree(out)
    print(f"SOURCE {src}", flush=True)
    build.mkdir(parents=True, exist_ok=True)
    out.mkdir(parents=True)

    # ---------- files that ship as files
    # the transfer storeys are the storeys SAFE results were shipped for
    TRANSFER_STOREYS = sorted(p.name for p in (src / "safe/results").iterdir() if p.is_dir())
    copies: list[tuple[Path, Path]] = []
    viewer = src / "01819-01 Renfrew - 3D model viewer.html"
    copies.append((viewer, out / "02 3D model viewer.html"))
    copies.append((src / "etabs/01819-01 Renfrew - ETABS model.e2k", out / "03 Models/ETABS/01819-01 Renfrew - ETABS model.e2k"))
    for f in sorted((src / "safe/models").glob("*.f2k")):
        st = f.stem
        dest = out / "03 Models/SAFE" if st in TRANSFER_STOREYS else out / "03 Models/SAFE/Storeys with no transfer"
        for ext in (".f2k", ".assumptions.txt", ".provenance.csv"):
            copies.append((src / "safe/models" / f"{st}{ext}", dest / f"{st}{ext}"))
    for f in ("ETABS-base-reactions.csv", "ETABS-loads-onto-transfer-storeys.csv"):
        copies.append((src / "etabs" / f, out / "04 Results/ETABS" / f))
    for f in sorted((src / "safe/results").rglob("*")):
        if f.is_file():
            copies.append((f, out / "04 Results/SAFE" / f.relative_to(src / "safe/results")))
    copies.append((src / "answer-key.csv", out / "05 Appendix A - answer key.csv"))
    for s, d in copies:
        d.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(s, d)
        if sha(s) != sha(d):
            raise SystemExit(f"copy differs: {d}")
    print(f"COPIED {len(copies)} files, each SHA-256 equal to its source")
    n_tr = len(TRANSFER_STOREYS)
    n_other = len(list((src / "safe/models").glob("*.f2k"))) - n_tr
    for st in TRANSFER_STOREYS:
        if not (src / "safe/models" / f"{st}.f2k").exists():
            raise SystemExit(f"SAFE results for {st} but no model {st}.f2k")
    n_key = len(list(csv.reader(open(src / "answer-key.csv", encoding="utf-8", newline="")))) - 1

    # ---------- viewer pins and the register CSV
    vtxt = read(viewer)
    pins = {m.group(1): m.group(2) for m in re.finditer(r'\{"id": "(F\d\d)", "storey": "([^"]+)"', vtxt)}
    reg = {r["id"]: r for r in csv.DictReader(open(src / "review/FINDINGS-REGISTER.csv", encoding="utf-8"))}
    for fid, r in reg.items():
        if pins.get(fid) != r["pin_storey"]:
            raise SystemExit(f"{fid}: viewer pin storey {pins.get(fid)} != register pin_storey {r['pin_storey']}")
    print(f"PINS: {len(reg)} of {len(reg)} register rows have a viewer pin on the register's storey")

    # ===================================================================== PDF 01
    sources01: list[str] = []
    letter_md = read(src / "review/REVIEW-LETTER.md")
    meta_line = letter_md.splitlines()[2]
    mm = re.fullmatch(r"\*\*For:\*\* (.+?) \| \*\*From:\*\* KOR Structural \| \*\*Date:\*\* (\d{4}-\d\d-\d\d) \| "
                      r"\*\*Status:\*\* (.+?) \| Confidential: another firm's design", meta_line)
    if not mm:
        raise SystemExit(f"letter meta line not in the expected form: {meta_line}")
    for_who, letter_date, status = mm.groups()
    letter = md_sections(letter_md)
    enclosed_old = letter["Enclosed"]
    enclosed_new = (
        "## Enclosed\n\n"
        "- **This document:** the findings register (Section 3; each finding with sheet, grid, source, viewer pin and "
        "pictures), the gravity load path storey by storey (Section 4) and the transfer slab results (Section 5).\n"
        f"- **`02 3D model viewer.html`:** every one of the {len(reg)} findings is a pin at its storey and location; a set-wide "
        "finding stands at a representative location, named in its register entry.\n"
        "- **`03 Models` and `04 Results`:** our ETABS and SAFE models and their result files.\n"
        "- **`05 Appendices.pdf`:** the answer key, every value in the set (Appendix A, with "
        "`05 Appendix A - answer key.csv`); the information requests to WHM (Appendix B); the gravity check "
        "(Appendix C); and the model basis and SAFE notes (Appendices D to H).\n")
    letter_body = "\n".join(v for k, v in letter.items() if k not in ("_head", "Enclosed")) + "\n" + enclosed_new
    sources01.append("\n".join(v for k, v in letter.items() if k not in ("_head", "Enclosed")))
    letter_body = remap(letter_body)

    readme = read(src / "README.md")
    rs = md_sections(readme)
    readme_head = strip_h1(rs["_head"])
    for k in ("_head", "What is in this folder", "What was checked, and how", "ETABS and SAFE results",
              "Assumptions you must confirm", "What this package does not do"):
        if k not in rs:
            raise SystemExit(f"README section missing: {k}")
    pkg_md = (
        "## What is in this package\n\n"
        "| File | What it is |\n|---|---|\n"
        "| `01 Review letter and findings.pdf` | This document: the review letter, this introduction, the findings "
        f"register ({len(reg)} entries, ranked, each with its sheets, grid, viewer pin and pictures), the gravity load path "
        "storey by storey, and the transfer slab results. |\n"
        "| `02 3D model viewer.html` | One offline page (open it in a browser): the ETABS model of WHM's set as drawn, "
        "storey by storey and in 3D, with its 20 grid lines, the {len(reg)} findings and the assumptions of Appendix E as "
        "numbered pins (click one to read it and where it is stated). Geometry only. |\n"
        "| `03 Models/ETABS/` | The ETABS model (`.e2k`). Every member's f'c is from the set (Appendix E, "
        "\"Materials\"). |\n"
        f"| `03 Models/SAFE/` | One SAFE model for each of the {n_tr} transfer storeys (`<storey>.f2k`), each with its "
        "assumptions (`.assumptions.txt`) and the source of every slab use and load (`.provenance.csv`; "
        "thicknesses, f'c and geometry are not in it: they come from the composed model, Appendix E, "
        f"\"Materials\"). The other {n_other} storey layouts, which carry no load from above of that kind, are in "
        "`Storeys with no transfer/`. Which storeys each model stands for: Appendix F2. |\n"
        "| `04 Results/ETABS/` | The ETABS base reactions and the loads it passes down onto the transfer storeys. |\n"
        "| `04 Results/SAFE/<storey>/` | SAFE's deflection, reaction and punching tables of each transfer storey. |\n"
        "| `05 Appendices.pdf` | A: answer key. B: early comment list to WHM. C: tower column gravity hand check "
        "(preliminary). D: the design at a glance. E: model basis, ETABS and SAFE results, and the assumptions "
        "you must confirm. F: SAFE storey notes. G: the P1/GF supports with no reaction. H: SAFE plans of the "
        "loads not applied at P2 and P3. |\n"
        f"| `05 Appendix A - answer key.csv` | Every value the set states, {n_key:,} rows, each with its sheet and page "
        "and whether it was read off the page, inferred by a stated rule, or is missing from the set. |\n")
    intro_md = (readme_head + "\n\n" + pkg_md + "\n" + rs["What was checked, and how"] + "\n\n"
                + rs["What this package does not do"])
    sources01.append(readme_head + rs["What was checked, and how"] + rs["What this package does not do"])
    intro_md = remap(intro_md)

    reg_md = read(src / "review/FINDINGS-REGISTER.md")
    sources01.append(strip_h1(reg_md))
    reg_html = pandoc(remap(strip_h1(reg_md)), "reg")
    soup = BeautifulSoup(reg_html, "html.parser")
    # summary table: severity pills and a viewer-pin column
    summ = soup.find("table")
    summ["class"] = "register"
    hdr = summ.find("thead").find("tr")
    th = soup.new_tag("th")
    th.string = "Viewer pin"
    hdr.append(th)
    for tr in summ.find("tbody").find_all("tr"):
        tds = tr.find_all("td")
        fid = tds[0].get_text(strip=True)
        sev = tds[1].get_text(strip=True)
        tds[1].clear()
        tds[1].append(BeautifulSoup(pill(sev), "html.parser"))
        td = soup.new_tag("td")
        td.string = f"{fid} on {reg[fid]['pin_storey']}"
        tr.append(td)
    used_pics: list[str] = []
    picture_tables_to_figures(soup, src / "review/pictures", used_pics)
    # each entry: heading with pill and title, a sheet/grid/pin table; checked against the CSV
    entries = 0
    for h in soup.find_all("h2"):
        fid = h.get_text(strip=True)
        if not re.fullmatch(r"F\d\d", fid):
            continue
        r = reg[fid]
        p = h.find_next_sibling("p")
        got = p.get_text("\n", strip=False).strip()
        want = f"{r['severity']}: {r['title']}\n{r['storey']}; {r['grid']}; sheets {r['sheets']}"
        if re.sub(r"\s+", " ", got) != re.sub(r"\s+", " ", want):
            raise SystemExit(f"{fid}: register heading differs from the CSV:\n{got}\n---\n{want}")
        entries += 1
        hid = h["id"]
        new_h = BeautifulSoup(
            f'<h2 class="finding" id="{hid}" data-toc="{fid}\u2003{html.escape(r["title"])}">'
            f'<span class="fid">{fid}</span>{pill(r["severity"])}{html.escape(r["title"])}</h2>', "html.parser").h2
        meta = BeautifulSoup(
            f'<table class="meta"><tr><th>Storey</th><td>{html.escape(r["storey"])}</td></tr>'
            f'<tr><th>Grid</th><td>{html.escape(r["grid"])}</td></tr>'
            f'<tr><th>Sheets</th><td>{html.escape(r["sheets"])}</td></tr>'
            f'<tr><th>Viewer pin</th><td>{fid} on storey {html.escape(r["pin_storey"])} in '
            f'<code>02 3D model viewer.html</code></td></tr></table>', "html.parser").table
        wrap = soup.new_tag("div", attrs={"class": "entry" + (" first" if entries == 1 else "")})
        h.insert_before(wrap)
        wrap.append(new_h)
        p.extract()
        h.extract()
        wrap.append(meta)
        # move the entry's body (up to the next h2) into the wrapper
        nxt = wrap.next_sibling
        while nxt is not None and not (getattr(nxt, "name", None) == "h2"):
            cur = nxt
            nxt = nxt.next_sibling
            wrap.append(cur.extract())
    if entries != len(reg):
        raise SystemExit(f"register entries {entries} != CSV rows {len(reg)}")
    want_pics = sorted(p.name for p in (src / "review/pictures").iterdir())
    if sorted(set(used_pics)) != want_pics:
        raise SystemExit("register pictures placed != pictures in the folder")
    print(f"REGISTER: {entries} of {len(reg)} entries checked against the CSV; {len(set(used_pics))} of "
          f"{len(want_pics)} pictures placed at their finding")
    reg_html = str(soup)

    lp_md = read(src / "review/LOAD-PATH-SUMMARY.md")
    tr_md = read(src / "review/TRANSFER-RESULTS.md")
    sources01 += [strip_h1(lp_md), strip_h1(tr_md)]

    def prep(h: str) -> str:
        s = BeautifulSoup(h, "html.parser")
        widen_tables(s)
        return str(s)

    d1 = Doc("d1")
    d1.section("1", "Review letter", prep(pandoc(letter_body, "letter")), "s1", first=True)
    d1.section("2", "About this package", prep(pandoc(intro_md, "intro")), "s2")
    d1.section("3", "Findings register", reg_html, "s3")
    d1.section("4", "Gravity load path, storey by storey", prep(pandoc(remap(strip_h1(lp_md)), "lp")), "s4")
    d1.section("5", "Transfer slabs: what our SAFE runs give, and what they do not",
               prep(pandoc(remap(strip_h1(tr_md)), "tr")), "s5")
    cov1 = cover(
        f"Structural peer review \u2013 {PROJECT}, Vancouver",
        f"Kor Structural \u00b7 Project {JOB} \u00b7 Gravity review",
        "Review letter, findings register, load path and transfer slab results. Peer review of WHM Structural's "
        "drawings, <strong>Re-issued for Tender 2026-09-09</strong> (42 sheets, S001 to S811).",
        [("For", html.escape(for_who)), ("From", "Kor Structural"), ("Date", letter_date),
         ("Status", html.escape(status[:1].upper() + status[1:])), ("Confidential", "Another firm's design"),
         ("Start here", "Section 1, the review letter; then the findings register, Section 3. "
                        "Open <code>02 3D model viewer.html</code> beside it: each finding is a pin.")])
    pdf1 = out / "01 Review letter and findings.pdf"
    build_pdf(f"Structural peer review - {PROJECT} ({JOB})", "01 Review letter and findings", cov1, d1, build,
              "01-letter", pdf1, "Review letter, findings register, load path, transfer slab results")

    # ===================================================================== PDF 05
    sources05: list[str] = []
    d5 = Doc("d5")
    ak = read(src / "ANSWER-KEY.md")
    sources05.append(strip_h1(ak))
    d5.section("A", APPX["A"], prep(pandoc(remap(strip_h1(ak), "05"), "ak")), "aA", kicker="Appendix A", first=True)
    ec = read(src / "EARLY-COMMENTS.md")
    sources05.append(strip_h1(ec))
    d5.section("B", APPX["B"], prep(pandoc(remap(strip_h1(ec), "05"), "ec")), "aB", kicker="Appendix B")

    gc = read(src / "GRAVITY-CHECK.md")
    if not gc.startswith("# PRELIMINARY - hand check, not the model\n"):
        raise SystemExit("gravity check banner changed")
    sources05.append(strip_h1(gc))
    gc_html = ('<div class="box box--warn"><p><strong>PRELIMINARY - hand check, not the model.</strong></p></div>'
               + pandoc(remap(strip_h1(gc), "05"), "gc"))
    grav = src / "gravity-tower-columns.csv"
    grows = list(csv.DictReader(open(grav, encoding="utf-8", newline="")))
    notes = []
    for r in grows:
        if r["notes"] not in notes:
            notes.append(r["notes"])
    note_no = {n: f"N{i + 1}" for i, n in enumerate(notes)}
    t1 = ["column", "level", "storey_height_ft", "trib_area_ft2", "trib_area_m2", "balcony_part_ft2", "slab_thk_in",
          "D_floor_kN", "L_floor_kN", "column_sw_kN", "cum_D_kN", "cum_L_kN", "Pf_kN", "size", "bars"]
    t2 = ["column", "level", "As_mm2", "fc_MPa", "rho_t", "Pr_max_factor", "Pr_max_kN", "utilisation",
          "res_area_cum_B_m2", "LLR_factor_residential", "cum_L_res_kN", "cum_L_reduced_kN", "Pf_reduced_kN",
          "utilisation_reduced", "utilisation_status", "source_sheets", "notes"]
    if set(t1) | set(t2) != set(grows[0].keys()):
        raise SystemExit("gravity csv columns changed")
    gc_html += (
        '<div class="wide"><h2 id="gc-every-column-level">Every column-level</h2>'
        f"<p>The data of this check, {len(grows)} rows, one per column per level, in two tables with the same rows "
        "in the same order: loads, then capacity and utilisation. Column names are as in the check's data file. "
        f"The notes column gives a note number; the {len(notes)} distinct notes follow the tables.</p>"
        '<h3>Loads</h3>' + csv_table(grav, t1) + "</div>"
        '<div class="wide"><h3>Capacity and utilisation</h3>'
        + csv_table(grav, t2, extra={"notes": lambda r: note_no[r["notes"]]}) + "</div>"
        "<h3>Notes</h3><table class=\"data\"><thead><tr><th>Note</th><th>Text</th></tr></thead><tbody>"
        + "".join(f"<tr><td>{note_no[n]}</td><td>{html.escape(n)}</td></tr>" for n in notes) + "</tbody></table>")
    # the numbers the tables must carry: every cell of every row, each distinct note once
    sources05.append("\n".join(" ".join(r[c] for c in t1 + t2 if c != "notes") for r in grows) + "\n" + "\n".join(notes))
    d5.section("C", APPX["C"], prep(gc_html), "aC", kicker="Appendix C")

    dg = read(src / "review/value-add/DESIGN-AT-A-GLANCE.md")
    sources05.append(strip_h1(dg))
    dg_soup = BeautifulSoup(pandoc(remap(strip_h1(dg), "05"), "dg"), "html.parser")
    img = dg_soup.find("img")
    fig = BeautifulSoup(
        f'<figure class="single"><img src="{(src / "review/value-add/design-at-a-glance.png").as_uri()}" '
        f'alt="{html.escape(img["alt"])}"><figcaption><b>Figure D1.</b> {html.escape(img["alt"])}</figcaption></figure>',
        "html.parser").figure
    (img.find_parent("figure") or img.find_parent("p") or img).replace_with(fig)
    dg_html = str(dg_soup) + ('<div class="wide"><h2 id="dg-per-storey">Per storey, as computed</h2><p>The per-storey '
                              'figures behind Sections 1 and 2 and the figure, one row per storey.</p>'
                              + csv_table(src / "review/value-add/design-at-a-glance.csv") + "</div>")
    sources05.append(csv_cells(src / "review/value-add/design-at-a-glance.csv"))
    d5.section("D", APPX["D"], prep(dg_html), "aD", kicker="Appendix D")

    e_md = (rs["ETABS and SAFE results"] + "\n\n" + rs["Assumptions you must confirm"])
    sources05.append(e_md)
    d5.section("E", APPX["E"], prep(pandoc(remap(e_md, "05"), "e")), "aE", kicker="Appendix E")

    f_parts = []
    for code, title, path, kind in (
            ("F1", "SAFE results by transfer storey", src / "safe/SAFE-RESULTS.txt", "pre"),
            ("F2", "SAFE models and the storeys they stand for", src / "safe/safe-set.txt", "pre"),
            ("F3", "Members standing on a transfer storey, and what is under them", src / "safe/safe-transfers.csv", "csv"),
            ("F4", "Storey levels and the SAFE model of each", src / "safe/safe-levels.csv", "csv"),
            ("F5", "Load rows of the set: applied to the SAFE slabs, or listed, and why", src / "safe/safe-loads.csv", "csv")):
        txt = read(path)
        if kind == "pre":
            sources05.append(txt)
            body = pre_block(remap(txt, "05"))
        else:
            sources05.append(csv_cells(path))
            body = csv_table(path)
            ncols = len(txt.split("\n", 1)[0].split(","))
            if ncols >= 8:
                f_parts.append(f'<div class="wide"><h2 id="f-{code.lower()}">{code}\u2002{html.escape(title)}</h2>{body}</div>')
                continue
        f_parts.append(f'<h2 id="f-{code.lower()}">{code}\u2002{html.escape(title)}</h2>{body}')
    d5.section("F", APPX["F"], "".join(f_parts), "aF", kicker="Appendix F", number_h2=False)

    pg = read(src / "safe/P1_GF-unattached-supports.md")
    sources05.append(strip_h1(pg))
    d5.section("G", APPX["G"], prep(pandoc(remap(strip_h1(pg), "05"), "pg")), "aG", kicker="Appendix G")

    # the paragraph that says how to read the pictures
    legend = next((p for p in rs["ETABS and SAFE results"].split("\n\n") if p.startswith("Forces below are in kN")), None)
    if legend is None:
        raise SystemExit("picture legend paragraph moved in the README")
    figs_h = [("H1", "P2-overview.png", "P2: overview of the SAFE input"),
              ("H2", "P2-KW61.png", "P2: wall KW61 of P1/GF, east of grid 9, where no P2 slab is"),
              ("H3", "P3-overview.png", "P3: overview of the SAFE input"),
              ("H4", "P3-KW6.png", "P3: wall KW6 of P2, east of the slab edge")]
    h_html = pandoc(remap(legend, "05").replace("Pictures in Appendix H show", "These pictures show"), "h")
    for code, name, cap in figs_h:
        p = src / "safe/pictures/beyond-300mm" / name
        h_html += (f'<figure class="single"><img src="{p.as_uri()}" alt="{html.escape(cap)}"><figcaption>'
                   f'<b>Figure {code}.</b> {html.escape(cap)}</figcaption></figure>')
    if sorted(n for _, n, _ in figs_h) != sorted(p.name for p in (src / "safe/pictures/beyond-300mm").iterdir()):
        raise SystemExit("beyond-300mm pictures changed")
    d5.section("H", APPX["H"], h_html, "aH", kicker="Appendix H", number_h2=False)

    cov5 = cover(
        f"Appendices \u2013 {PROJECT}, Vancouver",
        f"Kor Structural \u00b7 Project {JOB} \u00b7 Structural peer review",
        "Reference material behind the review letter and findings register: the answer key, the early comments to "
        "WHM, the column hand check, the model basis and the SAFE notes.",
        [("Appendices", "<br>".join(f"<b>{k}</b>\u2002{html.escape(v)}" for k, v in APPX.items())),
         ("Date", letter_date), ("Confidential", "Another firm's design")])
    pdf5 = out / "05 Appendices.pdf"
    build_pdf(f"Appendices - Structural peer review - {PROJECT} ({JOB})", "05 Appendices", cov5, d5, build,
              "05-appendices", pdf5, "Appendices A-H to the structural peer review")

    # ===================================================================== checks on what was written
    faults = []
    for name, srcs, pdf in (("01", sources01, pdf1), ("05", sources05, pdf5)):
        short = check_numbers(name, [remap(s, name) for s in srcs], pdf)
        faults += [f"{name}: number {s}" for s in short]
        t = pdf_text(pdf)
        for bad in (".md", "safe/", "etabs/", "README", "C:\\", "file:///"):
            if bad in t:
                faults.append(f"{name}: '{bad}' in the PDF text")
    d = fitz.open(pdf1)
    placements = sum(len(p.get_images(full=True)) for p in d)
    print(f"IMAGES 01: {placements} image placements (42 register pictures + the logo expected)")
    shipped = sorted(str(p.relative_to(out)) for p in out.rglob("*") if p.is_file())
    if any(s.lower().endswith(".md") for s in shipped):
        faults.append("a .md file shipped")
    for pdf in (pdf1, pdf5):
        print(f"PAGES {pdf.name}: {fitz.open(pdf).page_count}")
    print(f"SHIPPED {len(shipped)} files")
    if faults:
        print("FAULTS:\n  " + "\n  ".join(faults))
        return 1
    print("CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
