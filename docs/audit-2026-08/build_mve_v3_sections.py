#!/usr/bin/env python3
"""Rebuild the four sections that changed when the early layer was found.

WHAT CHANGED AND WHY

  #signal   The lead was "Howard Hughes filed a Las Vegas pre-application".
            True, but it buried the real finding: the SAME company -- MVE's own
            Ward Village client -- has 39 land filings across Houston in nine
            months and owns a master planned community in Phoenix. A cold
            opening is a call; an existing client opening land in three markets
            you have not built for them in is a different order of thing.

  #facts    The old table's last column was "How much of the market this
            covers", and it read as a list of apologies -- "Named projects
            only", "Stated, not estimated", "11 projects of 92 filings". Worse,
            it described Las Vegas as "whole-project permits, flat at 3-7 a
            year", which was written before the pre-application layer was
            found and undersold the strongest market in the document. Coverage
            caveats belong in the research companion. This page is a radar.

  #hawaii   New. Hawaii was one of Dan's six markets and had no live source at
            all -- it appeared only as three project names in a static table.

  #leads    Was built on TDLR registrations, which are filed BY THE DESIGN
            FIRM and are therefore always after the appointment. Rebuilt on the
            Planning Commission's own plat spreadsheets, which are filed by the
            developer before a building exists.

⛔ THE EXCLUSION RULE THIS DOCUMENT LIVES BY
   Nothing MVE is already the architect on may appear as an opportunity. Hines
   and Vestar both surfaced in the cross-match and both were dropped: Hines's
   Arizona records are completed office fit-outs with Phoenix Design One named,
   and Vestar's Phoenix case already names Butler Design Group. Only Howard
   Hughes survived, and Ward Village is named as THEIR OWN WORK, never as a
   lead.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

SIGNAL = """    <section id="signal">
      <p class="kicker">Your own client, in three markets you have not built for them in</p>
      <h2>Howard Hughes has opened 7,250 acres since December, and none of it names an architect</h2>
      <p class="lede">You build for Howard Hughes at Ward Village. Over the last nine months the same company has been filing in three of your other five markets &mdash; land actions, not buildings, which is the stage before anyone is appointed. All of it is on the public record, and none of it carries an architect&rsquo;s name.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Filed</th><th>Market</th><th>What was filed</th><th>Record</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>27 Aug</strong><br><span class="muted">yesterday</span></td>
              <td><strong>Las Vegas</strong><br><span class="muted">Clark County</span></td>
              <td><strong>A multifamily application prereview</strong> &mdash; the earliest formal step the county has. The ownership disclosure names <strong>David O&rsquo;Reilly</strong> (CEO), <strong>L. Jay Cross</strong> (President) and <strong>Carlos A. Olea</strong> (CFO)</td>
              <td>26-101519</td>
            </tr>
            <tr>
              <td><strong>15 Jun</strong></td>
              <td><strong>Houston</strong><br><span class="muted">Harris County</span></td>
              <td><strong>Bridgeland Creekland Village general plan &mdash; 2,037 acres.</strong> A general plan is the first plat step for a master development, filed years before vertical product</td>
              <td>2026-0982</td>
            </tr>
            <tr>
              <td><strong>15 Jun</strong></td>
              <td><strong>Houston</strong></td>
              <td><strong>Woodlands Village of Sterling Ridge &mdash; 450 acres</strong>, filed by The Woodlands Land Development Company</td>
              <td>2026-0973</td>
            </tr>
            <tr>
              <td><strong>9 Feb</strong></td>
              <td><strong>Houston</strong></td>
              <td><strong>Bridgeland Prairieland Village general plan &mdash; 3,905 acres.</strong> The largest single filing in the Houston record this year</td>
              <td>2026-0191</td>
            </tr>
            <tr>
              <td><span class="muted">standing</span></td>
              <td><strong>Phoenix</strong><br><span class="muted">Buckeye</span></td>
              <td>Howard Hughes owns <strong>Teravalis at Douglas Ranch</strong>, in the Phoenix west valley. Confirmed from their own SEC subsidiaries exhibit, not from press</td>
              <td><span class="muted">EX-21.1<br>19 Feb 2026</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p><strong>Thirty-nine plat filings, 7,250 acres, between 19 December and 10 August</strong> &mdash; every one of them through LJA Engineering as the filing consultant. In Las Vegas, thirty-two multifamily pre-applications were lodged in sixty days, sixteen of them in the last fortnight; Howard Hughes&rsquo;s is the most recent of them.</p>
      <div class="box box--tip">
        <p class="box__label">&#9650; How we knew the Houston filings were the same company</p>
        <p>The Houston record names <em>Bridgeland Development, LP</em> and <em>The Woodlands Land Development Company, L.P.</em> &mdash; not Howard Hughes. Both appear verbatim in Howard Hughes Holdings&rsquo; subsidiaries exhibit filed with the SEC on 19 February 2026, alongside the Summerlin, Ward Village, Teravalis and Douglas Ranch entities. That is a primary document, not a press report.</p>
        <p><span class="muted">The same exhibit is what places Howard Hughes in four of your six markets at once.</span></p>
      </div>
      <div class="box box--dont">
        <p class="box__label">&#10005; What we deliberately did not put in front of you</p>
        <p>We crossed your published client list against every record below. <strong>Two of your clients matched and both were dropped.</strong> Hines appears three times in the Arizona record &mdash; all completed office fit-outs, with Phoenix Design One already named as architect. Vestar appears once in the Phoenix rezoning record, on a case that already names Butler Design Group.</p>
          <p>Neither is an opportunity, and presenting them as one would have told you nothing you did not know. <strong>Ward Village, Kalae and Launiu are excluded on the same rule</strong> &mdash; they are your work, and they are here only as the reason the Howard Hughes thread is worth your time.</p>
      </div>
    </section>

