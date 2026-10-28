"""Members with a joint east of x (mm): name, kind, first joints, storeys. usage: far.py <model.e2k> <x>"""
import re, sys
pts, members, st = {}, {}, {}
for s in open(sys.argv[1], encoding="latin-1"):
    s = s.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'(LINE|AREA)\s+"([^"]+)"\s+(\w+)\s+(.*)', s)
    if m: members[m[2]] = (m[3], re.findall(r'"([^"]+)"', m[4])); continue
    m = re.match(r'(LINEASSIGN|AREAASSIGN)\s+"([^"]+)"\s+"([^"]+)"', s)
    if m: st.setdefault(m[2], []).append(m[3])
limit = float(sys.argv[2])
for name, (kind, js) in members.items():
    if any(j in pts and pts[j][0] > limit for j in js):
        print(name, kind, [tuple(round(v) for v in pts[j]) for j in js if j in pts][:2], st.get(name))
