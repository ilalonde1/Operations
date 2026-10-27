r"""⚠ ITS REGISTRATION IS NOT TRUSTWORTHY (2026-10-27): both page->DXF votes tried fail - by filled shapes a steel page has too
few; by stroke endpoints the vote picks wrong quarter turns (31130 pages registered q1/q3 where q0 is right). The build does not
record where it placed each sheet; until sheets.csv carries the page->model transform, read only the CANDIDATE counts this
tool's candidates() finds (scratchpad hollow_count.py: 18 squares on 2 of 57 gate sets, 39,124 circles - bubbles and marks),
not its "on hers" / "on nothing" verdicts. And 40112's SC1 square, drawn as separate strokes, is not a candidate at all.

hollow_column_outlines.py <gate-work-dir> [job ...] : how often a column is drawn ONLY AS ITS OUTLINE on a plan page - the
class behind 40112-01 S2.03's SC1 HSS columns (a hollow square, no fill, no hatch), which the reader does not read because it
takes columns from filled or hatched shapes. Measured BEFORE any fix (operations-ee, 2026-10-27), with the false-positive side.

Per placed plan page of each set (the gate's sheets.csv: page, scale, storeys, its DXF):
  1. CANDIDATES - PyMuPDF paths with NO fill that close a rectangle (an 're', or four 'l' strokes) or a circle (four 'c'),
     150-1,200 mm a side at the sheet's scale, sides within 3:1; one that a filled path overlaps is dropped (that is a filled
     column or a hatch, read or not by other rules).
  2. SHEET -> OUR MODEL - page points to the sheet's mm frame as the reader maps them (x * s, (H - y) * s, s = scale x 25.4/72);
     then the translation the sheet DXF's own KOR_V_COL centres and our model's columns on the sheet's storey vote for (200 mm
     bins, refined to the median within 300 mm). A sheet whose vote carries fewer than 3 columns is NOT JUDGED (counted).
  3. OUR MODEL -> HERS - the yardstick.txt registration ("column registration (dx, dy) mm"), turned first where it says
     "rotated N deg"; storeys matched by judge_drift_moves' names.
Each candidate is then: ON OURS (a column we read within 150 mm - drawn twice, once filled), ON HERS (one of the engineer's within
300 mm and none of ours - a column we MISSED), or ON NOTHING (the false-positive side: tags, sleeves, symbols, boxes). Candidates
on a storey she does not model, or on a set with no registration, are counted NOT JUDGED.

COVERS: unfilled closed rectangles and circles on placed plan pages, by position. DOES NOT: a hollow column drawn as two
nested outlines (counted twice, and both land on the same column); a column whose outline is broken by other strokes; rotated
squares (an HSS turned 45 deg is a diamond: four 'l' strokes not axis-aligned are skipped); the page's rotation (pages with
/Rotate are skipped and counted).
SAME-CLASS FAULT IT MISSES: an outline column on a storey she does not model (40112's roof itself: her model stops at P2) -
counted NOT JUDGED, not as a miss.
"""
import collections, csv, glob, math, os, re, sys
import fitz, ezdxf
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import members, her_storey, norm, YARD, MIRROR   # noqa: E402

work = sys.argv[1]
only = set(sys.argv[2:])
PT = 25.4 / 72


