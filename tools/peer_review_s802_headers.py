#!/usr/bin/env python3
r"""JM 01819: every core door opening on WHM's S802 (page index 40, 1/8 in = 1 ft -> 9 pt per ft) and the header depth
above it, scaled (row 1982; JM-LATERAL-CAP's scratch s802_hdr.py, brought into the repo by JM-LAT-PREP 14 Nov 2026).
Depth = top of the door opening (0.6 pt rectangle) to the datum line under the level tag above (word bottom + 2 pt).
Same-class fault it would miss: where WHM draw the slab ABOVE the datum (ROOF - BLDG B REZ on CSW4/CSW5) the concrete over
the door is deeper than this depth (S802 looked at: 1116 mm vs 33.9 in here).
USAGE: python -I peer_review_s802_headers.py --pdf <Structural IFT.pdf> --out <s802-headers.csv>
"""
import argparse
# Scales every core door opening on S802 (1/8"=1'-0" -> 9 pt per ft) and the header above it.
import fitz,collections,csv
_ap=argparse.ArgumentParser(); _ap.add_argument('--pdf',required=True); _ap.add_argument('--out',required=True); _a=_ap.parse_args()
PDF=_a.pdf
p=fitz.open(PDF)[40]
ELEV={'CSW5':(1320,1610),'CSW4':(1810,2150),'CSW1':(2330,2620)}
LEVELS=['P3/FDN','P2/FDN','GF/P1']+['L%d'%i for i in range(1,32)]
H=[];V=[]
for dr in p.get_drawings():
    w=dr.get('width') or 0
    if abs(w-0.6)>0.05: continue
    for it in dr['items']:
        if it[0]!='l': continue
        a,b=it[1],it[2]
        if abs(a.y-b.y)<0.2: H.append((min(a.x,b.x),max(a.x,b.x),a.y))
        elif abs(a.x-b.x)<0.2: V.append((a.x,min(a.y,b.y),max(a.y,b.y)))
ops=collections.defaultdict(set)
for x,y0,y1 in V: ops[(round(y0,1),round(y1,1))].add(round(x,1))
openings=set()
for (y0,y1),xs in ops.items():
    xs=sorted(xs)
    for i in range(len(xs)):
        for j in range(i+1,len(xs)):
            if any(abs(h[0]-xs[i])<0.3 and abs(h[1]-xs[j])<0.3 and abs(h[2]-y0)<0.3 for h in H):
                openings.add((xs[i],xs[j],y0,y1))
words=p.get_text('words')
rows=[]
for name,(xa,xb) in ELEV.items():
    datum={}
    for w in words:
        if xb<w[0]<xb+130 and w[4] in LEVELS: datum[w[4]]=w[3]+2.0
    # roof labels
    for w in words:
        if xb<w[0]<xb+130 and w[4]=='B_Rez': datum['ROOF AMENITY']=w[3]+2.0
        if xb<w[0]<xb+130 and w[4]=='REZ': datum['ROOF']=w[3]+2.0
    lv=sorted(datum.items(),key=lambda kv:-kv[1])  # bottom to top
    for o in sorted(o for o in openings if xa<o[0]<xb):
        x0,x1,top,bot=o
        below=[k for k,y in lv if y>=bot-1.0]
        above=[(k,y) for k,y in lv if y<top]
        if not above: continue
        k_above,y_above=max(above,key=lambda t:t[1])
        k_below=min(below,key=lambda k:datum[k]) if below else ''
        rows.append(dict(wall=name,x0_pt=x0,clear_span_ft=round((x1-x0)/9,2),opening_h_ft=round((bot-top)/9,2),
            storey_from=k_below,header_at_level=k_above,header_depth_total_in=round((top-y_above)/9*12,1)))
rows.sort(key=lambda r:(r['wall'],r['x0_pt'],LEVELS.index(r['header_at_level']) if r['header_at_level'] in LEVELS else 99))
with open(_a.out,'w',newline='',encoding='utf-8') as f:
    w=csv.DictWriter(f,fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
for r in rows: print(r)
