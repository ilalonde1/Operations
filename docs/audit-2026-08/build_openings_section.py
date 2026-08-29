#!/usr/bin/env python3
"""The verified-openings section: only what survived individual verification.

EVERY ROW BELOW HAS PASSED TWO TESTS
  1. the case file / EA / site plan scanned for an architect, including the
     DRAWING TITLE BLOCK, via tools/find_architect_in_case.py
  2. a trade-press check, because Fifield's architect appears in no city
     document at all

SIX LEADS DIED IN THAT PROCESS AND THEY ARE NAMED IN THE BOX. That is not an
apology -- it is the reason the nine that remain are worth reading. A lead list
with no exclusions is a list nobody checked.

⛔ Do NOT add a row here without running both tests. The failure this section
exists to prevent is handing an architecture firm a lead on a job that already
has an architect.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

SECTION = """    <section id="openings">
      <p class="kicker">Verified one at a time, and six did not survive it</p>
      <h2>Nine schemes where the record names no architect</h2>
      <p class="lede">Every project below was checked twice: the case file, the environmental assessment or the site plan read in full &mdash; <strong>including the drawing title block</strong>, which is where an architect usually appears &mdash; and then a separate trade-press check. Nine stood up. Six did not, and those are named at the foot of the page.</p>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Market</th><th>Who</th><th>What, and how far along</th><th>Record</th></tr></thead>
          <tbody>
            <tr>
              <td rowspan="2"><strong>Phoenix</strong></td>
              <td><strong>Host Hotels &amp; Resorts</strong><br><span class="muted">NYSE: HST</span></td>
              <td><strong>Copper Residences</strong> &mdash; 72 acres of the Westin Kierland&rsquo;s Mesquite golf course. <strong>16.16 acres of resort condominium and condo-hotel</strong>; 55.64 acres of single-family, townhome and duplex. Greey&thinsp;|&thinsp;Pickett on site design and landscape, Woodpatel civil, CivTech traffic &mdash; <strong>no building architect</strong>. Third application, in review after the May 2026 submittal. <span class="muted">Neighbourhood opposition is active and on the record</span></td>
              <td>Z-169-25-2</td>
            </tr>
            <tr>
              <td><strong>Vintage Partners</strong></td>
              <td><strong>1,000 residential units</strong> and 22 acres of commercial on 63 acres. The site was earmarked for a data centre until Phoenix changed its data-centre policy and Vintage converted it. RVi on land planning, Precision Civil engineering</td>
              <td>Z-24-26-7</td>
            </tr>
            <tr>
              <td rowspan="4"><strong>Charlotte</strong></td>
              <td><strong>Mid-America Apartments</strong><br><span class="muted">NYSE: MAA</span></td>
              <td>3.65 acres in <strong>SouthPark</strong> &mdash; south of Fairview Road, east of Cameron Valley Parkway. MUDD-O&nbsp;SPA to RAC(CD). Kimley-Horn drew the rezoning plan; no architect on it</td>
              <td>2026-050</td>
            </tr>
            <tr>
              <td><strong>Crosland Southeast</strong><br><span class="muted">C4 Investments</span></td>
              <td>39.41 acres north of Wilkinson Boulevard at Little Rock Road, RAC to CG(CD). Three community meetings held. <strong>The engineer&rsquo;s seal block on the site plan is still a blank placeholder</strong> &mdash; this is as early as a filing gets</td>
              <td>2026-027</td>
            </tr>
            <tr>
              <td><strong>Middleburg</strong></td>
              <td>20.15 acres south of Wilkinson Boulevard, CG to N2-B(CD). The site plan already lays out buildings at <strong>63 units, four and five storeys</strong> &mdash; so the massing is settled and the architect is not named</td>
              <td>2026-023</td>
            </tr>
            <tr>
              <td><strong>DreamKey Partners</strong></td>
              <td>6.00 acres east of Beatties Ford Road, N1-B to N2-A(CD). Public hearing already held, <strong>17 August 2026</strong></td>
              <td>2026-035</td>
            </tr>
            <tr>
              <td rowspan="2"><strong>Hawaii</strong></td>
              <td><strong>AREG AC Makena Propco</strong><br><span class="muted">Makena Golf &amp; Beach Club</span></td>
              <td><strong>M&#257;kena Mauka</strong>, Maui &mdash; <strong>652 units</strong> including 109 workforce, single-family and multi-family, plus <strong>135,000 sq ft of operational support buildings</strong>. Final EIS <strong>accepted 23 August</strong>, so entitlement is finished and the building work is next. Munekiyo Hiraga planned it; <span class="muted">the architect field in its own permit appendix is blank</span></td>
              <td>FEIS<br>23 Aug</td>
            </tr>
            <tr>
              <td><strong>Ho&#699;onani Development</strong></td>
              <td><strong>Ho&#699;onani Village</strong>, Maui &mdash; mixed-use, at <strong>Draft EIS</strong> before the State Land Use Commission. The earliest stage of anything on this page. Pioneer Design Group&ndash;Hawai&#699;i on planning</td>
              <td>DEIS<br>23 Mar</td>
            </tr>
            <tr>
              <td><strong>Las Vegas</strong></td>
              <td><strong>Howard Hughes</strong><br><span class="muted">your Ward Village client</span></td>
              <td>Multifamily application prereview filed <strong>27 August</strong>. The scheme now before the county is <strong>354 apartments with 6,556 sq ft of commercial on four acres</strong> at Spruce Goose Street, Downtown Summerlin. No architect named</td>
              <td>26-101519</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="box box--dont">
        <p class="box__label">&#10005; Six we removed, and where each one&rsquo;s architect was hiding</p>
        <p><strong>Fifield Companies</strong>, Phoenix &mdash; <strong>Todd &amp; Associates Architecture</strong>, on design and landscape. Named by the trade press and <strong>in no city document at all</strong>, which is why a file check alone is not enough. &middot; <strong>Elevation Living</strong> &mdash; <strong>Woods Associates Architects</strong>, in the drawing title block. &middot; <strong>J&amp;K Luxury Group</strong> &mdash; <strong>Kontexture</strong>, same place. &middot; <strong>Miami Design District</strong> &mdash; <strong>David Chipperfield</strong> on the 25-storey condo and 12-storey hotel. &middot; <strong>Wailuku Mission Senior Housing</strong> &mdash; EAH Housing is already working with <strong>MASON Architects</strong> on the historic rehabilitation. &middot; <strong>Kittle Property Group</strong>, Charlotte &mdash; their own staff are the architect of record in every state they build in.</p>
        <p><strong>And three more we have flagged rather than listed.</strong> <strong>Ovation Development</strong> (Las Vegas, 13,000 units, 1,650 more due by 2028) filed a prereview in July and names no architect &mdash; but it carries an in-house design principal, so treat it as a question rather than an opening. <strong>K&#299;lauea Town Expansion</strong> (Kaua&#699;i, 310 affordable units) and <strong>Mililani Teacher Workforce Housing</strong> (O&#699;ahu) name none either, but both are public bodies &mdash; the County of Kaua&#699;i and the State School Facilities Authority &mdash; so they will be procured rather than appointed, and K&#299;lauea breaks ground this quarter.</p>
        <p><strong>Not on the public record is not the same as not appointed.</strong> If you are already engaged on one of the nine above, what that tells you is how far the public record lags your own commissions &mdash; which is worth knowing on its own.</p>
      </div>
    </section>

"""


def main():
    s = io.open(BODY, encoding="utf-8").read()
    if 'id="openings"' in s:
        s = re.sub(r'    <section id="openings">.*?\n    </section>\n',
                   lambda m: SECTION, s, count=1, flags=re.S)
        where = "replaced"
    else:
        anchor = '    <section id="leads">'
        if anchor not in s:
            raise SystemExit("cannot find #leads to place #openings before")
        s = s.replace(anchor, SECTION + anchor, 1)
        where = "inserted before #leads"

    entry = ('<li><a href="#openings">Nine schemes where the record names '
             'no architect</a></li>')
    if 'href="#openings"' in s:
        s = re.sub(r'<li><a href="#openings">.*?</a></li>',
                   lambda m: entry, s, count=1)
    else:
        a = '<li><a href="#leads">'
        i = s.find(a)
        if i < 0:
            raise SystemExit("cannot place #openings in the contents")
        s = s[:i] + entry + "\n      " + s[i:]

    io.open(BODY, "w", encoding="utf-8").write(s)
    print("#openings %s" % where)
    n = len(re.findall(r"<tr>", re.search(
        r'<section id="openings">.*?</section>', s, re.S).group(0)))
    print("   rows in the table: %d" % n)


if __name__ == "__main__":
    main()
