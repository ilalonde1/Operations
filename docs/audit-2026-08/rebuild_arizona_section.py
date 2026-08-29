#!/usr/bin/env python3
"""Rebuild the Arizona section on a true premise.

⛔ THREE ERRORS THIS FIXES, ALL FOUND BY THE CLIENT AND NOT BY US

 1. THE PREMISE. The page presented site-plan cases as leads. A site plan is a
    DRAWING: to file one, somebody has to have drawn the buildings. Every one of
    the 373 therefore has a design team already, whether or not the city names
    it. Preliminary vs final changes how much is still moveable; it does not
    change whether an architect exists. This is the same test that ruled out
    Houston TDLR (the design firm files it) and Miami UDRB (the architect's own
    drawings) -- applied to two markets and not to the third.

 2. THE NOUN. "373 open site-plan and rezoning cases" is wrong. Verified with
    returnCountOnly against the layer:
        site plan, open ....... 373   (281 preliminary + 92 final)
        rezoning, open .......... 0
        rezoning, any status .... 0
    The layer holds NO rezoning records. The Phoenix rezonings that produced the
    two verified openings come from a different source entirely -- the PUD case
    narratives on phoenix.gov. The page conflated them.

 3. THE COUNTING METHOD. The error was nearly compounded by asking for
    resultRecordCount=3000, getting the server's 2000-row cap, and reading an
    arbitrary slice as evidence. Never count by len() of a returned page; ask
    with returnCountOnly.

WHAT THE SECTION SAYS NOW
    It answers the question asked -- here is the live record, here are the exact
    counts, here is how current each layer is -- and then says plainly which
    layer can be acted on and why the big one cannot. Being able to say which
    half of a record is useless is worth more than the record.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

SECTION = """    <section id="arizona">
      <p class="kicker">The thing you asked for, and an honest read on it</p>
      <h2>Phoenix submitted projects &mdash; and which layer of the record can actually be acted on</h2>
      <p class="lede">Phoenix publishes a live plan-review service, row-level, reachable without an account. Queried for site plans it returns <strong>373 open cases &mdash; 281 preliminary and 92 final</strong>. That is the search you asked for and the numbers are exact. It is also, for finding unlet work, close to useless, and the reason is worth a minute of your time.</p>
      <p><strong>A site plan is a drawing.</strong> To file one, somebody has to have drawn the buildings. So all 373 of those cases have a design team already, whether or not the city names it &mdash; and preliminary versus final only tells you how much is still moveable, not whether an architect exists. It is the same reason we do not quote you Houston&rsquo;s state registrations, which the design firm files, or Miami&rsquo;s design-review packets, which are the architect&rsquo;s own drawings.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Layer of the Phoenix record</th><th>What a record in it proves</th><th>Newest</th><th>Any use to you</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>Building permits</strong><br><span class="muted">68,292 rows</span></td>
              <td>Construction documents approved and work starting. Fire connections, haul routes, landscape</td>
              <td><strong>27 Aug</strong><br><span class="muted">yesterday</span></td>
              <td><strong>No.</strong> Years past the appointment</td>
            </tr>
            <tr>
              <td><strong>Site-plan review cases</strong><br><span class="muted">373 open</span></td>
              <td>Buildings have been drawn and submitted for review</td>
              <td>27 May<br><span class="muted">runs a quarter behind</span></td>
              <td><strong>No.</strong> The drawing is itself the proof that a designer is engaged</td>
            </tr>
            <tr>
              <td><strong>Rezoning and PUD case files</strong><br><span class="muted">published separately, as narratives</span></td>
              <td>Land being made buildable. Filed by land-use counsel with a civil engineer and a land planner &mdash; the consultants that come <em>before</em> a building is designed</td>
              <td>27 May</td>
              <td><strong>Yes.</strong> This is the only Phoenix layer where a building architect is routinely not yet engaged</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p><strong>That is why both Phoenix entries on the openings page are rezonings, and why none of the 281 preliminary site-plan cases is on it.</strong> We read thirty PUD case files in full, including the drawing title blocks. Three of them named an architect there &mdash; Woods Associates, Kontexture, and one more &mdash; and were dropped. Two survived that and a trade-press check.</p>
      <div class="box box--dont">
        <p class="box__label">&#10005; The field that looks like the answer, and is not</p>
        <p>The record carries a <em>professional name</em> field, and on most open cases it reads <strong>&ldquo;TO BE BID&rdquo;</strong> or nothing at all. It is tempting to filter on that and call the result unawarded work. <strong>We tested it and it fails:</strong> &ldquo;TO BE BID&rdquo; appears on <strong>59 of the 92 final site plans</strong> &mdash; schemes whose design is finished. It is a procurement-route field, not a design-team field, and nothing in this document rests on it.</p>
      </div>
      <div class="box box--tip">
        <p class="box__label">&#9650; On the test you set us</p>
        <p>You asked for everything submitted in Arizona, of the kind you run in CoStar. The record above is that, current and exact, and it travels with this document in full. What we have added is the part a listing service will not tell you: <strong>which layer of it is already spoken for.</strong></p>
      </div>
    </section>

"""


def main():
    s = io.open(BODY, encoding="utf-8").read()
    m = re.search(r'    <section id="arizona">.*?\n    </section>\n', s, re.S)
    if not m:
        raise SystemExit("#arizona not found")
    s = s[:m.start()] + SECTION + s[m.end():]

    entry = ('<li><a href="#arizona">Phoenix submitted projects &mdash; and '
             'which layer can be acted on</a></li>')
    s = re.sub(r'<li><a href="#arizona">.*?</a></li>', lambda x: entry, s,
               count=1)
    io.open(BODY, "w", encoding="utf-8").write(s)

    sec = re.search(r'<section id="arizona">.*?</section>', s, re.S).group(0)
    print("#arizona rebuilt")
    for probe, want in [("Momentum Apartments", 0), ("Sierra Verde", 0),
                        ("Final site plan", 0), ("site-plan and rezoning", 0),
                        ("373", 2), ("281", 2), ("TO BE BID", 2)]:
        n = len(re.findall(re.escape(probe), sec))
        flag = "ok" if (n == 0 if want == 0 else n >= 1) else "CHECK"
        print("   %-24s %d  %s" % (probe, n, flag))


if __name__ == "__main__":
    main()
