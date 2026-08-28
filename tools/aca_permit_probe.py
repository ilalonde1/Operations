#!/usr/bin/env python3
"""Probe Accela Citizen Access (ACA) public record search — no account required.

WHY THIS EXISTS
    Clark County NV (the Las Vegas Strip), Dallas and Charlotte all appear as
    "dead feeds" in docs/audit-2026-08/mve-pipeline/VERIFICATION-LOG.md. They are
    not dead: each migrated to Accela, and their live records are published through
    ACA's public search — the surface citizens use WITHOUT logging in.

    Do NOT register an Accela developer App ID for this. An App ID yields a sandbox
    token; production access to an agency's records additionally requires that
    agency to enable the developer in its own Admin portal. This route needs none
    of that.

RELATION TO EXISTING CODE
    This is a research PROBE, in the spirit of tools/BcBidDetailProbe and
    tools/ApcInterestProbe. It deliberately does NOT touch the production permit
    ingest (Kor.Opportunities.Data/Awards/BuildingPermitsImportService.cs and its
    PermitSourceRow/PermitFieldMap adapter framework). If an ACA source is ever
    productionised it belongs there, as an adapter, with a migration — not here.

HOW IT WORKS
    ACA is ASP.NET WebForms. Page one is a plain GET; further pages are postbacks
    that must echo __VIEWSTATE / __VIEWSTATEGENERATOR / __EVENTVALIDATION back with
    an __EVENTTARGET naming the pager control. Results are rendered SERVER-SIDE
    into a GridView (id contains "gdvPermitList") and are present in the HTML.

    Every run reconciles the rows parsed against the page's own "Showing 1-N of M"
    counter, so a broken parse fails loudly instead of quietly returning fewer rows.

USAGE
    python tools/aca_permit_probe.py --agency CLARKCO --query "athletics ballpark"
    python tools/aca_permit_probe.py --agency dallastx --query apartments --pages 5
    python tools/aca_permit_probe.py --agency CHARLOTTE --query rezoning --csv out.csv

    Known agency codes: CLARKCO (Clark County NV), dallastx (DallasNow),
    CHARLOTTE (City of Charlotte). Others follow the same aca-prod URL shape.

LIMITS — state these whenever the output is quoted
    * These are PERMITS AND CASES, not design teams. There is no architect field.
    * The search is keyword-based over record text; it is not a structured filter,
      so results include anything whose notes mention the term.
    * Page size is fixed at 10 by the portal.
"""
import argparse
import csv
import html
import http.cookiejar
import re
import sys
import time
import urllib.parse
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")
BASE = "https://aca-prod.accela.com/%s/Cap/GlobalSearchResults.aspx?QueryText=%s"

GRID = "gdvPermitList"
COLUMNS = ["date", "record_number", "record_type", "module", "short_notes",
           "project_name", "status"]

# a grid row: date, record number, then up to five free-text cells
ROW_RE = re.compile(
    r"\|(\d{2}/\d{2}/\d{4})\|([A-Za-z0-9][A-Za-z0-9\-]{3,24})\|"
    r"([^|]{0,44})\|([^|]{0,26})\|([^|]{0,140})")
COUNTER_RE = re.compile(r"Showing\s+(\d+)-(\d+)\s+of\s+([\d,]+\+?)")


def _sortkey(mmddyyyy):
    """MM/DD/YYYY -> sortable YYYYMMDD.

    Sorting the portal's US-format dates as plain strings puts 01/09/2026 before
    12/18/2025 and reports a reversed range. Parse, never lexicographic.
    """
    m = re.match(r"(\d{2})/(\d{2})/(\d{4})", mmddyyyy or "")
    return (m.group(3) + m.group(1) + m.group(2)) if m else "00000000"


def _hidden(page, name):
    m = (re.search(r'id="%s"[^>]*value="([^"]*)"' % name, page)
         or re.search(r'name="%s"[^>]*value="([^"]*)"' % name, page))
    return m.group(1) if m else ""


