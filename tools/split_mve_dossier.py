#!/usr/bin/env python3
"""Split the MVE material into the document that gets SENT and the research.

WHY
    They are two different documents and merging them produced a 21-page hybrid
    that does neither job. Dan asked where the work is. He knows his own
    markets; a concentration analysis is not news to him, it is our evidence
    that the markets are worth working, and it belongs in a companion he can
    ask for.

    SEND      what is in each market's record, how current it is, and the
              actual projects. Direct, short, every figure checkable.
    RESEARCH  the concentration work, the design-build read, the cross-market
              firms - the argument, with its working.

    Both are assembled from the SAME section blocks in mve-designteam-body.html,
    so a figure corrected once is corrected in both and they cannot drift.

USAGE
    python split_mve_dossier.py
"""
import io
import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(REPO, "docs", "audit-2026-08")
BODY = os.path.join(D, "mve-designteam-body.html")

SEND = ["facts", "arizona", "everywhere", "reach", "leads", "markets", "closed"]
RESEARCH = ["finding", "raleigh-test", "houston-miami", "tempo", "designbuild",
            "crossmarket"]

SEND_HERO = """
<header class="hero">
  <div class="hero__wrap fade">
    <p class="hero__eyebrow">KOR Structural &nbsp;·&nbsp; prepared for MVE + Partners &nbsp;·&nbsp; 28 August 2026</p>
    <h1>Six markets. What each one's<br>public record holds today.</h1>
    <p class="hero__lede">The Arizona submitted-projects search you asked for, and the same search pointed at Nevada, Hawaii, Houston, Charlotte and Miami. Checked against every source on 28 August: Raleigh had been updated that morning, Houston and Miami the day before. Where a record runs behind, it says so and by how much.</p>
    <div class="hero__meta">
      <span>373 open Phoenix cases</span>
      <span>390 Houston projects since 1 June</span>
      <span>Raleigh current to today</span>
    </div>
  </div>
</header>
"""

RESEARCH_HERO = """
<header class="hero">
  <div class="hero__wrap fade">
    <p class="hero__eyebrow">KOR Structural &nbsp;·&nbsp; prepared for MVE + Partners &nbsp;·&nbsp; 28 August 2026</p>
    <h1>The working behind the numbers.</h1>
    <p class="hero__lede">This is the companion to the six-market record: how the design-team figures were arrived at, what each rests on, and where a market cannot be measured at all. It is here because a number is worth what its method is worth, not because any of it needs explaining to you.</p>
    <div class="hero__meta">
      <span>Houston: a census, 806 firms</span>
      <span>Arizona: two independent samples</span>
      <span>Raleigh: 11 of 92 filings</span>
    </div>
  </div>
</header>
"""


def build(ids, hero, out_path, title_note, drop_facts_box=False):
    s = io.open(BODY, encoding="utf-8").read()
    blocks = dict(
        (m.group(1), m.group(0)) for m in
        re.finditer(r'    <section id="([a-z-]+)">.*?\n    </section>\n', s, re.S))

    if drop_facts_box and "facts" in blocks:
        # The four-point box is concentration commentary and it overflowed the
        # facts page, stranding three-quarters of the next one. It belongs in
        # the research companion. Without it the facts sheet is exactly one page.
        blocks["facts"] = re.sub(
            r'      <div class="box box--tip">.*?</div>\n', "",
            blocks["facts"], flags=re.S)
    labels = dict(re.findall(r'<li><a href="#([a-z-]+)">(.*?)</a></li>', s))
    missing = [i for i in ids if i not in blocks]
    if missing:
        raise SystemExit("missing sections: %s" % missing)

    toc = "\n".join('      <li><a href="#%s">%s</a></li>' % (i, labels[i])
                    for i in ids)
    body = hero + """
<div class="shell">
  <nav class="toc" aria-label="Contents">
    <p class="toc__title">On this page</p>
    <ol>
""" + toc + """
    </ol>
  </nav>
  <main>

""" + "\n".join(blocks[i] for i in ids) + """
    <footer>
      <p class="muted">""" + title_note + """ Every figure is drawn from public municipal records or from the published statements of the parties named. Where something could only be sourced to secondary reporting it was left out rather than hedged. Happy to walk through any single number on a call.</p>
    </footer>
  </main>
</div>
"""
    io.open(out_path, "w", encoding="utf-8").write(body)
    return len(ids)


if __name__ == "__main__":
    n1 = build(SEND, SEND_HERO, os.path.join(D, "mve-send-body.html"),
               "KOR Structural, prepared for MVE + Partners, 28 August 2026.",
               drop_facts_box=True)
    n2 = build(RESEARCH, RESEARCH_HERO, os.path.join(D, "mve-research-body.html"),
               "Companion to the six-market record, 28 August 2026.")
    print("send document   : %d sections -> mve-send-body.html" % n1)
    print("research companion: %d sections -> mve-research-body.html" % n2)
