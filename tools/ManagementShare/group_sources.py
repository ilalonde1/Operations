"""Group the knowledge bundle into a few coherent KOR sources (one .md per source) for BdKnowledgeIngest.
Drops: page-marker-only scans (< 150 real chars), exact-duplicate texts, near-duplicates (same first 1,500 normalised chars).
Usage: python group_sources.py [--write]"""
import json, os, re, sys, hashlib, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
OUT = r'C:\Users\ilalonde\Desktop\BD\Management\sources-2026-10-18'
docs = [json.loads(l) for l in open(os.path.join(base, 'bundle_knowledge.jsonl'), encoding='utf-8')]

def group(p):
    q = p.lower()
    if '\\indemnification notes\\' in q: return 'KOR Contract Indemnification and Liability Positions'
    if q.startswith('meeting minutes'): return 'KOR Department Meeting Minutes 2021-2023'
    if q.startswith('business development\\bizbrain'): return 'KOR BizBrain BD Reports and Leduc RFP Pursuit 2026'
    if q.startswith('project management\\project specifications'): return 'KOR Structural Specification Templates'
    if any(k in q for k in ('\\company introduction\\', '\\project experience & portfolios\\', '\\resumes\\', 'business development\\', 'rfps detailed projects', 'fraser valley health')):
        return 'KOR Company Introductions Portfolios and Proposal Resumes'
    if q.startswith('correspondence\\01 fee proposals'): return 'KOR Fee Proposal Templates Terms Rates and Reference Notes'
    return 'KOR Office Field Review and Administration Procedures'

def real(t): return re.sub(r'\[page \d+\]', '', t).strip()
def norm(t): return re.sub(r'\W+', ' ', t.lower()).strip()
seen_full, seen_head = {}, {}
groups = collections.defaultdict(list); drop = collections.Counter(); dropped = []
for d in docs:
    t = real(d['text'])
    if len(t) < 150: drop['scan-or-tiny'] += 1; dropped.append(('tiny', d['path'])); continue
    n = norm(t); h1 = hashlib.sha1(n.encode()).hexdigest(); h2 = hashlib.sha1(n[:1500].encode()).hexdigest()
    if h1 in seen_full: drop['exact-dup'] += 1; dropped.append(('dup of ' + seen_full[h1], d['path'])); continue
    if h2 in seen_head: drop['near-dup'] += 1; dropped.append(('near-dup of ' + seen_head[h2], d['path'])); continue
    seen_full[h1] = d['path']; seen_head[h2] = d['path']
    groups[group(d['path'])].append((d['path'], t))
for g, items in sorted(groups.items()):
    print('%-62s %4d docs %9d chars' % (g, len(items), sum(len(t) for _, t in items)))
print('dropped', dict(drop))
if '--write' in sys.argv:
    os.makedirs(OUT, exist_ok=True)
    for g, items in groups.items():
        body = [f'# {g}', '', 'KOR Structural internal documents from the Management share (M:), extracted 2026-10-18. Each document starts with a [Document: ...] line naming its path.', '']
        for p, t in sorted(items):
            t = re.sub(r'\n\[page (\d+)\]\n', '\n', '\n' + t).strip()
            body += [f'[Document: M:\\{p}]', '', t, '', '']
        fn = os.path.join(OUT, f'{g} -- KOR Structural -- 2026.md')
        open(fn, 'w', encoding='utf-8').write('\n'.join(body))
    with open(os.path.join(OUT, '_dropped.tsv'), 'w', encoding='utf-8') as fh:
        for why, p in dropped: fh.write(f'{why}\t{p}\n')
    print('written to', OUT)
