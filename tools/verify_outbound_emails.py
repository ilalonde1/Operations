"""Read the built .eml files back and check them, rather than trusting the write.

Checks that matter before these are sent:
  - the draft flag is present, or Outlook opens them read-only
  - the recipient is the intended one
  - the attachment is byte-identical to the CLIENT dossier, and is not the
    internal Regional Intel file, whose name differs by two words
  - the body survived encoding intact
"""
import hashlib
import os
from email import policy
from email.parser import BytesParser

REPO = r"C:\VIsual Studio Projects\Operations"
OUT = os.path.join(REPO, "docs", "audit-2026-08", "outbound")
CLIENT_PDF = os.path.join(REPO, "docs",
                          "KOR-MVE-Market-Snapshot-2026-08-28-web.pdf")
INTERNAL = "KOR-MVE-Regional-Intel-2026-08-27-web.pdf"

want = hashlib.sha256(open(CLIENT_PDF, "rb").read()).hexdigest()
ok_all = True

for fn in sorted(os.listdir(OUT)):
    if not fn.endswith(".eml"):
        continue
    path = os.path.join(OUT, fn)
    msg = BytesParser(policy=policy.default).parse(open(path, "rb"))
    print("=== %s" % fn)
    checks = [
        ("opens as a sendable draft (X-Unsent)", msg["X-Unsent"] == "1"),
        ("addressed to Dan Gura",
         "dgura@mve-architects.com" in (msg["To"] or "")),
        ("has a subject", bool((msg["Subject"] or "").strip())),
        ("from a korstructural.com address",
         "korstructural.com" in (msg["From"] or "")),
    ]
    body = msg.get_body(preferencelist=("plain",))
    text = body.get_content() if body else ""
    checks.append(("body is present and not truncated",
                   len(text) > 400 and text.rstrip().endswith("korstructural.com")))
    # The defect that shipped the first time: a single non-ASCII character
    # forces quoted-printable, and any reader that fails to decode it shows a
    # literal "=" and eats the character after it.
    checks.append(("body is 7bit, not quoted-printable",
                   (body["Content-Transfer-Encoding"] or "").lower() == "7bit"))
    checks.append(("body is pure ASCII",
                   all(ord(c) < 128 for c in text)))
    checks.append(("no stray '=' anywhere in the body", "=" not in text))
    checks.append(("subject is pure ASCII",
                   all(ord(c) < 128 for c in (msg["Subject"] or ""))))
    # Every newline in a plain-text mail is a HARD break, so a body wrapped at
    # 78 columns renders as a narrow column with a wide empty margin. If no
    # line runs long, the paragraphs have been hard-wrapped again.
    lines = [l for l in text.split("\n") if l.strip()]
    checks.append(("paragraphs are unwrapped, not hard-wrapped at 78 cols",
                   any(len(l) > 100 for l in lines)))
    checks.append(("no line exceeds the RFC 5322 limit of 998",
                   all(len(l) <= 998 for l in lines)))
    checks.append(("no internal-only document referenced",
                   INTERNAL not in text))

    atts = [p for p in msg.iter_attachments()]
    if fn.startswith("01"):
        checks.append(("exactly one attachment", len(atts) == 1))
        if atts:
            data = atts[0].get_payload(decode=True)
            got = hashlib.sha256(data).hexdigest()
            checks.append(("attachment is the CLIENT dossier, byte-identical",
                           got == want))
            checks.append(("attachment is NOT the internal file",
                           atts[0].get_filename() != INTERNAL))
            print("    attachment: %s  (%.0f KB)"
                  % (atts[0].get_filename(), len(data) / 1024))
    else:
        checks.append(("no attachment, as intended", len(atts) == 0))

    for label, good in checks:
        ok_all &= good
        print("  %s %s" % ("ok  " if good else "!!!!", label))
    print("  To: %s\n  Subject: %s\n" % (msg["To"], msg["Subject"]))

print("RESULT: %s" % ("PASS" if ok_all else "FAIL"))
