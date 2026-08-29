#!/usr/bin/env python3
"""Read an .xlsx as rows without going through openpyxl's stylesheet parser.

WHY THIS EXISTS
    openpyxl 3.1.5 raises "TypeError: Fill() takes no arguments" on workbooks
    written by some server-side generators -- the City of Houston Plat Activity
    Reports among them. The failure is in apply_stylesheet, before a single
    cell is read, so a perfectly readable file becomes unreadable for a reason
    that has nothing to do with its data.

    An .xlsx is a zip of XML. The values live in xl/worksheets/sheetN.xml with
    string cells indirected through xl/sharedStrings.xml. Reading those two
    directly skips styles entirely and cannot be broken by them.

⚠ WHAT THIS DOES NOT DO
    No formatting, no formulas (cached values only), no date conversion beyond
    the serial-to-date rule below. If a number looks like a date, it is on the
    caller to know the column.

USAGE
    python xlsx_rows.py file.xlsx [sheet-index] [max-rows]
    from xlsx_rows import sheets, rows
"""
import datetime
import re
import sys
import zipfile
from xml.etree import ElementTree as ET

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
REL = "{http://schemas.openxmlformats.org/package/2006/relationships}"
EPOCH = datetime.date(1899, 12, 30)  # Excel's serial-0, 1900 leap-year bug included


def _shared(z):
    if "xl/sharedStrings.xml" not in z.namelist():
        return []
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    out = []
    for si in root.findall(NS + "si"):
        out.append("".join(t.text or "" for t in si.iter(NS + "t")))
    return out


def sheets(path):
    """[(name, zip member path)] in workbook order."""
    with zipfile.ZipFile(path) as z:
        wb = ET.fromstring(z.read("xl/workbook.xml"))
        rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
        target = {r.get("Id"): r.get("Target") for r in rels}
        out = []
        for sh in wb.iter(NS + "sheet"):
            rid = sh.get("{http://schemas.openxmlformats.org/officeDocument/"
                         "2006/relationships}id")
            t = target.get(rid, "")
            if t.startswith("/"):
                t = t[1:]
            elif not t.startswith("xl/"):
                t = "xl/" + t
            out.append((sh.get("name"), t))
        return out


def _colnum(ref):
    m = re.match(r"([A-Z]+)", ref or "")
    n = 0
    for ch in m.group(1) if m else "":
        n = n * 26 + (ord(ch) - 64)
    return n - 1


def rows(path, sheet=0, limit=None, dates=()):
    """Yield lists of cell values. `dates` = column indices to read as dates."""
    with zipfile.ZipFile(path) as z:
        strings = _shared(z)
        name, member = sheets(path)[sheet] if isinstance(sheet, int) else \
            next((n, m) for n, m in sheets(path) if n == sheet)
        root = ET.fromstring(z.read(member))
        n = 0
        for row in root.iter(NS + "row"):
            cells = {}
            for c in row.findall(NS + "c"):
                i = _colnum(c.get("r"))
                t = c.get("t")
                if t == "inlineStr":
                    v = "".join(x.text or "" for x in c.iter(NS + "t"))
                else:
                    ve = c.find(NS + "v")
                    v = ve.text if ve is not None else None
                    if v is not None and t == "s":
                        v = strings[int(v)]
                if v is not None and t not in ("s", "str", "inlineStr"):
                    try:
                        f = float(v)
                        if i in dates and 1 < f < 80000:
                            v = (EPOCH + datetime.timedelta(days=int(f))).isoformat()
                        else:
                            v = int(f) if f == int(f) else f
                    except ValueError:
                        pass
                cells[i] = v
            if cells:
                width = max(cells) + 1
                yield [cells.get(i) for i in range(width)]
                n += 1
                if limit and n >= limit:
                    return


if __name__ == "__main__":
    p = sys.argv[1]
    idx = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    lim = int(sys.argv[3]) if len(sys.argv) > 3 else 12
    print("sheets:", [n for n, _ in sheets(p)])
    print()
    for i, r in enumerate(rows(p, idx, lim), 1):
        cells = [("" if c is None else str(c))[:24] for c in r]
        while cells and cells[-1] == "":
            cells.pop()
        print("r%-3d %s" % (i, " | ".join(cells)[:250]))
