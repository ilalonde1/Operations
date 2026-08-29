#!/usr/bin/env python3
"""Rewrite the "closed developers" section in plain American English.

⛔ WHAT WAS WRONG WITH IT

  "Where not to spend the call"
      Jargon. "The call" is a business-development idiom that means nothing to
      the reader. Say what it is: developers he can cross off.

  "This falls out of the same data and is the part we would want if the
   positions were reversed."
      Us, talking about ourselves and our data, in a client document. Cut.

  "the most active developer in the set" / "anywhere in the 50"
      "The set" and "the 50" refer to a dataset the reader cannot see. Any
      denominator quoted to him has to be named in the same sentence.

  "another way of saying the same thing as the first section"
      Document navigation. He is reading it; he does not need a pointer back.

WHAT IT SAYS NOW
    The same four developers and the same facts, stated as findings: these firms
    own their design capability, so the work is structurally unavailable. That
    is worth a page because it is where NOT to spend effort, and nothing else in
    the document tells him that.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

SECTION = """    <section id="closed">
      <p class="kicker">Four you can cross off</p>
      <h2>Four developers who will never hire an outside architect</h2>
      <p class="lede">Not because they have turned anyone down &mdash; because of how they are built. Each of these owns its design capability outright, so the work is structurally unavailable however good the pitch.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Developer</th><th>Projects</th><th>What the record shows</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>Creation Equity</strong></td>
              <td><strong>6</strong><br><span class="muted">the most active of the fifty largest Arizona projects</span></td>
              <td>LGE Design Build on all six &mdash; five as design-builder, one as contractor alongside GFF Design. <strong>No outside architect appears on any Creation Equity project</strong></td>
            </tr>
            <tr>
              <td><strong>Ryan Companies US</strong></td>
              <td><strong>4</strong></td>
              <td>Developer and general contractor on all four, and it brings its own architect &mdash; Butler Design Group on three, Deutsch on the fourth</td>
            </tr>
            <tr>
              <td><strong>Statesman Group</strong></td>
              <td><strong>1</strong><br><span class="muted">565,729 sq ft, six buildings</span></td>
              <td>Developer and contractor both. No architect named at all</td>
            </tr>
            <tr>
              <td><strong>StreetLights Residential</strong><br><span class="muted">Houston</span></td>
              <td><strong>The Langley</strong><br><span class="muted">134 units</span></td>
              <td>Fully vertically integrated: StreetLights Creative Studio is architect and interior designer, SLR Construction is the general contractor. <strong>Every seat in-house</strong></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p><strong>Butler Design Group and Ryan Companies are the one pairing that repeats.</strong> Across the fifty largest Arizona projects they appear together three times, and no other architect-to-contractor relationship recurs even twice. Every other pairing in the state is a one-off.</p>
    </section>

"""


def main():
    s = io.open(BODY, encoding="utf-8").read()
    pat = r'    <section id="closed">.*?\n    </section>\n'
    if not re.search(pat, s, re.S):
        raise SystemExit("#closed not found")
    s = re.sub(pat, lambda m: SECTION, s, count=1, flags=re.S)

    entry = ('<li><a href="#closed">Four developers who will never hire an '
             'outside architect</a></li>')
    s = re.sub(r'<li><a href="#closed">.*?</a></li>', lambda m: entry, s,
               count=1)
    io.open(BODY, "w", encoding="utf-8").write(s)

    sec = re.search(r'<section id="closed">.*?</section>', s, re.S).group(0)
    print("#closed rewritten")
    for probe in ["spend the call", "positions were reversed", "in the set",
                  "in the 50", "the first section", "falls out of"]:
        n = len(re.findall(re.escape(probe), sec, re.I))
        print("   %-24s %d %s" % (probe, n, "ok" if n == 0 else "STILL THERE"))


if __name__ == "__main__":
    main()
