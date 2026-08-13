"""Merge same-building records and drop small service jobs.

The two corrections Jim asked for on the 12 Aug call, in one place so the
proposal figure and the website cannot disagree about what the portfolio is.

1. MERGE. A building can hold one curated WordPress project post and one or
   more Deltek job records; they share no job number, so the existing WBS-base
   grouping never joined them. Result on screen was "eight projects up here
   when we only have three". Rules and their guard live in dedupe_rule.py.

2. FLOOR. The map's only test was "somebody charged time to it", which is why a
   $100 vault-lid repair and an $11,780 peer review sat on it as equals with a
   tower. Jobs under $25,000 of billable labour drop off.

   Three exemptions, or the floor would delete the portfolio it is meant to
   clean: a record with a project page stays (it is the marketing), a PRIOR-era
   record stays (Jim's pre-KOR work was billed at another firm, so KOR shows
   nil against it), and a record whose name never matched Deltek stays, because
   a failed join is ignorance, not evidence of a small job.

The surviving pin takes the curated record's identity -- name, photo, link and
coordinates -- so a merged building reads "Electra" and not "Electra SDG&E
Vault Lid Repair". Countable fields are summed; `era` resolves KOR over PRIOR
over blank; `people` is a union.
"""
import collections, csv, html, io, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dedupe_rule import build, pair_up, norm_name

FLOOR = 25000.0


def billed_by_name(fee_csv):
    """Deltek billable labour, aggregated to the WBS base and keyed by every
    normalised job name in that base -- the map pin carries a name, not a
    number, so the name is the only join available on this side."""
    grp = collections.defaultdict(float)
    names = collections.defaultdict(set)
    for r in csv.DictReader(io.open(fee_csv, encoding="utf-8-sig")):
        base = r["WBS1"].split("-")[0]
        try:
            grp[base] += float(r["Billed"] or 0)
        except ValueError:
            pass
        names[base].add(" ".join(norm_name(r["Name"])))
    out = {}
    for base, total in grp.items():
        for n in names[base]:
            if n:
                out[n] = max(out.get(n, 0.0), total)
    return out


def _pick(cluster):
    """The record whose identity the merged pin adopts."""
    return sorted(cluster, key=lambda r: (
        not r["curated"],                                   # a project page wins
        not str(r["p"].get("photo") or "").startswith("http"),
        -int(r["p"].get("jobs") or 0),
        r["i"],
    ))[0]


def _era(cluster):
    eras = {(r["p"].get("era") or "").upper() for r in cluster}
    return "KOR" if "KOR" in eras else ("PRIOR" if "PRIOR" in eras else "")


def merge_cluster(cluster):
    lead = _pick(cluster)
    out = dict(lead["p"])
    # Fill blanks from the other records rather than inheriting the leader's
    # gaps -- the curated post often omits the developer the Deltek row has.
    for field in ("address", "description", "developer", "architect", "photo", "link"):
        if not str(out.get(field) or "").strip():
            for r in cluster:
                v = str(r["p"].get(field) or "").strip()
                if v:
                    out[field] = r["p"][field]
                    break
    people = set()
    for r in cluster:
        people.update(p for p in (r["p"].get("people") or "").split(",") if p)
    out["people"] = ",".join(sorted(people))
    out["era"] = _era(cluster)
    out["jobs"] = sum(int(r["p"].get("jobs") or 0) for r in cluster)
    return {"type": "Feature",
            "geometry": {"type": "Point", "coordinates": list(lead["xy"])},
            "properties": out}


def apply(features, fee_csv=None, floor=FLOOR, verbose=True):
    rows = build(features)
    clusters, owner, reasons, refused = pair_up(rows)
    billed = billed_by_name(fee_csv) if fee_csv else {}

    kept, dropped = [], []
    for members in clusters.values():
        curated = any(r["curated"] or str(r["p"].get("photo") or "").startswith("http")
                      for r in members)
        era = _era(members)
        hit = False
        total = 0.0
        for r in members:
            v = billed.get(r["key"])
            if v is not None:
                total += v
                hit = True
        exempt = curated or era == "PRIOR" or not hit
        if billed and not exempt and total < floor:
            dropped.append((total, members))
            continue
        kept.append(merge_cluster(members))

    if verbose:
        merged_away = len(rows) - len(clusters)
        print("  merged   %4d records into %d pins (-%d)" % (len(rows), len(clusters), merged_away))
        if billed:
            print("  floor    dropped %d pins under $%s billed" % (len(dropped), f"{int(floor):,}"))
        print("  result   %d pins" % len(kept))
    return kept, dropped


if __name__ == "__main__":
    S = os.path.dirname(os.path.abspath(__file__))
    src = sys.argv[1] if len(sys.argv) > 1 else S + "/km_now.json"
    feats = json.load(io.open(src, encoding="utf-8"))["features"]
    kept, dropped = apply(feats, S + "/fee.csv")
    io.open(S + "/km_clean.json", "w", encoding="utf-8").write(
        json.dumps({"type": "FeatureCollection", "features": kept}, ensure_ascii=False))
    print("wrote km_clean.json")
    print("\n-- 25 largest jobs the floor removed --")
    for total, m in sorted(dropped, key=lambda x: -x[0])[:25]:
        print("   $%9s  %s" % (f"{total:,.0f}",
                               " / ".join(html.unescape(r["p"]["name"])[:38] for r in m)))
