#!/usr/bin/env python3
"""Take our working out of the client document.

⛔ THE RULE THIS RESTORES
    A client document carries FINDINGS, never technique. Dan has never seen a
    previous version of this, so a box headed "Six we removed" is meaningless to
    him -- it describes OUR process, and it is also the edge, given away free.

    Cut outright:
      #openings  "Six we removed, and where each one's architect was hiding"
      #openings  the lede explaining the four tests each row passed
      #facts     "What we deliberately did not put in front of you"
      #arizona   "The field that looks like the answer, and is not"
      #arizona   "We read thirty PUD case files in full ... and were dropped"

    Kept, because they are findings about the RECORD rather than about us:
      #hawaii    what Chapter 343 does not cover -- it is why Ward Village is
                 absent, which Dan would otherwise ask about
      #arizona   "On the test you set us" -- answers his question
      #leads     a general plan is not a commission -- reframed from "why we
                 excluded these" into what the Houston record actually is

    Compressed to one line: how Bridgeland ties to Howard Hughes. Dan would
    reasonably doubt the link, so the SEC exhibit stays as a citation -- but as
    a footnote, not a box about our research.
"""
import io
import re

BODY = r"C:\VIsual Studio Projects\Operations\docs\audit-2026-08\mve-designteam-body.html"

OPENINGS_LEDE = (
    '      <p class="lede">All nine are at a stage before an architect is '
    'normally appointed &mdash; land being rezoned, a petition pending, an '
    'environmental assessment in review, or a pre-application lodged with the '
    'county. None of them names an architect on the public record. Newest '
    'filing 27 August.</p>')

SIGNAL_FOOT = (
    '      <p><span class="muted">The Houston filings are under '
    '<em>Bridgeland Development, LP</em> and <em>The Woodlands Land Development '
    'Company, L.P.</em>; both are named in Howard Hughes Holdings&rsquo; '
    'subsidiaries exhibit filed with the SEC on 19 February 2026, alongside '
    'Summerlin, Ward Village and Teravalis.</span></p>')

LEADS_BOX = """      <div class="box box--tip">
        <p class="box__label">&#9650; What a 600-acre general plan is, and is not</p>
        <p>Brookfield&rsquo;s 591-acre Midline, Johnson Development&rsquo;s 696-acre Amira and Taylor Morrison&rsquo;s 262-acre Avalon at Cypress are all on this year&rsquo;s record with no architect named. <strong>None is required.</strong> A Chapter 42 general plan divides land into streets, blocks and reserves; the homes that follow are production builders&rsquo; own designs. These filings tell you where capital is going in Houston. They are not work.</p>
      </div>
"""

HAWAII_BOX = """      <div class="box box--dont">
        <p class="box__label">&#10005; What this bulletin does not reach</p>
        <p>Chapter 343 is triggered by <em>state or county involvement</em>. A wholly private project, on private land, with no shoreline or conservation trigger, never appears &mdash; <strong>Ward Village is exactly that kind of project</strong>, which is why your own Honolulu work is not in it. The Kaka&#699;ako pipeline runs instead through the Hawai&#699;i Community Development Authority, and <strong>no new development permit is before its board for the 2 September sitting</strong>.</p>
      </div>
"""


def cut_box(sec, label_fragment):
    pat = (r'      <div class="box[^"]*">\s*\n'
           r'        <p class="box__label">[^<]*' + label_fragment +
           r'[^<]*</p>.*?\n      </div>\n')
    new, n = re.subn(pat, "", sec, flags=re.S)
    return new, n


def main():
    s = io.open(BODY, encoding="utf-8").read()
    report = []

    def section(sid):
        m = re.search(r'    <section id="%s">.*?\n    </section>\n' % sid,
                      s, re.S)
        if not m:
            raise SystemExit("#%s not found" % sid)
        return m

    # ---- #openings: drop the exclusions box and the four-tests lede
    m = section("openings")
    sec = m.group(0)
    sec, n = cut_box(sec, "removed")
    report.append(("#openings exclusions box", n))
    sec, n2 = re.subn(r'      <p class="lede">Each row passed four tests.*?</p>',
                      OPENINGS_LEDE, sec, flags=re.S)
    report.append(("#openings four-tests lede", n2))
    s = s[:m.start()] + sec + s[m.end():]

    # ---- #signal: box -> one-line footnote
    m = section("signal")
    sec = m.group(0)
    sec, n = cut_box(sec, "How we knew")
    sec = sec.replace("    </section>\n", SIGNAL_FOOT + "\n    </section>\n")
    report.append(("#signal method box -> footnote", n))
    s = s[:m.start()] + sec + s[m.end():]

    # ---- #facts: drop the client-exclusion box
    m = section("facts")
    sec = m.group(0)
    sec, n = cut_box(sec, "did not put in front of you")
    report.append(("#facts client-exclusion box", n))
    s = s[:m.start()] + sec + s[m.end():]

    # ---- #arizona: drop the TO BE BID box and the PUD-reading sentence
    m = section("arizona")
    sec = m.group(0)
    sec, n = cut_box(sec, "looks like the answer")
    report.append(("#arizona TO-BE-BID box", n))
    sec, n2 = re.subn(
        r'\s*<p><strong>That is why both Phoenix entries.*?</p>',
        '\n      <p><strong>That is why both Phoenix entries on the opening '
        'chart are rezonings, and why none of the 281 preliminary site-plan '
        'cases is among them.</strong></p>', sec, flags=re.S)
    report.append(("#arizona PUD-reading sentence", n2))
    s = s[:m.start()] + sec + s[m.end():]

    # ---- #leads: reframe the exclusion box as a finding
    m = section("leads")
    sec = m.group(0)
    sec, n = cut_box(sec, "not on this list")
    sec = sec.replace("    </section>\n", LEADS_BOX + "    </section>\n")
    report.append(("#leads exclusion box -> finding", n))
    s = s[:m.start()] + sec + s[m.end():]

    # ---- #hawaii: keep the limit, drop the "we read its agenda" method
    m = section("hawaii")
    sec = m.group(0)
    sec, n = cut_box(sec, "does not cover")
    sec = sec.replace("    </section>\n", HAWAII_BOX + "    </section>\n")
    report.append(("#hawaii limits box tightened", n))
    s = s[:m.start()] + sec + s[m.end():]

    io.open(BODY, "w", encoding="utf-8").write(s)

    for what, n in report:
        print("   %-40s %s" % (what, "done" if n else "NOT MATCHED"))

    print()
    print("method language remaining in the body:")
    for probe in ["we removed", "we checked", "we read", "did not survive",
                  "we threw away", "we tested", "four tests", "we crossed",
                  "we have left", "we would rather"]:
        c = len(re.findall(re.escape(probe), s, re.I))
        print("   %-22s %d" % (probe, c))


if __name__ == "__main__":
    main()
