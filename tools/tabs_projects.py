#!/usr/bin/env python3
"""Harvest Texas design teams from the TDLR Architectural Barriers registry.

WHY THIS EXISTS
    Every other design-team source in the MVE work is a SAMPLE -- a magazine
    listicle, the rezoning cases that happened to need a PUD, the projects a
    board happened to hear. Concentration measured on a sample can be an
    artefact of whoever assembled it.

    Texas is different. Occupations Code ch. 469 requires EVERY commercial
    construction project over $50,000 in the state to be registered with the
    Department of Licensing and Regulation for accessibility review, and the
    registration names the DESIGN FIRM -- which TDLR defines as the architect
    or engineer with overall responsibility for the project. That makes this a
    CENSUS, not a sample: the denominator is the law's, not an editor's.

    It is free, unauthenticated, statewide, and current to the previous day.

WHAT IT GIVES, PER PROJECT
    Owner (name, address, phone) - Design firm (name, address, phone) -
    scope-of-work narrative - square footage - estimated cost - type of work -
    type of funds - status - registered accessibility specialist.

USAGE
    python tabs_projects.py list   2101 01/01/2024 08/28/2026 harris.jsonl
    python tabs_projects.py detail harris.jsonl harris_detail.jsonl 10000000 9001
    python tabs_projects.py analyse harris_detail.jsonl

    County and city codes are the <option value> attributes on
    https://www.tdlr.texas.gov/tabs/search  (Harris = 2101, Houston city = 785,
    Dallas county = 2057). TypeOfWork: 9001 new construction, 9002 renovation,
    9003 addition, 9004 historic, 9005 public right of way.

TRAPS PAID FOR ALREADY -- read before trusting output
    * FILTER ON COUNTY, NOT CITY. The city field is applicant-typed. A record
      was found filed City=Houston, County=Dallas, at a 75038 (Irving) address.
    * NEVER CLASSIFY A PROJECT BY ITS NAME. Houston developers file under code
      names -- "Project Astro", "Cerberus", "Fairbanks D", "The RO Parcel 4".
      Name-keyword matching found 74 residential projects in 4,087; classifying
      from the scope narrative found 32 multifamily in the >=$10M slice alone.
    * "DESIGN FIRM" IS NOT ALWAYS AN ARCHITECT. The field legitimately admits
      engineers, landscape architects and interior designers, and developers
      self-file. Kittle Property Group, M Lanza Engineering, Doshi Engineering
      & Surveying, Kimley-Horn, Nelson Byrd Woltz and Clay Development &
      Construction all came back as "design firms". Sanity-check every firm
      against what it actually does before quoting it.
    * Estimated cost is self-reported at registration, not a contract value.
    * The index endpoint caps page size at 100 however large a length you send.
"""
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request
from collections import Counter, defaultdict

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")
SEARCH = "https://www.tdlr.texas.gov/TABS/Search/SearchProjects"
DETAIL = "https://www.tdlr.texas.gov/TABS/Search/Project/%s"
REFERER = "https://www.tdlr.texas.gov/tabs/search"
PAGE = 100

WORK = {9001: "New Construction", 9002: "Renovation/Alteration",
        9003: "Additions to Existing", 9004: "Historic Preservation",
        9005: "Public Right of Way"}


def _post(payload):
    body = urllib.parse.urlencode(payload).encode()
    req = urllib.request.Request(SEARCH, data=body, headers={
        "User-Agent": UA,
        "X-Requested-With": "XMLHttpRequest",
        "Referer": REFERER,
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
    })
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.load(r)


def cmd_list(county, dfrom, dto, out):
    """Page the whole county index into JSONL."""
    def fetch(start):
        return _post({"draw": 1, "start": start, "length": PAGE,
                      "DataVersionId": 900001, "LocationCounty": county,
                      "RegistrationDateBegin": dfrom, "RegistrationDateEnd": dto})

    first = fetch(0)
    total = first["recordsTotal"]
    print("county=%s %s..%s  total=%d" % (county, dfrom, dto, total))
    seen, start = set(), 0
    with open(out, "w", encoding="utf-8") as fh:
        while start < total:
            page = first if start == 0 else fetch(start)
            rows = page.get("data", [])
            if not rows:
                print("empty page at %d -- stopping short of %d" % (start, total))
                break
            for row in rows:
                if row.get("ProjectNumber") in seen:
                    continue
                seen.add(row["ProjectNumber"])
                fh.write(json.dumps(row) + "\n")
            start += len(rows)
            if start % 1000 < PAGE:
                print("  %d/%d" % (start, total))
                sys.stdout.flush()
            time.sleep(0.25)
    print("wrote %d unique rows to %s" % (len(seen), out))


