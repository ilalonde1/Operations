#!/usr/bin/env python3
"""Reframe Houston and the Howard Hughes page around the COMMISSION test.

⛔ THE SECOND TEST, WHICH WAS MISSING

    The premise test asks: does filing this prove a designer already exists?
    A Houston general plan passes it -- no drawing is required.

    But there is a second question, and Houston fails it:
        IS THERE AN ARCHITECT COMMISSION IN THIS SCOPE AT ALL?

    A Chapter 42 general plan is a land-subdivision instrument -- streets,
    blocks, rights-of-way, reserves. No architect is required to file one, so
    of course none is named. And the vertical product inside Bridgeland's
    Prairieland Village is roughly 7,000 production homes by Highland, David
    Weekley, Chesmar, Perry, Newmark, Century and Brightland, every one of them
    working from an in-house plan book.

    So "3,905 acres and none of it names an architect" was true and empty.
    None of it would.

WHAT THE HOUSTON RECORD ACTUALLY HOLDS FOR THIS CLIENT
    1,426 plats. 35 general plans (land actions). 41 tagged multi-unit or
    mixed-use, of which 31 are under half an acre -- townhouse and duplex infill
    on single lots. FIVE are 2.5 acres or more. That is the real pipeline, and
    saying so is worth more than a table of 600-acre general plans.

    The record's QUALITY claim survives intact and is the point: developer,
    consultancy, named applicant and office phone on 100% of 1,426 rows. It is
    the best-structured of the six. What it currently CONTAINS is thin, and a
    weekly watch on it is the thing worth having.

AND THE HOWARD HUGHES PAGE
    Reframed from a lead into what it actually is: relationship intelligence.
    Where an existing client is putting money, in markets MVE does not serve
    them in. The Las Vegas filing is multifamily and is a genuine commission
    signal; the Houston acreage is land for production builders and is not.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

SIGNAL = """    <section id="signal">
      <p class="kicker">Where your own client is putting money, in markets you do not serve them in</p>
      <h2>Howard Hughes has 7,250 acres in motion in Houston, and filed for multifamily in Las Vegas yesterday</h2>
      <p class="lede">You build for Howard Hughes at Ward Village. The same company is moving in three of your other five markets. <strong>We are going to be precise about which part of that is a commission and which part is not</strong>, because the difference is the whole point.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Filed</th><th>Market</th><th>What it is</th><th>Is there work in it</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>27 Aug</strong><br><span class="muted">yesterday</span></td>
              <td><strong>Las Vegas</strong><br><span class="muted">Clark County</span></td>
              <td><strong>Multifamily application prereview</strong> &mdash; the earliest formal step the county has. The scheme now before the county is <strong>354 apartments with 6,556 sq ft of commercial on four acres</strong> at Spruce Goose Street, Downtown Summerlin. The ownership disclosure names David O&rsquo;Reilly, L. Jay Cross and Carlos A. Olea</td>
              <td><strong>Yes.</strong> Multifamily, no architect on the record, no drawing set yet <span class="muted">26-101519</span></td>
            </tr>
            <tr>
              <td><strong>9 Feb</strong><br>&amp; 15 Jun</td>
              <td><strong>Houston</strong><br><span class="muted">Harris County</span></td>
              <td><strong>39 plat filings, 7,250 acres</strong> &mdash; Bridgeland Prairieland Village at 3,905 acres, Creekland Village at 2,037, Woodlands Village of Sterling Ridge at 450. All through LJA Engineering</td>
              <td><strong>Not directly.</strong> A general plan divides land; Prairieland&rsquo;s ~7,000 homes go to production builders with their own plan books</td>
            </tr>
            <tr>
              <td><span class="muted">standing</span></td>
              <td><strong>Phoenix</strong><br><span class="muted">Buckeye</span></td>
              <td>Owns <strong>Teravalis at Douglas Ranch</strong> in the west valley, per their own SEC subsidiaries exhibit</td>
              <td><span class="muted">Land position. Worth knowing, not a bid</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p><strong>So one line of that is a live commission signal and two are intelligence.</strong> The two still matter: they tell you where a client you already have is committing capital, in markets where you do not currently do their work &mdash; and that is a conversation you can have on the strength of the public record alone, without waiting for a bid.</p>
      <div class="box box--tip">
        <p class="box__label">&#9650; How we knew these were all the same company</p>
        <p>The Houston record names <em>Bridgeland Development, LP</em> and <em>The Woodlands Land Development Company, L.P.</em>, not Howard Hughes. Both appear verbatim in Howard Hughes Holdings&rsquo; subsidiaries exhibit &mdash; <span class="muted">EX-21.1, filed with the SEC 19 February 2026</span> &mdash; alongside Summerlin, Ward Village, and the <strong>Teravalis and Douglas Ranch</strong> entities. A primary document, not a press report, and it is what puts them in four of your six markets at once.</p>
      </div>
    </section>

