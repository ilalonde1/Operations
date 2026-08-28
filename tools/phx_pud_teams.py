#!/usr/bin/env python3
"""Extract design teams from City of Phoenix PUD rezoning narratives.

WHY THIS EXISTS
    Arizona's design-team data in the MVE work rests on a magazine listicle
    (AZ Big Media, "50 projects to know"). That is a CURATED set, so any
    concentration finding drawn from it risks being an artefact of the editing
    rather than a fact about the market. This is the independent source that
    tests it — and it is better than the listicle in every respect.

    Phoenix publishes PUD narratives at
      phoenix.gov/content/dam/phoenix/pddsite/documents/planning-zoning-pud/
    indexed from the "PUD and PCD Current Cases" page. A narrative is written BY
    the applicant's team and opens with a project-directory block naming the
    developer, the ARCHITECT, the civil engineer and the land-use attorney —
    with named individuals, emails and phone numbers.

    Verified by hand first on Z-112-24-1 (3014 W Deer Valley Rd):
      DEVELOPER/OWNER            Roers Companies
      ARCHITECTS & DESIGN TEAM   Kass Wilson Architects
      CIVIL ENGINEER & SURVEY    Rick Engineering
      APPLICANT/LAND USE ATTORNEY Earl & Curley, P.C.

USAGE
    python tools/phx_pud_teams.py --limit 25 --csv phx_teams.csv
    python tools/phx_pud_teams.py --case z-112-24-1

LIMITS — say these whenever the output is quoted
    * PUD narratives cover REZONING cases only. A project that does not need a
      rezoning never appears, so this is not a census of Phoenix multifamily.
    * Label wording varies between narratives; a role that is not matched is
      reported as blank, never guessed.
    * The blocks are prose in a Word-to-PDF export, so the firm name is taken up
      to the first address-like token and may need an eye on edge cases.
"""
import argparse
import csv
import io
import os
import re
import subprocess
import sys
import urllib.parse
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")
INDEX = ("https://www.phoenix.gov/administration/departments/pdd/"
         "planning-zoning/zoning-rezoning/pud-cases.html")
DOCROOT = "https://www.phoenix.gov"

# role -> label variants, longest/most specific first.
# ⚠ Matching is CASE-INSENSITIVE. The narratives are inconsistent: one writes
# "ARCHITECTS & DESIGN TEAM:" in caps, the next writes "Architect" in title case
# on its own line. An uppercase-only pattern returned 0 of 18 and looked like the
# source had no architects, when every one of them named one.
# ⚠ "Architect" must NOT match inside "Landscape Architect" — hence the lookbehind.
ROLES = [
    ("architect", [r"ARCHITECTS?\s*(?:&|AND)\s*DESIGN\s*TEAM", r"PROJECT\s+ARCHITECT",
                   r"DESIGN\s+ARCHITECT", r"ARCHITECTURE\s+FIRM",
                   r"(?<!LANDSCAPE )(?<!Landscape )ARCHITECTS?"]),
    ("developer", [r"OWNER\s*REP\.?\s*/\s*DEVELOPER", r"DEVELOPER\s*/\s*OWNER",
                   r"OWNER\s*/\s*DEVELOPER", r"DEVELOPER", r"PREPARED\s+FOR",
                   r"PROPERTY\s+OWNER", r"OWNER"]),
    ("civil", [r"CIVIL\s+ENGINEER\s*(?:&|AND)\s*SURVEY\s*TEAM",
               r"PLANNING\s+AND\s+ENTITLEMENTS\s*/\s*ENGINEERING",
               r"CIVIL\s+ENGINEERING?", r"ENGINEER\s*/\s*SURVEY", r"CIVIL"]),
    ("landscape", [r"LANDSCAPE\s+ARCHITECTS?", r"LANDSCAPE"]),
    ("attorney", [r"APPLICANT\s*/\s*LAND\s*USE\s*ATTORNEY",
                  r"LAND\s*USE\s*ATTORNEY", r"ATTORNEY"]),
]

