r"""hollow_count.py <gate-work> : per set, unfilled column-size SQUARES and CIRCLES on placed plan pages (candidates() of
tools/CorpusGateBisect/hollow_column_outlines.py), no registration - does the outline-column class exist beyond 40112?"""
import collections, csv, hashlib, os, sys
import fitz
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
src = open(r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect\hollow_column_outlines.py", encoding="utf-8").read()
exec(src[src.index("import collections"):src.index("def vote")])
work = sys.argv[1]
tot = collections.Counter(); per = []
for job in sorted(os.listdir(work)):
    jd = os.path.join(work, job); sc = os.path.join(jd, "sheets.csv")
    if not os.path.isfile(sc): continue
    r = next(csv.DictReader(open(os.path.join(jd, "set.csv"), encoding="utf-8-sig")))
    d_, f_ = os.path.split(r["pdf"])
    pdf = os.path.join(MIRROR, hashlib.sha1(d_.lower().encode("utf-8")).hexdigest().upper()[:16], f_)
    if not os.path.isfile(pdf): continue
    doc = fitz.open(pdf); c = collections.Counter()
    for row in csv.DictReader(open(sc, encoding="utf-8-sig")):
        if row["sheet_type"] != "plan" or row["placed"] != "True": continue
        s = float(row["scale_denominator"] or 0) * PT
        if s <= 0: continue
        c["pages"] += 1
        for shape, *_ in candidates(doc[int(row["page"]) - 1], s): c[shape] += 1
    per.append((c["square"], job, c)); tot.update(c)
for n, job, c in sorted(per, reverse=True):
    if c["square"]: print(f"{job}: {c['square']} hollow squares, {c['round']} circles on {c['pages']} placed plan pages")
print(f"\n{sum(1 for n, *_ in per if n)} of {len(per)} sets have a hollow column-size square on a placed plan page; "
      f"total {tot['square']} squares, {tot['round']} circles, {tot['pages']} pages")
