"""Re-measure the 2026-08-28 claim: every force in AEM's 31 hand-made .SCO files on 30961-01
matches the Calculator row it was typed from. The .SCO Comment is the Calculator Remarks cut
short (S-Concrete truncates near 60 chars), so a row matches when Remarks starts with Comment.
usage: reconcile_sco.py <dir with .SCO files> <SEC ELEMS.xlsm>"""
import sys, os, glob, openpyxl, collections

sco_dir, wb_path = sys.argv[1], sys.argv[2]
ws = openpyxl.load_workbook(wb_path, read_only=True, data_only=True)['Calculator']
by20 = collections.defaultdict(list)
for r in ws.iter_rows(min_row=3, values_only=True):
    if r[11]:
        rem = str(r[11]).strip()
        by20[rem[:20]].append((rem, tuple(float(x or 0) for x in r[4:10])))

files = sorted(glob.glob(os.path.join(sco_dir, '*.SCO')))
demands = matched = values = mism = missing = ambiguous = 0
worst, worst_at = 0.0, ''
for f in files:
    L = open(f, encoding='latin-1').read().splitlines()
    for i, line in enumerate(L):
        if not (line.startswith('@Object@') and 'Sectional Loads' in line):
            continue
        header = L[i + 2].split('\t')
        ci = header.index('Comment')
        j = i + 3
        while j < len(L) and not L[j].startswith('@EndTable@'):
            c = L[j].split('\t')
            if len(c) > ci and not c[0].strip().startswith('**'):
                demands += 1
                sco = [float(c[k]) for k in (1, 2, 3, 4, 6, 7)]   # Nf Tf Vfz Mfy Vfy Mfz
                com = c[ci].strip()
                rows = [v for rem, v in by20.get(com[:20], []) if rem.startswith(com)]
                if not rows:
                    missing += 1
                else:
                    if len(set(rows)) > 1:
                        ambiguous += 1
                    best = min(rows, key=lambda r: sum(abs(a - b) for a, b in zip(r, sco)))
                    matched += 1
                    for a, b in zip(best, sco):
                        values += 1
                        d = abs(a - b)
                        if d > worst:
                            worst, worst_at = d, os.path.basename(f) + ' | ' + com
                        if d > 0.001 * max(1.0, abs(a)):
                            mism += 1
            j += 1

print(f"files {len(files)}  demands {demands}  matched {matched}  no Calculator row {missing}  comment prefix on >1 distinct row {ambiguous}")
print(f"values compared {values}  outside 0.1% {mism}  worst abs diff {worst:.6g} at {worst_at}")
