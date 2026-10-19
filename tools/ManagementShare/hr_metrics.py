"""People metrics from FILE NAMES ONLY (no personal record is opened). Prints counts; names are never printed."""
import csv, os, re, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
rows = [r for r in csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')) if r['Dir'] != 'True']
def P(r): return r['Path'].strip('\\').split('\\')
rec = [(P(r), r) for r in rows if P(r)[0] == 'Recruitment']
print('Recruitment files', len(rec))
byrole = collections.Counter(p[2] if len(p) > 3 else '(root)' for p, r in rec)
print('by role folder', dict(byrole))
# candidates = distinct person-name stems (first two words of file name), by role and year of file modification
cand = collections.defaultdict(set)
for p, r in rec:
    nm = re.sub(r'\.[^.]+$', '', p[-1]); who = ' '.join(re.findall(r'[A-Za-z]+', nm)[:2]).lower()
    cand[(p[2] if len(p) > 3 else '(root)', r['Modified'][:4])].add(who)
tab = collections.defaultdict(dict)
for (role, yr), s in cand.items(): tab[role][yr] = len(s)
for role, d in sorted(tab.items(), key=lambda kv: -sum(kv[1].values())):
    print('  candidates %-20s total %4d  %s' % (role, sum(d.values()), dict(sorted(d.items()))))
offers = [p for p, r in rec if re.search(r'offer', p[-1], re.I)]
print('letters of offer in Recruitment:', len(offers))
sens = [p for p, r in rec if re.search(r'\bSIN\b|void cheque|direct deposit|passport|driver|licen[cs]e|medical|health|td1', p[-1], re.I)]
print('recruitment files whose NAME suggests ID/banking/medical:', len(sens))
ts = [(p, r) for p, r in rows and [(P(r), r) for r in rows] if p[:2] == ['Administration', 'Timesheet Archive']]
print('\nTimesheet Archive files', len(ts))
pay = [p for p, r in ts if 'PAYROLL' in '\\'.join(p).upper()]
print('  in PAYROLL folders:', len(pay))
staff_year = collections.defaultdict(set)
for p, r in ts:
    m = re.match(r'(\d{4})-\d\d-\d\d\s+([A-Z][A-Za-z\'\-]+(?:\s+[A-Z][A-Za-z\'\-]+)+)', p[-1])
    if m: staff_year[m.group(1)].add(m.group(2).upper())
print('  distinct staff names on timesheets by year (count only):', {y: len(s) for y, s in sorted(staff_year.items())})
years = collections.Counter(r['Modified'][:4] for p, r in ts)
print('  timesheet files by year modified:', dict(sorted(years.items())))
other = [('\\'.join(p), r['Modified'][:10]) for p, r in [(P(r), r) for r in rows]
         if re.search(r'employee|staff list|extensions|org ?chart|egbc file|apegbc.*complaint|apegbc vs|resignat|liability insurance.*distribution', '\\'.join(p), re.I)
         and p[0] not in ('Recruitment',) and not (p[:2] == ['Administration', 'Timesheet Archive'])]
print('\nother people-related files (paths):', len(other))
for o in other: print('  ', o[1], o[0][:150])
