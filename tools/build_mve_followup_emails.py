#!/usr/bin/env python3
"""Build the two MVE follow-up emails as .eml drafts that open in Outlook.

⛔ DO NOT HARD-WRAP THE PARAGRAPHS.

    In a plain-text mail EVERY NEWLINE IS A HARD BREAK. A body wrapped at 78
    columns therefore renders as a narrow column with a wide empty margin in
    any window wider than that, which is what "squished" looked like on the
    first send. Keep each paragraph on ONE line and let the reader's client
    flow it to their window. Only the indented market list keeps its own
    line breaks, and it is indented precisely so it survives as a block.

⛔ THE BODY MUST BE PURE ASCII. THIS IS NOT A STYLE PREFERENCE.

A single em-dash makes Python encode the whole body as quoted-printable, where
"=" at the end of a line is a SOFT LINE BREAK. Anything that fails to decode
that -- a preview pane, a paste into another client, a text editor -- shows a
literal "=" AND SWALLOWS THE FOLLOWING CHARACTER:

    "of them filed"        becomes   "of th=m filed"
    "preliminary or final" becomes   "prelimina=y or final"

The first build of these went out looking like that. Git's newline conversion
on .eml compounds it, which is why .gitattributes now marks *.eml as -text.

With an ASCII-only body the transfer encoding is 7bit, the raw file is exactly
what the reader sees, and there is nothing left to decode wrongly. Use " - "
where an em-dash is wanted and a straight quote for an apostrophe.

X-Unsent: 1 is what makes Outlook open an .eml as an editable, SENDABLE draft.
Without it the file opens read-only with no Send button.

Mark Kim's address is deliberately absent: it is not in our records, and Dan
undertook to copy him in. Nothing here is guessed from a naming convention.
"""
import os
import sys
from email.message import EmailMessage

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "docs", "audit-2026-08", "outbound")
PDF = os.path.join(REPO, "docs", "KOR-MVE-Six-Market-Record-2026-08-28-web.pdf")

IAN = "Ian Lalonde <ilalonde@korstructural.com>"
JIM = "Jim DesRoches <jdesroches@korstructural.com>"
DAN = "Dan Gura <dgura@mve-architects.com>"

IAN_SUBJECT = "Your Ward Village client has opened 7,250 acres in three of your other markets"

IAN_BODY = """Dan,

Good to meet. Here is the package I promised, produced by our Ops Brain.

The Arizona submitted-projects search you asked for is in there, and it is the third page: 373 open site-plan and rezoning cases out of Phoenix's own record, 280 filed since January 2025, each marked preliminary or final. I re-ran it against the live service before sending.

Page one is the part I would look at first, because it is about a company you already work for. Howard Hughes filed a Las Vegas multifamily pre-application on 27 August - the ownership disclosure names David O'Reilly, Jay Cross and Carlos Olea, so there is no doubt whose it is. In Houston the same group has filed thirty-nine subdivision plats since December, 7,250 acres, including a 3,905-acre general plan in February and a 2,037-acre one in June. And their own SEC subsidiaries exhibit puts them in the Phoenix west valley as well, at Teravalis. Four of your six markets, one owner, and no architect named on any of it.

Page two is nine schemes where the public record names no architect - Host Hotels & Resorts turning 72 acres of the Westin Kierland golf course into resort condominium and condo-hotel, Vintage Partners putting 1,000 units on a site that was going to be a data centre, Mid-America Apartments on 3.65 acres in SouthPark, Crosland Southeast on 39 acres where the engineer's seal block is still a blank placeholder, and Makena Mauka on Maui at 652 units with its final EIS accepted five days ago.

The part I would judge us on is the six we took OUT. Every one of those nine was checked twice - the case file, environmental assessment or site plan read in full including the drawing title block, and then a separate trade-press check. Six failed. Fifield Companies in Phoenix has Todd & Associates on it, named in the trade press and in no city document at all. Elevation Living has Woods Associates and J&K Luxury has Kontexture, both hiding in the drawing title block rather than the contact list. The Miami Design District expansion has David Chipperfield. Wailuku Mission on Maui already has MASON Architects on the historic rehabilitation. And Kittle Property Group in Charlotte is its own architect of record in every state it builds in.

We also crossed your published client list against the whole pack and dropped two of your own clients: Hines appears three times in the Arizona record, all completed office fit-outs with Phoenix Design One already named, and Vestar appears on a Phoenix rezoning that already names Butler Design Group. Ward Village, Kalae and Launiu come out on the same rule - they are your work, and they are in the document only as the reason the Howard Hughes thread is worth your time.

The rest is the same treatment for the other five markets. Houston publishes the developer company, the filing consultancy and a named contact on 100% of 1,426 plat applications - which is the opposite of Houston's reputation. Hawaii turned out to have the best-structured record of the six: a statewide bulletin, twice a month, naming the applicant and the planning consultant on every project before design. Charlotte has eighty-four pending petitions, of which five are worth a call. Miami is genuinely thin and the document says why rather than padding it.

The limits matter more than the list. Not on the public record is not the same as not appointed - if you are already engaged on something in here, what that tells you is how far the public record lags your own commissions. And we threw away nine of twelve apparent Phoenix openings before printing three.

There is a companion setting out how the design-team concentration figures were arrived at and what each rests on. Say the word and I will send it.

This was a single sweep, run by hand. The useful version is the same thing running every week and telling you only what changed - a new pre-application, a developer who has just retained counsel, an architect appearing on a case that did not have one. That is what I will show you on the next call.

Ian


Ian Lalonde
KOR Structural
ilalonde@korstructural.com
"""