SECTIONS = {"PROJECT", "PERSON FILING FORM", "RAS", "OWNER", "TENANT",
            "DESIGN FIRM", "CONTRACTOR"}
WANT = ["Project Name", "Facility Name", "Location Address", "Location County",
        "Estimated Cost", "Type of Work", "Type of Funds", "Scope of Work",
        "Square Footage", "Current Status", "Owner Name", "Owner Address",
        "Design Firm Name", "Design Firm Address", "Design Firm Phone"]


def _parse_detail(html):
    """Detail pages are label/value: a line ending ':' owns the lines under it."""
    html = re.sub(r"(?is)<(script|style).*?</\1>", " ", html)
    html = re.sub(r"(?i)<br\s*/?>", "\n", html)
    html = re.sub(r"<[^>]*>", "\n", html)
    html = html.replace("&nbsp;", " ").replace("&amp;", "&")
    lines = [l.strip() for l in html.split("\n") if l.strip()]
    rec, i = {}, 0
    while i < len(lines):
        ln = lines[i]
        if ln.upper() in SECTIONS or not ln.endswith(":"):
            i += 1
            continue
        label, vals, j = ln[:-1].strip(), [], i + 1
        while j < len(lines) and not lines[j].endswith(":") \
                and lines[j].upper() not in SECTIONS:
            vals.append(lines[j])
            j += 1
        if label in WANT and label not in rec:
            rec[label] = " ".join(vals).strip()
        i = j
    return rec


def cmd_detail(src, out, min_cost=0.0, work=None):
    """Fetch detail records for a filtered slice of the index. Resumable."""
    rows = [json.loads(l) for l in open(src, encoding="utf-8")]
    sel = [r for r in rows
           if float(r.get("EstimatedCost") or 0) >= min_cost
           and (work is None or r.get("TypeOfWork") == work)]
    sel.sort(key=lambda r: -float(r.get("EstimatedCost") or 0))
    print("selected %d of %d" % (len(sel), len(rows)))

    done = set()
    if os.path.exists(out):
        for line in open(out, encoding="utf-8"):
            try:
                done.add(json.loads(line)["ProjectNumber"])
            except Exception:
                pass
        print("resuming; %d already fetched" % len(done))

    fh = open(out, "a", encoding="utf-8")
    named = 0
    for i, r in enumerate(sel, 1):
        pn = r["ProjectNumber"]
        if pn in done:
            continue
        try:
            req = urllib.request.Request(DETAIL % pn, headers={
                "User-Agent": UA, "Referer": REFERER})
            with urllib.request.urlopen(req, timeout=90) as resp:
                rec = _parse_detail(resp.read().decode("utf-8", "replace"))
            rec["ProjectNumber"] = pn
            rec["IndexCost"] = float(r.get("EstimatedCost") or 0)
            rec["IndexCreated"] = (r.get("ProjectCreatedOn") or "")[:10]
            fh.write(json.dumps(rec) + "\n")
            fh.flush()
            named += 1 if rec.get("Design Firm Name") else 0
        except Exception as e:
            print("  FAIL %s %s" % (pn, str(e)[:60]))
        if i % 250 == 0:
            print("  %d/%d  named so far %d" % (i, len(sel), named))
            sys.stdout.flush()
        time.sleep(0.2)
    fh.close()
    print("done; design firm named on %d newly fetched" % named)


_SUFFIX = re.compile(
    r"(?i)[,\.]?\s*\b(inc|llc|l\.l\.c|llp|l\.l\.p|pllc|p\.l\.l\.c|ltd|lp|l\.p|"
    r"pc|p\.c|co|corp|corporation|company|architects?|architecture|"
    r"and associates|& associates|associates|assoc|group|studio|studios|"
    r"design|designs|international|usa|texas)\b\.?")


def norm_firm(name):
    """Collapse 'Ziegler Cooper' / 'Ziegler Cooper Architects, Inc.' to one key."""
    n = re.sub(r"\s+", " ", (name or "").strip())
    core = re.sub(r"(?i)\bthe\b", " ", n)
    for _ in range(6):
        new = _SUFFIX.sub(" ", core)
        if new == core:
            break
        core = new
    core = re.sub(r"[^A-Za-z0-9& ]", " ", core)
    core = re.sub(r"\s+", " ", core).strip().upper()
    return core or re.sub(r"[^A-Za-z0-9 ]", "", n).upper().strip()