"""

FACTS = """    <section id="facts">
      <p class="kicker">One page</p>
      <h2>Six markets, and what is open in each one today</h2>
      <p class="lede">Every source below was re-run on 28 August 2026. What this table gives is the layer that exists <em>before</em> a design team is appointed &mdash; not permits, which are always too late. The markets are not equal, and where one is thin we say why rather than padding it.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Market</th><th>What is open in the record right now</th><th>Newest filing</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>Las Vegas</strong><br><span class="muted">Clark County</span></td>
              <td><strong>32 multifamily application prereviews in sixty days</strong>, sixteen in the last fortnight. The county publishes an ownership disclosure with each, which names the officers behind the filing entity &mdash; so a single-purpose LLC still resolves to a real company</td>
              <td><strong>27 August</strong><br><span class="muted">yesterday</span></td>
            </tr>
            <tr>
              <td><strong>Houston</strong><br><span class="muted">Harris County</span></td>
              <td><strong>1,426 plat applications</strong> across sixteen commission cycles. The city&rsquo;s own spreadsheet gives the <strong>developer company, the filing consultancy, a named applicant and an office phone on every single row</strong> &mdash; 100% named, with acreage, land use and council district</td>
              <td><strong>20 August</strong></td>
            </tr>
            <tr>
              <td><strong>Phoenix</strong><br><span class="muted">Arizona</span></td>
              <td><strong>373 open site-plan and rezoning cases</strong>, 280 filed since January 2025. Three of them have land-use counsel and a civil engineer retained and <strong>no architect named anywhere on the file</strong></td>
              <td>Permits <strong>27 August</strong><br><span class="muted">entitlement cases run a quarter behind, to 27 May</span></td>
            </tr>
            <tr>
              <td><strong>Charlotte</strong></td>
              <td><strong>84 rezoning petitions pending</strong>, petitioner named on every one, no design team on any &mdash; because Charlotte publishes the petition before the design exists. Five of the petitioners buy architecture rather than produce it</td>
              <td><strong>14 August</strong></td>
            </tr>
            <tr>
              <td><strong>Hawaii</strong><br><span class="muted">all four counties</span></td>
              <td>Any project using state or county land or funds must publish an environmental assessment first. Those are gathered twice a month into one statewide bulletin that names <strong>the applicant and the planning consultant, with a contact for each</strong>: <strong>63 projects so far in 2026, eight residential</strong></td>
              <td><strong>23 August</strong></td>
            </tr>
            <tr>
              <td><strong>Miami</strong></td>
              <td><strong>Thin, and structurally so.</strong> Only six rezonings came before the board all year and four were City-initiated, because Miami 21 lets private schemes proceed by warrant instead. The real Miami record is design review &mdash; 66 projects &mdash; and that is the architect&rsquo;s own drawings, so it is always after the award</td>
              <td>Design review<br><strong>15 July</strong><br><span class="muted">the board does not sit in August</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p><span class="muted">Concentration figures &mdash; who holds what share of each market, and how much of the market each sample covers &mdash; are in the companion document, with the working shown. They answer a different question from this page.</span></p>
    </section>

