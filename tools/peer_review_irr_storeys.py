# JM 01819 irregularity screen (row 1982, JM-LAT-PREP 14 Nov 2026; scratch irr_storeys.py brought into the repo). Reads WHM's drawn walls only
# (model-audit/drawn-walls.csv), never a model. Output to stdout; independent/irregularity/storeys.txt is its committed output. See IRREGULARITY.md.
# SCREEN, from WHM's DRAWN walls (jobs/01819-01-renfrew/model-audit/drawn-walls.csv, read off S201-S227), not a model.
# SFRS lines = WHM's CSW walls (S801 key plans; located from the CSW tags on S201 p4 and the S801 views).
# Per storey: each CSW line's drawn length (clipped to its S801 extent) and thickness; then, per building and
# direction, sum t*L (shear-area proxy) and sum t*L^3 (flexure proxy) for (a) CSW walls and (b) all other
# non-compact walls (WHM CW#/unmarked: NOT SFRS per S111 legend), plus the plan extent of each set.
import csv, sys, math, collections
rows = [r for r in csv.DictReader(l for l in open(sys.argv[1]) if not l.startswith('#')) if 'COMPACT' not in r['how']]
order = ['P2','P1/GF','L1','L2'] + [f'L{i}' for i in range(3,32)] + ['ROOF AMENITY','ROOF','MECHANICAL SERVICE','ELEVATOR PENTHOUSE']
# (name, dir, coord, lo, hi, building)
CSW = [('CSW1','H',59070,74891,84751,'B'),('CSW2','H',53576,74891,84751,'B'),('CSW3','H',50577,74891,84751,'B'),
       ('CSW4','V',75298,50171,59375,'B'),('CSW5','V',84344,50171,59375,'B'),
       ('CSW6','V',24772,54196,59653,'A'),('CSW9','V',27617,54196,59806,'A'),('CSW8','H',59653,24620,27769,'A'),
       ('CSW7','H',54196,24620,27769,'A'),('CSW10','V',52936,54222,59432,'A'),('CSW11','V',40081,52706,57189,'A'),
       ('CSW12','H',57036,40081,45925,'A'),('CSW13','V',45925,52706,57189,'A')]
TOL = 450
def parse(r):
    x1,y1,x2,y2 = (float(r[k]) for k in ('x1','y1','x2','y2'))
    d = 'H' if abs(x2-x1) >= abs(y2-y1) else 'V'
    return d, x1,y1,x2,y2, float(r['thk_mm'])
out = collections.defaultdict(dict)
for st in order:
    segs = [(r,)+parse(r) for r in rows if r['storey']==st]
    used = set()
    for name,d,c,lo,hi,b in CSW:
        L=0; ts=set()
        for r,dd,x1,y1,x2,y2,t in segs:
            if dd!=d: continue
            cc = (y1+y2)/2 if d=='H' else (x1+x2)/2
            a,bb = (sorted((x1,x2)) if d=='H' else sorted((y1,y2)))
            if abs(cc-c) <= TOL and min(bb,hi+200) - max(a,lo-200) > 300:
                L += min(bb,hi) - max(a,lo); ts.add(int(t)); used.add(r['label'])
        out[st][name] = (round(L), sorted(ts))
    for b in 'AB':
        for d in 'HV':
            for kind in ('sfrs','other'):
                tl=tl3=0; xs=[]; ys=[]
                for r,dd,x1,y1,x2,y2,t in segs:
                    mx = (x1+x2)/2
                    if dd!=d or (mx<61000) != (b=='A'): continue
                    if (r['label'] in used) != (kind=='sfrs'): continue
                    L = math.hypot(x2-x1,y2-y1)/1000; tl += t/1000*L; tl3 += t/1000*L**3; xs += [x1,x2]; ys += [y1,y2]
                ext = (round((max(xs)-min(xs))/1000,1), round((max(ys)-min(ys))/1000,1)) if xs else (0,0)
                out[st][(b,d,kind)] = (round(tl,2), round(tl3,1), ext)
print('storey | ' + ' | '.join(c[0] for c in CSW))
for st in order:
    print(st, '|', ' | '.join(f"{out[st][c[0]][0]}:{'/'.join(map(str,out[st][c[0]][1]))}" for c in CSW))
print()
print('storey | b dir: SFRS tL m2, tL3 m4, extent x,y m || OTHER walls tL, tL3, extent')
for st in order:
    print(st, '|', ' | '.join(f"{b}{d}: {out[st][(b,d,'sfrs')][0]} {out[st][(b,d,'sfrs')][1]} {out[st][(b,d,'sfrs')][2]} || {out[st][(b,d,'other')][0]} {out[st][(b,d,'other')][1]} {out[st][(b,d,'other')][2]}" for b in 'AB' for d in 'HV'))
