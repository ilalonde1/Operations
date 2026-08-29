#!/usr/bin/env python3
"""Houston Plat Activity Reports -- the developer, before the architect.

WHY THIS SOURCE AND NOT THE OTHER TWO
    Houston has no zoning, so there is no rezoning petition to watch, and the
    TDLR registry -- though a genuine census of every commercial project over
    $50k -- is filed BY THE DESIGN FIRM. Every TDLR row is therefore dated
    after the architect was appointed. Good history, useless as a lead.

    A subdivision plat is filed to divide land, by the developer's planner,
    surveyor or civil engineer, BEFORE a building is designed. The City of
    Houston publishes the full agenda of each Planning Commission cycle as a
    spreadsheet, and that spreadsheet carries:

        Developer Company Name .... who is behind it
        Organization .............. the consultancy that filed
        Applicant Name ............ a named person
        Office Phone .............. published by the city
        Land Use, Total Acreage, Number of Lots Created
        Council District, TIRZ, Super Neighborhood
        a link to the plat PDF itself

    That is the Houston equivalent of a Clark County pre-application, with more
    on it. Reports are posted per commission cycle, roughly fortnightly.

    Index: houstontx.gov/planning/DevelopRegs/dev_reports.html
    Files: .../docs_pdfs/Plat_report/<year>/Plat-Current-Agenda-Spreadsheet-<MM-DD-YYYY>.xlsx
    Note the 8 Jan 2026 cycle is published under the file date 01-05-2026, so
    scrape the index rather than generating filenames.

⚠ TRAPS
    * openpyxl 3.1.5 cannot open these files at all -- it dies in
      apply_stylesheet with "Fill() takes no arguments" before reading a cell.
      Use tools/xlsx_rows.py, which reads the sheet XML directly.
    * Dates are Excel serials in columns 2 and 3. Convert, or you will report
      "46244" as a date.
    * A plat is a LAND action. It proves a developer is committed to a site.
      It does NOT prove no architect is engaged -- the same discipline as
      everywhere else in this work: a call, not a commission.
    * Most rows are single-family subdivisions in the ETJ. Filter on land use
      and scale before reading anything into a row.
    * "Organization" is usually the civil engineer or land planner, not the
      developer. Both are named; do not conflate them.

USAGE
    python houston_plat_reports.py fetch  [year] [dir]
    python houston_plat_reports.py survey [dir]
    python houston_plat_reports.py leads  [dir] [out.json]
"""
import datetime
import json
import os
import re
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_rows import rows as xlsx_rows  # noqa: E402

INDEX = "https://www.houstontx.gov/planning/DevelopRegs/dev_reports.html"
ROOT = "https://www.houstontx.gov/planning/DevelopRegs/"
UA = {"User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                     "AppleWebKit/537.36 (KHTML, like Gecko) "
                     "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")}
EPOCH = datetime.date(1899, 12, 30)

# ⛔ DO NOT INDEX THESE FILES BY COLUMN POSITION.
#    The 2026 cycles do not share a layout: the January reports carry no
#    "Land Use" column at all, so position 22 is Total Acreage there and Land
#    Use in August. Hardcoding positions silently reports acreage as land use.
#    Resolve every column by matching its header text instead, and record which
#    fields a given report simply does not have.
FIELDS = {
    "subdivision": ("subdivision name",),
    "app_no": ("app no",),
    "pc_date": ("pc date",),
    "submitted": ("date submitted",),
    "app_type": ("application type",),
    "council": ("council district",),
    "in_city": ("in city limits",),
    "county": ("county",),
    "land_use": ("land use",),
    "acreage": ("total acreage",),
    "lots": ("number of lots",),
    "developer": ("developer company name",),
    "organization": ("organization",),
    "applicant": ("applicant name",),
}
REQUIRED = ("app_no", "subdivision", "submitted")


def serial(v):
    try:
        return (EPOCH + datetime.timedelta(days=int(float(v)))).isoformat()
    except (TypeError, ValueError):
        return ""


def fetch(year="2026", dest="houston-plats"):
    os.makedirs(dest, exist_ok=True)
    html = urllib.request.urlopen(
        urllib.request.Request(INDEX, headers=UA), timeout=120
    ).read().decode("utf-8", "replace")
    hrefs = re.findall(r'href="(docs_pdfs/Plat_report/%s/[^"]+\.xlsx)"' % year, html)
    hrefs = list(dict.fromkeys(hrefs))
    print("index lists %d %s reports" % (len(hrefs), year))
    got = []
    for h in hrefs:
        name = h.rsplit("/", 1)[-1]
        path = os.path.join(dest, name)
        if not os.path.exists(path):
            data = urllib.request.urlopen(
                urllib.request.Request(ROOT + h, headers=UA), timeout=180).read()
            with open(path, "wb") as fh:
                fh.write(data)
            print("  fetched %-52s %6d bytes" % (name, len(data)))
        else:
            print("  have    %s" % name)
        got.append(path)
    return got