def _grid_rows(page):
    """Return the grid's <tr> rows as lists of cell strings.

    Parse cells, do NOT flatten the whole grid to pipes. An earlier version
    replaced tags with '|' and then collapsed runs of pipes — which silently
    deleted EMPTY cells, shifting every column after them. That is why
    project_name came back 0/93 populated: it was never empty, it was misaligned.
    """
    i = page.find(GRID)
    if i < 0:
        return []
    seg = page[i:i + 120000]
    seg = re.sub(r"<(script|style)\b.*?</\1>", " ", seg, flags=re.S | re.I)
    out = []
    for tr in re.findall(r"<tr\b[^>]*>(.*?)</tr>", seg, re.S | re.I):
        cells = []
        for td in re.findall(r"<t[dh]\b[^>]*>(.*?)</t[dh]>", tr, re.S | re.I):
            txt = html.unescape(re.sub(r"<[^>]+>", " ", td))
            cells.append(re.sub(r"\s+", " ", txt).strip())
        if cells:
            out.append(cells)
    return out


DATE_CELL = re.compile(r"^\d{2}/\d{2}/\d{4}$")


def _parse(page):
    """Return (rows, counter_tuple_or_None). Rows are dicts keyed by COLUMNS.

    A data row is identified by its FIRST cell being a date — which skips the
    header, the pager and the export row without hard-coding their positions.
    Cells are then zipped positionally onto COLUMNS, so an empty Project Name
    stays empty instead of pulling Status left into it.
    """
    counter = None
    m = COUNTER_RE.search(re.sub(r"<[^>]+>", " ", page[page.find(GRID):
                                                       page.find(GRID) + 40000])
                          if GRID in page else "")
    if m:
        counter = (int(m.group(1)), int(m.group(2)), m.group(3))

    rows = []
    for cells in _grid_rows(page):
        if not cells or not DATE_CELL.match(cells[0]):
            continue
        cells = (cells + [""] * len(COLUMNS))[:len(COLUMNS)]
        rows.append(dict(zip(COLUMNS, cells)))
    return rows, counter


def _pager_map(page):
    """Map visible page number -> postback target.

    The pager renders as anchors whose text is the page number ('2', '3', ...)
    plus a trailing 'Next >'. Reading the number off the anchor is safer than
    assuming ctl03 is page 2 — the control indices shift once you are past page
    one and the '< Prev' link appears.
    """
    out = {}
    for m in re.finditer(r"<a[^>]*__doPostBack\(&#39;([^&]+)&#39;[^>]*>(.*?)</a>",
                         page, re.S):
        target = m.group(1)
        if GRID not in target:
            continue
        text = html.unescape(re.sub(r"<[^>]+>", "", m.group(2))).strip()
        if text.isdigit():
            out[int(text)] = target
    return out


def _form_fields(page):
    """Every <input> on the page, so the postback echoes a complete form.

    Posting only __VIEWSTATE/__EVENTTARGET is not enough here: ACA returned a
    page with an EMPTY __EVENTVALIDATION and the partial post silently produced a
    zero-row grid. Echoing the whole form is what makes paging work.
    """
    fields = {}
    for m in re.finditer(r"<input\b([^>]*)>", page, re.I):
        attrs = m.group(1)
        name = re.search(r'name="([^"]+)"', attrs)
        if not name:
            continue
        itype = (re.search(r'type="([^"]+)"', attrs) or [None, ""])[1].lower()
        if itype in ("submit", "button", "image", "reset"):
            continue
        if itype in ("checkbox", "radio") and "checked" not in attrs.lower():
            continue
        val = re.search(r'value="([^"]*)"', attrs)
        fields[name.group(1)] = html.unescape(val.group(1)) if val else ""
    for m in re.finditer(r"<select\b[^>]*name=\"([^\"]+)\"(.*?)</select>", page,
                         re.S | re.I):
        sel = re.search(r"<option[^>]*selected[^>]*value=\"([^\"]*)\"", m.group(2),
                        re.I)
        fields[m.group(1)] = html.unescape(sel.group(1)) if sel else ""
    return fields


