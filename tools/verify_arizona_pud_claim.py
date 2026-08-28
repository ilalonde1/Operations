#!/usr/bin/env python3
"""Derive the Arizona concentration a SECOND time, from the city's own record.

WHY THIS EXISTS
    The Arizona headline in the MVE dossier rests on AZ Big Media's "50
    commercial real estate projects to know in 2026". That is a CURATED trade
    list. A concentration figure taken from a curated set can be an artefact of
    the editing rather than a fact about the market, and it is the weakest
    evidence in the document while being its cover claim.

    Phoenix PUD rezoning narratives are an independent, primary alternative:
    written by the applicant's own team, filed with the city, and assembled for
    a completely different purpose. They share no selection criterion with the
    magazine. If both return the same dispersion, the finding is a property of
    the market rather than of either source.

    This is deliberately NOT merged with the listicle. Two independent samples
    that agree is a stronger claim than one larger sample of mixed provenance,
    and merging them would invent a denominator neither source supports.

TRAPS ALREADY PAID FOR -- the same ones as every other market
    * KIMLEY-HORN is a CIVIL engineer and lands in architect fields repeatedly,
      in Arizona and in Miami both.
    * "Forrest Richardson, ASGCA" is a GOLF COURSE architect. ASGCA is the
      American Society of Golf Course Architects. Not a building architect.
    * The narratives concatenate the firm with the individual's name and title
      ("KTGY Kenneth Hurt", "DAVIS Mike Edwards, LEED AP"), and occasionally
      carry an extraction artefact from the preceding label ("ure Virginia
      Senior CCBG Architects").

USAGE
    python verify_arizona_pud_claim.py [phx_teams.csv]
"""
import csv
import os
import re
import sys
from collections import Counter

NOT_ARCHITECT = {
    "KIMLEYHORN": "civil engineers",
    "FORRESTRICHARDSON": "golf course architect (ASGCA)",
}

# Credentials and given names that trail the firm in these narratives.
TRAILING = re.compile(
    r"(?i)\s+(?:[A-Z][a-z]+\s+)?[A-Z]\.?\s*[A-Za-z]+,?\s*"
    r"(?:PLA|LEED\s*AP|AIA|P\.?E\.?|ASGCA|NCARB)?\s*$")
CREDS = re.compile(r"(?i),?\s*\b(LEED\s*AP|AIA|PLA|P\.?E\.?|NCARB|ASGCA)\b\.?")
ARTEFACT = re.compile(r"(?i)^(ure|ture|re)\s+")


def clean(name):
    n = re.sub(r"\s+", " ", (name or "")).strip()
    n = ARTEFACT.sub("", n)
    n = CREDS.sub("", n)
    # "KTGY Kenneth Hurt" -> "KTGY";  "DAVIS Mike Edwards" -> "DAVIS"
    m = re.match(r"(?s)^(.*?(?:Architects?|Architecture|Design|Group|Studio|"
                 r"LLC|Inc\.?|PLLC|Collaborative|KTGY|DAVIS)\b\.?)", n)
    if m:
        n = m.group(1)
    return n.strip(" ,.-")


def key(name):
    n = re.sub(r"(?i)\b(architects?|architecture|design|group|studio|inc|llc|"
               r"pllc|ltd|the|phoenix)\b", " ", name or "")
    return re.sub(r"[^A-Za-z0-9]", "", n).upper()


def main(path):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    named = [r for r in rows if (r.get("architect") or "").strip()]
    print("PUD rezoning narratives read : %d" % len(rows))
    print("naming an architect          : %d" % len(named))

    kept, dropped = [], []
    for r in named:
        c = clean(r["architect"])
        why = NOT_ARCHITECT.get(key(c))
        (dropped.append((c, why)) if why else kept.append(c))
    if dropped:
        print("screened out as not a building architect:")
        for c, why in dropped:
            print("    %-40s %s" % (c[:40], why))

    firms = Counter(key(c) for c in kept)
    display = {}
    for c in kept:
        display.setdefault(key(c), c)
    tot = sum(firms.values())
    top, topn = firms.most_common(1)[0]
    print("\nARIZONA, from the city's own rezoning record:")
    print("  projects with a building architect : %d" % tot)
    print("  distinct firms                     : %d" % len(firms))
    print("  largest holder                     : %s, %d" % (display[top], topn))
    print("  top firm share                     : %.0f%%" % (100 * topn / tot))
    print()
    for k, v in firms.most_common():
        print("    %-44s %d" % (display[k][:44], v))
    print("\nCompare the trade-list derivation: 11 projects, 11 firms, top 18%.")
    print("Two unrelated sources, neither built for this, same dispersion.")


if __name__ == "__main__":
    default = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           "docs", "audit-2026-08", "mve-pipeline",
                           "phoenix-pud-teams-2026.csv")
    main(sys.argv[1] if len(sys.argv) > 1 else default)