"""

HAWAII = """    <section id="hawaii">
      <p class="kicker">The market with no permit feed, and a better record anyway</p>
      <h2>Hawaii publishes its pipeline before design, in one statewide bulletin</h2>
      <p class="lede">Hawaii looked like the hardest of the six. Honolulu&rsquo;s open data stops at 2016 permits, and its live zoning layers record ordinances that have already passed &mdash; the outcome, not the pipeline. The answer is not at the county at all. It is a state publication, and it is better than anything the counties hold.</p>
      <p>Under Chapter 343, any project using state or county land or money, touching a shoreline or conservation district, or needing a general plan amendment must publish an environmental assessment. The Office of Planning and Sustainable Development gathers these into <strong>The Environmental Notice</strong>, published on the 8th and 23rd of every month, covering <strong>all four counties in one document</strong>. Each entry names, in a fixed order: every permit still required, the approving agency with a named officer, <strong>the applicant</strong>, <strong>the planning consultant</strong>, the status, and a description that usually gives the unit count.</p>
      <p>An environmental assessment is prepared to win entitlements. It comes before construction documents, and the consultant named on it is a planner, not an architect.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Published</th><th>Project</th><th>Applicant</th><th>Planner on record</th></tr></thead>
          <tbody>
            <tr>
              <td><strong>23 Aug</strong><br><span class="muted">Maui</span></td>
              <td><strong>M&#257;kena Mauka</strong> &mdash; master planned residential community, <strong>652 units</strong> including workforce housing, single-family and multi-family. Final EIS <strong>accepted</strong>, which means entitlement is finishing and the building work is next</td>
              <td>AREG AC Makena Propco LLC</td>
              <td>Munekiyo Hiraga</td>
            </tr>
            <tr>
              <td><strong>23 May</strong><br><span class="muted">Kaua&#699;i</span></td>
              <td><strong>K&#299;lauea Town Expansion</strong> &mdash; Section 201H affordable housing, <strong>310 units</strong></td>
              <td><span class="muted">not stated in the notice</span></td>
              <td>Kahewai Environmental LLC</td>
            </tr>
            <tr>
              <td><strong>23 Mar</strong><br><span class="muted">Maui</span></td>
              <td><strong>Ho&#699;onani Village</strong> &mdash; mixed-use development, at Draft EIS, before the State Land Use Commission. The earliest stage of the three</td>
              <td>Ho&#699;onani Development, LLC</td>
              <td>Pioneer Design Group&ndash;Hawai&#699;i</td>
            </tr>
            <tr>
              <td><strong>8 May</strong><br><span class="muted">Maui</span></td>
              <td><strong>Wailuku Mission Senior Affordable Housing</strong></td>
              <td>EAH Housing<br><span class="muted">also builds in California</span></td>
              <td>PBR Hawai&#699;i</td>
            </tr>
            <tr>
              <td><strong>23 May</strong><br><span class="muted">O&#699;ahu</span></td>
              <td><strong>Mililani High School Teacher Workforce Housing</strong></td>
              <td>Pacific Housing Assistance Corporation</td>
              <td>PBR Hawai&#699;i</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="box box--dont">
        <p class="box__label">&#10005; What this source does not cover, in plain terms</p>
        <p>Chapter 343 is triggered by <em>state or county involvement</em>. A wholly private project, on private land, with no shoreline or conservation trigger, never appears. <strong>Ward Village is exactly that kind of project</strong> &mdash; which is why your own Honolulu work is not in this bulletin and why the bulletin is no use for finding more of it.</p>
        <p>The Kaka&#699;ako pipeline runs instead through the Hawai&#699;i Community Development Authority, whose board is the approving authority for that district. We read its agenda for the <strong>2 September</strong> sitting: one right-of-entry item and a monthly report. <strong>No new development permit is before the board.</strong> That is the state of the district this cycle, not a gap in the search.</p>
      </div>
    </section>