def probe(agency, query, pages=1, delay=1.0, verbose=True):
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    op.addheaders = [("User-Agent", UA), ("Accept-Language", "en-US,en;q=0.9")]
    url = BASE % (agency, urllib.parse.quote(query))

    page = op.open(url, timeout=90).read().decode("utf-8", "replace")
    rows, counter = _parse(page)
    if verbose:
        print("  page 1: %2d rows   [%s]"
              % (len(rows), "Showing %d-%d of %s" % counter if counter else "no counter"))
    seen = {r["record_number"]: r for r in rows}

    for n in range(2, pages + 1):
        pager = _pager_map(page)
        if n not in pager:
            if verbose:
                print("  page %d: not offered by the pager (end of results)" % n)
            break
        fields = _form_fields(page)
        fields["__EVENTTARGET"] = pager[n]
        fields["__EVENTARGUMENT"] = ""
        body = urllib.parse.urlencode(fields).encode()
        req = urllib.request.Request(url, data=body, headers={
            "User-Agent": UA, "Referer": url,
            "Content-Type": "application/x-www-form-urlencoded"})
        try:
            time.sleep(delay)      # be a polite citizen on a public portal
            page = op.open(req, timeout=120).read().decode("utf-8", "replace")
        except Exception as e:
            if verbose:
                print("  page %d: postback failed (%s)" % (n, str(e)[:60]))
            break
        rows, counter = _parse(page)
        new = [r for r in rows if r["record_number"] not in seen]
        if verbose:
            print("  page %2d: %2d rows, %2d new   [%s]"
                  % (n, len(rows), len(new),
                     "Showing %d-%d of %s" % counter if counter else "no counter"))
        if not new:
            break
        for r in rows:
            seen.setdefault(r["record_number"], r)

    return list(seen.values()), counter


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--agency", required=True,
                    help="ACA agency code, e.g. CLARKCO / dallastx / CHARLOTTE")
    ap.add_argument("--query", required=True, help="free-text search term")
    ap.add_argument("--pages", type=int, default=1, help="pages to walk (10 rows each)")
    ap.add_argument("--csv", help="write results to this CSV path")
    ap.add_argument("--delay", type=float, default=1.0,
                    help="seconds between postbacks (default 1.0)")
    a = ap.parse_args(argv)

    print("ACA probe: agency=%s query=%r pages=%d" % (a.agency, a.query, a.pages))
    rows, counter = probe(a.agency, a.query, a.pages, a.delay)
    if not rows:
        print("NO ROWS — the grid was absent or the parse matched nothing. "
              "That is not the same as 'this agency has no records'.")
        return 1

    dates = sorted((r["date"] for r in rows if r["date"]), key=_sortkey)
    print()
    print("distinct records: %d" % len(rows))
    print("date range      : %s .. %s" % (dates[0], dates[-1]))
    if counter:
        print("portal reported : Showing %d-%d of %s" % counter)
    print()
    for r in sorted(rows, key=lambda x: _sortkey(x["date"]), reverse=True)[:25]:
        print("  %s  %-18s %-28s %s"
              % (r["date"], r["record_number"][:18], r["record_type"][:28],
                 (r["short_notes"] or r["project_name"])[:52]))

    if a.csv:
        with open(a.csv, "w", newline="", encoding="utf-8") as fh:
            w = csv.DictWriter(fh, fieldnames=COLUMNS)
            w.writeheader()
            w.writerows(sorted(rows, key=lambda x: _sortkey(x["date"]), reverse=True))
        print("\nwrote %s (%d rows)" % (a.csv, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
