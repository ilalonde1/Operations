import csv, os, re, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))
rows = list(csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')))
pat = re.compile(r'handbook|polic|procedure|employ|payroll|salar|wage|compensation|bonus|benefit|vacation|holiday|sick|leave|offer letter|offer|termination|resign|performance|review form|appraisal|org ?chart|organi[sz]ation|job description|position description|onboard|orientation|hr\b|human resource|hiring|recruit|interview|candidate|resume|cv\b|t4|roe\b|sin\b|void cheque|direct deposit|td1|medical|health|wcb|worksafe|safety|disciplin|warning letter|contract of employment|employment agreement|timesheet|overtime|raise|increase|staff|personnel|training|mentor|egbc|apegbc|permit to practice|quality manag|qms|insurance', re.I)
hits = collections.Counter(); ex = collections.defaultdict(list)
for r in rows:
    if r['Dir'] == 'True': continue
    p = r['Path'].strip('\\').split('\\')
    if p[0] in ('Monthly Billings',): continue
    name = p[-1]
    for m in set(x.lower() for x in pat.findall('\\'.join(p))):
        key = (m, '\\'.join(p[:2]))
        hits[key] += 1
        if len(ex[key]) < 3: ex[key].append('\\'.join(p[1:])[:140])
for (m, area), c in sorted(hits.items(), key=lambda kv: (kv[0][0], -kv[1])):
    if area.startswith('Project Opportunities') and m in ('review', 'insurance', 'offer'): continue
    print('%5d  %-22s %-60s  e.g. %s' % (c, m, area[:60], ' || '.join(ex[(m, area)])))
