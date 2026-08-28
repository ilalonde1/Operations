"""Emit the Houston residential lead table as HTML, straight from the record.

Generated rather than transcribed: every one of these is a name, an owner and a
figure that a reader can check, and hand-copying twelve rows is how a wrong
number gets into a document that has been verified everywhere else.
"""
import html
import json
import sys

sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools")
from tabs_projects import sector_of  # noqa: E402

rows = [json.loads(l) for l in open("harris_detail.jsonl", encoding="utf-8")]
res = [r for r in rows
       if sector_of(r) == "multifamily"
       and r.get("IndexCreated", "") >= "2025-06"
       and (r.get("Owner Name") or "").strip()
       and (r.get("Project Name") or "").strip()]
res.sort(key=lambda r: -r.get("IndexCost", 0))

MONTH = {"01": "Jan", "02": "Feb", "03": "Mar", "04": "Apr", "05": "May",
         "06": "Jun", "07": "Jul", "08": "Aug", "09": "Sep", "10": "Oct",
         "11": "Nov", "12": "Dec"}


def money(v):
    return "$%.0fM" % (v / 1e6) if v >= 1e6 else "$%.0fk" % (v / 1e3)


def when(d):
    return "%s %s" % (MONTH.get(d[5:7], d[5:7]), d[:4])


out = []
for r in res[:12]:
    out.append(
        "            <tr><td><strong>%s</strong></td><td>%s</td>"
        "<td>%s</td><td><span class=\"muted\">%s</span></td></tr>"
        % (html.escape((r["Project Name"] or "").strip()[:38]),
           html.escape((r["Owner Name"] or "").strip()[:36]),
           money(r.get("IndexCost", 0)),
           when(r.get("IndexCreated", ""))))

print("total in the slice: %d" % len(res))
print("value of the twelve shown: $%.0fM"
      % (sum(r.get("IndexCost", 0) for r in res[:12]) / 1e6))
print("value of all %d: $%.1fB" % (len(res), sum(r.get("IndexCost", 0) for r in res) / 1e9))
print()
print("\n".join(out))
