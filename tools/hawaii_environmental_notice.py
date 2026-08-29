#!/usr/bin/env python3
"""Hawaii's early-signal source: The Environmental Notice, statewide.

WHY THIS AND NOT A COUNTY PERMIT DESK
    Hawaii's county records are the weakest of the six markets for this
    purpose. Honolulu's open data stops at 2016 building permits; its live
    GIS layers (Zoning Amendment Points, Unilateral Agreements) record
    ENACTED ordinances, which is the outcome, not the pipeline; and the HCDA
    board agenda -- the authority for Kakaako, where Ward Village sits -- is
    current but carries no new development permit in the September 2026 cycle.

    The state layer is far better. Under HRS Chapter 343, any project using
    state or county land or funds, or touching a shoreline, conservation
    district or a general plan amendment, must publish an Environmental
    Assessment or Impact Statement. The Office of Planning and Sustainable
    Development publishes those twice a month in The Environmental Notice,
    covering ALL FOUR COUNTIES in one document.

    Each entry names, in a fixed field order:

        Permit(s) ............ every approval the project still needs
        Approving Agency ..... the county or state body, with a named officer
        Applicant ............ the entity, address, a named person, phone, email
        Consultant ........... the planning firm, with a named person
        Status ............... where in the process it is
        plus a description that routinely gives the unit count

    An EA or EIS is prepared to get entitlements. It comes BEFORE construction
    documents, and the design team named on it is a planner, not an architect.
    That makes it the Hawaii equivalent of a Clark County pre-application, with
    considerably more on the record.

    Index:  planning.hawaii.gov/erp/environmental-notice/
    Issues: files.hawaii.gov/dbedt/erp/The_Environmental_Notice/<YYYY-MM-DD>-TEN.pdf
    Published on the 8th and 23rd of each month.

⚠ LIMITS
    * Chapter 343 is triggered by state or county involvement. A wholly private
      project on private land with no shoreline or conservation trigger will
      NOT appear. This is a rich seam, not a census - unlike Houston's plats.
    * "Consultant" is the environmental or planning consultant. It is not an
      architect and must never be reported as one.
    * A Final EIS acceptance means the entitlement work is finishing, which is
      later than a Draft EA. Read the determination in the title, not just the
      project name.
    * Hawaiian text uses the okina (U+02BB) and kahako. Print through a UTF-8
      wrapper or the Windows console will throw before you see the record.

USAGE
    python hawaii_environmental_notice.py fetch   [year] [dir]
    python hawaii_environmental_notice.py parse   [dir]
    python hawaii_environmental_notice.py housing [dir] [out.json]
"""
import io
import json
import os
import re
import sys
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

INDEX = "https://planning.hawaii.gov/erp/environmental-notice/"
FILES = "https://files.hawaii.gov/dbedt/erp/The_Environmental_Notice/"
UA = {"User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                     "AppleWebKit/537.36 (KHTML, like Gecko) "
                     "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")}

ISLANDS = ("OʻAHU", "OAHU", "MAUI", "KAUAʻI", "KAUAI", "HAWAIʻI", "HAWAII",
           "MOLOKAʻI", "MOLOKAI", "LĀNAʻI", "LANAI")
SECTION = re.compile(r"^\s*(%s)\s+EAS?/EISS?\s*$"
                     % "|".join(re.escape(i) for i in ISLANDS), re.I)
# ⛔ STOP AT THESE HEADINGS.
#    After the island sections the issue REPEATS earlier projects under
#    "PREVIOUSLY PUBLISHED DOCUMENTS OPEN FOR COMMENT", with no island heading
#    of their own. Keep reading and every repeat inherits whichever island was
#    last seen -- which is how "Hilo Benioff Medical Center" (Hawaii island)
#    ends up filed under Oahu and Maui in the same run.
END = re.compile(r"^\s*(PREVIOUSLY PUBLISHED|EXEMPTION LISTS|"
                 r"COASTAL ZONE MANAGEMENT|SHORELINE NOTICES|FEDERAL NOTICES|"
                 r"GLOSSARY)", re.I)
# Field labels appear at line start, in this order, inside every entry.
LABELS = ["HRS", "District(s)", "TMK(s)", "Permit(s)", "Approving",
          "Applicant", "Consultant", "Status", "Proposing", "Determination"]
LABEL_RE = re.compile(r"^(%s)" % "|".join(re.escape(l) for l in LABELS))


