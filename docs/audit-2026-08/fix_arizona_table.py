#!/usr/bin/env python3
"""Cut the committed schemes out of the Arizona table.

⛔ WHAT THIS FIXES, AFTER BEING RAISED THREE TIMES
    The table listed eleven cases. FIVE were "Final site plan", three of those
    filed in December 2025 -- Sierra Verde Townhomes, 4th St Residences, Deer
    Valley Apartments. A final site plan is a COMMITTED scheme: the design is
    finished and the architect was engaged long before. Those rows could not be
    acted on by anyone.

    The page argued for keeping them -- "the difference tells you how much of
    the design conversation is still open". That is a rationalisation for
    printing dead rows, and the client rejected it three separate times.

    The final COUNT stays, in a sentence, because it answers the literal
    question that was asked and the ratio is genuinely useful. The rows go.

⛔ AND THE TABLE MAY NOT IMPLY AN UNAPPOINTED ARCHITECT
    "TO BE BID" in PROFESS_NAME was tested and failed -- it appears on 59 of 92
    FINAL site plans, so it is a procurement-route field, not a design-team one.
    This table claims stage and date. Nothing more.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

# Verified live 28 Aug 2026 via tools/phoenix_preliminary_residential.py
ROWS = [
    ("12 May 2026", "2602491", "Momentum Apartments", "734 E Vineyard Rd"),
    ("6 May 2026", "2602468", "Senior Housing at 41st Ave", "2700 N 41st Ave"),
    ("21 Apr 2026", "2602361", "17th St Apartments", "1634 E Wood St"),
    ("9 Apr 2026", "2602015", "Sagewood Phase 4 &mdash; residential &amp; clubhouse",
     "4400 E Palo Verde Dr"),
    ("1 Apr 2026", "2601847", "Virginia Townhome Project", "4840 E Virginia Ave"),
    ("5 Mar 2026", "2601460", "2900 E Van Buren LIHTC", "2900 E Van Buren St"),
    ("6 Feb 2026", "2600890", "Lion Foundation Housing", "1415 E Wood St"),
    ("12 Jan 2026", "2600208", "Banyan Van Buren Multi-Family", "2220 E Van Buren St"),
]

TABLE = """      <div class="table-wrap">
        <table>
          <thead><tr><th>Submitted</th><th>Case</th><th>Project</th><th>Address</th></tr></thead>
          <tbody>
%s          </tbody>
        </table>
      </div>
      <p><strong>Every one of these is at preliminary stage, and all eight were filed this year.</strong> Of the 116 cases filed in the last eight months, <strong>74 are preliminary and 42 are final</strong>. We have left the 42 out: a final site plan is a committed scheme, and by the time one is filed the design is settled and the team has been engaged. The full set travels with this document if you want it.</p>
      <p><span class="muted">Two caveats we would rather state than have you find. Reading &ldquo;residential&rdquo; off a project name is a judgement, not a field in the record &mdash; treat eight as an indication. And none of this says an architect is unappointed; the record&rsquo;s professional field turned out to mean procurement route, not design team, so it carries no weight here. The nine verified openings earlier in this document are the ones that went through that test.</span></p>
"""


def rows_html():
    out = []
    for d, case, proj, addr in ROWS:
        out.append(
            "            <tr><td><strong>%s</strong></td><td><span class=\"muted\">%s</span></td>"
            "<td><strong>%s</strong></td><td>%s</td></tr>\n" % (d, case, proj, addr))
    return "".join(out)


def main():
    s = io.open(BODY, encoding="utf-8").read()
    m = re.search(r'    <section id="arizona">.*?\n    </section>\n', s, re.S)
    if not m:
        raise SystemExit("#arizona not found")
    sec = m.group(0)

    # The stage table is the SECOND table-wrap in the section; the first is the
    # permits-vs-cases comparison and stays.
    wraps = list(re.finditer(r'      <div class="table-wrap">.*?      </div>\n',
                             sec, re.S))
    if len(wraps) < 2:
        raise SystemExit("expected two tables in #arizona, found %d" % len(wraps))
    target = wraps[1]

    # Drop the paragraph that argued for keeping final rows, wherever it sits.
    sec2 = sec[:target.start()] + (TABLE % rows_html()) + sec[target.end():]
    sec2 = re.sub(
        r'\s*<p>The stage column matters as much as the name\..*?</p>', "",
        sec2, flags=re.S)
    sec2 = re.sub(
        r'\s*<p>Eleven of the forty-one, newest first\..*?</p>', "",
        sec2, flags=re.S)

    s = s[:m.start()] + sec2 + s[m.end():]
    io.open(BODY, "w", encoding="utf-8").write(s)

    check = re.search(r'<section id="arizona">.*?</section>', s, re.S).group(0)
    print("arizona table replaced")
    print("   'Final site plan' occurrences now: %d"
          % len(re.findall(r"Final site plan", check)))
    print("   Sierra Verde present:              %s"
          % ("YES - FAIL" if "Sierra Verde" in check else "no"))
    print("   rows in the new table:             %d"
          % len(re.findall(r"<tr><td><strong>", check)))


if __name__ == "__main__":
    main()
