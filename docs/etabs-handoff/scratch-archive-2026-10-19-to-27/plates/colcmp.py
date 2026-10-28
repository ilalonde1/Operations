import sys, re, collections, openpyxl
# colcmp.py <revit_per_level.txt> <ours.xlsx> : column volume by storey, ours against Revit (both towers summed)
rev = collections.defaultdict(float)
for line in open(sys.argv[1]):
    p = line.split()
    if len(p) < 5 or p[0] == 'level': continue
    name = ' '.join(p[:-4]); col = float(p[-2])
    m = re.match(r'LEVEL (P?\d+)', name)
    key = (('P' + m.group(1)[1:]) if m.group(1).startswith('P') else 'L' + m.group(1)) if m else name
    rev[key] += col
ws = openpyxl.load_workbook(sys.argv[2], data_only=True).worksheets[0]
ours = {}
for row in ws.iter_rows(min_row=6, values_only=True):
    if row[0] and isinstance(row[3], (int, float)): ours[str(row[0])] = row[3]
for lv in sorted(set(rev) | set(ours), key=lambda s: (s[0] != 'P', -int(s[1:]) if s[0] == 'P' and s[1:].isdigit() else (int(s[1:]) if s[1:].isdigit() else 999))):
    r, o = rev.get(lv, 0), ours.get(lv, 0)
    print(f"{lv:22} Revit {r:6.1f}  ours {o:6.1f}  diff {o - r:+6.1f}")
print(f"total Revit {sum(rev.values()):.0f} ours {sum(ours.values()):.0f}")
