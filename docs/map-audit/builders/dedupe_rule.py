"""Prove the same-building merge rule before it goes anywhere near the site.

Jim's complaint on the 12 Aug call: "it looks like we had eight projects up here
when we only have three... actually there's only two." The existing merge groups
Deltek sub-jobs by WBS base (2,624 -> 1,435). It cannot catch what he saw,
because those pairs are one curated WordPress project post and one Deltek record
for the same building -- two different origins, no shared job number.

The two origins are told apart by `link`: a curated post has a project page, a
Deltek-only record has nothing to link to. The curated record is the one worth
keeping (photo, developer, architect); the Deltek record only contributes its
job count, era and people.

Merge when EITHER
  * normalised names are equal  -- at any distance, because a same-named pair
    that sits 640 m apart is a geocoding error, not two buildings
    ("Courthouse North Block"), and the curated coordinate is the trustworthy one
  * a distinctive name token is shared AND the pins are within 250 m

...and never when the names carry conflicting position or phase tokens
(north/south, tower one/two). That guard is what keeps The Grande North and The
Grande South as two dots while still folding the bare "The Grande" into one.

Distinctive means: not a word that half the portfolio shares. "grande" is
distinctive, "tower" is not -- matching on "tower" alone would weld together
every high-rise in the city.
"""
import io, json, html, math, re, sys, collections

S = r"C:/Users/ilalonde/AppData/Local/Temp/claude/C--VIsual-Studio-Projects-Operations/912461f4-d333-42a6-8a2a-c879ddd0d90b/scratchpad"

# Words that appear all over a structural portfolio and identify nothing.
GENERIC = set("""
the a an at of and on in for to by
project projects building buildings tower towers block blocks phase phases
north south east west upper lower new old
lofts loft apartments apartment residences residence condos condo suites suite
centre center place plaza park parkade garage hotel office offices retail
street st avenue ave road rd boulevard blvd drive dr way lane court
san diego california ca vancouver bc british columbia usa
kor structural residential commercial mixed use
one two three i ii iii
""".split())

# Tokens that mean "a DIFFERENT part of the same development".
POSITION = set("north south east west".split())
ORDINAL = {"one": 1, "two": 2, "three": 3, "four": 4, "1": 1, "2": 2, "3": 3, "4": 4,
           "i": 1, "ii": 2, "iii": 3, "iv": 4}

ORD_WORD = {"first": "1", "second": "2", "third": "3", "fourth": "4", "fifth": "5",
            "sixth": "6", "seventh": "7", "eighth": "8", "ninth": "9", "tenth": "10"}

TRAIL = re.compile(
    r"[\s,]*(san\s+diego|vancouver|burnaby|surrey|richmond|victoria|kelowna|calgary|edmonton|seattle|portland|los\s+angeles)"
    r"([\s,]*(ca|calif|california|bc|ab|wa|or|usa))*[\s,]*$", re.I)


def norm_name(raw):
    """Lowercase token list, with the trailing city/state noise removed.

    Deltek names carry the city appended ("6th & Palm San Diego CA") while the
    curated post does not ("6th & Palm"). Stripping that is what makes the two
    compare equal.
    """
    s = html.unescape(raw or "").lower()
    s = s.replace("&", " and ").replace("@", " at ")
    for _ in range(3):                       # "... San Diego, CA" can nest
        s2 = TRAIL.sub("", s)
        if s2 == s:
            break
        s = s2
    s = re.sub(r"[^a-z0-9 ]+", " ", s)
    out = []
    for t in s.split():
        t = ORD_WORD.get(t, t)
        t = re.sub(r"^(\d+)(st|nd|rd|th)$", r"\1", t)   # 6th -> 6
        if t:
            out.append(t)
    return out


