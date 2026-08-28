"""Build the two MVE follow-up emails as .eml drafts that open in Outlook.

X-Unsent: 1 is what makes Outlook open an .eml as an EDITABLE, SENDABLE draft
rather than as a received message. Without it the file opens read-only and the
Send button is absent.

Ian's carries the dossier as a real attachment; Jim's does not, because Jim's
message refers to Ian's package rather than duplicating it.

Mark Kim's address is deliberately absent -- it is not in our records and Dan
undertook to copy him in. Nothing here is guessed from a naming convention.
"""
import os
from email.message import EmailMessage

REPO = r"C:\VIsual Studio Projects\Operations"
OUT = os.path.join(REPO, "docs", "audit-2026-08", "outbound")
PDF = os.path.join(REPO, "docs", "KOR-MVE-Design-Team-Dossier-2026-08-28-web.pdf")

IAN = "Ian Lalonde <ilalonde@korstructural.com>"
JIM = "Jim DesRoches <jdesroches@korstructural.com>"
DAN = "Dan Gura <dgura@mve-architects.com>"

IAN_SUBJECT = ("Arizona submitted projects "
               "\u2014 and the same search in your other five markets")

IAN_BODY = """Dan,

Good to talk. Here is the package I promised.

You asked for the Arizona submitted-projects search. It is in there \u2014 373 open site-plan and rezoning cases out of Phoenix's own record, 280 of them filed since January 2025, and 41 residential. Each one is marked preliminary or final, so you can see how much of the design conversation is still open before you spend a call on it.

The list turned out to be the least interesting part.

Counted properly, the Arizona multifamily projects that name an architect are spread across eleven different firms. One firm is on two of them. Nobody is on three. There is no incumbent to displace, because there isn't one.

That is only worth saying if the same measurement can produce a different answer somewhere else, so we ran it on Raleigh. It does: a clear leader holding 36% of the field, and fifteen months into an ownership change. Then we ran it on Houston and Miami as well. Ranked by how much of each market the leading firm holds:

    Houston   10%
    Miami     17%
    Arizona   18%
    Raleigh   36%

Three of your four measurable markets have no incumbent worth the name. Raleigh is the outlier, and it is the one where the leader has just changed hands.

On the test you set us \u2014 what happens to a 40-storey project filed that morning \u2014 the honest answer is in the document rather than hidden in it. It is not in any public feed yet, and nobody's is. It surfaces in days. What we can defend is that nothing published gets missed; not that nothing unpublished is known.

Every figure is drawn from public municipal records or from the published statements of the parties named, current to 28 August. The Houston record is current to the 27th. Where something could only be sourced to secondary reporting we left it out rather than hedge it, and there are a couple of places in there where we say plainly that a market cannot be measured yet.

Happy to walk through any single number on a call.

I will have the full lifecycle demo ready for the next one \u2014 opportunity through to proposal, rather than the tour we gave you.

Ian

Ian Lalonde
KOR Structural
ilalonde@korstructural.com
"""

JIM_SUBJECT = "Following up \u2014 and getting the right people in the room"

JIM_BODY = """Dan,

Thanks for making the time, and for being as open as you were about how MVE actually picks a structural engineer. That was the most useful hour we have had with anyone this year.

What I took away:

You have a full plate for the next eighteen months to two years, mostly wood frame, with concrete coming back on the luxury condo side. We are comfortable in both \u2014 Hartford Boulevard in LA, affordable housing in San Diego, La Jolla.

You have not done mass timber yet. We are building a mass-timber roof in San Diego right now, and I would like to show you that one.

And the offices are Californian but the work is national \u2014 Arizona, Nevada, Hawaii, Houston, Charlotte, Miami. We are registered in all of them. Ian has sent you a package that shows what we can see in each.

Two things.

First, you mentioned pulling Matt, Mark and Ken together for a follow-up next month. If you copy them on this, we will work around whatever date suits them. Ian will bring the full lifecycle demo rather than the walk-through we gave you.

Second, I am down your way periodically and I would like to make the next trip count. I will give you and Matt a week to ten days' notice so you can get the people who decide on structural into the office. I would like to come to Irvine, and meet Chase in San Diego on the same trip.

Jim

Jim DesRoches
KOR Structural
jdesroches@korstructural.com
"""


def build(path, sender, subject, body, attach=None):
    m = EmailMessage()
    m["From"] = sender
    m["To"] = DAN
    m["Subject"] = subject
    m["X-Unsent"] = "1"          # Outlook: open as a sendable draft
    m.set_content(body)
    if attach:
        with open(attach, "rb") as fh:
            data = fh.read()
        m.add_attachment(data, maintype="application", subtype="pdf",
                         filename=os.path.basename(attach))
    with open(path, "wb") as fh:
        fh.write(bytes(m))
    return len(bytes(m)), (len(data) if attach else 0)


os.makedirs(OUT, exist_ok=True)
a = os.path.join(OUT, "01-Ian-to-Dan-dossier.eml")
b = os.path.join(OUT, "02-Jim-to-Dan-followup.eml")
sa, pa = build(a, IAN, IAN_SUBJECT, IAN_BODY, PDF)
sb, _ = build(b, JIM, JIM_SUBJECT, JIM_BODY)
print("built:")
print("  %-34s %7.0f KB  (pdf attached: %.0f KB)" % (os.path.basename(a), sa / 1024, pa / 1024))
print("  %-34s %7.0f KB" % (os.path.basename(b), sb / 1024))
print("  in %s" % OUT)