def resolve(header):
    """{field: column index} by matching header text, not position."""
    hdr = [str(c or "").strip().lower() for c in header]
    idx = {}
    for field, wants in FIELDS.items():
        for i, h in enumerate(hdr):
            if any(w in h for w in wants):
                idx[field] = i
                break
    missing = [f for f in REQUIRED if f not in idx]
    if missing:
        raise SystemExit("%s: cannot find %s in header %r"
                         % ("report", missing, hdr[:8]))
    return idx


def read(path):
    rs = list(xlsx_rows(path, 0))
    if not rs:
        return []
    idx = resolve(rs[0])
    name = os.path.basename(path)
    absent = sorted(set(FIELDS) - set(idx))
    out = []
    for r in rs[1:]:
        def g(k):
            i = idx.get(k)
            if i is None or i >= len(r) or r[i] is None:
                return ""
            return str(r[i]).strip()
        if not g("app_no"):
            continue
        rec = {k: g(k) for k in FIELDS}
        rec["submitted"] = serial(r[idx["submitted"]]) if idx["submitted"] < len(r) else ""
        if "pc_date" in idx and idx["pc_date"] < len(r):
            rec["pc_date"] = serial(r[idx["pc_date"]])
        rec["cycle_file"] = name
        rec["fields_absent"] = absent
        out.append(rec)
    return out


def load_all(d="houston-plats"):
    seen, out = set(), []
    for f in sorted(os.listdir(d)):
        if not f.endswith(".xlsx"):
            continue
        for r in read(os.path.join(d, f)):
            if r["app_no"] in seen:
                continue
            seen.add(r["app_no"])
            out.append(r)
    return out


def survey(d="houston-plats"):
    rs = load_all(d)
    print("%d distinct applications across %d cycle reports"
          % (len(rs), len({r["cycle_file"] for r in rs})))
    dates = sorted(r["submitted"] for r in rs if r["submitted"])
    print("submitted %s .. %s" % (dates[0], dates[-1]) if dates else "no dates")
    print()
    for field in ("land_use", "app_type", "county"):
        c = {}
        for r in rs:
            c[r[field] or "(blank)"] = c.get(r[field] or "(blank)", 0) + 1
        print("%s -- %d distinct" % (field.upper(), len(c)))
        for k, n in sorted(c.items(), key=lambda x: -x[1])[:14]:
            print("   %5d  %s" % (n, k[:64]))
        print()
    named = sum(1 for r in rs if r["developer"])
    print("developer company named on %d of %d (%.0f%%)"
          % (named, len(rs), 100.0 * named / max(1, len(rs))))
    return rs


MULTI = re.compile(r"(?i)multi.?family|apartment|condo|mixed.?use|townhome|"
                   r"town home|residential building|high.?rise|mid.?rise")


def leads(d="houston-plats", out=None, min_acres=2.0, since="2026-06-01"):
    rs = load_all(d)
    hits = []
    for r in rs:
        if r["submitted"] < since:
            continue
        try:
            ac = float(r["acreage"] or 0)
        except ValueError:
            ac = 0.0
        if not MULTI.search(r["land_use"] or ""):
            continue
        if ac and ac < min_acres:
            continue
        hits.append(r)
    hits.sort(key=lambda r: r["submitted"], reverse=True)
    print("%d multifamily/mixed-use plat applications submitted since %s"
          % (len(hits), since))
    print()
    print("  %-11s %-9s %-26s %-24s %s"
          % ("SUBMITTED", "APP NO.", "DEVELOPER", "ORGANISATION", "LAND USE"))
    for r in hits[:40]:
        print("  %-11s %-9s %-26s %-24s %s"
              % (r["submitted"], r["app_no"], (r["developer"] or "-")[:26],
                 (r["organization"] or "-")[:24], (r["land_use"] or "")[:30]))
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(hits, fh, indent=1)
        print("\nwrote %s" % out)
    return hits


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "survey"
    a = sys.argv[2:]
    if cmd == "fetch":
        fetch(*(a or ["2026", "houston-plats"]))
    elif cmd == "survey":
        survey(*(a or ["houston-plats"]))
    elif cmd == "leads":
        leads(*(a or ["houston-plats"]))
    else:
        raise SystemExit(__doc__)
