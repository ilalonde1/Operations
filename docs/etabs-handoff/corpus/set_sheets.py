"""One set's sheet rows from the analyzer's ledger: page, number, type, level, scale, views written, placed.

    python docs/etabs-handoff/corpus/set_sheets.py 30940-01 [31009-01 ...]

The question it answers without opening a PDF: did the reader name the sheet, give it a level, and
write a view for it - and what is that view called. PROTOTYPE of `takeoff corpus-query` (2026-09-11);
replaced by it when the ledger is in the DB (migration 083).
"""
import csv
import os
import sys


def main():
    path = os.path.join(os.environ["LOCALAPPDATA"], "Temp", "kor-drawings", "corpus", "ledger-sheets.csv")
    rows = list(csv.DictReader(open(path, encoding="utf-8-sig", newline="")))
    for job in sys.argv[1:]:
        rs = [r for r in rows if r["job"] == job]
        plans = [r for r in rs if r["sheet_type"] == "plan"]
        print(f"{job}: {len(rs)} pages, {len(plans)} plans; plans with a sheet number {sum(1 for r in plans if r['sheet_number'])}, "
              f"with a level {sum(1 for r in plans if r['level'])}, with a bookmark title {sum(1 for r in plans if r['title'])}, with a view {sum(1 for r in plans if r['dxf_files'])}")
        for r in plans[:8]:
            print(f"   p{int(r['page']):02}  {r['sheet_number'] or '-':10} level={r['level'] or '-':8} scale={r['scale_note'] or '-':14} placed={r['placed'] or '-':5} {(r['dxf_files'] or '')[:70]}")


if __name__ == "__main__":
    main()
