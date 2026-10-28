import fitz, sys, collections
doc = fitz.open(sys.argv[1])
for pi in range(1, 8):
    p = doc[pi]; W = p.rect.width
    hs = [(d["rect"].x0, d["rect"].x1, d["rect"].y0) for d in p.get_drawings() if d["type"] == "s" and len(d["items"]) == 1 and d["items"][0][0] == "l" and d["rect"].height < 1 and d["rect"].width > 20]
    lines = collections.defaultdict(list)
    for x0, y0, x1, y1, w, b, l, n in p.get_text("words"): lines[(b, l)].append((x0, y0, x1, y1, w))
    found = []
    for ws in lines.values():
        x0 = min(w[0] for w in ws); x1 = max(w[2] for w in ws); y1 = max(w[3] for w in ws); h = y1 - min(w[1] for w in ws)
        if x0 > 0.8 * W: continue
        if any(s[0] < x1 and s[1] > x0 and min(s[1], x1) - max(s[0], x0) >= 0.5 * (x1 - x0) and -0.2 * h <= s[2] - y1 <= 1.2 * h for s in hs):
            t = " ".join(w[4] for w in ws).upper()
            if ("LEVEL" in t or "ROOF" in t) and "AREA" not in t: found.append(f"'{' '.join(w[4] for w in ws)}'@x{x0:.0f}")
    print(f"page {pi+1}: " + "  ".join(found))
