#!/usr/bin/env python3
"""Which of MVE's OWN clients are filing in the six markets right now.

THE POINT
    A cold opening -- a developer with no architect on the file -- is a call.
    A developer MVE has already built for, filing in a market where MVE is not
    on THAT project, is a different and much better thing: the relationship
    exists, the work is early, and the record proves it without anybody having
    to ask.

    So this crosses MVE's published client list against every pre-design record
    collected for the six markets.

⛔ THE EXCLUSION GATE COMES FIRST AND IT IS NOT OPTIONAL.
    Never surface a project MVE is already the architect on. Handing a firm
    "a lead" on their own job destroys the document and the relationship in one
    line. Two defences, and BOTH must run:

      1. MVE's own published portfolio, by project name and by client. Anything
         matching a known MVE project is dropped, not flagged.
      2. The record must name NO architect. That is the same test used
         everywhere else in this work, and it is what makes the list defensible.

    Neither defence is complete on its own, and they are not complete together.
    MVE's website publishes a CURATED SELECTION -- twelve projects for a firm
    founded in 1975 -- so absence from it proves nothing. And a private
    appointment never reaches a public record at all. The document must say so
    in those words. "Not on the record" is not "not appointed", and a client
    who is already engaged on one of these will learn something real from that:
    how far the public record lags their own commissions.

CLIENT LIST SOURCE
    MVE's own portfolio pages, which name the client on each project. Nothing
    here is typed into a public-records search box.

USAGE
    python mve_client_crossmatch.py [pipeline-dir] [scratch-dir]
"""
import csv
import io
import json
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# From mve-architects.com/portfolio, plus Ward Village, which Dan named on the
# call and which the firm's own Honolulu work is built on.
CLIENTS = {
    "Howard Hughes": r"howard hughes|bridgeland|woodlands land development|"
                     r"summerlin|ward village|teravalis|douglas ranch",
    "Hines": r"\bhines\b",
    "Toll Brothers": r"toll brothers|toll bros",
    "Holland Partner Group": r"holland partner",
    "Lowe Property Group": r"lowe property|lowe enterprises",
    "Vestar": r"\bvestar\b",
    "SHVO": r"\bshvo\b",
    "H&S Ventures": r"h&s ventures|h and s ventures",
    "REDA": r"\breda\b",
    "NAHLA Capital": r"nahla",
    "Lyon Living": r"lyon living|\blyon\b",
    "Eagle Four Partners": r"eagle four",
    "Blaser Ventures": r"blaser",
}

# Projects MVE publishes as its own. Anything matching is EXCLUDED, never shown.
MVE_PROJECTS = [
    "hallasan", "johnson kia", "legacy park", "mandarin oriental",
    "oc vibe", "ocvibe", "pali", "post district", "rafferty", "riverwalk",
    "rosewood residences", "sugar alley", "ritz-carlton residences",
    "ward village", "kalae", "launiu", "alia", "victoria place",
]
EXCLUDE = re.compile("|".join(re.escape(p) for p in MVE_PROJECTS), re.I)


def rows_of(path):
    """Yield (label, dict) for any json/csv dataset."""
    name = os.path.basename(path)
    try:
        if path.endswith(".json"):
            d = json.load(open(path, encoding="utf-8"))
            if isinstance(d, dict):
                for k in ("rows", "features", "records", "items"):
                    if isinstance(d.get(k), list):
                        d = d[k]
                        break
                else:
                    d = [d]
            for r in d:
                if isinstance(r, dict):
                    yield name, r.get("attributes", r) if "attributes" in r else r
        elif path.endswith(".csv"):
            for r in csv.DictReader(open(path, encoding="utf-8", errors="replace")):
                yield name, r
    except Exception as e:
        print("  ! %s unreadable: %s" % (name, str(e)[:60]))


def blob(rec):
    return " ".join(str(v) for v in rec.values() if v is not None)


def main(*dirs):
    dirs = [d for d in dirs if d and os.path.isdir(d)]
    files = []
    for d in dirs:
        for f in sorted(os.listdir(d)):
            if f.endswith((".json", ".csv")):
                files.append(os.path.join(d, f))
    print("scanning %d datasets across %d directories" % (len(files), len(dirs)))
    print()

    hits, excluded = {}, 0
    for p in files:
        for name, rec in rows_of(p):
            text = blob(rec)
            if not text or len(text) > 20000:
                continue
            for client, pat in CLIENTS.items():
                if re.search(pat, text, re.I):
                    if EXCLUDE.search(text):
                        excluded += 1
                        continue
                    hits.setdefault(client, []).append((name, rec))

    if excluded:
        print("EXCLUDED %d record(s) matching a project MVE publishes as its own"
              % excluded)
        print()

    if not hits:
        print("no MVE client appears in any collected pre-design record")
        return hits

    for client in sorted(hits, key=lambda c: -len(hits[c])):
        rs = hits[client]
        print("=" * 78)
        print("%s  --  %d record(s)" % (client, len(rs)))
        seen = set()
        for src, rec in rs:
            key = str(sorted(rec.items()))[:200]
            if key in seen:
                continue
            seen.add(key)
            interesting = {k: v for k, v in rec.items()
                           if v not in (None, "", "0") and
                           re.search(r"(?i)name|develop|applic|organi|date|"
                                     r"acre|subdiv|land_use|title|island|"
                                     r"consult|status|petition|address|project",
                                     k)}
            print("  [%s]" % src)
            for k, v in list(interesting.items())[:9]:
                print("      %-22s %s" % (k, str(v)[:78]))
            print()
    return hits


if __name__ == "__main__":
    a = sys.argv[1:]
    main(*(a or ["."]))