"""

LEADS = """    <section id="leads">
      <p class="kicker">The best-built record of the six, and an honest read on what is in it</p>
      <h2>Houston names the developer on every filing &mdash; and most of what it names is land</h2>
      <p class="lede">Houston has no zoning, and its state registrations are filed <em>by the design firm</em>, which puts every one of them after the appointment. The Planning Commission&rsquo;s own plat spreadsheets are the opposite: filed by the developer&rsquo;s planner or engineer to divide land, before anything is designed. <strong>1,426 applications this year, with the developer company named on 100% of them</strong> &mdash; along with the filing consultancy, a named applicant, an office phone, acreage, land use and council district. Nothing else in the six markets is built like it.</p>
      <p><strong>Now the part that matters more than the record.</strong> Of those 1,426: <strong>35 are general plans</strong>, which divide land for master-planned communities &mdash; Bridgeland&rsquo;s next village alone is about 7,000 homes going to production builders, so there is no architectural commission in it. <strong>41 are tagged multi-unit or mixed-use, and 31 of those sit under half an acre</strong> &mdash; townhouse and duplex infill on single lots. <strong>Five are multi-unit at a scale that implies a building somebody has to design.</strong></p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Submitted</th><th>Scheme</th><th>Developer</th><th>Acres</th></tr></thead>
          <tbody>
            <tr><td><strong>10 Jul</strong></td><td><strong>Buffalo Bayou Lighthouse</strong></td><td>filed through Patrick Planning Services</td><td>3.31</td></tr>
            <tr><td><strong>18 May</strong></td><td><strong>Pruitt Reserve</strong></td><td>Ward, Getz &amp; Associates &mdash; filed through Windrose</td><td>10.79</td></tr>
            <tr><td><strong>18 May</strong></td><td><strong>Lofts at Wayfarer</strong></td><td>Jesse &amp; Johnny Villareal, through Quiddity Engineering</td><td>2.98</td></tr>
            <tr><td><strong>4 May</strong></td><td><strong>Avvento</strong></td><td>Contempo Builder &mdash; filed through Windrose</td><td>8.20</td></tr>
            <tr><td><strong>6 Apr</strong></td><td><strong>Grove on 11th</strong></td><td>ALJ Lindsey</td><td>11.68</td></tr>
          </tbody>
        </table>
      </div>
      <p>These are local developers rather than the national names further up this page, and that is the honest state of Houston&rsquo;s multifamily platting this year. <strong>The value here is not the five rows &mdash; it is that the record is complete enough to watch weekly</strong>, so that when a genuine multifamily parcel plats, you know before anyone has been asked to draw it.</p>
      <div class="box box--dont">
        <p class="box__label">&#10005; Why the big acreages are not on this list</p>
        <p>It would be easy to hand you Brookfield&rsquo;s 591-acre Midline, Johnson Development&rsquo;s 696-acre Amira or Taylor Morrison&rsquo;s 262-acre Avalon at Cypress, and note that no architect is named on any of them. <strong>No architect is named because none is required.</strong> A Chapter 42 general plan divides land into streets, blocks and reserves; the homes that follow are production builders&rsquo; own designs. Those filings tell you where capital is going. They are not work.</p>
      </div>
    </section>

"""


def main():
    s = io.open(BODY, encoding="utf-8").read()
    for sid, new in (("signal", SIGNAL), ("leads", LEADS)):
        pat = r'    <section id="%s">.*?\n    </section>\n' % sid
        if not re.search(pat, s, re.S):
            raise SystemExit("#%s not found" % sid)
        s = re.sub(pat, lambda m, n=new: n, s, count=1, flags=re.S)

    toc = {
        "#signal": "Howard Hughes has 7,250 acres in motion, and filed for "
                   "multifamily yesterday",
        "#leads": "Houston names the developer on every filing &mdash; and "
                  "most of it is land",
    }
    for href, label in toc.items():
        s = re.sub(r'<li><a href="%s">.*?</a></li>' % re.escape(href),
                   lambda m, h=href, l=label: '<li><a href="%s">%s</a></li>' % (h, l),
                   s, count=1)

    io.open(BODY, "w", encoding="utf-8").write(s)
    print("rebuilt #signal and #leads on the commission test")
    for probe, want in [("none of it names an architect", 0),
                        ("Octa Business Park", 0),
                        ("Midline general plan", 0),
                        ("no architect is named because none is required", 0)]:
        n = len(re.findall(re.escape(probe), s, re.I))
        print("   %-46s %d %s" % (probe, n, "ok" if n == want or want else ""))


if __name__ == "__main__":
    main()
