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

# "signal" leads: it is the only section about work that has not been let.
#
# "leads" is BACK IN SEND, and the reason matters. It was moved out when it was
# built on Houston TDLR registrations -- those are filed BY THE DESIGN FIRM, so
# that list recorded who had already won, while being presented as what was
# winnable. It has since been rebuilt on the Planning Commission's own plat
# spreadsheets, which are filed by the developer to divide land before any
# building is designed, and which name the developer on 100% of rows. That is
# the opposite end of the process and it belongs in front of the client.
#
# "hawaii" is new: it was one of Dan's six markets with no live source at all.
# "openings" sits at position 2, straight after the Howard Hughes page: it is
# the only section that is a list of work that has not been let, and every row
# in it survived an individual architect check. Six that did not are named in
# its own exclusions box.
SEND = ["signal", "openings", "facts", "hawaii", "arizona", "leads",
        "everywhere", "reach", "markets", "closed"]
RESEARCH = ["finding", "raleigh-test", "houston-miami", "tempo",
            "designbuild", "crossmarket"]

SEND_HERO = """
<header class="hero">
  <div class="hero__wrap fade">
    <p class="hero__eyebrow">KOR Structural &nbsp;·&nbsp; prepared for MVE + Partners &nbsp;·&nbsp; 28 August 2026</p>
    <h1>Nine schemes in your six markets<br>where nobody has been hired yet.</h1>
    <p class="hero__lede">Each one checked twice &mdash; the case file, environmental assessment or site plan read in full including the drawing title block, then a separate trade-press check. Six more did not survive that and are named inside. Alongside them: the Arizona search you asked for and an honest read on which layer of it is already spoken for, and where Howard Hughes &mdash; your Ward Village client &mdash; is putting money in three markets you do not serve them in.</p>
    <div class="hero__meta">
      <span>Nine verified, six removed</span>
      <span>Newest filing: 27 August</span>
      <span>Nothing you already design</span>
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