def page_points(page, s, cap=1500):
    """stroke endpoints of the page, mapped as candidates are - registered against the sheet DXF's own vertices"""
    out = []
    for d in page.get_drawings():
        for it in d["items"]:
            if it[0] == "l":
                for p in (it[1], it[2]): out.append((round(p.x * s), round((page.rect.height - p.y) * s)))
    out = sorted(set(out))
    return out[:: max(1, len(out) // cap)]


def dxf_points(path, cap=1500):
    out = []
    try: doc = ezdxf.readfile(path)
    except Exception: return out
    for e in doc.modelspace():
        if e.dxftype() == "LWPOLYLINE": out += [(round(v[0]), round(v[1])) for v in e.get_points()]
        elif e.dxftype() == "LINE": out += [(round(e.dxf.start.x), round(e.dxf.start.y)), (round(e.dxf.end.x), round(e.dxf.end.y))]
    out = sorted(set(out))
    return out[:: max(1, len(out) // cap)]


def quarter(p, q):
    x, y = p
    return [(x, y), (-y, x), (-x, -y), (y, -x)][q]


def candidates(page, s):
    out = []
    fills = [d["rect"] for d in page.get_drawings() if d.get("fill") is not None]
    for d in page.get_drawings():
        if d.get("fill") is not None: continue
        items = d["items"]; kinds = [i[0] for i in items]
        shape = None
        if kinds == ["re"]: shape = "square"
        elif len(kinds) == 4 and all(k == "l" for k in kinds):
            pts = [i[1] for i in items] + [items[-1][2]]
            if all(abs(a.x - b.x) < 0.3 or abs(a.y - b.y) < 0.3 for a, b in ((i[1], i[2]) for i in items)) \
               and abs(pts[0].x - pts[-1].x) < 0.5 and abs(pts[0].y - pts[-1].y) < 0.5: shape = "square"
        elif len(kinds) == 4 and all(k == "c" for k in kinds): shape = "round"
        if not shape: continue
        r = d["rect"]; w, h = r.width * s, r.height * s
        if not (150 <= w <= 1200 and 150 <= h <= 1200) or max(w, h) > 3 * min(w, h): continue
        if any(f.intersects(r) and f.width * s < 2000 for f in fills): continue
        out.append((shape, (r.x0 + r.x1) / 2 * s, (page.rect.height - (r.y0 + r.y1) / 2) * s, w, h))
    return out


def vote(a, b):
    """translation b = a + t most pairs agree on (200 mm bins, refined); and how many of a then land within 300 mm"""
    if len(a) == 0 or len(b) == 0: return None, 0
    a, b = np.array(a), np.array(b)
    d = (b[None, :, :] - a[:, None, :]).reshape(-1, 2)
    keys, counts = np.unique(np.round(d / 200).astype(np.int64), axis=0, return_counts=True)
    best, sup = None, 0
    for k in keys[np.argsort(-counts)[:3]]:
        t = k * 200.0
        dist = np.min(np.hypot(*(((a + t)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)
        m = dist <= 300
        if m.sum():
            near = b[np.argmin(np.hypot(*(((a + t)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)]
            t = t + np.median((near - (a + t))[m], axis=0)
            dist = np.min(np.hypot(*(((a + t)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)
        s_ = int((dist <= 150).sum())
        if s_ > sup: best, sup = t, s_
    return best, sup


def dxf_columns(path):
    out = []
    try: doc = ezdxf.readfile(path)
    except Exception: return out
    for e in doc.modelspace().query('*[layer=="KOR_V_COL"]'):
        if e.dxftype() == "LWPOLYLINE": p = [(v[0], v[1]) for v in e.get_points()]
        elif e.dxftype() == "POLYLINE": p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
        elif e.dxftype() == "CIRCLE": out.append((e.dxf.center.x, e.dxf.center.y)); continue
        else: continue
        out.append(((min(x for x, _ in p) + max(x for x, _ in p)) / 2, (min(y for _, y in p) + max(y for _, y in p)) / 2))
    return out


tot = collections.Counter()
for job in sorted(os.listdir(work)):
    if only and job not in only: continue
    jd = os.path.join(work, job)
    sc, e2k, ys = os.path.join(jd, "sheets.csv"), os.path.join(jd, "out.e2k"), os.path.join(jd, "yardstick.txt")
    yard = os.path.join(YARD, job + ".e2k")
    if not all(os.path.isfile(p) for p in (sc, e2k)): continue
    setrow = next(csv.DictReader(open(os.path.join(jd, "set.csv"), encoding="utf-8-sig")))
    d_, f_ = os.path.split(setrow["pdf"])
    import hashlib
    pdf = os.path.join(MIRROR, hashlib.sha1(d_.lower().encode("utf-8")).hexdigest().upper()[:16], f_)
    if not os.path.isfile(pdf): tot["sets with no local pdf"] += 1; continue
    ours = members(e2k)
    hers = members(yard) if os.path.isfile(yard) else {}
    reg = None; turn = 0.0
    if os.path.isfile(ys):
        txt = open(ys, encoding="utf-8", errors="replace").read()
        if (m := re.search(r"column registration \(([-\d,]+), ([-\d,]+)\) mm", txt)): reg = (float(m.group(1).replace(",", "")), float(m.group(2).replace(",", "")))
        if (m := re.search(r"rotated (-?[\d.]+) deg", txt)): turn = float(m.group(1))
    doc = fitz.open(pdf)
    c = collections.Counter()
    for row in csv.DictReader(open(sc, encoding="utf-8-sig")):
        if row["sheet_type"] != "plan" or row["placed"] != "True" or not row["storeys"] or not row["dxf_files"]: continue
        page = doc[int(row["page"]) - 1]
        s = float(row["scale_denominator"] or 0) * PT
        if s <= 0: continue
        cand = candidates(page, s)
        if not cand: continue
        storeys = row["storeys"].split(",")
        oc = [p for st in storeys for p in ours.get(norm(st), {"column": []})["column"]]
        dc = dxf_columns(os.path.join(jd, "dxf", row["dxf_files"].split(";")[0]))
        # PAGE -> SHEET DXF, registered not assumed: the quarter turn and translation the page's stroke endpoints and the
        # DXF's vertices agree on (a page with /Rotate is turned in one of the two frames); support = DXF vertices within 150 mm
        pp, dp = page_points(page, s), dxf_points(os.path.join(jd, "dxf", row["dxf_files"].split(";")[0]))
        best = (None, 0, 0)
        for q in range(4):
            t1, s1 = vote([quarter(p, q) for p in pp], dp)
            if s1 > best[1]: best = (t1, s1, q)
        t1, s1, q = best
        if t1 is None or s1 < 0.3 * min(len(pp), len(dp)): c["not judged: page not registered to its DXF"] += len(cand); continue
        t, sup = vote(dc, oc)
        if t is None or sup < 3: c["not judged: sheet not registered"] += len(cand); continue
        c[f"pages registered (q{q})"] += 1
        for shape, x0, y0, w, h in cand:
            x, y = quarter((x0, y0), q); x, y = x + t1[0], y + t1[1]
            mx, my = x + t[0], y + t[1]
            if oc and min(math.hypot(mx - a, my - b) for a, b in oc) <= 150: c["on ours (read)"] += 1; continue
            hs = [her_storey(hers, st) for st in storeys]
            hc = [p for k in hs if k for p in hers[k]["column"]]
            if reg is None or not hc: c["not judged: no registration or storey not hers"] += 1; continue
            # ours -> hers: hers turned back by -turn met ours by reg, so hers = turn(ours + reg)
            r = math.radians(turn); px, py = mx + reg[0], my + reg[1]
            hx, hy = px * math.cos(r) - py * math.sin(r), px * math.sin(r) + py * math.cos(r)
            if min(math.hypot(hx - a, hy - b) for a, b in hc) <= 300: c[f"ON HERS, missed ({shape})"] += 1
            else: c[f"on nothing ({shape})"] += 1
    if c:
        print(f"{job}: " + "; ".join(f"{k} {v}" for k, v in sorted(c.items())))
        tot.update(c)
print("\nTOTAL: " + "; ".join(f"{k} {v}" for k, v in sorted(tot.items())))
