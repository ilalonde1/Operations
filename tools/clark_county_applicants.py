#!/usr/bin/env python3
"""Who is behind each Clark County pre-application.

WHY THIS IS THE ONE THAT MATTERS
    Clark County publishes "Application Prereview" -- a developer taking a
    scheme to the county BEFORE filing anything binding. It is the earliest
    formal signal found in any of the six markets, and the county posts them
    within a day.

    The search grid gives a record number and nothing else. But the record page
    carries an OWNERSHIP / INTEREST DISCLOSURE block naming the officers of the
    entity behind the filing - which is exactly the shell-resolution problem
    solved, by the county, for free. Record 26-101519 discloses David O'Reilly,
    Chief Executive Officer: Howard Hughes.

    So this turns "sixteen multifamily pre-applications" into sixteen named
    developers with a live scheme and no drawing set.

⚠ LIMITS
    * A disclosure names officers of the applicant entity, which may still be a
      single-purpose LLC. A recognisable name is a resolution; an unrecognisable
      one is a lead to a registry lookup, not a dead end.
    * TMP-prefixed records are incomplete submissions and usually disclose
      nothing. They are reported separately rather than counted as misses.
    * Being on a pre-application says nothing about whether an architect is
      engaged. Clark County does not publish the design team at this stage.

USAGE
    python clark_county_applicants.py [records.csv] [limit]
"""
import csv
import os
import re
import sys
import time
import urllib.parse
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                    "AppleWebKit/537.36 (KHTML, like Gecko) "
                    "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0"}
URL = ("https://aca-prod.accela.com/CLARKCO/Cap/GlobalSearchResults.aspx"
       "?QueryText=%s")


def text_lines(html):
    html = re.sub(r"(?is)<(script|style).*?</\1>", " ", html)
    out = []
    for l in re.sub(r"<[^>]*>", "\n", html).split("\n"):
        l = l.replace("&nbsp;", " ").replace("&#39;", "'").replace("&amp;", "&")
        l = l.strip()
        if l:
            out.append(l)
    return out


def disclosure(lines):
    """Pull the Full Name / Title pairs out of the ownership disclosure."""
    people, i = [], 0
    while i < len(lines):
        if lines[i].lower().startswith("full name"):
            name = lines[i + 1] if i + 1 < len(lines) else ""
            title = ""
            if i + 3 < len(lines) and lines[i + 2].lower().startswith("title"):
                title = lines[i + 3]
            if name and not name.lower().startswith(("title", "full name")):
                people.append((name, title))
            i += 4
            continue
        i += 1
    return people


def address_of(lines):
    for i, l in enumerate(lines):
        if re.match(r"(?i)^(work location|location|address)$", l) and i + 1 < len(lines):
            nxt = lines[i + 1]
            if re.search(r"\d", nxt) and len(nxt) > 8:
                return nxt
    return ""


def fetch(record):
    html = urllib.request.urlopen(
        urllib.request.Request(URL % urllib.parse.quote(record), headers=UA),
        timeout=90).read().decode("utf-8", "replace")
    return text_lines(html)


def main(path, limit=20):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    rows = [r for r in rows if "Prereview" in r.get("record_type", "")]

    def key(r):
        m, d, y = r["date"].split("/")
        return (y, m, d)

    rows.sort(key=key, reverse=True)
    real = [r for r in rows if not r["record_number"].startswith("26TMP")]
    tmp = [r for r in rows if r["record_number"].startswith("26TMP")]
    print("pre-application records: %d  (%d numbered, %d TMP/incomplete)"
          % (len(rows), len(real), len(tmp)))
    print()
    named = 0
    for r in real[:limit]:
        try:
            lines = fetch(r["record_number"])
            people = disclosure(lines)
            addr = address_of(lines)
        except Exception as e:
            print("  %-12s %-11s FETCH FAILED %s"
                  % (r["date"], r["record_number"], str(e)[:40]))
            continue
        who = "; ".join("%s%s" % (n, " (%s)" % t if t else "")
                        for n, t in people[:3]) or "(no disclosure published)"
        if people:
            named += 1
        print("  %-11s %-11s %s" % (r["date"], r["record_number"], who[:96]))
        if addr:
            print("  %-11s %-11s %s" % ("", "", addr[:96]))
        time.sleep(0.6)
    print()
    print("disclosed an applicant: %d of %d fetched" % (named, min(limit, len(real))))


if __name__ == "__main__":
    default = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           "docs", "audit-2026-08", "mve-pipeline",
                           "clark-county-multifamily-2026.csv")
    main(sys.argv[1] if len(sys.argv) > 1 else default,
         int(sys.argv[2]) if len(sys.argv) > 2 else 20)