def norm_addr(raw):
    """House number + street stem, or None when the address has no street number.

    "1401 Union St, San Diego, CA 92101" and "1401 Union St, San Diego, CA"
    both reduce to ("1401", "union"). Cross-street addresses ("4th Avenue &
    Spruce Street") have no house number and return None rather than a guess.
    """
    s = html.unescape(raw or "").lower()
    s = s.split(",")[0].strip()
    m = re.match(r"^(\d+)\s+(.+)$", s)
    if not m:
        return None
    num, rest = m.group(1), m.group(2)
    rest = re.sub(r"[^a-z0-9 ]+", " ", rest)
    toks = []
    for t in rest.split():
        t = ORD_WORD.get(t, t)
        t = re.sub(r"^(\d+)(st|nd|rd|th)$", r"\1", t)
        if t in ("street", "st", "avenue", "ave", "road", "rd", "boulevard", "blvd",
                 "drive", "dr", "way", "lane", "ln", "court", "ct", "place", "pl"):
            continue
        toks.append(t)
    return (num, " ".join(toks)) if toks else None


def metres(a, b):
    (lng1, lat1), (lng2, lat2) = a, b
    return math.hypot((lat1 - lat2) * 111320.0,
                      (lng1 - lng2) * 111320.0 * math.cos(math.radians((lat1 + lat2) / 2)))


def conflicting(ta, tb):
    """True when the two names name different pieces of one development."""
    pa, pb = set(ta) & POSITION, set(tb) & POSITION
    if pa and pb and pa != pb:
        return True
    oa = [ORDINAL[t] for t in ta if t in ORDINAL]
    ob = [ORDINAL[t] for t in tb if t in ORDINAL]
    if oa and ob and set(oa) != set(ob):
        return True
    return False


def load(path):
    return json.load(io.open(path, encoding="utf-8"))["features"]


def build(features):
    """Attach the derived keys once, so the O(n^2) pass stays cheap."""
    rows = []
    for i, f in enumerate(features):
        p = f["properties"]
        toks = norm_name(p.get("name"))
        rows.append({
            "i": i, "f": f, "p": p,
            "xy": tuple(f["geometry"]["coordinates"]),
            "toks": toks,
            "key": " ".join(toks),
            "addr": norm_addr(p.get("address")),
            "curated": bool((p.get("link") or "").strip()),
        })
    return rows


def subsumes(ta, tb):
    """True when name A is name B plus extra words -- "Quince" vs "Quince at
    Bankers Hill", "Stella" vs "Stella Apartments", "Electra" vs "Electra SDG&E
    Vault Lid Repair". The shorter side must carry at least one word that is not
    boilerplate, so "The Tower" does not swallow every tower in the block."""
    sa, sb = set(ta), set(tb)
    small, big = (sa, sb) if len(sa) < len(sb) else (sb, sa)
    if not small or small == big or not small < big:
        return False
    return bool(small - GENERIC)


