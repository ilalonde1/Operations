#!/usr/bin/env python3
"""Fetch pages that refuse plain HTTP, using the Edge already on this machine.

WHY
    Some portals answer curl and the plain fetcher with 403 and a WAF challenge
    page, while serving a real browser normally. Charlotte's rezoning petition
    pages are the case in point: every petition detail page returns 403 to curl
    even with a full browser header set, and a 1.7 KB "Powered and protected by"
    challenge when it does answer. A real browser passes because it executes the
    challenge.

    This is the same approach the Phoenix PDD work used and is deliberately the
    PYTHON Playwright driven against `channel="msedge"` -- the Edge that
    Format-BdWebPdf.ps1 already proved is installed. No ~150 MB Chromium
    download, and nothing to do with the .NET Microsoft.Playwright inside
    Kor.Opportunities.Data/Ingestion/Scraping.

⚠ USE IT WHEN HTTP HAS ACTUALLY FAILED, NOT BY DEFAULT
    A browser is ~2 s per page against ~100 ms for curl. Reach for it when a
    site 403s or returns a challenge, not as the normal path.

⚠ A CHALLENGE PAGE IS NOT AN ERROR, SO CHECK THE CONTENT
    The WAF returns HTTP 200 with a near-empty body. Any caller must assert on
    the text it wanted, never on the status code. `--min-chars` does that here:
    a page shorter than the threshold is reported as BLOCKED, not as success.

USAGE
    python browser_fetch.py <url> [url ...] --out DIR [--min-chars N]
                                           [--pdfs] [--wait MS] [--visible]

    --pdfs   also download every PDF linked from each page, into DIR
"""
import argparse
import os
import re
import sys
import time
import urllib.parse

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    raise SystemExit("pip install playwright   (no browser download needed; "
                     "this uses the installed Edge via channel='msedge')")


def slug(url):
    p = urllib.parse.urlparse(url)
    s = (p.path.strip("/") or p.netloc).replace("/", "-")
    s = re.sub(r"[^A-Za-z0-9._-]", "-", s)
    return (s or "page")[:120]


def text_of(html):
    html = re.sub(r"(?is)<(script|style|noscript).*?</\1>", " ", html)
    t = re.sub(r"<[^>]+>", "\n", html)
    for a, b in (("&nbsp;", " "), ("&amp;", "&"), ("&#39;", "'"),
                 ("&quot;", '"'), ("&rsquo;", "'"), ("&mdash;", "-")):
        t = t.replace(a, b)
    return "\n".join(l.strip() for l in t.split("\n") if l.strip())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("urls", nargs="+")
    ap.add_argument("--out", default="browser-out")
    ap.add_argument("--min-chars", type=int, default=1200,
                    help="below this the page is treated as a WAF challenge")
    ap.add_argument("--wait", type=int, default=3500,
                    help="ms to settle after load, for the challenge to clear")
    ap.add_argument("--pdfs", action="store_true")
    ap.add_argument("--visible", action="store_true")
    a = ap.parse_args()

    os.makedirs(a.out, exist_ok=True)
    ok = blocked = 0

    with sync_playwright() as pw:
        browser = pw.chromium.launch(channel="msedge", headless=not a.visible)
        ctx = browser.new_context(
            viewport={"width": 1440, "height": 2200},
            user_agent=("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                        "AppleWebKit/537.36 (KHTML, like Gecko) "
                        "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0"),
            locale="en-US")
        page = ctx.new_page()

        for url in a.urls:
            name = slug(url)
            try:
                page.goto(url, wait_until="domcontentloaded", timeout=90000)
                page.wait_for_timeout(a.wait)
                try:
                    page.wait_for_load_state("networkidle", timeout=20000)
                except Exception:
                    pass
                html = page.content()
            except Exception as e:
                print("  %-42s ERROR %s" % (name[:42], str(e)[:52]))
                continue

            txt = text_of(html)
            if len(txt) < a.min_chars:
                blocked += 1
                print("  %-42s BLOCKED (%d chars) %s"
                      % (name[:42], len(txt), txt[:60].replace("\n", " ")))
                continue

            ok += 1
            with open(os.path.join(a.out, name + ".html"), "w",
                      encoding="utf-8") as fh:
                fh.write(html)
            with open(os.path.join(a.out, name + ".txt"), "w",
                      encoding="utf-8") as fh:
                fh.write(txt)
            print("  %-42s ok  %6d chars" % (name[:42], len(txt)))

            if a.pdfs:
                links = set()
                for href in re.findall(r'href="([^"]+)"', html):
                    if ".pdf" in href.lower():
                        links.add(urllib.parse.urljoin(url, href))
                for i, L in enumerate(sorted(links)):
                    fn = os.path.join(a.out, "%s--%02d.pdf" % (name[:60], i))
                    if os.path.exists(fn):
                        continue
                    try:
                        r = ctx.request.get(L, timeout=180000)
                        if r.ok:
                            with open(fn, "wb") as fh:
                                fh.write(r.body())
                            print("        pdf %-56s %8d bytes"
                                  % (L.rsplit("/", 1)[-1][:56],
                                     os.path.getsize(fn)))
                    except Exception as e:
                        print("        pdf FAILED %s %s"
                              % (L[-50:], str(e)[:40]))
            time.sleep(0.3)

        browser.close()

    print()
    print("%d fetched, %d blocked -> %s" % (ok, blocked, a.out))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