# a firm name ends where an address, a contact label or a phone begins
STOP = (r"(?=\s+\d{2,6}\s|\s+(?:Contact|Attn|Email|Phone|Tel|Address|Principal|"
        r"Attorney|Planner)\s*:|\s+[A-Z]{2}\s+\d{5}|$)")
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")


def fetch(url, dest=None):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    data = urllib.request.urlopen(req, timeout=120).read()
    if dest:
        open(dest, "wb").write(data)
    return data


def pdf_text(path):
    return subprocess.run(["pdftotext", "-enc", "UTF-8", path, "-"],
                          capture_output=True, timeout=240).stdout.decode(
                              "utf-8", "replace")


def team_from(text):
    """Pull the directory block. Blank, never guessed, when a role is absent."""
    flat = re.sub(r"\s+", " ", text[:20000])   # the block is always near the front
    out = {}
    for role, variants in ROLES:
        val = ""
        for v in variants:
            m = re.search(v + r"\s*:?\s*" + r"([A-Z][A-Za-z0-9&.,'’\- ]{3,60}?)" + STOP,
                          flat, re.IGNORECASE)
            if m:
                cand = re.sub(r"\s+", " ", m.group(1)).strip(" .,;-")
                if len(cand) > 3 and not cand.upper().startswith(("THE PROJECT",
                                                                 "THIS ")):
                    val = cand
                    break
        out[role] = val
    mails = EMAIL.findall(flat)
    out["emails"] = ";".join(dict.fromkeys(mails))[:200]
    return out


def index_cases():
    html = fetch(INDEX).decode("utf-8", "replace")
    links = re.findall(r'href="([^"]*planning-zoning-pud[^"]*\.pdf)"', html, re.I)
    seen, out = set(), []
    for l in links:
        l = l.replace("&amp;", "&")
        base = os.path.basename(urllib.parse.unquote(l)).lower()
        # Keep ONLY narratives. Everything else in this folder — staff reports,
        # planning-commission memos, ordinances, findings-and-legal, bump memos,
        # exhibits — has no project-team block, and counting them as misses makes
        # the recovery rate look far worse than it is.
        if not re.search(r"(?:-n|\dn|narrative)\.pdf$", base):
            continue
        if re.search(r"(-sr|-pcm|pc-memo|ordinance|-fral|-fal|-al|-bum|-cc|"
                     r"-exhibit|-ord|g-?7\d{3})", base):
            continue
        if base in seen:
            continue
        seen.add(base)
        out.append(l if l.startswith("http") else DOCROOT + l)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--limit", type=int, default=20)
    ap.add_argument("--csv")
    ap.add_argument("--cache", default="pud_cache")
    a = ap.parse_args(argv)

    os.makedirs(a.cache, exist_ok=True)
    cases = index_cases()
    print("narrative-like PDFs on the index: %d   (processing %d)"
          % (len(cases), min(a.limit, len(cases))))

    rows, got = [], 0
    for url in cases[:a.limit]:
        name = os.path.basename(urllib.parse.unquote(url))
        path = os.path.join(a.cache, name)
        try:
            if not os.path.exists(path):
                fetch(url, path)
            txt = pdf_text(path)
        except Exception as e:
            print("  %-34s fetch/parse failed: %s" % (name[:34], str(e)[:40]))
            continue
        t = team_from(txt)
        t["case"] = re.sub(r"\.pdf$", "", name, flags=re.I)
        rows.append(t)
        if t["architect"]:
            got += 1
        print("  %-30s ARCH %-32s DEV %s"
              % (t["case"][:30], (t["architect"] or "-- none --")[:32],
                 (t["developer"] or "-")[:26]))

    print()
    print("architect recovered on %d of %d narratives (%.0f%%)"
          % (got, len(rows), 100.0 * got / len(rows) if rows else 0))

    if a.csv and rows:
        cols = ["case", "architect", "developer", "civil", "landscape",
                "attorney", "emails"]
        with open(a.csv, "w", newline="", encoding="utf-8") as fh:
            w = csv.DictWriter(fh, fieldnames=cols)
            w.writeheader()
            for r in rows:
                w.writerow({c: r.get(c, "") for c in cols})
        print("wrote %s" % a.csv)
    return 0


if __name__ == "__main__":
    sys.exit(main())