JIM_SUBJECT = "Following up, and getting the right people in the room"

JIM_BODY = """Dan,

Thanks for making the time, and for being as open as you were about how MVE actually picks a structural engineer. That was the most useful hour we have had with anyone this year.

What I took away:

You have a full plate for the next eighteen months to two years, mostly wood frame, with concrete coming back on the luxury condo side. We are comfortable in both: Hartford Boulevard in LA, affordable housing in San Diego, La Jolla.

You have not done mass timber yet. We are building a mass-timber roof in San Diego right now, and I would like to show you that one.

And the offices are Californian but the work is national: Arizona, Nevada, Hawaii, Houston, Charlotte, Miami. We are registered in all of them. Ian has sent you a package that shows what we can see in each.

Two things.

First, you mentioned pulling Matt, Mark and Ken together for a follow-up next month. If you copy them on this, we will work around whatever date suits them. Ian will bring the full lifecycle demo rather than the walk-through we gave you.

Second, I am down your way periodically and I would like to make the next trip count. I will give you and Matt a week to ten days' notice so you can get the people who decide on structural into the office. I would like to come to Irvine, and meet Chase in San Diego on the same trip.

Jim


Jim DesRoches
KOR Structural
jdesroches@korstructural.com
"""


def assert_ascii(label, text):
    bad = sorted({c for c in text if ord(c) > 127})
    if bad:
        raise SystemExit(
            "%s contains non-ASCII %s -- that forces quoted-printable and "
            "corrupts the message. Replace them." % (label, [hex(ord(c)) for c in bad]))


def build(path, sender, subject, body, attach=None):
    assert_ascii("subject", subject)
    assert_ascii("body", body)
    m = EmailMessage()
    m["From"] = sender
    m["To"] = DAN
    m["Subject"] = subject
    m["X-Unsent"] = "1"
    m.set_content(body, cte="7bit")
    size_attached = 0
    if attach:
        with open(attach, "rb") as fh:
            data = fh.read()
        size_attached = len(data)
        m.add_attachment(data, maintype="application", subtype="pdf",
                         filename=os.path.basename(attach))
    raw = bytes(m)
    with open(path, "wb") as fh:
        fh.write(raw)
    return len(raw), size_attached


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    a = os.path.join(OUT, "01-Ian-to-Dan-dossier.eml")
    b = os.path.join(OUT, "02-Jim-to-Dan-followup.eml")
    sa, pa = build(a, IAN, IAN_SUBJECT, IAN_BODY, PDF)
    sb, _ = build(b, JIM, JIM_SUBJECT, JIM_BODY)
    print("built:")
    print("  %-32s %6.0f KB  (pdf %.0f KB)" % (os.path.basename(a), sa / 1024, pa / 1024))
    print("  %-32s %6.0f KB" % (os.path.basename(b), sb / 1024))
    print("  in %s" % OUT)
    print("\nnow run: python tools/verify_outbound_emails.py")
