#!/usr/bin/env python3
"""Charlotte rezoning petitions that are PENDING, newest first, developer named.

WHY THIS ONE IS DIFFERENT FROM EVERYTHING ELSE IN THIS REPO
    Almost every public construction record exists BECAUSE A DESIGN TEAM FILED
    IT. Texas TDLR names the design firm because the design firm submitted it.
    A site plan was drawn by somebody. A Miami UDRB packet is the architect's
    own drawing set. By the time any of that is public the commission is gone,
    and a list built from it is a record of decisions already taken.

    A rezoning petition is filed by the DEVELOPER, before the scheme is
    designed. Charlotte publishes the petitioner, the type and the status, and
    names no design team -- which is the honest reason its concentration cannot
    be measured, and precisely why the list is useful. These are companies that
    want to build something and have not yet announced who is drawing it.

    Status "Pen" is pending: filed, not yet decided.

USAGE
    python charlotte_pending_petitions.py [rezonings.json] [months]
"""
import datetime
import json
import os
import sys
from collections import Counter


def load(path):
    d = json.load(open(path, encoding="utf-8"))
    return [f["attributes"] for f in d["features"]]


def when(v):
    if not v:
        return None
    return datetime.datetime.fromtimestamp(
        v / 1000, datetime.timezone.utc).date()


def main(path, months=6):
    rows = load(path)
    for a in rows:
        a["_d"] = when(a.get("Received"))
    dated = [a for a in rows if a["_d"]]
    newest = max(a["_d"] for a in dated)
    print("petitions in the file : %d" % len(rows))
    print("received range        : %s .. %s" % (min(a["_d"] for a in dated), newest))

    pending = [a for a in dated if (a.get("Status") or "").startswith("Pen")]
    cutoff = newest - datetime.timedelta(days=months * 31)
    recent = sorted([a for a in pending if a["_d"] >= cutoff],
                    key=lambda a: a["_d"], reverse=True)
    print("pending               : %d" % len(pending))
    print("pending, last %d months: %d" % (months, len(recent)))
    print()
    print("%-12s %-11s %-46s %s" % ("RECEIVED", "PETITION", "PETITIONER", "TYPE"))
    for a in recent:
        print("%-12s %-11s %-46s %s"
              % (a["_d"], str(a.get("Petition"))[:11],
                 str(a.get("Petitioner"))[:46], a.get("Type") or ""))
    print()
    print("by month:")
    for k, v in sorted(Counter(a["_d"].strftime("%Y-%m") for a in recent).items()):
        print("   %s  %d" % (k, v))


if __name__ == "__main__":
    default = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           "docs", "audit-2026-08", "mve-pipeline",
                           "charlotte-rezoning-pending-2026.json")
    main(sys.argv[1] if len(sys.argv) > 1 else default,
         int(sys.argv[2]) if len(sys.argv) > 2 else 6)