SECTORS = [
    ("infrastructure", r"(?i)\b(CSJ|roadway|highway|IH[ -]?\d|SH[ -]?\d|"
                       r"US[ -]?\d+\b|right of way|sidewalk|paving|bridge)\b"),
    ("multifamily", r"(?i)\b(multi[- ]?family|apartment|dwelling units?|"
                    r"residential units?|senior living|independent living|"
                    r"student housing|townhome|town home|condominium|condo\b|"
                    r"affordable housing|\d+[- ]unit)\b"),
    ("hospitality", r"(?i)\b(hotel|resort|hospitality|guest ?rooms?)\b"),
    ("healthcare", r"(?i)\b(hospital|clinic|medical|patient|surgery|"
                   r"health ?care|emergency department|cancer|dental)\b"),
    ("education", r"(?i)\b(school|isd\b|elementary|middle school|high school|"
                  r"university|college|campus|classroom|academy)\b"),
    ("industrial", r"(?i)\b(warehouse|distribution|industrial|manufacturing|"
                   r"tilt[- ]?wall|spec building|logistics|data ?cent(er|re))\b"),
    ("retail", r"(?i)\b(retail|restaurant|shopping|store|grocery|"
               r"quick service|drive[- ]?thru|car wash|dealership)\b"),
    ("office", r"(?i)\b(office|corporate headquarters|hq\b|workplace)\b"),
    ("civic", r"(?i)\b(library|museum|city of|county|municipal|fire station|"
              r"police|courthouse|convention|community cent)\b"),
    ("religious", r"(?i)\b(church|worship|chapel|mosque|synagogue|temple)\b"),
]


def sector_of(rec):
    """Classify from the SCOPE narrative and facility name -- never the name."""
    hay = " ".join([rec.get("Scope of Work") or "",
                    rec.get("Facility Name") or "",
                    rec.get("Project Name") or ""])
    for name, pat in SECTORS:
        if re.search(pat, hay):
            return name
    return "unclassified"


def concentration(recs, label):
    firms, display = Counter(), {}
    for r in recs:
        fn = (r.get("Design Firm Name") or "").strip()
        if not fn:
            continue
        k = norm_firm(fn)
        firms[k] += 1
        display.setdefault(k, fn)
    tot = sum(firms.values())
    if not tot:
        print("\n%s: no named firms" % label)
        return
    top = firms.most_common(12)
    print("\n%s -- %d projects with a named firm, %d distinct firms" %
          (label, tot, len(firms)))
    print("  top firm %.0f%%   top 3 %.0f%%" %
          (100 * top[0][1] / tot, 100 * sum(c for _, c in top[:3]) / tot))
    for k, c in top:
        print("    %-44s %3d  (%.0f%%)" % (display[k][:44], c, 100 * c / tot))


def cmd_analyse(src):
    rows = [json.loads(l) for l in open(src, encoding="utf-8")]
    named = [r for r in rows if (r.get("Design Firm Name") or "").strip()]
    print("detail records: %d   with a design firm: %d (%.0f%%)" %
          (len(rows), len(named), 100 * len(named) / max(1, len(rows))))
    buckets = defaultdict(list)
    for r in rows:
        buckets[sector_of(r)].append(r)
    print("\nby sector:")
    for k, v in sorted(buckets.items(), key=lambda kv: -len(kv[1])):
        print("  %-16s %5d   $%0.1fB" %
              (k, len(v), sum(r.get("IndexCost", 0) for r in v) / 1e9))
    concentration(named, "ALL")
    for s in ("multifamily", "industrial", "office", "hospitality",
              "healthcare", "education", "retail"):
        recs = [r for r in buckets.get(s, [])
                if (r.get("Design Firm Name") or "").strip()]
        if len(recs) >= 5:
            concentration(recs, "SECTOR: " + s)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(2)
    mode = sys.argv[1]
    if mode == "list":
        cmd_list(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5])
    elif mode == "detail":
        cmd_detail(sys.argv[2], sys.argv[3],
                   float(sys.argv[4]) if len(sys.argv) > 4 else 0.0,
                   int(sys.argv[5]) if len(sys.argv) > 5 else None)
    elif mode == "analyse":
        cmd_analyse(sys.argv[2])
    else:
        print(__doc__)
        raise SystemExit(2)
