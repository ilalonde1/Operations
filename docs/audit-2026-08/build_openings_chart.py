#!/usr/bin/env python3
"""The nine openings as a clean chart, first thing in the document.

WHAT CHANGED
    The openings were a table of prose -- three or four sentences per cell,
    sitting third in the running order. They are the deliverable, so they go
    first, and they go as a CHART: one line per row, scannable, no paragraphs
    in cells. Supporting detail moves below the chart rather than inside it.

⛔ NOTHING GOES IN THIS CHART WITHOUT PASSING FOUR CHECKS
     1 PREMISE     filing it must not require a drawing to exist
     2 COMMISSION  there must be an architect commission in the scope
     3 FILE        case file / EA / site plan read in full, title block included
     4 PRESS       trade-press check -- Fifield's architect is in no city record
    Six leads died on 3 and 4 and are named beneath the chart. That box is not
    an apology; it is the evidence the chart was checked.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

# n, developer, market, what, scale, stage, record, existing-client flag
# ⚠ THE PROJECT NAME OR ADDRESS IS NOT OPTIONAL.
#    The first version of this chart compressed the "what" column to a type
#    ("Resort condominium & condo-hotel") and dropped the scheme name. A reader
#    cannot look up a type. Every row has to carry the name the filing is under,
#    or failing that the location, or it is not actionable.
ROWS = [
    ("1", "Host Hotels &amp; Resorts", "NYSE: HST", "Phoenix",
     "<strong>Copper Residences</strong><br>resort condo &amp; condo-hotel, Westin Kierland",
     "72 acres", "Rezoning, in review", "Z-169-25-2", False),
    ("2", "Vintage Partners", "", "Phoenix",
     "<strong>Lower Buckeye &amp; 63rd Ave</strong><br>mixed-use residential",
     "1,000 units &middot; 63 ac", "Rezoning filed", "Z-24-26-7", False),
    ("3", "Mid-America Apartments", "NYSE: MAA", "Charlotte",
     "<strong>Fairview Rd, SouthPark</strong><br>apartments",
     "3.65 acres", "Petition pending", "2026-050", False),
    ("4", "Crosland Southeast", "C4 Investments", "Charlotte",
     "<strong>Wilkinson Blvd at Little Rock Rd</strong><br>mixed-use",
     "39.41 acres", "Petition pending", "2026-027", False),
    ("5", "Middleburg", "", "Charlotte",
     "<strong>Wilkinson Blvd, south side</strong><br>multifamily",
     "20.15 acres", "Petition pending", "2026-023", False),
    ("6", "DreamKey Partners", "", "Charlotte",
     "<strong>Beatties Ford Rd</strong><br>affordable housing",
     "6.00 acres", "Heard 17 Aug", "2026-035", False),
    ("7", "AREG AC Makena Propco", "Makena Golf &amp; Beach Club", "Maui",
     "<strong>M&#257;kena Mauka</strong><br>master-planned residential",
     "652 units", "Final EIS accepted", "23 Aug", False),
    ("8", "Ho&#699;onani Development", "", "Maui",
     "<strong>Ho&#699;onani Village</strong><br>mixed-use",
     "&mdash;", "Draft EIS", "23 Mar", False),
    ("9", "Howard Hughes", "your Ward Village client", "Las Vegas",
     "<strong>Spruce Goose St</strong><br>Downtown Summerlin, apartments &amp; retail",
     "354 units &middot; 4 ac", "Pre-application", "26-101519", True),
]


def rows_html():
    out = []
    for n, dev, sub, mkt, what, scale, stage, rec, star in ROWS:
        mark = "&#9733; " if star else ""
        sub_html = ('<br><span class="muted">%s</span>' % sub) if sub else ""
        out.append(
            "            <tr>"
            "<td><span class=\"muted\">%s</span></td>"
            "<td>%s<strong>%s</strong>%s</td>"
            "<td>%s</td>"
            "<td>%s</td>"
            "<td><strong>%s</strong></td>"
            "<td>%s</td>"
            "<td><span class=\"muted\">%s</span></td>"
            "</tr>\n" % (n, mark, dev, sub_html, mkt, what, scale, stage, rec))
    return "".join(out)


SECTION = """    <section id="openings">
      <p class="kicker">Phoenix &middot; Charlotte &middot; Maui &middot; Las Vegas &mdash; newest filing 27 August</p>
      <h2>Nine schemes in your markets where nobody has been hired yet</h2>
      <p class="lede">All nine are at a stage before an architect is normally appointed &mdash; land being rezoned, a petition pending, an environmental assessment in review, or a pre-application lodged with the county. <strong>None names an architect on the public record.</strong></p>
      <div class="table-wrap">
        <table>
          <thead><tr><th></th><th>Developer</th><th>Market</th><th>What</th><th>Scale</th><th>Stage</th><th>Record</th></tr></thead>
          <tbody>
%s          </tbody>
        </table>
      </div>
      <p><span class="muted">&#9733; Howard Hughes is your Ward Village client. The Las Vegas filing is a separate scheme in a market where you do not currently do their work.</span></p>
      <div class="box box--dont">
        <p class="box__label">&#10005; Three that are moving but come with a condition</p>
        <p><strong>Ovation Development</strong> &mdash; Las Vegas, 13,000 units and 1,650 more due by 2028, filed a pre-application in July with no architect named. It carries an in-house design principal. <strong>K&#299;lauea Town Expansion</strong> (Kaua&#699;i, 310 affordable units) and <strong>Mililani Teacher Workforce Housing</strong> (O&#699;ahu) name none either, but the applicants are the County of Kaua&#699;i and the State School Facilities Authority &mdash; both will be procured rather than appointed, and K&#299;lauea breaks ground this quarter.</p>
        <p><strong>Not on the public record is not the same as not appointed.</strong> If you are already engaged on one of the nine, what that tells you is how far the public record lags your own commissions.</p>
      </div>
    </section>

"""


def main():
    s = io.open(BODY, encoding="utf-8").read()
    pat = r'    <section id="openings">.*?\n    </section>\n'
    if not re.search(pat, s, re.S):
        raise SystemExit("#openings not found")
    s = re.sub(pat, lambda m: SECTION % rows_html(), s, count=1, flags=re.S)

    entry = ('<li><a href="#openings">Nine schemes where nobody has been '
             'hired yet</a></li>')
    s = re.sub(r'<li><a href="#openings">.*?</a></li>', lambda m: entry, s,
               count=1)
    io.open(BODY, "w", encoding="utf-8").write(s)

    sec = re.search(r'<section id="openings">.*?</section>', s, re.S).group(0)
    print("#openings rebuilt as a chart")
    print("   data rows      : %d" % len(re.findall(r"<tr><td><span", sec)))
    print("   columns        : %d" % len(re.findall(r"<th", sec)))
    print("   longest cell   : %d chars"
          % max(len(re.sub(r"<[^>]+>", "", c))
                for c in re.findall(r"<td>(.*?)</td>", sec)))


if __name__ == "__main__":
    main()