def pair_up(rows):
    """Cluster by three high-precision rules.

    Deliberately NOT union-find. Transitivity is what broke the first attempt:
    "The Grande North" and "The Grande South" were each merged with the bare
    "The Grande" and so ended up in one cluster, even though the rule refuses
    that pair directly. Here a candidate must clear the conflict test against
    EVERY member already in the cluster, so a refused pair can never be joined
    through a third record.

    Arm 1 is capped at 2 km: two buildings really can share a name in different
    cities, and the point is to merge one building's records, not everything
    called "Aria".
    """
    grid = collections.defaultdict(list)
    for r in rows:
        gx, gy = int(r["xy"][0] * 300), int(r["xy"][1] * 300)   # ~370 m cells
        grid[(gx, gy)].append(r)

    def near(r, radius_cells=1):
        gx, gy = int(r["xy"][0] * 300), int(r["xy"][1] * 300)
        out = []
        for dx in range(-radius_cells, radius_cells + 1):
            for dy in range(-radius_cells, radius_cells + 1):
                out.extend(grid.get((gx + dx, gy + dy), ()))
        return out

    by_name = collections.defaultdict(list)
    for r in rows:
        if r["key"]:
            by_name[r["key"]].append(r)

    # Candidate pairs, each with the reason it qualified.
    cand = {}

    def offer(a, b, why):
        if a["i"] == b["i"]:
            return
        k = (min(a["i"], b["i"]), max(a["i"], b["i"]))
        cand.setdefault(k, why)

    for group in by_name.values():                          # arm 1: same name
        for x in range(len(group)):
            for y in range(x + 1, len(group)):
                d = metres(group[x]["xy"], group[y]["xy"])
                if d <= 2000:
                    offer(group[x], group[y], "same name (%dm)" % d)

    for a in rows:
        for b in near(a, 2):                                # arms 2 and 3
            if b["i"] <= a["i"]:
                continue
            d = metres(a["xy"], b["xy"])
            if a["addr"] and b["addr"] and a["addr"] == b["addr"] and d <= 400:
                offer(a, b, "same address (%dm)" % d)
            elif d <= 150 and subsumes(a["toks"], b["toks"]):
                offer(a, b, "name contains (%dm)" % d)

    # Grow clusters, refusing any candidate that conflicts with a sitting member.
    owner = {}
    clusters = {}
    reasons = {}
    refused = []
    for (i, j), why in sorted(cand.items()):
        a, b = rows[i], rows[j]
        ci, cj = owner.get(i), owner.get(j)
        if ci is not None and ci == cj:
            continue
        members = list(clusters.get(ci, [a])) + list(clusters.get(cj, [b]))
        bad = [(p, q) for x, p in enumerate(members) for q in members[x + 1:]
               if conflicting(p["toks"], q["toks"])]
        if bad:
            refused.append((a, b, why, bad[0]))
            continue
        cid = ci if ci is not None else (cj if cj is not None else i)
        clusters[cid] = members
        for m in members:
            owner[m["i"]] = cid
        reasons[(i, j)] = why
        if ci is not None and cj is not None and ci != cj:
            clusters.pop(cj if cj != cid else ci, None)

    for r in rows:                                          # singletons
        if r["i"] not in owner:
            clusters[r["i"]] = [r]
            owner[r["i"]] = r["i"]
    return clusters, owner, reasons, refused


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else S + "/km_now.json"
    feats = load(src)
    rows = build(feats)

    clusters, owner, reasons, refused = pair_up(rows)
    multi = {k: v for k, v in clusters.items() if len(v) > 1}
    merged_away = sum(len(v) - 1 for v in multi.values())
    print("clusters merging: %d, pins removed: %d, %d -> %d"
          % (len(multi), merged_away, len(rows), len(rows) - merged_away))

    def insd(v):
        return any(32.5 <= r["xy"][1] <= 33.1 and -117.4 <= r["xy"][0] <= -116.8 for r in v)

    sd = {k: v for k, v in multi.items() if insd(v)}
    print("\n--- %d merges in the San Diego area ---" % len(sd))
    for k, v in sorted(sd.items(), key=lambda kv: -len(kv[1])):
        v = sorted(v, key=lambda r: (not r["curated"], r["i"]))
        keep = v[0]
        print("  KEEP  %-46s %s" % (html.unescape(keep["p"]["name"])[:46],
                                    "[curated]" if keep["curated"] else "[deltek]"))
        for r in v[1:]:
            kk = (min(keep["i"], r["i"]), max(keep["i"], r["i"]))
            print("   +--  %-46s %-9s  %s" % (html.unescape(r["p"]["name"])[:46],
                                              "[curated]" if r["curated"] else "[deltek]",
                                              reasons.get(kk, "via cluster")))

    big = [v for v in multi.values() if len(v) >= 3]
    print("\n--- every cluster of 3+ anywhere (%d) ---" % len(big))
    for v in sorted(big, key=lambda x: -len(x)):
        print("  %d: %s" % (len(v), " | ".join(html.unescape(r["p"]["name"])[:34] for r in v)))

    print("\n--- refused as conflicting (%d) ---" % len(refused))
    for a, b, why, (p, q) in refused[:14]:
        print("  %-34s x %-34s (%s)" % (html.unescape(a["p"]["name"])[:34],
                                        html.unescape(b["p"]["name"])[:34], why))


if __name__ == "__main__":
    main()