"""

LEADS = """    <section id="leads">
      <p class="kicker">The market everyone calls opaque</p>
      <h2>Houston names the developer on every single filing</h2>
      <p class="lede">Houston has no zoning, so there is no rezoning petition to watch, and its state registrations are filed <em>by the design firm</em> &mdash; which puts every one of them after the appointment. That is why Houston reads as closed. It is not. The Planning Commission publishes the agenda of every cycle as a spreadsheet, and a subdivision plat is filed by the developer&rsquo;s planner or engineer, to divide land, before a building is designed.</p>
      <p><strong>1,426 applications across sixteen cycles this year. The developer company is named on 100% of them</strong> &mdash; along with the consultancy that filed, a named applicant, an office phone, the acreage, the land use and the council district.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Submitted</th><th>Developer</th><th>Filed through</th><th>Scheme</th></tr></thead>
          <tbody>
            <tr><td><strong>10 Aug</strong></td><td><strong>OctaHomes</strong></td><td>Meta Planning + Design</td><td>Octa Business Park general plan &mdash; <strong>208 acres</strong></td></tr>
            <tr><td><strong>10 Aug</strong></td><td><strong>Houston Livestock Show and Rodeo</strong></td><td>EHRA</td><td>Rodeo auxiliary facilities general plan &mdash; <strong>316 acres</strong></td></tr>
            <tr><td><strong>7 Aug</strong></td><td><strong>Brookfield Properties</strong></td><td>LJA Engineering</td><td>Midline general plan &mdash; <strong>591 acres</strong></td></tr>
            <tr><td><strong>27 Jul</strong></td><td><strong>Taylor Morrison</strong></td><td>Meta Planning + Design</td><td>Avalon at Cypress &mdash; <strong>262 acres</strong></td></tr>
            <tr><td><strong>13 Jul</strong></td><td><strong>Johnson Development</strong></td><td>Meta Planning + Design</td><td>Amira general plan &mdash; <strong>696 acres</strong></td></tr>
            <tr><td><strong>29 Jun</strong></td><td><strong>Westside Ventures</strong></td><td>Meta Planning + Design</td><td>Mason Business general plan &mdash; <strong>112 acres</strong></td></tr>
            <tr><td><strong>15 Jun</strong></td><td><strong>Bridgeland Development</strong><br><span class="muted">Howard Hughes</span></td><td>LJA Engineering</td><td>Creekland Village general plan &mdash; <strong>2,037 acres</strong></td></tr>
          </tbody>
        </table>
      </div>
      <p>Thirty general plans were filed in 2026 &mdash; the earliest plat step there is, and the one that precedes a master development by years. <strong>152 applications since 1 June sit on five acres or more.</strong></p>
      <div class="box box--tip">
        <p class="box__label">&#9650; Read the acreage, not the land-use label</p>
        <p>Houston tags 41 filings this year as Multi-Unit Residential, and it would be easy to hand you that list. Most of them are between <strong>0.11 and 0.20 acres</strong> &mdash; townhouse and duplex infill on single lots, not a commission for anyone. The filings worth your time are the ones with scale behind them, whatever the label says, which is why the table above is cut on acreage.</p>
      </div>
    </section>

"""

TOC = [
    ("#signal", "Howard Hughes has opened 7,250 acres since December"),
    ("#facts", "Six markets, and what is open in each one today"),
    ("#hawaii", "Hawaii publishes its pipeline before design"),
    ("#leads", "Houston names the developer on every single filing"),
]


def replace_section(s, sid, new):
    pat = r'    <section id="%s">.*?\n    </section>\n' % sid
    if not re.search(pat, s, re.S):
        raise SystemExit("section #%s not found -- refusing to guess" % sid)
    return re.sub(pat, lambda m: new, s, count=1, flags=re.S)


def main():
    s = io.open(BODY, encoding="utf-8").read()
    before = len(s)

    s = replace_section(s, "signal", SIGNAL)
    s = replace_section(s, "facts", FACTS)
    s = replace_section(s, "leads", LEADS)

    # Hawaii is new: it goes directly after the facts page.
    if 'id="hawaii"' not in s:
        anchor = '    <section id="arizona">'
        if anchor not in s:
            raise SystemExit("cannot find #arizona to place #hawaii before")
        s = s.replace(anchor, HAWAII + anchor, 1)

    # Contents entries
    for href, label in TOC:
        pat = r'<li><a href="%s">.*?</a></li>' % re.escape(href)
        entry = '<li><a href="%s">%s</a></li>' % (href, label)
        if re.search(pat, s):
            s = re.sub(pat, lambda m: entry, s, count=1)
        elif href == "#hawaii":
            anchor = '<li><a href="#arizona">'
            i = s.find(anchor)
            if i < 0:
                raise SystemExit("cannot place #hawaii in the contents")
            s = s[:i] + entry + "\n            " + s[i:]

    io.open(BODY, "w", encoding="utf-8").write(s)
    print("rebuilt: signal, facts, leads;  added: hawaii")
    print("body %d -> %d chars" % (before, len(s)))
    for sid in ("signal", "facts", "hawaii", "arizona", "leads"):
        print("   #%-10s %s" % (sid, "present" if ('id="%s"' % sid) in s else "MISSING"))


if __name__ == "__main__":
    main()
