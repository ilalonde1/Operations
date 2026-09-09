"""Compare two pdf-inventory text ledgers page by page: the five buckets and their total.
usage: ledger_diff.py <old.txt> <new.txt>"""
import sys, re
def rows(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"\s*(\d+)\s+\S+\s+(.*?)\s+(\d+)\s+(\d+)\s+\|\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$", line)
        if m:
            page = int(m.group(1))
            vals = tuple(int(m.group(k)) for k in range(5, 10))
            out[page] = vals
    return out
old, new = rows(sys.argv[1]), rows(sys.argv[2])
same_total = moved = 0
for p in sorted(new):
    if p not in old: continue
    o, n = old[p], new[p]
    if sum(o) == sum(n): same_total += 1
    if o != n:
        moved += 1
        print(f"  p{p:3d}  read {o[0]}->{n[0]}  discard {o[1]}->{n[1]}  unread {o[2]}->{n[2]}  ignore {o[3]}->{n[3]}  unacct {o[4]}->{n[4]}  total {sum(o)}->{sum(n)}")
print(f"{len(new)} pages; totals equal on {same_total} of {len(new)}; buckets moved on {moved}")
