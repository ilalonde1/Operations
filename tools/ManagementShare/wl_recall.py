# Recall test for the Deltek splitter: among pursuits whose outcome is KNOWN from M: (tracker/folders), how often does a Deltek
# real-job match (score >= 3) appear? High on Won and low on Lost = the splitter discriminates; then "no match" on Submitted means
# probably not won.
import csv, os, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
P = list(csv.DictReader(open(os.path.join(base, 'winloss-records.csv'), encoding='utf-8-sig')))
for label, rows in [('tracker Won 2020+', [p for p in P if 'Won' in p['tracker_status'] and p['outcome'] == 'Won']),
                    ('folder Won 2017+ new commissions', [p for p in P if p['outcome'] == 'Won' and p['year'] and int(p['year']) >= 2017 and p['pursuit_type'] == 'new commission']),
                    ('Lost 2017+', [p for p in P if p['outcome'] == 'Lost' and p['year'] and int(p['year']) >= 2017])]:
    c = collections.Counter((p['deltek_conf'] or 'no match').split(' ')[0] for p in rows)
    hit = sum(v for k, v in c.items() if k in ('high', 'medium'))
    print(f'{label}: {len(rows)} pursuits; high/medium Deltek match {hit} ({hit / max(len(rows), 1):.0%}); {dict(c)}')
