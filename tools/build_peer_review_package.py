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
        <name>/02 3D model viewer.html              the viewer, renamed; its pin texts' file names mapped (VIEWER_REMAP)
        <name>/03 Models/ETABS, 03 Models/SAFE      .e2k; .f2k (SAFE/Notes: assumptions, provenance, F tables)
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
             no ".md", "safe/", "etabs/", "README", "{" or "composed model" left in the PDFs; the copied
             files hash equal to their sources; (JM-V6) every picture link, file and appendix the viewer's
             pins name resolves from the laid-out folder, X of Y; the smallest text in each PDF, read from
             its spans, is 7 pt or more (a wide table that overflows makes Edge shrink the whole PDF).
    does NOT check: that a sentence reads the same (only numbers are compared, and only as a
             multiset - a number moved to the wrong row would pass); that a table kept its columns on
             the page (LOOK at the pages for that: wide tables clip silently in Edge); that a pin's
             appendix or file is the RIGHT one (only that it exists).
    same-class fault it would miss: a remap that turns one file reference into the wrong appendix
             letter keeps every number and passes.

THE COMPLETE REVIEW (row 1982 JM-BUILD-PREP, 13 Nov 2026: gravity + lateral in one package)
    --lateral <draft dir>   LATERAL-SECTION.md (letter pieces 1.1-1.5 spliced into the v8 letter, each replacing v8 text
                            found exactly once; Section 6) and LATERAL-FINDINGS.md (F32-F38 after F31, F15 rewritten in
                            place; register rows and viewer pins from them; counts counted, not typed)
    --changes <md>          Section 7, what we corrected in our own model since the issue JM has
    --numbers <json>        v9-numbers.json: every number a rerun sets, by key ({{KEY}} in a source, or a GRAVITY SITE:
                            a v8 number in a hand source found by the text around it). A key fills from 'value', else
                            from its 'rule' read off the package being laid out (or '@run/' = the lateral run), else
                            stays {{KEY}} and the build refuses it. status 'STAND-IN...' puts a NOT FOR ISSUE banner on
                            both covers.
    --write-filled <dir>    write the hand sources with their sites filled and stop (the assembly's TR_SRC/README_SRC)
    checks added: no "first issue"/"next issue"/"to follow"/"gravity only"/lateral not reviewed in any shipped text
    (SCOPE_FAULT_RE); no unfilled {{KEY}} (PLACEHOLDERS, besides the '{' check); every lateral file a PDF names is in the
    package. Same-class fault they would miss: a number the rerun moves that no site lists (the site inventory is the
    list), and prose whose sense changes with the numbers (the numbers file's prose_to_review lists the known ones).

USAGE
    python -I build_peer_review_package.py --src <frozen package dir> --out <new package dir>
        [--numbers v9-numbers.json --lateral <lateral draft dir> --changes CHANGES-SINCE-V8.md]
        [--job 01819-01]
    The last step is the hand-off gate (row 2135, handoff_gate below): `takeoff handoff-gate check --kind peer-review`.
    Any fault or a refusal renames --out to <out>_REFUSED (NOT FOR ISSUE) and exits 1; CHECKS PASSED only when it passed.
"""
from __future__ import annotations

import argparse
import collections
import csv
import gc
import hashlib
import html
import json
import os
import re
import shutil
import subprocess
import sys
import time
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
     "(the full table, with element counts, is the file `05 Appendix D - per storey.csv` beside this PDF; its main "
     "columns are \"Per storey, as computed\" at the end of this appendix)"),
    ("Pictures: pictures/beyond-300mm/<storey>-overview.png and <storey>-<member>.png.", "Pictures: Appendix H."),
    ("ANSWER-KEY.md MISSING (", "Appendix A, missing values ("),
    ("(README, 'Slab uses')", "(Appendix E, 'Slab uses')"),
    ("and the README's\nslab-use table below", "and the\nslab-use table below"),
    ("The SAFE files and this README give", "The SAFE files and this appendix give"),
    ("README (base reactions)", "Appendix E (base reactions)"),
    ('see README "Materials"', 'see Appendix E "Materials"'),
    ('against the one rule under "What was checked" above.', 'against the one rule under "What was checked, and how" (Section 2 of OF_LETTER).'),
    ("Data: `gravity-tower-columns.csv` (this folder), one row per column per level.",
     "Data: the file `05 Appendix C - every column at every level.csv` beside this PDF, one row per column per level (Section C.14 says what it holds)."),
    # Row 1982 JM-V6: the letter names the README's ETABS paragraph for the two model files
    ("the README's ETABS paragraph names both files", "Appendix E, 'ETABS', names both files"),
    ("the package's `safe/` folder (", "the package's SAFE models and results ("),
    ("from the model in `etabs/`.", "from the model in `03 Models/ETABS/`."),
    ("the model file in `etabs/`", "the model file in `03 Models/ETABS/`"),
    ("`P2-overview.png`, `P2-KW61.png`", "Appendix H, Figures H1 and H2"),
    ("`P3-overview.png`, `P3-KW6.png`", "Appendix H, Figures H3 and H4"),
    ("Pictures in `safe/pictures/beyond-300mm/` show", "Pictures in Appendix H show"),
    ("pictures in `safe/pictures/beyond-300mm/`", "pictures in Appendix H"),
    ("(each listed in safe-loads.csv with why)", "(each listed in Appendix F.5 with why)"),
    ("The 14 transfer storeys are the storeys of safe-transfers.csv:", "The 14 transfer storeys are the storeys of Appendix F.3:"),
    ("COORDINATION-SWEEP.md (architect against structure on 37 of 38 storeys)",
     "our coordination sweep (architect against structure on 37 of 38 storeys)"),
    # Row 1982 JM-V7 (Codex V6-04): the L10 pop-up roof offset is stated in the README's model-geometry list
    ("(README, 'Model geometry to check against the sheets')", "(Appendix E, 'Model geometry to check against the sheets')"),
]
REMAP_RE = [
    # Row 1982 JM-V7 (Codex V6-05): the model as written, the takeoff's source, ships beside the analysed save
    (r"`?(?:\.\./)?etabs/takeoff-source/(01819-01 Renfrew - ETABS model as written \(takeoff source\)\.e2k)`?",
     r"`03 Models/ETABS/\1`"),
    (r"`?(?:\.\./)?etabs/01819-01 Renfrew - ETABS model\.e2k`?", "`03 Models/ETABS/01819-01 Renfrew - ETABS model.e2k`"),
    (r"`?(?:\.\./)?etabs/ETABS-loads-onto-transfer-storeys\.csv`?", "`04 Results/ETABS/ETABS-loads-onto-transfer-storeys.csv`"),
    (r"`?(?:\.\./)?etabs/ETABS-base-reactions\.csv`?", "`04 Results/ETABS/ETABS-base-reactions.csv`"),
    # Row 1982 JM-V8 (Codex V7-04): the run-log extracts behind the run checks ship in 04 Results, tabulated in F.6
    (r"`?(?:\.\./)?etabs/ETABS-solver-log-extract\.txt`?", "`04 Results/ETABS/ETABS-solver-log-extract.txt`"),
    (r"`?(?:safe/)?solver-log-extracts/<storey>\.txt`?",
     "`04 Results/SAFE/solver-log-extracts/<storey>.txt` (tabulated in Appendix F.6)"),
    (r"`?safe/SAFE-RESULTS\.txt`?", "Appendix F.1"),
    (r"SAFE-RESULTS\.txt", "Appendix F.1"),
    (r"`?safe/safe-set\.txt`?", "Appendix F.2"),
    (r"`?safe/safe-transfers\.csv`?", "Appendix F.3"),
    (r"`?safe/safe-levels\.csv`?", "Appendix F.4"),
    (r"`?safe/safe-loads\.csv`?", "Appendix F.5"),
    (r"`?(?:safe/)?P1_GF-unattached-supports\.md`?", "Appendix G"),
    # Row 1982 JM-SAFE-FOLDER (Ian 13 Nov): 03 Models/SAFE/ holds the models; every other file is in its Notes/ folder
    (r"(?:safe/|(?<![\w/.]))models/(<storey>|\w+)(\.(?:assumptions\.txt|provenance\.csv))", r"03 Models/SAFE/Notes/\1\2"),
    # a bare `<storey>.assumptions.txt` (Section 5's table) stays bare: Section 5's source line names the folder
    (r"(`safe/models/<storey>\.f2k`)", r"\1 (its assumptions: `03 Models/SAFE/Notes/<storey>.assumptions.txt`)"),
    (r"safe/models/", "03 Models/SAFE/"),
    (r"(?<![\w/.])models/", "03 Models/SAFE/"),
    (r"safe/results/", "04 Results/SAFE/"),
    (r"(?<![\w/.])results/", "04 Results/SAFE/"),
    (r"`?GRAVITY-CHECK\.md`?", "Appendix C"),
    (r"`?gravity-tower-columns\.csv`?", "`05 Appendix C - every column at every level.csv`"),
    (r"`?ANSWER-KEY\.md`?", "Appendix A"),
    (r"`answer-key\.csv`", "`05 Appendix A - answer key.csv`"),
    (r"`?EARLY-COMMENTS\.md`?(?= [AB]\d)", "Appendix B,"),
    (r"`?EARLY-COMMENTS\.md`?", "Appendix B"),
    (r"`?DESIGN-AT-A-GLANCE\.md`?", "Appendix D"),
    (r"`?TRANSFER-RESULTS\.md`?", "Section 5 of OF_LETTER"),
    (r"`?LOAD-PATH-SUMMARY\.md`?", "Section 4 of OF_LETTER"),
    (r"`?(?:review/)?REVIEW-LETTER\.md`?", "Section 1 of OF_LETTER, the review letter"),
    (r"`?(?:review/)?FINDINGS-REGISTER\.(?:csv|md)`?", "the findings register (Section 3 of OF_LETTER)"),
    (r"`01819-01 Renfrew - 3D model viewer\.html`", "`02 3D model viewer.html`"),
    # Row 1982 JM-BUILD-PREP: the lateral draft's working files, as the package ships them (LAT_RES, LAT_CHK)
    (r"`?(?:(?:review/)?lateral/)?run-r\d+/([\w.-]+\.csv)`?", r"`04 Results/ETABS lateral/\1`"),
    (r"`?(?:lateral/)?capacity/CAPACITY\.md`?(?: section \d+)?", "our resistance tables (`04 Results/Lateral checks/`)"),
    (r"`?(?:lateral/)?capacity/([\w.-]+\.csv)`?", r"`04 Results/Lateral checks/\1`"),
    (r"`?(?:(?:lateral/)?yardsticks/)?YARDSTICKS\.md`?(?: [\d.]+(?:(?:,| and) [\d.]+)*)?", "our code yardsticks (Section 6.1)"),
    (r"`?(?:lateral/)?(?:yardsticks/)?yardsticks\.csv`?", "`04 Results/Lateral checks/yardsticks.csv`"),
    (r"`?(?:lateral/)?LATERAL-BASIS\.md`?(?: sections? [\d-]+)?", "Section 6.1"),
    (r"\(`check\.txt`: ", "(Section 6.2: "),
]


# Row 1982 JM-NO-PHASE2 (Ian, 13 Nov: "remove phase 2"; #133596: a FULL peer review) and JM-BUILD-PREP (Ian 13 Nov,
# #133612/#133614/#133621: JM 01819 ships ONE complete package, gravity and lateral): each v8 source wording that framed
# lateral as excluded or later is rewritten here (the sources are not edited) to the complete review, and the build
# refuses "phase 2" (PHASE2_RE) and any "first issue" / "next issue" / "to follow" / "gravity only" (SCOPE_FAULT_RE) left
# in the PDFs, the viewer or a shipped text file. 'Gravity' stays where it names a load case or the gravity models.
SCOPE_RE = [
    # v8 letter 'Scope limits' first bullet: replaced whole by the lateral splice; this is the fallback wording
    (r"\*\*Gravity only\.\*\* Lateral \(wind and seismic\) is \*\*not reviewed\*\*\. For a 31-storey concrete tower in "
     r"Vancouver with\s+coupled shear walls, we recommend a \*\*phase 2 lateral review\*\*: the core's coupling beams, "
     r"drift, the core pad's\s+overturning, and the diaphragms at the transfer levels\. F10, F15 and F21 list what WHM "
     r"would need to provide\.",
     "**This review covers gravity and lateral.** The lateral and seismic review is Section 6."),
    (r"\(F15, for phase 2\)", "(F15, Section 6)"),
    (r" \(for phase 2\)", ""),
    (r"WHM to provide these with the phase 2 lateral review\.", "WHM to provide these (Section 6 states what we assumed)."),
    (r"Not reviewed \(gravity only\)\.", "Our assumptions for each item are stated in Section 6."),
    # F10 (register .md and .csv): the wall geometry also sets the lateral stiffness of Section 6
    (r" now and lateral stiffness in phase 2\.", " and the lateral stiffness of our lateral model (Section 6)."),
    (r"\(lateral, phase 2\)", "(lateral: F36 and Section 6)"),
    (r"\(not reviewed, phase 2\)", "(Section 6)"),
    (r"; this review does not cover lateral loads\.", "; Section 6 gives our lateral review."),
    (r"S001 to S811\), gravity only\.", "S001 to S811), gravity and lateral."),
    # register intro, line 11: the forces of the gravity entries; Section 6 gives the lateral ones
    (r"unfactored service loads, gross \(uncracked\) sections, gravity only, given as",
     "for the gravity entries, unfactored service loads on gross (uncracked) sections, given as"),
    # register severity definition (CRITICAL): this review covers the lateral load path too
    (r"\*\*CRITICAL\*\*: the gravity load path or", "**CRITICAL**: the gravity or lateral load path, or"),
    # package README, 'Modelling': the gravity models carry gravity loads alone; the lateral model is Section 6
    (r"Gravity only: no wind and no seismic load is in\s+either model\.",
     "The gravity models carry gravity loads alone: no wind and no seismic load is in either of them (the lateral "
     "model, its loads and its stiffness are Section 6 of OF_LETTER)."),
    # SAFE-RESULTS.txt (Appendix F.1): the SAFE models are gravity models
    (r"- Gravity only \(no wind, no seismic\);", "- Gravity loads alone (no wind, no seismic: these are the gravity models);"),
    (r"^gravity review, first issue$", "gravity and lateral review"),
]
# no offer of a "phase 2", and no framing of lateral as excluded from the review
PHASE2_RE = r"(?i)phase[\s-]*(?:2|two|ii)\b|outside (?:the scope|this review)"
# Row 1982 JM-BUILD-PREP: this issue is the complete review. COVERS: the words of the v8 framing (first/next issue, to
# follow, gravity only, lateral not reviewed or run; 'gravity review' stays: it names the gravity part of the review). DOES NOT: the same idea in other words - same-class fault it
# would miss: "lateral is reviewed separately".
SCOPE_FAULT_RE = (r"(?i)\bfirst issue\b|\bnext issue\b|\bto follow\b|\bgravity only\b"
                  r"|lateral[^.]{0,60}\bnot (?:yet )?(?:reviewed|run)\b")


# ---------------------------------------------------------------- v9 numbers by data (row 1982 JM-BUILD-PREP)
# Every number a rerun sets is a key of ONE data file (jobs/01819-01-renfrew/review/v9-numbers.json): a {{KEY}} in a
# source (the lateral draft's {{R23:...}} / {{ISSUE:...}}, the changes page's {{AUDIT:...}}) or a GRAVITY SITE - a number
# in a v8 hand source, found by its exact text around it, which the build turns into {{KEY}} before filling. A key's
# value comes from 'value' (set from the run output the entry names); a key with no value stays {{KEY}} and the build
# refuses it (the '{' and PLACEHOLDER checks). COVERS: every site the data file lists (each must be found once, either
# with its v8 number or already filled); every {{KEY}} in a shipped text. DOES NOT: a model-moved number the data file
# does not list (the site inventory is the list; same-class fault it would miss: a v8 number in a sentence no site names,
# e.g. a count that changes but was never inventoried).
PH_RE = re.compile(r"\{\{([A-Z0-9]+:[^{}\s]+?)\}\}")


class Numbers:
    def __init__(self, path: Path | None):
        self.path = path
        self.d = json.loads(path.read_text(encoding="utf-8")) if path else {"keys": {}, "gravity_sites": []}
        self.keys = self.d.get("keys", {})
        self.computed: dict[str, str] = {}
        self.used: set[str] = set()
        self.unfilled: set[str] = set()
        self.site_log = collections.Counter()
        self.pkg: Path | None = None        # the package being laid out: rules read its run outputs
        self.from_pkg: set[str] = set()
        self.checked: set[str] = set()      # typed values checked against their rule (JM-V9-PREP)
        self.disagree: list[str] = []

    @property
    def stand_in(self) -> bool:
        return str(self.d.get("status", "")).upper().startswith("STAND-IN")

    def value(self, k: str) -> str | None:
        if k in self.computed:
            self.used.add(k)
            return self.computed[k]
        e = self.keys.get(k)
        if e is not None and e.get("value") in (None, "") and e.get("rule") and self.pkg is not None:
            self.computed[k] = self.rule(e["rule"], k)   # read off the package being laid out
            self.from_pkg.add(k)
            return self.value(k)
        if e is None or e.get("value") in (None, ""):
            self.unfilled.add(k)
            return None
        # Row 1982 JM-V9-PREP (13 Nov 20:0x): a key with a rule AND a typed value is checked against the rule read off
        # THIS package; a typed value the package does not carry is refused. Found on the r29 rehearsal: 289 transfer
        # members typed (v8's), 294 in r29's own ETABS-loads csv. Skipped for '@run/' rules when no lateral run is there.
        if e.get("rule") and self.pkg is not None and k not in self.checked:
            self.checked.add(k)
            r = e["rule"]
            f = (self.path.parent / self.d.get("lateral_run_dir", "") / r["file"][5:]) if r["file"].startswith("@run/") \
                else self.pkg / r["file"]
            if f.is_file():
                got = self.rule(r, k)
                if got != str(e["value"]):
                    self.disagree.append(f"{k}: typed {e['value']}, the package's {r['file']} gives {got}")
        self.used.add(k)
        return str(e["value"])

    def rule(self, r: dict, k: str) -> str:
        """A key's value read off the package's own run outputs. Kinds: 'line' (the n-th number on the one line of the
        file whose text, spaces folded, starts with 'start'), 'after' (the first number after a literal), 'csv' (a
        column of the row matching 'row'), 'rows' (data rows of a csv), 'distinct' (distinct values of 'cols'), 'sum' (a
        column summed over matching rows), 'list' (names a table gives, joined or counted; see below), 'sha'.
        'fmt': {'dp': decimals, 'comma': thousands separator, 'abs': drop the sign, 'neg': negate}."""
        fp = r["file"]
        f = (self.path.parent / self.d.get("lateral_run_dir", "") / fp[5:]) if fp.startswith("@run/") else self.pkg / fp
        if not f.is_file():
            raise SystemExit(f"{k}: rule file {r['file']} not in the package")
        kind = r["kind"]
        if kind == "line":
            fold = lambda s: re.sub(r"\s+", " ", s.strip())
            hits = [fold(ln) for ln in read(f).split("\n") if fold(ln).startswith(r["start"])]
            if len(hits) != 1:
                raise SystemExit(f"{k}: {len(hits)} lines of {r['file']} start '{r['start']}', not 1")
            v = NUM_RE.findall(hits[0])[r["n"]]
        elif kind == "after":
            t = read(f)
            if t.count(r["after"]) != 1:
                raise SystemExit(f"{k}: '{r['after']}' found {t.count(r['after'])} times in {r['file']}, not once")
            v = NUM_RE.search(t, t.index(r["after"]) + len(r["after"]))[0]
        elif kind == "csv":
            rows = list(csv.DictReader(open(f, encoding="utf-8")))
            rows = rows[-1:] if r["row"] == "last" else [x for x in rows if all(x[c] == w for c, w in r["row"].items())]
            if len(rows) != 1:
                raise SystemExit(f"{k}: {len(rows)} rows of {r['file']} match {r['row']}, not 1")
            v = rows[0][r["col"]]
        elif kind in ("rows", "distinct"):
            rows = [x for x in csv.DictReader(open(f, encoding="utf-8"))
                    if all(x[c] == w for c, w in r.get("where", {}).items())]
            # a col 'a|b' is the first non-empty of a, b (JM-V9-PREP: 'pier|member' counts a pier once, as the texts do)
            col = lambda x, c: next((x[p] for p in c.split("|") if x[p]), "")
            v = str(len(rows) if kind == "rows" else len({tuple(col(x, c) for c in r["cols"]) for x in rows}))
        elif kind == "sha":
            v = sha(f)[:r.get("n", 16)]
        elif kind == "sum":
            # JM-V9-BUILDER: a column summed over the csv rows that match 'where' (equal), 'in' (one of) and 'prefix'
            # (starts with); refuses when no row matches (6-readme.sh's P2_COLS: the 13 P2 pinned columns at station 0)
            rows = [x for x in csv.DictReader(open(f, encoding="utf-8"))
                    if all(x[c] == w for c, w in r.get("where", {}).items())
                    and all(x[c] in ws for c, ws in r.get("in", {}).items())
                    and all(x[c].startswith(p) for c, p in r.get("prefix", {}).items())]
            if not rows:
                raise SystemExit(f"{k}: no row of {r['file']} matches the sum rule")
            v = str(sum(float(x[r["col"]]) for x in rows))
        elif kind == "list":
            # Row 1982 JM-V9-BUILDER (14 Nov): the names a run's own table gives (the README's proven list and not-proven
            # bullets, counted off SAFE-RESULTS.txt, never typed). 'pattern': a regex whose group 1 is the name, matched
            # line by line; 'count': the number of names; else each name through 'item' ({name}, {key} = the name with
            # '/' and ' ' as '_'; '{{' / '}}' for a literal brace) joined by 'sep'. An 'item' may name a {{KEY}} per name:
            # fill() fills it in a second pass, and a key with no value stays for the build to refuse. No match refuses
            # (a pattern that matches nothing is a fault) unless 'empty_ok' (a run with no not-proven storey).
            names = [m[1] for m in re.finditer(r["pattern"], read(f), re.M)]
            if not names and not r.get("count") and not r.get("empty_ok"):
                raise SystemExit(f"{k}: no line of {r['file']} matches {r['pattern']}")
            item = r.get("item", "{name}")
            v = str(len(names)) if r.get("count") else r.get("sep", ", ").join(
                item.format(name=n, key=n.replace("/", "_").replace(" ", "_")) for n in names)
            return v
        else:
            raise SystemExit(f"{k}: rule kind {kind}?")
        fm = r.get("fmt", {})
        if fm:
            x = float(v.replace(",", ""))
            x = (abs(x) if fm.get("abs") else (-x if fm.get("neg") else x)) * fm.get("mul", 1)
            v = f"{x:{',' if fm.get('comma') else ''}.{fm.get('dp', 0)}f}"
        return v

    def sites(self, text: str, rel: str) -> str:
        """Each gravity site of source `rel`: its v8 number becomes {{KEY}} (found by before+v8+after, exactly once)."""
        edits = []
        for s in self.d.get("gravity_sites", []):
            if s["file"] != rel:
                continue
            anchor = s["before"] + s["v8"] + s["after"]
            n = text.count(anchor)
            if n == 1:
                i = text.index(anchor) + len(s["before"])
                edits.append((i, i + len(s["v8"]), "{{" + s["key"] + "}}"))
                self.site_log["templated"] += 1
                continue
            if n == 0 and text.count(s["before"] + "{{" + s["key"] + "}}" + s["after"]) == 1:
                self.site_log["already a key"] += 1   # the source names the key itself (JM-V9-PREP: a run-set value)
                continue
            v = self.value(s["key"])
            if n == 0 and v is not None and text.count(s["before"] + v + s["after"]) == 1:
                self.site_log["already filled"] += 1   # the source already carries the new number (e.g. TR_SRC filled)
                continue
            raise SystemExit(f"gravity site {s['key']} ({rel}:{s['line']}): '{anchor}' found {n} times, not once "
                             "(the source changed: update the site in the numbers file)")
        for i, j, new in sorted(edits, reverse=True):
            text = text[:i] + new + text[j:]
        return text

    def fill(self, text: str) -> str:
        # a value may itself name keys (a 'list' rule's item, a TEXT key quoting a G: number): fill until nothing changes
        for _ in range(4):
            new = PH_RE.sub(lambda m: self.value(m[1]) if self.value(m[1]) is not None else m[0], text)
            if new == text:
                break
            text = new
        return text

    def source(self, text: str, rel: str) -> str:
        return self.fill(self.sites(text, rel))


NUMS = Numbers(None)


def lateral_entries(md: str) -> dict[str, str]:
    """LATERAL-FINDINGS.md: {id: entry body from its bold heading line to the end}, for F32-F38 and the rewritten F15."""
    out = {}
    for m in re.finditer(r"^## (F\d\d)(?: \(rewritten[^)\n]*\))?\n(.*?)(?=^## |^---\s*$|\Z)", md, re.S | re.M):
        out[m[1]] = m[2].strip()
    return out


def entry_head(body: str) -> dict:
    """The register columns an entry's first two lines give: severity, title, storey, grid, sheets."""
    l1, l2 = body.split("\n")[:2]
    m = re.fullmatch(r"\*\*(CRITICAL|SIGNIFICANT|MINOR): (.+)\*\*\s*", l1)
    if not m:
        raise SystemExit(f"lateral entry heading not '**SEVERITY: title**': {l1}")
    rest, _, sheets = l2.strip().rpartition("; sheets ")
    storey, _, grid = rest.partition("; ")
    bul = dict(re.findall(r"^- \*\*([\w ]+)\.\*\* (.*)$", body, re.M))
    return {"severity": m[1], "title": m[2], "storey": storey, "grid": grid, "sheets": sheets,
            "source": bul.get("Source", ""), "pin": bul.get("Pin", "")}


def splice(text: str, old: str, new: str, what: str, regex: bool = False) -> str:
    """Replace `old` (exactly once) by `new`, or stop the build naming the piece."""
    n = len(re.findall(old, text, re.M | re.S)) if regex else text.count(old)
    if n != 1:
        raise SystemExit(f"lateral splice '{what}': the v8 text it replaces was found {n} times, not once")
    return re.sub(old, lambda _: new, text, flags=re.M | re.S) if regex else text.replace(old, new)


def draft_piece(sec: dict, start: str) -> str:
    """The body of the LATERAL-SECTION.md piece whose heading starts with `start`."""
    k = [h for h in sec if h.startswith(start)]
    if len(k) != 1:
        raise SystemExit(f"LATERAL-SECTION.md: piece '{start}' found {len(k)} times")
    return sec[k[0]].split("\n", 1)[1].strip()


def lateral_letter(md: str, sec: dict, sev_n: collections.Counter) -> str:
    """The v8 letter with the lateral pieces of LATERAL-SECTION.md (1.1 to 1.5; 1.6 is the build's Enclosed). Each piece
    replaces or follows v8 text found exactly once, or the build stops naming the piece."""
    meta = re.search(r"`(\*\*For:\*\*.+?)`", draft_piece(sec, "Letter meta line"))[1]
    md = splice(md, md.splitlines()[2], meta, "meta line")
    md = splice(md, r"We reviewed the gravity design of WHM Structural's drawings.*?\(the issue before it, 2026-08-28 "
                    r"Issued for Tender\)\.", draft_piece(sec, "1.1 Scope"), "1.1 Scope", regex=True)
    md = splice(md, "the 7 it does not are C42 at L5 to L9 (F30) and C34 at L1 and L2 (F31).\n",
                "the 7 it does not are C42 at L5 to L9 (F30) and C34 at L1 and L2 (F31).\n"
                + draft_piece(sec, "1.2 Method") + "\n", "1.2 Method")
    md = splice(md, r"^## Key findings \(\d+ entries: \d+ critical, \d+ significant, \d+ minor\)$",
                f"## Key findings ({sum(sev_n.values())} entries: {sev_n['CRITICAL']} critical, "
                f"{sev_n['SIGNIFICANT']} significant, {sev_n['MINOR']} minor)", "1.3 heading", regex=True)
    p13 = draft_piece(sec, "1.3 Key findings")
    block = re.search(r"^(- \*\*Lateral and seismic.*?)\n\n", p13, re.M | re.S)[1]
    md = splice(md, "\n\n**MINOR:**", "\n" + block + "\n\n**MINOR:**", "1.3 lateral block")
    md = splice(md, "7. The missing values in F09-F15", "7. The missing values in F09-F14", "1.4 item 7")
    md = splice(md, "with the assumption we use meanwhile.\n",
                "with the assumption we use meanwhile.\n" + draft_piece(sec, "1.4 Items") + "\n", "1.4 items 8-12")
    b15 = re.findall(r"^- (.*?)(?=^- |\Z)", draft_piece(sec, "1.5 Scope limits"), re.M | re.S)
    if len(b15) != 4:
        raise SystemExit(f"1.5 Scope limits: {len(b15)} bullets in the draft, 4 expected")
    becomes = [re.sub(r"^The \"[^\"]+\" bullet becomes: ", "", b.strip()) for b in b15]
    md = splice(md, r"^- \*\*Gravity only\.\*\*.*?would need to provide\.\n", "- " + becomes[0] + "\n",
                "1.5 gravity-only bullet", regex=True)
    md = splice(md, r"^- \*\*Unfactored service loads\.\*\*.*?the only factored check\.\n", "- " + becomes[1] + "\n",
                "1.5 unfactored bullet", regex=True)
    md = splice(md, r"^- \*\*Uncracked sections\.\*\*.*?long-term effects\.\n",
                "- " + becomes[2] + "\n- " + re.sub(r"^New bullet: ", "", becomes[3]) + "\n", "1.5 uncracked bullet",
                regex=True)
    return md


def section6(sec: dict) -> str:
    """Section 6 of PDF 01: the draft's '## 6. Lateral and seismic' piece; its '### 6.n' headings become numbered H2s."""
    body = draft_piece(sec, "6. Lateral and seismic")
    body = "\n".join(ln for ln in body.split("\n") if ln.strip() != "---")
    return re.sub(r"^### 6\.\d+ ", "## ", body, flags=re.M)


def inject_pins(html_text: str, rows: list[dict]) -> str:
    """The viewer's KOR_JOB: a pin per lateral register row (new, or F15's text replaced), as the assembly writes them."""
    i = html_text.index("window.KOR_JOB = ") + len("window.KOR_JOB = ")
    j = html_text.index(";</script>", i)
    job = json.loads(html_text[i:j])
    have = {a.get("id"): a for a in job["assumptions"]}
    for r in rows:
        a = have.get(r["id"]) or {"id": r["id"]}
        a.update({"storey": r["pin_storey"], "x_mm": float(r["x_mm"]), "y_mm": float(r["y_mm"]),
                  "text": f"{r['severity']} {r['id']}: {r['title']}. For the engineer of record to confirm.",
                  "rule": f"Review finding {r['id']} (FINDINGS-REGISTER.csv); source: {r['source']}",
                  "severity": r["severity"]})
        if r["id"] not in have:
            job["assumptions"].append(a)
    return html_text[:i] + json.dumps(job) + html_text[j:]


def scope(text: str) -> str:
    for pat, new in SCOPE_RE:
        text = re.sub(pat, new, text)
    return text


def remap(text: str, doc: str = "01") -> str:
    """doc '01': a section reference points inside the same PDF; '05': it names PDF 01."""
    text = scope(text)
    for old, new in REMAP_EXACT:
        text = text.replace(old, new)
    for pat, new in REMAP_RE:
        text = re.sub(pat, new, text)
    return text.replace(" of OF_LETTER", "" if doc == "01" else " of 01 Review letter and findings")


# ---------------------------------------------------------------- helpers
PDF05 = "05 Appendices.pdf"
PDF01 = "01 Review letter and findings.pdf"
# Row 1982 JM-V6 (Codex V5-03, V5-04): files a reader opens beside the PDFs. The viewer's picture links point into
# VIEWER_PICS; the full gravity table and the long SAFE tables are files, the PDF carries legible summaries of them.
VIEWER_PICS = "02 Viewer pictures"
GRAVCSV = "05 Appendix C - every column at every level.csv"
E2K_WRITTEN = "01819-01 Renfrew - ETABS model as written (takeoff source).e2k"
DGCSV = "05 Appendix D - per storey.csv"
F_CSVS = ("safe-transfers.csv", "safe-levels.csv", "safe-loads.csv")   # shipped in SAFE_NOTES
# Row 1982 JM-SAFE-FOLDER (Ian, 13 Nov: "I would expect to see the Safe model(s) when I open the SAFE folder"): the
# folder opens to the .f2k models alone; each model's assumptions and provenance and the F tables' files are in Notes/
SAFE_NOTES = "03 Models/SAFE/Notes"
# The viewer's pin texts name the old files. No double quote may be introduced: they sit inside JS strings.
VIEWER_REMAP = [
    ("(FINDINGS-REGISTER.csv)", f"(the findings register, Section 3 of {PDF01})"),
    (r"safe/P1_GF-unattached-supports.md", f"Appendix G in {PDF05}"),
    (r"etabs/ETABS-loads-onto-transfer-storeys.csv", "04 Results/ETABS/ETABS-loads-onto-transfer-storeys.csv"),
    ("gravity-tower-columns.csv", "05 Appendix C - every column at every level.csv"),
    ("GRAVITY-CHECK.md", f"Appendix C in {PDF05}"),
    ("ANSWER-KEY.md", f"Appendix A in {PDF05}"),
    ("EARLY-COMMENTS.md", f"Appendix B in {PDF05}"),
]


H_FIGS = {"P2-overview.png": "H1", "P2-KW61.png": "H2", "P3-overview.png": "H3", "P3-KW6.png": "H4"}


def viewer_ref(s: str, transfer: set) -> str:
    """Row 1982 JM-V6 (Codex V5-04, V5-05): a pin's source or picture path, as the laid-out package has it."""
    s = re.sub(r"safe/pictures/beyond-300mm/([\w.-]+\.png)",
               lambda m: f"Appendix H, Figure {H_FIGS[m[1]]} in {PDF05}", s)
    s = re.sub(r"(?:\breview/|(?<![\w/]))pictures/(?=[\w.-]+\.(?:png|jpg))", f"{VIEWER_PICS}/", s)
    s = s.replace("safe/SAFE-RESULTS.txt", f"Appendix F.1 in {PDF05}")
    s = re.sub(r"safe/models/(<storey>|[\w]+)(\.(?:assumptions\.txt|provenance\.csv))",
               lambda m: f"{SAFE_NOTES}/" + m[1] + m[2], s)
    s = re.sub(r"safe/models/(<storey>|[\w]+)(\.f2k)",
               lambda m: ("03 Models/SAFE/" if m[1] == "<storey>" or m[1] in transfer
                          else "03 Models/SAFE/Storeys with no transfer/") + m[1] + m[2], s)
    s = s.replace("etabs/01819-01 Renfrew - ETABS model.e2k", "03 Models/ETABS/01819-01 Renfrew - ETABS model.e2k")
    s = re.sub(r"README (?=')", f"Appendix E in {PDF05}, ", s)
    return s


def viewer_links(html_text: str, root: Path) -> tuple[int, list[str]]:
    """Every picture link and every file or appendix a pin names, resolved from the laid-out folder: (count, misses)."""
    i = html_text.index("window.KOR_JOB = ") + len("window.KOR_JOB = ")
    job = json.loads(html_text[i:html_text.index(";</script>", i)])
    n, bad = 0, []
    for a in job.get("assumptions", []):
        for pic in a.get("pictures", []):
            n += 1
            if not (root / pic["href"]).is_file():
                bad.append(f"{a['id']} picture link {pic['href']}")
        words = " ".join(str(a.get(k, "")) for k in ("text", "rule")) + " " + \
            " ".join(p.get("text", "") for p in a.get("pictures", []))
        for m in re.finditer(r"(?<!\w)(?:01 Review letter and findings\.pdf|02 3D model viewer\.html|05 Appendices\.pdf"
                             r"|0[2-5] (?:Viewer pictures|Models|Results)/[^;:,()'\"]*?\.(?:png|jpg|csv|f2k|txt|e2k)\b"
                             r"|05 Appendix [AC] - [^;:,()'\"/]*?\.csv)", words):
            n += 1
            ref = m[0]
            ok = any(root.glob(ref.replace("<storey>", "*"))) if "<storey>" in ref else (root / ref).is_file()
            if not ok:
                bad.append(f"{a['id']} names {ref}")
        for m in re.finditer(r"Appendix ([A-Z])(?:\.(\d))?(?:, Figure (H\d))?", words):
            n += 1
            if m[1] not in APPX or (m[2] and not (m[1] == "F" and m[2] in "12345")) or (m[3] and m[3] not in H_FIGS.values()):
                bad.append(f"{a['id']} names {m[0]}")
        for old in ("safe/", "etabs/", "README", "review/", ".md"):
            if old in words:
                bad.append(f"{a['id']} still names '{old}'")
    return n, bad


def viewer_text(p: Path, transfer: set | None = None, extra_pins: list | None = None) -> str:
    t = scope(read(p))
    if extra_pins:
        t = inject_pins(t, extra_pins)
    for old, new in VIEWER_REMAP:
        t = t.replace(old, new)
    if transfer is not None:
        i = t.index("window.KOR_JOB = ") + len("window.KOR_JOB = ")
        j = t.index(";</script>", i)
        job = json.loads(t[i:j])
        for a in job.get("assumptions", []):
            for k in ("text", "rule"):
                if isinstance(a.get(k), str):
                    a[k] = viewer_ref(a[k], transfer)
            for pic in a.get("pictures", []):
                pic["href"] = viewer_ref(pic["href"], transfer)
                pic["text"] = viewer_ref(pic["text"], transfer)
        t = t[:i] + json.dumps(job).replace("</", "<\\/") + t[j:]
    # Row 1982 JM-V5: find each '.md'/'.csv' first, then read back at most 200 chars for its name. The old
    # findall(r"[\w/.-]+\.(?:md|csv)\b") backtracks over the inlined model's base64 (one 1.8M-char run): quadratic, it hung.
    left = []
    for m in re.finditer(r"\.(?:md|csv)\b", t):
        name = re.search(r"[\w/.-]*$", t[max(0, m.start() - 200):m.start()])[0] + m[0]
        left.append(name)
    left = [x for x in left if not x.startswith(("04 Results", "Results/"))]   # '04 Results/...': the name read stops at the space
    left = [x for x in left if not GRAVCSV.endswith(x)]                        # the same for the gravity table's file
    left = [x for x in left if not x.startswith(("lateral/", "checks/"))]      # '04 Results/ETABS lateral/', '.../Lateral checks/'
    if left:
        raise SystemExit(f"viewer still names old files: {sorted(set(left))}")
    return t


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
            k = ("num" if numeric[c] else "txt") + (" over" if over else "") + (" nw" if len(v) <= 34 else "") + (" long" if len(v) > 80 else "")
            h.append(f'<td class="{k}">{html.escape(v)}</td>')
        h.append("</tr>")
    h.append("</tbody></table>")
    return "".join(h)


def csv_cells(path: Path) -> str:
    """The data cells of a CSV, one per line: the text whose numbers its table must carry."""
    rows = list(csv.reader(open(path, encoding="utf-8", newline="")))[1:]
    return "\n".join(c for r in rows for c in r)


def pre_block(text: str, prose_from: int = 0) -> str:
    """Fixed-width text. Row 1982 JM-V7 (Codex V5-03): with prose_from, a line longer than that is prose that the
    monospace wraps into a dense block, so it is set as a paragraph in the body face (indent kept as a margin); the
    shorter lines, which hold the column-aligned tables, stay fixed-width."""
    if not prose_from:
        return f'<pre class="plain">{html.escape(text)}</pre>'
    out, run = [], []
    flush = lambda: (out.append(f'<pre class="plain">{html.escape(chr(10).join(run))}</pre>') if run else None, run.clear())
    for ln in text.split("\n"):
        if len(ln) > prose_from:
            flush()
            ind = len(ln) - len(ln.lstrip(" "))
            out.append(f'<p class="prose" style="margin-left:{min(ind, 8) * 1.2:.1f}mm">{html.escape(ln.strip())}</p>')
        else:
            run.append(ln)
    flush()
    return "".join(out)


def summarise_over_080(gc: str, grav_csv: Path) -> str:
    """Row 1982 JM-V7 (Codex V5-03): Appendix C's 'Every numeric row with utilisation > 0.80 unreduced' table (125 rows,
    five pages) becomes the rows above 1.00 in full plus one line per column for the rows above 0.80; every row is in
    the gravity file. Each row of the source table is first found in the file (column, level, both utilisations to
    three decimals), so the summary cannot drop or alter a row the file does not hold."""
    m = re.search(r"^## Every numeric row with utilisation > 0\.80 unreduced: (\d+) of (\d+)\n(.*?)(?=^## )", gc, re.S | re.M)
    if not m:
        raise SystemExit("gravity check: the > 0.80 section moved")
    body = m.group(3)
    hdr = "| column | level | size | bars | f'c | Pf kN | Pf reduced kN | Pr,max kN | util unreduced | util reduced |"
    if hdr not in body:
        raise SystemExit("gravity check: the > 0.80 table's columns changed")
    lead = body[:body.index(hdr)].rstrip().replace(" Bold = above 1.00.", "")   # the summary tables carry no bold
    rows = []
    for ln in body[body.index(hdr):].splitlines()[2:]:
        if not ln.startswith("| TC"):
            break
        rows.append([c.strip().strip("*") for c in ln.strip("|").split("|")])
    if len(rows) != int(m.group(1)):
        raise SystemExit(f"gravity check: {len(rows)} rows in the > 0.80 table, heading says {m.group(1)}")
    fil = {(r["column"], r["level"]): r for r in csv.DictReader(open(grav_csv, encoding="utf-8", newline=""))}
    for r in rows:
        f = fil.get((r[0], r[1]))
        if f is None or f"{float(f['utilisation']):.3f}" != r[8] or f"{float(f['utilisation_reduced']):.3f}" != r[9]:
            raise SystemExit(f"gravity check: > 0.80 row {r[0]} {r[1]} is not the gravity file's")
    over1 = [r for r in rows if float(r[8]) > 1.0]
    per = {}
    for r in rows:
        per.setdefault(r[0], []).append(r)
    t1 = "\n".join("| " + " | ".join(r) + " |" for r in over1)
    t2 = "\n".join(
        f"| {c} | {len(rs)} | {sum(float(x[8]) > 1.0 for x in rs)} | {', '.join(x[1] for x in rs)} | "
        f"{max(rs, key=lambda x: float(x[8]))[8]} at {max(rs, key=lambda x: float(x[8]))[1]} |" for c, rs in per.items())
    new = (f"{lead}\n\nAll {len(rows)} rows are in the file `{GRAVCSV}` (filter `utilisation` above 0.800). The "
           f"{len(over1)} rows above 1.00, in full:\n\n{hdr}\n|---|---|---|---|---|---|---|---|---|---|\n{t1}\n\n"
           f"The {len(rows)} rows above 0.80, by column:\n\n"
           "| column | rows above 0.80 | of them above 1.00 | levels | highest, unreduced |\n|---|---|---|---|---|\n"
           f"{t2}\n\n")
    return gc[:m.start(3)] + new + gc[m.end(3):]


# ---------------------------------------------------------------- HTML post-processing
SEV_CLASS = {"CRITICAL": "crit", "SIGNIFICANT": "sig", "MINOR": "minor"}


def pill(sev: str) -> str:
    return f'<span class="pill pill--{SEV_CLASS[sev]}">{sev.title()}</span>'


def widen_tables(soup: BeautifulSoup, min_cols: int = 8) -> None:
    """Tables of min_cols or more columns go on a landscape page (Edge clips wide tables silently). Row 1982 JM-V7
    (Codex V5-07): PDF 05 passes 11 - every switch to a landscape page and back ends a page early, so its tables of up to
    10 short columns stay on the portrait page (the FONT check and the page looks catch one that does not fit); PDF 01
    keeps 8 (its 10-column transfer table of long cells overflows a portrait page and Edge then shrinks the whole PDF)."""
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
                kicker: str = "", first: bool = False, wide: bool = False) -> None:
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
        cls = ("sec first" if first else "sec") + (" wide" if wide else "")
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
 @bottom-left{content:"Confidential: another firm's design";font:7.5pt var(--sans);color:#8A97A2;vertical-align:top;padding-top:3mm}
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
.cover table.facts th{width:34mm;text-align:left;font:600 7.5pt var(--mono);letter-spacing:.12em;text-transform:uppercase;
 color:var(--ink-faint);padding:2.2mm 0;border-bottom:1px solid var(--line);vertical-align:top;background:none}
.cover table.facts td{padding:2.2mm 0;border-bottom:1px solid var(--line);background:none}
.cover .spacer{flex:1}
.cover .office{font-size:7.6pt;color:var(--ink-soft);border-top:2px solid var(--accent);padding-top:3mm;margin-top:8mm}
.cover .office b{color:#373435}
.pkg{margin-top:8mm}
.pkg table{width:100%}
.toc{break-before:page}
.toc__title,.kicker{font-family:var(--mono);font-size:7.4pt;letter-spacing:.18em;text-transform:uppercase;color:var(--accent-ink);margin:0 0 3mm;font-weight:600}
.toc ol{list-style:none;margin:0;padding:0;column-count:2;column-gap:9mm}
.toc li{break-inside:avoid}
.toc li a{display:flex;align-items:baseline;gap:2mm;color:var(--ink);text-decoration:none;padding:.9mm 0}
.toc li .dots{flex:1;border-bottom:1px dotted var(--line-strong);transform:translateY(-1mm)}
.toc li .pg{font-variant-numeric:tabular-nums;min-width:7mm;text-align:right;color:var(--ink-soft)}
.toc li.l1{margin-top:2.6mm;font-weight:650;font-size:10pt}
.toc li.l2{padding-left:8mm;font-size:8.8pt}
.toc li.l3{padding-left:8mm;font-size:8.2pt;color:var(--ink-soft)}
.toc li.l3 a{padding:.35mm 0}
section.sec{break-before:auto;margin-top:11mm}
section.sec.first{break-before:page;margin-top:0}
h1{font-size:17pt;line-height:1.15;margin:0 0 5mm;color:var(--slate-deep);font-weight:700;letter-spacing:-.01em;
 padding-bottom:2.5mm;border-bottom:2px solid var(--accent);break-after:avoid}
h2{font-size:12pt;margin:7mm 0 2.5mm;color:var(--slate-deep);font-weight:650;break-after:avoid}
h3{font-size:10pt;margin:5mm 0 2mm;color:var(--slate);font-weight:650;break-after:avoid}
h4{font-size:9.4pt;margin:4mm 0 1.5mm;break-after:avoid}
.hnum{color:var(--accent-ink);font-variant-numeric:tabular-nums;margin-right:1.5mm}
p{margin:0 0 2.4mm}
.kicker{break-after:avoid}
ul,ol{margin:0 0 2.6mm;padding-left:5.5mm}
li{margin:0 0 1.2mm}
li::marker{color:var(--accent)}
strong{font-weight:650}
code{font-family:var(--mono);font-size:.86em;color:#2C3A47;background:#F4F2F0;padding:0 .25em;border-radius:2px}
table code{font-size:1em}
a{color:var(--accent-ink);text-decoration:none}
hr{border:0;border-top:1px solid var(--line);margin:4mm 0}
table{border-collapse:collapse;width:100%;margin:2mm 0 4mm;font-size:8pt;line-height:1.32}
thead{display:table-header-group}
tr,figure{break-inside:avoid}
li{break-inside:auto}
th{background:var(--slate);color:#fff;text-align:left;font-weight:600;font-size:7.4pt;padding:1.4mm 1.6mm;vertical-align:bottom}
td{padding:1.2mm 1.6mm;border-bottom:1px solid var(--line);vertical-align:top}
tbody tr:nth-child(even) td{background:#F8F7F5}
td.num,th.num,td[style*="right"],th[style*="right"]{text-align:right;font-variant-numeric:tabular-nums}
.wide{page:wide}
.wide figure.single img{max-height:125mm}
.wide table{font-size:7.5pt}
.wide th{font-size:7.5pt}
table.data{font-size:7.5pt}
table.data th{font-size:7.5pt}
table.data td{padding:.7mm 1.2mm}
.wide table.data{font-size:7.5pt}
pre.plain{font-family:var(--mono);font-size:7.5pt;line-height:1.36;white-space:pre-wrap;word-break:break-word;
 background:#F8F7F5;border-left:2px solid var(--line-strong);padding:2.5mm 3mm;margin:0 0 4mm}
.pill{display:inline-block;font:700 7.5pt var(--sans);letter-spacing:.06em;text-transform:uppercase;padding:.5mm 2mm;border-radius:8px;color:#fff;vertical-align:1px}
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
table.data td.num{padding-left:3mm}
td.long{min-width:42mm}
.wide td.long{min-width:48mm}
table.data th{padding-left:2.4mm}
table.data td.num + td.txt{padding-left:3.5mm}
h1,h2,h3,h4{break-inside:avoid}
p:has(> strong:only-child){break-after:avoid}
table.meta th{background:#EEF1F4;color:var(--slate-deep);width:24mm;font-size:7.5pt;letter-spacing:.06em;text-transform:uppercase;vertical-align:top;padding:1.2mm 1.6mm}
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
p.prose{font-size:8.4pt;line-height:1.42;margin:0 0 2.2mm;padding-left:3mm;border-left:2px solid var(--line-strong)}
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
    ap.add_argument("--job", help="the job number for the hand-off gate (default: NNNNN-NN at the start of --out's or --src's name)")
    ap.add_argument("--build", type=Path, help="working folder for the HTML (default: <out>/../_build)")
    # Row 1982 JM-BUILD-PREP: the complete review (gravity + lateral), its numbers by data
    ap.add_argument("--numbers", type=Path, help="the numbers file (v9-numbers.json): every value a rerun sets, by key")
    ap.add_argument("--lateral", type=Path, help="folder with LATERAL-SECTION.md (letter pieces + Section 6) and "
                                                 "LATERAL-FINDINGS.md (F32-F38, F15 rewritten)")
    ap.add_argument("--changes", type=Path, help="CHANGES-SINCE-V8.md: what we corrected in our model (Section 7)")
    ap.add_argument("--write-filled", type=Path, help="write the hand sources with their gravity sites filled to this "
                                                      "folder and stop (for the assembly's TR_SRC / README_SRC)")
    a = ap.parse_args()
    global NUMS
    NUMS = Numbers(a.numbers.resolve() if a.numbers else None)
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
    NUMS.pkg = src
    if a.write_filled:
        jobdir = Path(NUMS.d["job_dir"]) if "job_dir" in NUMS.d else a.numbers.resolve().parents[1]  # jobs/<job>/review/x.json
        a.write_filled.mkdir(parents=True, exist_ok=True)
        for rel in sorted({s["file"] for s in NUMS.d.get("gravity_sites", []) if not s["file"].startswith("builder:")}):
            t = NUMS.source(read(jobdir / rel), rel)
            (a.write_filled / Path(rel).name).write_text(t, encoding="utf-8", newline="")
            left = sorted(set(PH_RE.findall(t)))
            print(f"FILLED {rel} -> {a.write_filled / Path(rel).name}: {len(left)} key(s) without a value"
                  + (f": {', '.join(left)}" if left else ""))
        print(f"TYPED vs RULE: {len(NUMS.checked) - len(NUMS.disagree)} of {len(NUMS.checked)} typed values with a rule "
              f"equal {src}'s run outputs" + "".join(f"\n   {x}" for x in NUMS.disagree))
        if NUMS.disagree:
            print("REFUSED: typed numbers the package does not carry (--src must be the package assembled from the run; "
                  "clear 'value' to let the rule fill it, or correct it)")
            return 1
        return 0
    build = a.build or out.parent / "_build"
    if out.exists():
        if not a.replace:
            raise SystemExit(f"{out} exists: pass --replace to rebuild it")
        shutil.rmtree(out)
    print(f"SOURCE {src}", flush=True)
    build.mkdir(parents=True, exist_ok=True)
    out.mkdir(parents=True)

    # ---------- Row 1982 JM-BUILD-PREP: the lateral review (draft pieces, findings) and the changes page, filled by data
    lat_rows: list[dict] = []
    lat_ents: dict[str, str] = {}
    lat_sec: dict[str, str] = {}
    if a.lateral:
        lat_ents = lateral_entries(read(a.lateral / "LATERAL-FINDINGS.md"))   # filled where spliced
        lat_sec = md_sections(read(a.lateral / "LATERAL-SECTION.md"))
        v8rows = {r["id"]: r for r in csv.DictReader(open(src / "review/FINDINGS-REGISTER.csv", encoding="utf-8"))}
        pin_storeys = NUMS.d.get("register_pins", {})
        for fid, body in lat_ents.items():
            h = entry_head(body)
            if fid in v8rows:                       # a rewritten entry keeps its pin (F15: "Pin. Unchanged")
                x, y, st = v8rows[fid]["x_mm"], v8rows[fid]["y_mm"], v8rows[fid]["pin_storey"]
            else:
                m = re.search(r"\(x ([\d,.]+), y ([\d,.]+) mm\)", h["pin"])
                st = pin_storeys.get(fid)
                if not m or not st or st not in h["pin"]:
                    raise SystemExit(f"{fid}: its Pin line needs '(x N, y N mm)' and the numbers file's register_pins "
                                     f"storey ({st}) named in it: {h['pin']}")
                x, y = m[1].replace(",", ""), m[2].replace(",", "")
            lat_rows.append({**{k: scope(v) for k, v in h.items()}, "id": fid, "x_mm": x, "y_mm": y, "pin_storey": st,
                             "source": remap(h["source"], "05"), "title": scope(h["title"])})
        print(f"LATERAL: {len(lat_ents)} register entries from LATERAL-FINDINGS.md ({', '.join(lat_ents)}); "
              f"{len(lat_sec)} pieces of LATERAL-SECTION.md")

    # ---------- files that ship as files
    # the transfer storeys are the storeys SAFE results were shipped for
    TRANSFER_STOREYS = sorted(p.name for p in (src / "safe/results").iterdir() if p.is_dir())
    copies: list[tuple[Path, Path]] = []
    viewer = src / "01819-01 Renfrew - 3D model viewer.html"
    copies.append((viewer, out / "02 3D model viewer.html"))
    copies.append((src / "etabs/01819-01 Renfrew - ETABS model.e2k", out / "03 Models/ETABS/01819-01 Renfrew - ETABS model.e2k"))
    # Row 1982 JM-V7 (Codex V6-05): the as-written model, the source of Appendix D's quantities, under its own name
    copies.append((src / f"etabs/takeoff-source/{E2K_WRITTEN}", out / f"03 Models/ETABS/{E2K_WRITTEN}"))
    for f in sorted((src / "safe/models").glob("*.f2k")):
        st = f.stem
        dest = out / "03 Models/SAFE" if st in TRANSFER_STOREYS else out / "03 Models/SAFE/Storeys with no transfer"
        copies.append((f, dest / f.name))
        for ext in (".assumptions.txt", ".provenance.csv"):
            copies.append((src / "safe/models" / f"{st}{ext}", out / SAFE_NOTES / f"{st}{ext}"))
    for f in ("ETABS-base-reactions.csv", "ETABS-loads-onto-transfer-storeys.csv", "ETABS-solver-log-extract.txt"):
        copies.append((src / "etabs" / f, out / "04 Results/ETABS" / f))
    # Row 1982 JM-V8 (Codex V7-04, V5-03): each SAFE storey's run-log extract; safe-set.txt (F.2's full slab-use wording)
    for f in sorted((src / "safe/solver-log-extracts").glob("*.txt")):
        copies.append((f, out / "04 Results/SAFE/solver-log-extracts" / f.name))
    copies.append((src / "safe/safe-set.txt", out / SAFE_NOTES / "safe-set.txt"))
    for f in sorted((src / "safe/results").rglob("*")):
        if f.is_file():
            copies.append((f, out / "04 Results/SAFE" / f.relative_to(src / "safe/results")))
    copies.append((src / "answer-key.csv", out / "05 Appendix A - answer key.csv"))
    # Row 1982 JM-V6: the gravity table, the long SAFE tables and the viewer's pictures ship as files
    copies.append((src / "gravity-tower-columns.csv", out / GRAVCSV))
    for n in F_CSVS:
        copies.append((src / "safe" / n, out / SAFE_NOTES / n))
    for f in sorted((src / "review/pictures").iterdir()):
        copies.append((f, out / VIEWER_PICS / f.name))
    copies.append((src / "review/value-add/design-at-a-glance.csv", out / DGCSV))
    # Row 1982 JM-BUILD-PREP: the lateral model and its results, as the numbers file names them (paths relative to it)
    lat_ship = NUMS.d.get("lateral_ship", {}) if a.lateral else {}
    lat_files: dict[str, list[str]] = {}
    for folder, files in lat_ship.items():
        for f in files:
            p = (NUMS.path.parent / f).resolve()
            if not p.is_file():
                raise SystemExit(f"lateral_ship: {p} not found")
            copies.append((p, out / folder / p.name))
            lat_files.setdefault(folder, []).append(p.name)
    for s, d in copies:
        d.parent.mkdir(parents=True, exist_ok=True)
        if s == viewer:
            d.write_text(viewer_text(viewer, set(TRANSFER_STOREYS), lat_rows), encoding="utf-8", newline="")
            continue
        shutil.copy2(s, d)
        if sha(s) != sha(d):
            raise SystemExit(f"copy differs: {d}")
    print(f"COPIED {len(copies)} files: {len(copies) - 1} each SHA-256 equal to its source; the viewer with its file names mapped")
    n_tr = len(TRANSFER_STOREYS)
    n_other = len(list((src / "safe/models").glob("*.f2k"))) - n_tr
    for st in TRANSFER_STOREYS:
        if not (src / "safe/models" / f"{st}.f2k").exists():
            raise SystemExit(f"SAFE results for {st} but no model {st}.f2k")
    # Row 1982 JM-SAFE-FOLDER: F.1's model count names both folders the models are in (counted, not typed)
    REMAP_RE.insert(0, (rf"{n_tr + n_other} SAFE models \(models/<storey>\.f2k\), one per storey layout that differs;",
                        f"{n_tr + n_other} SAFE models, one per storey layout that differs: {n_tr} in 03 Models/SAFE/, "
                        f"the other {n_other} in 03 Models/SAFE/Storeys with no transfer/ (<storey>.f2k);\n "))
    # (a line break: F.1 is a monospace block, and a line over its width turns into a prose paragraph mid-block)
    n_key = len(list(csv.reader(open(src / "answer-key.csv", encoding="utf-8", newline="")))) - 1
    n_grav = len(list(csv.reader(open(src / "gravity-tower-columns.csv", encoding="utf-8", newline="")))) - 1

    # ---------- viewer pins and the register CSV
    vtxt = read(out / "02 3D model viewer.html")   # as laid out: with the lateral pins (JM-BUILD-PREP)
    pins = {m.group(1): m.group(2) for m in re.finditer(r'\{"id": "(F\d\d)", "storey": "([^"]+)"', vtxt)}
    reg = {r["id"]: {k: scope(v) if isinstance(v, str) else v for k, v in r.items()}
           for r in csv.DictReader(open(src / "review/FINDINGS-REGISTER.csv", encoding="utf-8"))}
    for r in lat_rows:
        reg[r["id"]] = {**reg.get(r["id"], {}), **{k: r[k] for k in ("id", "severity", "title", "storey", "grid",
                                                                    "sheets", "x_mm", "y_mm", "pin_storey")}}
    sev_n = collections.Counter(r["severity"] for r in reg.values())
    n_reg = len(reg)
    NUMS.computed.update({"ISSUE:n_total": str(n_reg), "ISSUE:n_crit": str(sev_n["CRITICAL"]),
                          "ISSUE:n_sig": str(sev_n["SIGNIFICANT"]), "ISSUE:n_min": str(sev_n["MINOR"])})
    for fid, r in reg.items():
        if pins.get(fid) != r["pin_storey"]:
            raise SystemExit(f"{fid}: viewer pin storey {pins.get(fid)} != register pin_storey {r['pin_storey']}")
    print(f"PINS: {len(reg)} of {len(reg)} register rows have a viewer pin on the register's storey")

    # ===================================================================== PDF 01
    sources01: list[str] = []
    letter_md = NUMS.source(read(src / "review/REVIEW-LETTER.md"), "review/REVIEW-LETTER.md")
    if lat_sec:
        letter_md = NUMS.fill(lateral_letter(letter_md, lat_sec, sev_n))
    meta_line = letter_md.splitlines()[2]
    PREV = NUMS.value("ISSUE:first_issue_date") or "{{ISSUE:first_issue_date}}"   # the issue JM has (v8)
    mm = re.fullmatch(r"\*\*For:\*\* (.+?) \| \*\*From:\*\* KOR Structural \| \*\*Date:\*\* (\d{4}-\d\d-\d\d|\{\{[^{}]+\}\}) \| "
                      r"\*\*Status:\*\* (.+?) \| Confidential: another firm's design", meta_line)
    if not mm:
        raise SystemExit(f"letter meta line not in the expected form: {meta_line}")
    for_who, letter_date, status = mm.groups()
    status = scope(status)   # Row 1982 JM-NO-PHASE2 / JM-BUILD-PREP: the complete review
    letter = md_sections(letter_md)
    enclosed_old = letter["Enclosed"]
    enclosed_new = (
        "## Enclosed\n\n"
        "- **This document:** the findings register (Section 3; each finding with sheet, grid, source, viewer pin and "
        "pictures), the gravity load path storey by storey (Section 4) and the transfer slab results (Section 5)"
        + ("; the lateral and seismic review (Section 6); and what we corrected in our own model since our issue of " + PREV + " "
           "(Section 7).\n" if lat_sec else ".\n") +
        f"- **`02 3D model viewer.html`:** every one of the {len(reg)} findings is a pin at its storey and location; a set-wide "
        "finding stands at a representative location, named in its register entry.\n"
        "- **`03 Models` and `04 Results`:** our ETABS and SAFE models and their result files"
        + (": the gravity models, and the lateral model analysed, " + ", ".join(
            f"`03 Models/ETABS/{n}`" for n in lat_files.get("03 Models/ETABS", [])) + " (ETABS's own save), with its "
           "results in `04 Results/ETABS lateral/` (modes, base shears, drifts, pier and spandrel forces) and our "
           "resistance tables and code yardsticks in `04 Results/Lateral checks/`.\n" if lat_sec else ".\n") +
        "- **`05 Appendices.pdf`:** the answer key, every value in the set (Appendix A, with "
        "`05 Appendix A - answer key.csv`); the information requests to WHM (Appendix B); the gravity check "
        "(Appendix C); and the model basis and SAFE notes (Appendices D to H).\n")
    # Row 1982 JM-V6 (look L16): Appendix B has one name everywhere, APPX["B"]
    enclosed_new = enclosed_new.replace("the information requests to WHM (Appendix B)", "the early comment list to WHM (Appendix B)")
    letter_body = "\n".join(v for k, v in letter.items() if k not in ("_head", "Enclosed")) + "\n" + enclosed_new
    sources01.append("\n".join(v for k, v in letter.items() if k not in ("_head", "Enclosed")))
    letter_body = remap(letter_body)

    readme = NUMS.source(read(src / "README.md"), "PACKAGE-README.md")
    rs = md_sections(readme)
    readme_head = strip_h1(rs["_head"])
    for k in ("_head", "What is in this folder", "What was checked, and how", "ETABS and SAFE results",
              "Assumptions you must confirm", "What this package does not do"):
        if k not in rs:
            raise SystemExit(f"README section missing: {k}")
    # Row 1982 JM-V9-BUILDER (14 Nov): the sign example is the package's own - the DEAD of the 'LOADS FROM ABOVE ON NO SLAB'
    # line of each storey's assumptions file that has one and is downward (v8 typed 'DEAD -75.1 kN' / 'DEAD -408.7 kN',
    # gone since r38). COVERS: which files have the line, and the value printed. DOES NOT: a storey whose line holds
    # uplift only (no example is drawn from it); same-class fault it would miss: the README's own copy of the sentence.
    nos = []
    for f in sorted((src / "safe" / "models").glob("*.assumptions.txt")):
        m = re.search(r"LOADS FROM ABOVE ON NO SLAB:[^\n]*?of the totals above: DEAD (-[\d.]+) kN", read(f))
        if m:
            nos.append((f.name, m[1]))
    sign_eg = ("" if not nos else
               ", so on the line 'LOADS FROM ABOVE ON NO SLAB' of " + " and ".join(f"`{n}`" for n, _ in nos) + ", "
               + " and ".join(f"'DEAD {v} kN'" for _, v in nos) + (" is a load" if len(nos) == 1 else " are loads")
               + " of " + " and ".join(v[1:] for _, v in nos) + " kN downward, not uplift")
    pkg_md = (
        "## What is in this package\n\n"
        "| File | What it is |\n|---|---|\n"
        "| `01 Review letter and findings.pdf` | This document: the review letter, this introduction, the findings "
        f"register ({len(reg)} entries, ranked, each with its sheets, grid, viewer pin and pictures), the gravity load path "
        "storey by storey, and the transfer slab results"
        + ("; the lateral and seismic review (Section 6) and what we corrected in our own model since our issue of " + PREV + " "
           "(Section 7). |\n" if lat_sec else ". |\n") +
        "| `02 3D model viewer.html` | One offline page (open it in a browser): the ETABS model of WHM's set as drawn, "
        f"storey by storey and in 3D, with its 20 grid lines, the {len(reg)} findings and the assumptions of Appendix E as "
        "numbered pins (click one to read it and where it is stated; a finding's pictures open from "
        f"`{VIEWER_PICS}/`). Geometry only. |\n"
        f"| `{VIEWER_PICS}/` | The register's pictures, as files, for the viewer's picture links (the same pictures are in "
        "Section 3). |\n"
        "| `03 Models/ETABS/` | Two ETABS files. `01819-01 Renfrew - ETABS model.e2k` is ETABS's save of the model it "
        "analysed ({{G:ETABS.members_analysed}} members): the file to rerun, which reproduces the ETABS results. "
        f"`{E2K_WRITTEN}` is the model as we wrote it, the file ETABS was given ({{{{G:ETABS.members_written}}}} members, with every one of them "
        "in the save): it is enclosed only as the source of the quantities of Appendix D. Every member's "
        "f'c is from the set (Appendix E, \"Materials\"). |\n"
        f"| `03 Models/SAFE/` | The SAFE models: one for each of the {n_tr} transfer storeys (`<storey>.f2k`, SAFE's text "
        f"file, which SAFE 22 imports to open the model). The other {n_other} storey layouts, which carry no load from above "
        "of that kind, are in `Storeys with no transfer/`. Which storeys each model stands for: Appendix F.2. |\n"
        f"| `{SAFE_NOTES}/` | Each model's assumptions (`<storey>.assumptions.txt`: its loads carry SAFE's sign, z up, "
        f"negative = down{sign_eg}) and the source of every "
        "slab use and load (`<storey>.provenance.csv`; thicknesses, f'c and geometry are not in it: they are the ETABS "
        "model's, from WHM's sheets, Appendix E, \"Materials\"); the tables of Appendix F.2 to F.5 as files: "
        f"{', '.join(f'`{n}`' for n in ('safe-set.txt',) + F_CSVS)}. |\n"
        "| `04 Results/ETABS/` | The ETABS base reactions and the loads it passes down onto the transfer storeys. |\n"
        + ("| `03 Models/ETABS/` (lateral) | " + ", ".join(f"`{n}`" for n in lat_files.get("03 Models/ETABS", []))
           + ": the lateral model of Section 6 as ETABS saved it after the run, the file to rerun for the lateral "
           "results (the gravity model with the lateral properties, mass and load cases of Section 6.1 added). |\n"
           "| `04 Results/ETABS lateral/` | The lateral run's tables: " + ", ".join(
               f"`{n}`" for n in lat_files.get("04 Results/ETABS lateral", [])) + ". |\n"
           "| `04 Results/Lateral checks/` | Our resistance tables (coupling beams, walls, core pad) and code yardsticks "
           "behind Section 6: " + ", ".join(f"`{n}`" for n in lat_files.get("04 Results/Lateral checks", []))
           + ". |\n" if lat_sec else "") +
        "| `04 Results/SAFE/<storey>/` | SAFE's deflection, reaction and punching tables of each transfer storey. |\n"
        "| `05 Appendices.pdf` | A: answer key. B: early comment list to WHM. C: tower column gravity hand check "
        "(preliminary). D: the design at a glance. E: model basis, ETABS and SAFE results, and the assumptions "
        "you must confirm. F: SAFE storey notes. G: the P1/GF supports with no reaction. H: SAFE plans of the "
        "loads not applied at P2 and P3. |\n"
        f"| `05 Appendix A - answer key.csv` | Every value the set states, {n_key:,} rows, each with its sheet and page "
        "and whether it was read off the page, inferred by a stated rule, or is missing from the set. |\n"
        f"| `{GRAVCSV}` | The tower column hand check of Appendix C, {n_grav:,} rows, one per column per level "
        "(Section C.14 of Appendix C says what each column of the file holds). |\n"
        f"| `{DGCSV}` | The design at a glance per storey, every column (Appendix D.4 gives its main columns). |\n")
    pkg_md = NUMS.source(pkg_md, "builder:pkg_md")   # its model counts and loads are gravity sites too (JM-BUILD-PREP)
    intro_md = (readme_head + "\n\n" + pkg_md + "\n" + rs["What was checked, and how"] + "\n\n"
                + rs["What this package does not do"])
    sources01.append(readme_head + rs["What was checked, and how"] + rs["What this package does not do"])
    intro_md = remap(intro_md)

    reg_md = NUMS.source(read(src / "review/FINDINGS-REGISTER.md"), "review/FINDINGS-REGISTER.md")
    if lat_ents:
        # Row 1982 JM-BUILD-PREP: F15 rewritten in place; F32-F38 after the last SIGNIFICANT entry (F31), in the summary
        # table and as entries; the counts are counted from the merged register, not typed
        rowmd = lambda r: (f"| [{r['id']}](#{r['id'].lower()}) | {r['severity']} | {r['title']} | {r['storey']} | "
                           f"{r['grid']} | {r['sheets']} |")
        new = [r for r in lat_rows if r["id"] not in v8rows]
        for r in lat_rows:
            if r["id"] in v8rows:
                reg_md = splice(reg_md, rf"^\| \[{r['id']}\]\(#{r['id'].lower()}\) \|[^\n]*$", rowmd(r), f"{r['id']} row",
                                regex=True)
                reg_md = splice(reg_md, rf"^## {r['id']}\n.*?(?=^## )", f"## {r['id']}\n\n{lat_ents[r['id']]}\n\n",
                                f"{r['id']} entry", regex=True)
        f31 = re.search(r"^\| \[F31\]\(#f31\) \|.*$", reg_md, re.M)[0]
        reg_md = splice(reg_md, f31 + "\n", f31 + "\n" + "".join(rowmd(r) + "\n" for r in new), "summary rows")
        reg_md = splice(reg_md, "\n## F06\n", "\n" + "".join(f"## {r['id']}\n\n{lat_ents[r['id']]}\n\n" for r in new)
                        + "## F06\n", "entries after F31")
        reg_md = splice(reg_md, r"\*\*\d+ entries: \d+ CRITICAL, \d+ SIGNIFICANT, \d+ MINOR\.\*\* Ids run F01 to F\d\d",
                        f"**{n_reg} entries: {sev_n['CRITICAL']} CRITICAL, {sev_n['SIGNIFICANT']} SIGNIFICANT, "
                        f"{sev_n['MINOR']} MINOR.** Ids run F01 to {max(reg)}", "register count line", regex=True)
        reg_md = splice(reg_md, r"every one of the \d+ entries", f"every one of the {n_reg} entries", "register intro count",
                        regex=True)
        reg_md = NUMS.fill(reg_md)
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

    lp_md = NUMS.source(read(src / "review/LOAD-PATH-SUMMARY.md"), "review/LOAD-PATH-SUMMARY.md")
    tr_md = NUMS.source(read(src / "review/TRANSFER-RESULTS.md"), "review/TRANSFER-RESULTS.md")
    sources01 += [strip_h1(lp_md), strip_h1(tr_md)]

    def prep(h: str, min_cols: int = 8) -> str:
        # Row 1982 JM-V7: 8 in PDF 01 (its 10-column transfer table of long cells needs the landscape page); 11 in 05
        s = BeautifulSoup(h, "html.parser")
        widen_tables(s, min_cols)
        return str(s)

    d1 = Doc("d1")
    d1.section("1", "Review letter", prep(pandoc(letter_body, "letter")), "s1", first=True)
    d1.section("2", "About this package", prep(pandoc(intro_md, "intro")), "s2")
    d1.section("3", "Findings register", reg_html, "s3")
    # Row 1982 JM-V8 (Codex V7-03): the load-path summary starts on a fresh landscape page (a named page breaks before it)
    d1.section("4", "Gravity load path, storey by storey", prep(pandoc(remap(strip_h1(lp_md)), "lp")), "s4", wide=True)
    d1.section("5", "Transfer slabs: what our SAFE runs give, and what they do not",
               prep(pandoc(remap(strip_h1(tr_md)), "tr")), "s5")
    # Row 1982 JM-BUILD-PREP: Section 6, the lateral and seismic review; Section 7, what we corrected in our own model
    if lat_sec:
        s6 = NUMS.fill(section6(lat_sec))
        sources01.append(s6)
        d1.section("6", "Lateral and seismic", prep(pandoc(remap(s6), "lat")), "s6", wide=True)
    if a.changes:
        ch = NUMS.fill(strip_h1(read(a.changes)))
        sources01.append(ch)
        d1.section("7", f"Changes since our issue of {PREV}: what we corrected in our own model",
                   prep(pandoc(remap(ch), "chg")), "s7")
    banner = ""
    if NUMS.stand_in:
        banner = ('<div class="box box--warn"><p><strong>STAND-IN NUMBERS: NOT FOR ISSUE.</strong> Every number a rerun '
                  'sets is a stand-in value of the numbers file.</p></div>')
    cov1 = cover(
        f"Structural peer review \u2013 {PROJECT}, Vancouver",
        f"Kor Structural \u00b7 Project {JOB} \u00b7 " + ("Gravity and lateral" if lat_sec else "Gravity"),
        "Review letter, findings register, load path and transfer slab results"
        + (", the lateral and seismic review, and the changes to our model since our issue of " + PREV if lat_sec else "")
        + ". Peer review of WHM Structural's drawings, <strong>Re-issued for Tender 2026-09-09</strong> (42 sheets, "
        "S001 to S811).",
        [("For", html.escape(for_who)), ("From", "Kor Structural"), ("Date", letter_date),
         ("Status", html.escape(status[:1].upper() + status[1:])), ("Confidential", "Another firm's design"),
         ("Start here", "Section 1, the review letter; then the findings register, Section 3. "
                        "Open <code>02 3D model viewer.html</code> beside it: each finding is a pin.")], banner)
    pdf1 = out / "01 Review letter and findings.pdf"
    build_pdf(f"Structural peer review - {PROJECT} ({JOB})", "01 Review letter and findings", cov1, d1, build,
              "01-letter", pdf1, "Review letter, findings register, load path, transfer slab results")

    # ===================================================================== PDF 05
    sources05: list[str] = []
    d5 = Doc("d5")
    ak = read(src / "ANSWER-KEY.md")
    sources05.append(strip_h1(ak))
    d5.section("A", APPX["A"], prep(pandoc(remap(strip_h1(ak), "05"), "ak"), 11), "aA", kicker="Appendix A", first=True)
    ec = read(src / "EARLY-COMMENTS.md")
    sources05.append(strip_h1(ec))
    d5.section("B", APPX["B"], prep(pandoc(remap(strip_h1(ec), "05"), "ec"), 11), "aB", kicker="Appendix B")

    gc = read(src / "GRAVITY-CHECK.md")
    if not gc.startswith("# PRELIMINARY - hand check, not the model\n"):
        raise SystemExit("gravity check banner changed")
    gc = summarise_over_080(gc, src / "gravity-tower-columns.csv")
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
    # Row 1982 JM-V6 (Codex V5-03): the 672-row table at a legible size is about 60 pages; it ships as the file GRAVCSV
    # and the PDF says what each of its columns holds and gives its notes (the rows over 1.00 and over 0.80 are tabled
    # above, in the check's own text).
    col_desc = "".join(f"<tr><td><code>{html.escape(c)}</code></td><td>{html.escape(label(c))}</td></tr>"
                       for c in t1 + [c for c in t2 if c not in t1])
    gc_html += (
        '<h2 id="gc-every-column-level">Every column at every level (the data file)</h2>'
        f"<p>The data of this check, {len(grows)} rows, one per column per level, is the file "
        f"<code>{GRAVCSV}</code> beside this PDF; a spreadsheet opens it. Its columns, in order:</p>"
        '<table class="data"><thead><tr><th>Column in the file</th><th>What it holds (unit)</th></tr></thead><tbody>'
        + col_desc + "</tbody></table>"
        f"<p>The <code>notes</code> column carries one of {len(notes)} distinct notes:</p>"
        "<table class=\"data\"><thead><tr><th>Note</th><th>Text</th></tr></thead><tbody>"
        + "".join(f"<tr><td>{note_no[n]}</td><td>{html.escape(n)}</td></tr>" for n in notes) + "</tbody></table>")
    sources05.append("\n".join(notes))
    d5.section("C", APPX["C"], prep(gc_html, 11), "aC", kicker="Appendix C")

    dg = read(src / "review/value-add/DESIGN-AT-A-GLANCE.md")
    sources05.append(strip_h1(dg))
    dg_soup = BeautifulSoup(pandoc(remap(strip_h1(dg), "05"), "dg"), "html.parser")
    img = dg_soup.find("img")
    fig = BeautifulSoup(
        f'<figure class="single"><img src="{(src / "review/value-add/design-at-a-glance.png").as_uri()}" '
        f'alt="{html.escape(img["alt"])}"><figcaption><b>Figure D1.</b> {html.escape(img["alt"])}</figcaption></figure>',
        "html.parser").figure
    (img.find_parent("figure") or img.find_parent("p") or img).replace_with(fig)
    # Row 1982 JM-V7 (Codex V5-03, V5-07, V6-05): the 17-column table ships as the file DGCSV; the PDF gives its main
    # columns on the portrait page (no landscape page, so no page left short before and after it)
    dgc = src / "review/value-add/design-at-a-glance.csv"
    dg_cols = ["storey", "elevation_m", "slab_area_m2", "avg_slab_mm", "slab_m3", "wall_m3", "column_m3", "concrete_m3",
               "concrete_m3_per_m2"]
    dg_rows = list(csv.DictReader(open(dgc, encoding="utf-8", newline="")))
    if not set(dg_cols) <= set(dg_rows[0]):
        raise SystemExit("design-at-a-glance.csv columns changed")
    dg_html = str(dg_soup) + ('<h2 id="dg-per-storey">Per storey, as computed</h2><p>The per-storey figures behind Sections '
                              f'1 and 2 and the figure, one row per storey: its main columns. All {len(dg_rows[0])} columns '
                              '(with the counts of slabs, walls and columns, and wall and column plan area and % of floor) '
                              f'are in the file <code>{DGCSV}</code> beside this PDF.</p>'
                              + csv_table(dgc, dg_cols, rename={"avg_slab_mm": "average slab (mm)",
                                                                "concrete_m3_per_m2": "concrete (m³ per m²)"}))
    sources05.append("\n".join(r[c] for r in dg_rows for c in dg_cols))
    d5.section("D", APPX["D"], prep(dg_html, 11), "aD", kicker="Appendix D")

    e_md = (rs["ETABS and SAFE results"] + "\n\n" + rs["Assumptions you must confirm"])
    sources05.append(e_md)
    d5.section("E", APPX["E"], prep(pandoc(remap(e_md, "05"), "e"), 11), "aE", kicker="Appendix E")

    f_parts = []
    # Row 1982 JM-V6: F.n numbering (as A.1, C.1); each table's file is in 03 Models/SAFE/; the rule behind F.3's
    # sub-5 mm rows and the words of F.5 are stated, F.5's repeated reasons are numbered once (as Appendix C's notes)
    f_intro = {
        "F3": "<p>One row per member of the storey above that starts on the storey's slab (the file "
              "<code>03 Models/SAFE/Notes/safe-transfers.csv</code>). A wall is listed when any part of its line has no wall of "
              "the storey below under it, with no tolerance; the rows whose part is under 5 mm long are drafting tolerance "
              "between the two storeys' wall lines, not walls with nothing under them, and none is a finding "
              "(Section 5 of 01 Review letter and findings, Notes).</p>",
        "F4": "<p>Elevations in mm, P3 = 0 (the file <code>03 Models/SAFE/Notes/safe-levels.csv</code>).</p>",
        "F5": "<p>Every load row S101 and S111 state (the file <code>03 Models/SAFE/Notes/safe-loads.csv</code>). Every row was "
              "read off the sheet. Status: <b>mapped</b> = applied to the SAFE slabs of the use it names; <b>listed</b> = "
              "recorded and not applied, for the reason given. The reasons repeat, so each is given once, numbered, "
              "under the table. The line of the sheet each row was read from is the file's <code>note</code> column.</p>",
    }
    for code, title, path, kind in (
            ("F1", "SAFE results by transfer storey", src / "safe/SAFE-RESULTS.txt", "pre"),
            ("F2", "SAFE models and the storeys they stand for", src / "safe/safe-set.txt", "pre"),
            ("F3", "Members standing on a transfer storey, and what is under them", src / "safe/safe-transfers.csv", "csv"),
            ("F4", "Storey levels and the SAFE model of each", src / "safe/safe-levels.csv", "csv"),
            ("F5", "Load rows of the set: applied to the SAFE slabs, or listed, and why", src / "safe/safe-loads.csv", "csv")):
        txt = read(path)
        head = f'<h2 id="f-{code.lower()}">{code[0]}.{code[1:]}\u2002{html.escape(title)}</h2>' + f_intro.get(code, "")
        if code == "F2":
            # Row 1982 JM-V8 (Codex V5-03): the slab-use lines (one per storey, each listing every room label and sheet it
            # was read from) filled four pages of monospace. The PDF gives each storey's use, basis and loads as a table,
            # consecutive storeys with the same use as one row; the full wording ships as 03 Models/SAFE/Notes/safe-set.txt.
            # Every storey line is counted into the table, so a dropped line is a build fault.
            lines = txt.split("\n")
            k = next(i for i, ln in enumerate(lines) if ln.startswith("slab use from the plans' labels"))
            top, rule_lines, uses = lines[:k + 1], [], []
            for ln in lines[k + 1:]:
                m = re.match(r"^  storey (.+?): (.*)$", ln)
                if not m:
                    if ln.strip():
                        rule_lines.append(ln)
                    continue
                hd, _, tail = m[2].partition(" - ")
                mm = re.match(r"^(.*) \(([^()]*)\)$", hd)
                use, basis = mm.groups() if mm else (hd, "stated")
                lv = re.search(re.escape(use) + r" \(live ([\d.]+) kPa, SDL ([\d.]+) kPa\)", tail)
                uses.append((m[1], use, basis, lv[1] if lv else "-", lv[2] if lv else "-"))
            if len(uses) != sum(1 for ln in lines if ln.startswith("  storey ")):
                raise SystemExit("safe-set.txt: a storey's slab-use line was not read")
            rows2 = []
            for st, *rest in uses:
                if rows2 and rows2[-1][1:] == rest:
                    rows2[-1][0].append(st)
                else:
                    rows2.append([[st], *rest])
            trs = "".join(
                f'<tr><td class="txt nw">{html.escape(s[0] if len(s) == 1 else f"{s[0]} to {s[-1]} ({len(s)} storeys)")}'
                f'</td><td class="txt">{html.escape(u)}</td><td class="txt">{html.escape(b)}</td>'
                f'<td class="num">{lv}</td><td class="num">{sd}</td></tr>' for s, u, b, lv, sd in rows2)
            body = (pre_block(remap("\n".join(top), "05"), prose_from=170)
                    + "".join(f'<p class="prose">{html.escape(remap(ln.strip(), "05"))}</p>' for ln in rule_lines)
                    + '<table class="data"><thead><tr><th>storey</th><th>slab use taken</th><th>basis</th>'
                    '<th class="num">live (kPa)</th><th class="num">SDL (kPa)</th></tr></thead><tbody>' + trs
                    + "</tbody></table>"
                    f"<p>{len(uses)} storeys in all. The room labels and sheet pages each storey's use was read from are "
                    "in the file <code>03 Models/SAFE/Notes/safe-set.txt</code>, one line per storey.</p>")
            sources05.append("\n".join(top + rule_lines) + "\n" + "\n".join(
                f"{' '.join(s)} {u} {b} {lv} {sd}" for s, u, b, lv, sd in rows2))
            f_parts.append(f'<div class="wide">{head}{body}</div>')
            continue
        if kind == "pre":
            sources05.append(txt)
            # fixed-width text keeps its columns only if its lines fit: a landscape page holds them
            f_parts.append(f'<div class="wide">{head}{pre_block(remap(txt, "05"), prose_from=170)}</div>')
            continue
        if code == "F5":
            rows5 = list(csv.DictReader(open(path, encoding="utf-8", newline="")))
            if {r["read_as"] for r in rows5} != {"read"} or {r["section"] for r in rows5} != {"loads"}:
                raise SystemExit("safe-loads.csv: read_as or section is no longer one value")
            whys = []
            for r in rows5:
                if r["why"] not in whys:
                    whys.append(r["why"])
            wno = {w: f"R{i + 1}" for i, w in enumerate(whys)}
            # Row 1982 JM-V7 (Codex V5-03): the 'note' column (the sheet line each row was read from, often longer than the
            # row) stays in the file; the PDF table keeps what was read, where, and what was done with it
            cols5 = [c for c in rows5[0] if c not in ("section", "read_as", "note") and any(r[c].strip() for r in rows5)]
            body = (csv_table(path, cols5, rename={"why": "reason"}, extra={"why": lambda r: wno[r["why"]]})
                    + "<table class=\"data\"><thead><tr><th>Reason</th><th>Text</th></tr></thead><tbody>"
                    + "".join(f"<tr><td>{wno[w]}</td><td>{html.escape(w)}</td></tr>" for w in whys) + "</tbody></table>")
            sources05.append("\n".join(" ".join(r[c] for c in cols5 if c != "why") for r in rows5) + "\n" + "\n".join(whys))
            f_parts.append(f'<div class="wide">{head}{body}</div>')
            continue
        # Row 1982 JM-V7 (Codex V5-03): F.3 (238 rows) and F.4's sheet titles were full-row tables over many pages.
        # F.3 is given per storey with every member named (each row's position stays in the file); F.4 gives each
        # storey's sheet numbers (the full sheet titles stay in the file). Every row of the file is counted into its
        # summary, so a dropped row is a build fault.
        rows = list(csv.DictReader(open(path, encoding="utf-8", newline="")))
        if code == "F3":
            if {r["finding"] for r in rows} != {"nothing under it"} or {r["kind"] for r in rows} - {"COLUMN", "PANEL"}:
                raise SystemExit("safe-transfers.csv: finding or kind is no longer what F.3's summary states")
            per = {}
            for r in rows:
                per.setdefault((r["storey"], r["storey_above"]), {"COLUMN": [], "PANEL": []})[r["kind"]].append(r["member"])
            if sum(len(v["COLUMN"]) + len(v["PANEL"]) for v in per.values()) != len(rows):
                raise SystemExit("F.3 summary does not count every row of safe-transfers.csv")
            trs = "".join(
                f'<tr><td class="txt nw">{html.escape(s)}</td><td class="txt nw">{html.escape(a)}</td>'
                f'<td class="num">{len(v["COLUMN"])}</td><td class="txt">{html.escape(", ".join(v["COLUMN"]) or "-")}</td>'
                f'<td class="num">{len(v["PANEL"])}</td><td class="txt">{html.escape(", ".join(v["PANEL"]) or "-")}</td></tr>'
                for (s, a), v in per.items())
            body = ('<table class="data"><thead><tr><th>storey</th><th>members of</th><th class="num">columns</th>'
                    '<th>column members</th><th class="num">walls</th><th>wall members</th></tr></thead><tbody>'
                    + trs + "</tbody></table>"
                    f"<p>{len(rows)} rows in all: {sum(len(v['COLUMN']) for v in per.values())} columns and "
                    f"{sum(len(v['PANEL']) for v in per.values())} walls, each marked 'nothing under it'; each row of the "
                    "file also gives where the member stands, in mm, and what the storey below has there.</p>")
            sources05.append("\n".join(f"{s} {a} {len(v['COLUMN'])} {' '.join(v['COLUMN'])} {len(v['PANEL'])} "
                                       f"{' '.join(v['PANEL'])}" for (s, a), v in per.items()))
            f_parts.append(head + body)
            continue
        if code == "F4":
            show = lambda r: "; ".join(dict.fromkeys(re.findall(r"\bS\d{3}\b", r["sheets"])))   # each sheet once, in order
            if any(not re.findall(r"\bS\d{3}\b", r["sheets"]) for r in rows):
                raise SystemExit("safe-levels.csv: a row names no S-sheet number")
            body = csv_table(path, ["storey", "elevation", "status", "model", "sheets", "notes"],
                             rename={"elevation": "elevation (mm)", "sheets": "sheets (numbers; titles in the file)"},
                             extra={"sheets": show})
            sources05.append("\n".join(" ".join([r["storey"], r["elevation"], r["status"], r["model"], show(r), r["notes"]])
                                       for r in rows))
            f_parts.append(head + body)
            continue
        sources05.append(csv_cells(path))
        body = csv_table(path, rename={"elevation": "elevation (mm)"} if code == "F4" else None)
        f_parts.append(f'<div class="wide">{head}{body}</div>' if len(txt.split("\n", 1)[0].split(",")) >= 8 else head + body)
    # Row 1982 JM-V8 (Codex V7-04): F.6, the run checks behind PROVEN and the letter's run-check counts, one row per model,
    # read from the shipped extracts (each quotes its run's log lines with line numbers). Refuses unless there is one
    # extract per transfer storey and each gives a sha, the cases finished and the negative eigenvalues.
    def run_row(p: Path, label: str) -> tuple[str, str, str, str]:
        t = read(p)
        sh = re.search(r"SHA-256 ([0-9a-f]{64})", t)
        fin = re.search(r"load cases finished: +(\d+ of \d+)", t)
        neg = re.search(r"negative eigenvalues: +(\d+)", t)
        if not (sh and fin and neg):
            raise SystemExit(f"{p.name}: no sha, cases finished or negative eigenvalues line")
        return label, sh[1][:16], fin[1], neg[1]
    ex = {p.stem: p for p in (src / "safe/solver-log-extracts").glob("*.txt")}
    if sorted(ex) != TRANSFER_STOREYS:
        raise SystemExit(f"solver-log extracts {sorted(ex)} != transfer storeys {TRANSFER_STOREYS}")
    order = [ln.split(".f2k")[0].strip() for ln in read(src / "safe/safe-set.txt").split("\n") if ".f2k stands for" in ln]
    runs = [run_row(src / "etabs/ETABS-solver-log-extract.txt", "ETABS, the whole building (the model as written)")]
    runs += [run_row(ex[st], f"SAFE, {st.replace('_', ' ').replace('P1 GF', 'P1/GF')}") for st in order if st in ex]
    f6 = ('<h2 id="f-f6">F.6 Run checks: the solver logs behind them</h2>'
          "<p>One row per analysed model: the first 16 characters of the SHA-256 of the model file the run read, the load "
          "cases the run finished, and the negative eigenvalues its solver reported (0 = stable). Each row's log lines are "
          "quoted, with their line numbers, in the files <code>04 Results/ETABS/ETABS-solver-log-extract.txt</code> and "
          "<code>04 Results/SAFE/solver-log-extracts/&lt;storey&gt;.txt</code>; each SAFE model is the file of that name in "
          "<code>03 Models/SAFE/</code>.</p>"
          '<table class="data"><thead><tr><th>model</th><th>SHA-256 (first 16)</th><th>load cases finished</th>'
          '<th class="num">negative eigenvalues</th></tr></thead><tbody>'
          + "".join(f'<tr><td class="txt">{html.escape(a)}</td><td class="txt nw"><code>{b}</code></td><td class="txt">{c}'
                    f'</td><td class="num">{d}</td></tr>' for a, b, c, d in runs) + "</tbody></table>")
    sources05.append("\n".join(" ".join(r) for r in runs))
    f_parts.append(f6)
    print(f"RUN CHECKS F.6: {len(runs)} models ({len(runs) - 1} SAFE of {len(TRANSFER_STOREYS)} transfer storeys + ETABS)")
    d5.section("F", APPX["F"], "".join(f_parts), "aF", kicker="Appendix F", number_h2=False, wide=True)

    pg = read(src / "safe/P1_GF-unattached-supports.md")
    sources05.append(strip_h1(pg))
    # Row 1982 JM-V7 (Codex V5-07): F is landscape (its fixed-width tables), so G and H follow it on landscape pages: no
    # switch back to portrait, which would leave F's last page short
    d5.section("G", APPX["G"], prep(pandoc(remap(strip_h1(pg), "05"), "pg"), 11), "aG", kicker="Appendix G", wide=True)

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
    d5.section("H", APPX["H"], h_html, "aH", kicker="Appendix H", number_h2=False, wide=True)

    cov5 = cover(
        f"Appendices \u2013 {PROJECT}, Vancouver",
        f"Kor Structural \u00b7 Project {JOB} \u00b7 Structural peer review",
        "Reference material behind the review letter and findings register: the answer key, the early comments to "
        "WHM, the column hand check, the model basis and the SAFE notes.",
        [("Appendices", "<br>".join(f"<b>{k}</b>\u2002{html.escape(v)}" for k, v in APPX.items())),
         ("Date", letter_date), ("Confidential", "Another firm's design")], banner)
    pdf5 = out / "05 Appendices.pdf"
    build_pdf(f"Appendices - Structural peer review - {PROJECT} ({JOB})", "05 Appendices", cov5, d5, build,
              "05-appendices", pdf5, "Appendices A-H to the structural peer review")

    # ===================================================================== checks on what was written
    faults = []
    # Row 1982 JM-V6 (Codex V5-04, V5-05): once both PDFs exist, every link and file a pin names, from the layout root
    nlinks, badlinks = viewer_links(read(out / "02 3D model viewer.html"), out)
    print(f"VIEWER LINKS: {nlinks - len(badlinks)} of {nlinks} picture links, files and appendices named by the pins "
          "resolve from the laid-out folder")
    faults += [f"viewer: {b}" for b in badlinks]
    for name, srcs, pdf in (("01", sources01, pdf1), ("05", sources05, pdf5)):
        short = check_numbers(name, [remap(s, name) for s in srcs], pdf)
        faults += [f"{name}: number {s}" for s in short]
        t = pdf_text(pdf)
        for bad in (".md", "safe/", "etabs/", "README", "C:\\", "file:///", "{", "composed model"):
            if bad in t:
                faults.append(f"{name}: '{bad}' in the PDF text ({t.count(bad)})")
        # Row 1982 JM-V6 (Codex V5-03): the smallest text on any page, read from the PDF's own spans
        sizes = collections.Counter()
        for pg in fitz.open(pdf):
            for b in pg.get_text("dict")["blocks"]:
                for ln in b.get("lines", []):
                    for sp in ln["spans"]:
                        if sp["text"].strip():
                            sizes[round(sp["size"], 1)] += len(sp["text"])
        print(f"FONT {name}: smallest {min(sizes)} pt, most used {sizes.most_common(1)[0][0]} pt; characters under 7 pt: "
              f"{sum(v for k, v in sizes.items() if k < 6.95)} of {sum(sizes.values())}")
        if min(sizes) < 6.95:
            faults.append(f"{name}: text at {min(sizes)} pt (under 7 pt)")
    # Row 1982 JM-V7 (Codex V5-07): no near-empty page. A page's body is filled to the lowest text span or picture inside
    # its margins (running head and foot excluded); under 40% of the body height is near-empty, except the last page and
    # the cover. COVERS: text and placed pictures. DOES NOT: a page whose fill is one long thin table (it counts as full);
    # same-class fault it would miss: a page holding only a heading and a picture caption over white.
    for pdf in (pdf1, pdf5):
        d = fitz.open(pdf)
        thin = []
        # Row 1982 JM-BUILD-PREP: the contents ends where it ends - its last page (the last of the run of pages from
        # page 2 that each carry 5 or more links to a page of the PDF) is exempt, as the cover and the last page are
        k = 1
        while k + 1 < d.page_count and sum(1 for ln in d[k + 1].get_links() if ln.get("page", -1) >= 0) >= 5:
            k += 1
        toc_last = k if sum(1 for ln in d[1].get_links() if ln.get("page", -1) >= 0) >= 5 else -1
        for i, pg in enumerate(d):
            if i == 0 or i == d.page_count - 1 or i == toc_last:
                continue
            top, bot = 18 * 72 / 25.4, pg.rect.height - 15 * 72 / 25.4
            low = top
            for b in pg.get_text("dict")["blocks"]:
                y0, y1 = b["bbox"][1], b["bbox"][3]
                if y0 > top - 2 and y1 < bot + 2 and (b.get("type") == 1 or any(
                        sp["text"].strip() for ln in b.get("lines", []) for sp in ln["spans"])):
                    low = max(low, y1)
            for im in pg.get_image_info():
                low = max(low, min(im["bbox"][3], bot))
            fill = (low - top) / (bot - top)
            if fill < 0.40:
                thin.append(f"p{i + 1} {fill:.0%}")
        nck = d.page_count - 2 - (toc_last > 0)
        print(f"PAGE FILL {pdf.name}: {nck - len(thin)} of {nck} pages (not the cover, the last page, or the contents' "
              f"last page p{toc_last + 1}) filled to 40% or more; near-empty: {', '.join(thin) or 'none'}")
        faults += [f"{pdf.name}: near-empty page {t}" for t in thin]
    d = fitz.open(pdf1)
    placements = sum(len(p.get_images(full=True)) for p in d)
    print(f"IMAGES 01: {placements} image placements (42 register pictures + the logo expected)")
    # Row 1982 JM-SAFE-FOLDER: 03 Models/SAFE/ opens to the models alone, and no PDF names a moved file at its old place.
    # COVERS: the files at the top of the folder; PDF text with the path on one line. DOES NOT: a path the PDF breaks
    # across lines; same-class fault it would miss: "03 Models/SAFE/" at a line end and "safe-set.txt" on the next.
    top = sorted(p.name for p in (out / "03 Models/SAFE").iterdir() if p.is_file())
    not_model = [n for n in top if not n.endswith(".f2k")]
    print(f"SAFE FOLDER: {len(top) - len(not_model)} of {len(top)} files at its top are models (.f2k); folders: "
          f"{', '.join(sorted(p.name for p in (out / '03 Models/SAFE').iterdir() if p.is_dir()))}")
    faults += [f"03 Models/SAFE/{n}: not a model, at the top of the SAFE folder" for n in not_model]
    for pdf in (pdf1, pdf5):
        t = pdf_text(pdf)
        old = re.findall(r"03 Models/SAFE/(?:Storeys with no transfer/)?[^\s/]+\.(?:txt|csv)", t)
        old += re.findall(r"\d+ SAFE models \(03 Models/SAFE/<storey>\.f2k\)", t)
        faults += [f"{pdf.name}: names {o}" for o in old]
    # Row 1982 JM-NO-PHASE2: no "phase 2" and no "outside the scope / this review" (lateral is the next issue, #133596).
    # COVERS: both PDFs' text and every shipped .html/.csv/.txt as text (the README ships inside PDF 01). DOES NOT: the
    # same framing in other words - same-class fault it would miss: "lateral is not part of this review".
    texts = [(p.name, pdf_text(p)) for p in (pdf1, pdf5)] + [
        (str(p.relative_to(out)), read(p)) for p in out.rglob("*") if p.suffix.lower() in (".html", ".csv", ".txt")]
    hits = [(n, m[0]) for n, t in texts for m in re.finditer(PHASE2_RE, t)]
    print(f"PHASE 2: {len(hits)} occurrence(s) of 'phase 2' / 'phase two' / 'outside the scope' / 'outside this review' in {len(texts)} shipped texts (2 PDFs + "
          f"{len(texts) - 2} .html/.csv/.txt files)")
    faults += [f"{n}: '{w}' (phase 2 offer, or lateral framed as excluded)" for n, w in hits]
    # Row 1982 JM-BUILD-PREP: the complete review (no first/next issue framing), and no unfilled number. The '{' check of
    # the PDFs above also stops an unfilled {{KEY}}; this one names the keys and covers the viewer and shipped text files.
    hits = [(n, m[0]) for n, t in texts for m in re.finditer(SCOPE_FAULT_RE, t)]
    print(f"SCOPE: {len(hits)} occurrence(s) of first/next issue, to follow, gravity only, lateral not reviewed "
          f"in {len(texts)} shipped texts")
    faults += [f"{n}: '{w}' (this issue is the complete review)" for n, w in hits]
    ph = sorted({(n, m[1]) for n, t in texts for m in PH_RE.finditer(t)})
    print(f"PLACEHOLDERS: {len(ph)} unfilled {{{{KEY}}}} in the shipped texts; numbers file "
          f"{NUMS.path.name if NUMS.path else '(none)'}: {len(NUMS.used)} key(s) filled"
          + (" (STAND-IN values)" if NUMS.stand_in else "") + f", {len(NUMS.from_pkg)} read off the package's run outputs by rule, gravity sites {dict(NUMS.site_log)}"
          + (f"; keys with no value: {', '.join(sorted(NUMS.unfilled))}" if NUMS.unfilled else ""))
    faults += [f"{n}: unfilled {{{{{k}}}}}" for n, k in ph]
    print(f"TYPED vs RULE: {len(NUMS.checked) - len(NUMS.disagree)} of {len(NUMS.checked)} typed values with a rule equal "
          f"the package's own run outputs" + "".join(f"\n   {x}" for x in NUMS.disagree))
    faults += [f"numbers: {x}" for x in NUMS.disagree]
    # every lateral file a PDF names is in the package. COVERS: names on one line of the PDF text. DOES NOT: a name the
    # PDF breaks across lines (same-class fault it would miss: '04 Results/ETABS lateral/' at a line end).
    for pdf in (pdf1, pdf5):
        for m in set(re.findall(r"04 Results/(?:ETABS lateral|Lateral checks)/[\w.-]+\.csv", pdf_text(pdf))):
            if not (out / m).is_file():
                faults.append(f"{pdf.name}: names {m}, not in the package")
    shipped = sorted(str(p.relative_to(out)) for p in out.rglob("*") if p.is_file())
    if any(s.lower().endswith(".md") for s in shipped):
        faults.append("a .md file shipped")
    for pdf in (pdf1, pdf5):
        print(f"PAGES {pdf.name}: {fitz.open(pdf).page_count}")
    print(f"SHIPPED {len(shipped)} files")
    if faults:
        print("FAULTS:\n  " + "\n  ".join(faults))
    d = pg = None   # JM-V9-PREP: main's last fitz document and page kept a PDF in --out open, so a refusal could not rename it
    return handoff_gate(out, build, a.job or job_of(out, src), faults, NUMS.stand_in)


# ---------------------------------------------------------------- Row 2135 HANDOFF-GATE (Ian 13 Nov #133633, #133634)
# THE LAST STEP: `takeoff handoff-gate check --kind peer-review` on the laid-out folder. It refuses unless every shipped file
# has a look record in KorStandards oversight.SetLook by a session other than this one (CLAUDE_CODE_SESSION_ID), every page of
# every PDF; a docs/codex CODEX-<job>-*-RESPONSE.md names the folder's manifest sha256 with no open WRONG; this run's gates
# (written to <build>/handoff-gates.json: the layout checks above, and the numbers not being STAND-IN) all passed; and the
# PDFs pass the peer-review leak list and a 40% width fill per page. A REFUSAL (or a fault above) renames --out to
# <out>_REFUSED - NOT FOR ISSUE - and exits 1; "CHECKS PASSED" is printed only when the gate passed.
# COVERS: closes ship-exits rows 40 (a faulted build left complete and unmarked), 41 (STAND-IN numbers exit 0) and 43 (no
# look gate). DOES NOT: row 42 (the image placement count is still printed, not checked); a gate that cannot run (no pin, a
# crash) is a refusal. SAME-CLASS MISS: a send made from the folder BEFORE this step finishes (the folder is complete from
# the copies onward; only the rename marks it).
HANDOFF_TAKEOFF = Path(__file__).resolve().parents[2] / "KOR.Drafter" / "work" / "_tools" / "takeoff" / "45beed64" / "takeoff.exe"   # KOR.Drafter 45beed64


def job_of(out: Path, src: Path) -> str:
    for p in (out, src, *src.parents):
        m = re.match(r"(\d{5}-\d{2})\b", p.name)
        if m:
            return m.group(1)
    return ""


def handoff_gate(out: Path, build: Path, job: str, faults: list, stand_in: bool) -> int:
    gates = {"gates": [
        {"gate": "peer-review layout checks (build_peer_review_package.py)", "passed": not faults,
         "detail": f"{len(faults)} fault(s)" + (": " + "; ".join(faults[:20]) if faults else "")},
        {"gate": "the numbers are the run's, not STAND-IN", "passed": not stand_in,
         "detail": "STAND-IN numbers: the covers carry NOT FOR ISSUE" if stand_in else "numbers file values"},
    ]}
    record = build / "handoff-gates.json"
    record.write_text(json.dumps(gates, indent=1), encoding="utf-8")
    takeoff = Path(os.environ.get("KOR_HANDOFF_TAKEOFF") or HANDOFF_TAKEOFF)
    refused = bool(faults) or stand_in
    if not job:
        print("HANDOFF GATE: no job number in --job, --out or --src")
        refused = True
    elif not takeoff.is_file():
        print(f"HANDOFF GATE: no takeoff at {takeoff} (set KOR_HANDOFF_TAKEOFF): the gate cannot run, so the package is refused")
        refused = True
    else:
        cmd = [str(takeoff), "handoff-gate", "check", "--job", job, "--dir", str(out), "--kind", "peer-review",
               "--run-record", str(record)]
        print("HANDOFF GATE: " + " ".join(f'"{c}"' if " " in c else c for c in cmd), flush=True)
        try:
            rc = subprocess.run(cmd, timeout=1800).returncode
        except (OSError, subprocess.TimeoutExpired) as e:
            print(f"HANDOFF GATE could not run: {e}")
            rc = 1
        refused = refused or rc != 0
    if refused:
        dest = out.with_name(out.name + "_REFUSED")
        if dest.exists():
            shutil.rmtree(dest)      # an earlier refused build of the same --out: a work product
        # Row 1982 JM-V9-PREP (13 Nov 20:0x): on the v9 rehearsal the rename right after the gate failed with WinError 5
        # (main still held a fitz document of a PDF inside --out; released before this call now) and the build died on a
        # traceback with the folder NOT renamed. Retry; if it still cannot move, mark the folder itself and refuse.
        gc.collect()        # the PDFs this script opened with fitz.open() and never closed hold handles inside --out
        for i in range(20):
            try:
                out.rename(dest)
                break
            except PermissionError as e:
                err = e
                time.sleep(1.5)
        else:
            (out / "NOT FOR ISSUE - REFUSED.txt").write_text(
                f"The hand-off gate refused this package; it could not be renamed to {dest.name} ({err}).\n", encoding="utf-8")
            print(f"NOT FOR ISSUE: the package is refused; it could not be moved ({err}): marked in place with "
                  f"'NOT FOR ISSUE - REFUSED.txt'")
            return 1
        print(f"NOT FOR ISSUE: the package is refused and was moved to {dest}")
        return 1
    print("CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