def fetch(year="2026", dest="hawaii-ten"):
    os.makedirs(dest, exist_ok=True)
    html = urllib.request.urlopen(
        urllib.request.Request(INDEX, headers=UA), timeout=120
    ).read().decode("utf-8", "replace")
    names = re.findall(r"(%s-\d\d-\d\d-TEN\.pdf)" % year, html)
    names = sorted(set(names))
    print("index lists %d %s issues" % (len(names), year))
    for n in names:
        p = os.path.join(dest, n)
        if os.path.exists(p):
            print("  have    %s" % n)
            continue
        data = urllib.request.urlopen(
            urllib.request.Request(FILES + n, headers=UA), timeout=240).read()
        with open(p, "wb") as fh:
            fh.write(data)
        print("  fetched %-24s %8d bytes" % (n, len(data)))
    return [os.path.join(dest, n) for n in names]


def pages_text(path):
    import pypdf
    r = pypdf.PdfReader(path)
    return [pg.extract_text() or "" for pg in r.pages]


def entries(path):
    """Split one issue into project entries, keeping the island it sat under."""
    text = "\n".join(pages_text(path))
    lines = [l.rstrip() for l in text.split("\n")]
    issue = os.path.basename(path)[:10]

    out, island, cur = [], None, None
    for i, raw in enumerate(lines):
        l = raw.strip()
        m = SECTION.match(l)
        if m:
            island = m.group(1)
            continue
        if END.match(l):
            if cur:
                out.append(cur)
                cur = None
            island = None
            continue
        if island is None:
            continue
        # A title line carries an en/em dash and a parenthesised or named
        # determination, and is not itself a field label.
        is_title = (" – " in l or " — " in l) and not LABEL_RE.match(l) \
            and 8 < len(l) < 160 and not l.startswith(("•", "TMK", "("))
        if is_title:
            if cur:
                out.append(cur)
            cur = {"issue": issue, "island": island, "title": l, "body": []}
            continue
        if cur is not None:
            if l:
                cur["body"].append(l)
            if len(cur["body"]) > 90:
                out.append(cur)
                cur = None
    if cur:
        out.append(cur)

    for e in out:
        e.update(fields(e["body"]))
        e["text"] = " ".join(e["body"])
        del e["body"]
    return out


def fields(body):
    """Pull the labelled fields out of an entry body."""
    got, cur, buf = {}, None, []

    def flush():
        if cur:
            got[cur] = re.sub(r"\s+", " ", " ".join(buf)).strip()

    for l in body:
        m = LABEL_RE.match(l)
        if m:
            flush()
            cur = m.group(1)
            buf = [l[len(cur):].strip(" :–—")]
        elif cur:
            buf.append(l)
    flush()
    out = {}
    for k, dest in (("Applicant", "applicant"), ("Consultant", "consultant"),
                    ("Approving", "agency"), ("Status", "status"),
                    ("District(s)", "district"), ("Permit(s)", "permits")):
        v = got.get(k, "")
        if dest in ("applicant", "consultant", "agency"):
            # first clause is the entity; the rest is address and contact
            v = re.split(r";|\s{2,}", v)[0].strip()
        out[dest] = v[:220]
    return out


HOUSING = re.compile(
    r"(?i)housing unit|residential unit|dwelling unit|multi-?family|"
    r"apartment|condominium|affordable housing|workforce housing|"
    r"residential community|mixed-?use|senior housing|\d[\d,]*\s+units")


def housing(d="hawaii-ten", out=None):
    files = sorted(f for f in os.listdir(d) if f.endswith(".pdf"))
    all_e, hits = [], []
    for f in files:
        es = entries(os.path.join(d, f))
        all_e.extend(es)
        for e in es:
            if HOUSING.search(e["title"] + " " + e["text"]):
                hits.append(e)
    print("%d issues, %d project entries, %d residential"
          % (len(files), len(all_e), len(hits)))
    print()
    for e in hits:
        units = re.findall(r"(\d[\d,]{1,6})\s+(?:housing |residential |dwelling )?units",
                           e["text"], re.I)
        print("-" * 78)
        print("  %s  %s" % (e["issue"], e["island"]))
        print("  %s" % e["title"][:104])
        print("     applicant : %s" % e["applicant"][:92])
        print("     consultant: %s" % e["consultant"][:92])
        print("     agency    : %s" % e["agency"][:92])
        print("     status    : %s" % e["status"][:92])
        if units:
            print("     units     : %s" % ", ".join(units[:4]))
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(hits, fh, indent=1, ensure_ascii=False)
        print("\nwrote %s" % out)
    return hits


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "housing"
    a = sys.argv[2:]
    if cmd == "fetch":
        fetch(*(a or ["2026", "hawaii-ten"]))
    elif cmd == "parse":
        d = a[0] if a else "hawaii-ten"
        for f in sorted(os.listdir(d)):
            if f.endswith(".pdf"):
                es = entries(os.path.join(d, f))
                print("%-24s %d entries" % (f, len(es)))
                for e in es:
                    print("    [%-6s] %s" % (e["island"][:6], e["title"][:88]))
    elif cmd == "housing":
        housing(*(a or ["hawaii-ten"]))
    else:
        raise SystemExit(__doc__)
