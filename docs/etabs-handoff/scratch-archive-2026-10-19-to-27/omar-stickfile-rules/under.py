import fitz, sys
doc = fitz.open(sys.argv[1]); p = doc[6]
for d in p.get_drawings():
    r = d["rect"]
    if r.x1 > 320 and r.x0 < 470 and r.y1 > 1630 and r.y0 < 1690:
        segs = [(it[0], [(round(q.x), round(q.y)) for q in it[1:] if hasattr(q, "x")]) for it in d["items"]]
        print(d["type"], f"rect=({r.x0:.0f},{r.y0:.0f},{r.x1:.0f},{r.y1:.0f})", "w=", d.get("width"), segs[:6], "..." if len(segs) > 6 else "")
