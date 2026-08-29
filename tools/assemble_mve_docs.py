#!/usr/bin/env python3
"""Assemble the MVE documents from their parts, the same way every time.

WHY A SCRIPT AND NOT A SPLICE
    The pieces are: a <title> and comment header, the BdDocTemplate stylesheet,
    a print-specific stylesheet, and a body. Only the body changes between the
    two documents. Splicing that by hand once produced a 45 KB "full" document
    with no stylesheet at all, which renders as unstyled text and looks broken
    in exactly the way a client document must never look.

    The template stylesheet has no file of its own -- it is the FIRST <style>
    block inside mve-designteam.html, which is the canonical styled document.
    Take it from there rather than keeping a second copy that can drift.

⚠ CHECK THE OUTPUT, NOT THE SCRIPT
    A document that assembles without error can still be missing its stylesheet.
    This asserts that both style blocks survived and that the body carries the
    sections it was told to, and prints the byte counts so a silent truncation
    is visible.

USAGE
    python assemble_mve_docs.py
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(REPO, "docs", "audit-2026-08")
CANON = os.path.join(D, "mve-designteam.html")
HEAD = os.path.join(D, "mve-designteam-head.html")
PRINT = os.path.join(D, "mve-designteam-print.html")

DOCS = [
    ("mve-send-body.html", "mve-send.html",
     "Six Markets, and Where the Work Is Before It Is Let"),
    ("mve-research-body.html", "mve-research.html",
     "The Working Behind the Six-Market Record"),
]


def read(p):
    return io.open(p, encoding="utf-8").read()


def template_css():
    """The first <style> block of the canonical document."""
    s = read(CANON)
    m = re.search(r"<style>.*?</style>", s, re.S)
    if not m:
        raise SystemExit("no <style> block in %s" % CANON)
    css = m.group(0)
    if len(css) < 4000:
        raise SystemExit("stylesheet is only %d bytes -- that is not the "
                         "template, refusing to ship it" % len(css))
    return css


def main():
    head = read(HEAD)
    css = template_css()
    prn = read(PRINT)
    print("template stylesheet: %d bytes" % len(css))
    print("print stylesheet   : %d bytes" % len(prn))
    print()

    ok = True
    for src, out, title in DOCS:
        body = read(os.path.join(D, src))
        # give each document its own <title>; the head's is the old combined one
        h = re.sub(r"<title>.*?</title>",
                   "<title>%s</title>" % title, head, count=1, flags=re.S)
        doc = h + css + "\n" + prn + "\n" + body
        path = os.path.join(D, out)
        io.open(path, "w", encoding="utf-8").write(doc)

        nstyle = doc.count("<style>")
        nsect = len(re.findall(r'<section id="', doc))
        flag = "" if (nstyle == 2 and nsect >= 4) else "   <-- CHECK"
        if flag:
            ok = False
        print("  %-22s %7d bytes   %d style blocks, %d sections%s"
              % (out, len(doc), nstyle, nsect, flag))
    if not ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
