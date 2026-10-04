r"""The projects Jim has listed on the map, as a spreadsheet he can mark up.

Jim, 2026-09-09: "a spreadsheet showing the projects that I have listed on the
map in Vancouver. I will review and do the same as I did in California. Also pls
send me an update of the CA spreadsheet and I will review and backcheck."

That is a list, not a report. Two tabs -- Vancouver and California -- each one
every pin on the live map carrying his name, in the column order he already
works in, with Keep/Delete blank. His comments from the August sheet are carried
across where the row is the same project, so he is not re-deriving context he
already wrote.

Deliberately NOT here: backcheck columns, match-quality columns, flag blocks, a
questions tab. An earlier cut had all of it and it buried the thing he asked
for. What the data says about geocoding faults, duplicates and projects that
were never added is real, but it is Ian's to act on, so it prints to the console
and stays out of the workbook.

SOURCE is the regenerated kor-map-data.json -- the file every map fetches, with
its Last-Modified checked -- so the sheet is what is actually on the map now.
"""
import io
import json
import os
import re
import sys

from openpyxl import Workbook, load_workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

HERE = os.path.dirname(os.path.abspath(__file__))
SCRATCH = (sys.argv[1] if len(sys.argv) > 1 else
           r"C:/Users/ilalonde/AppData/Local/Temp/claude/C--VIsual-Studio-Projects-Operations"
           r"/912461f4-d333-42a6-8a2a-c879ddd0d90b/scratchpad")
MAP = os.path.join(SCRATCH, "kor-map-data.json")
STAMP = os.path.join(SCRATCH, "map_stamp.txt")
CA_SRC = r"C:/Users/ilalonde/Desktop/Jim/KOR-CA-Portfolio-Projects JD.xlsx"
OUT = r"C:/Users/ilalonde/Desktop/Jim/KOR-Map-Projects-JD-2026-09-09.xlsx"

JIM = "jim-desroches"
DARK = "373435"
GREY = "F2F2F2"

STOP = {"the", "a", "at", "of", "and", "on", "phase", "building", "tower",
        "towers", "apartments", "residences", "project"}


def norm(s):
    s = (s or "").lower().replace("&", " and ")
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9 ]+", " ", s)).strip()


def tokens(s):
    return {t for t in norm(s).split() if t not in STOP and len(t) > 2}


def street_key(addr):
    m = re.match(r"\s*(\d+)\s+([a-z0-9]+)", norm(addr))
    return "%s %s" % (m.group(1), m.group(2)) if m else None


def num(v):
    try:
        return float(str(v).strip())
    except (TypeError, ValueError):
        return None


def haversine_km(a, b, c, d):
    import math
    p1, p2 = math.radians(a), math.radians(c)
    h = (math.sin(math.radians(c - a) / 2) ** 2
         + math.cos(p1) * math.cos(p2) * math.sin(math.radians(d - b) / 2) ** 2)
    return 2 * 6371.0 * math.asin(math.sqrt(h))


def city_of(addr):
    """His sheet used city names in Region; the feed only knows 'united-states'."""
    parts = [p.strip() for p in (addr or "").split(",") if p.strip()]
    for p in reversed(parts):
        if re.fullmatch(r"[A-Z]{2}( \d{5})?", p) or re.fullmatch(r"\d{5}", p):
            continue
        if re.fullmatch(r"[A-Za-z .'-]+", p):
            return p
    return ""


def load_map():
    doc = json.load(io.open(MAP, encoding="utf-8"))
    feats = doc["features"] if isinstance(doc, dict) else doc
    out = []
    for f in feats:
        p = dict(f.get("properties") or {})
        g = (f.get("geometry") or {}).get("coordinates") or [None, None]
        p["lng"], p["lat"] = g[0], g[1]
        p["_people"] = [x.strip() for x in
                        str(p.get("people") or "").replace(";", ",").split(",") if x.strip()]
        out.append(p)
    return out


def ca_rows():
    wb = load_workbook(CA_SRC, data_only=True)
    ws = wb["CA Portfolio"]
    head, out = None, []
    for r in ws.iter_rows(values_only=True):
        vals = ["" if c is None else str(c).strip() for c in r]
        if not any(vals):
            continue
        low = vals[0].lower()
        if low.startswith("project") and len(vals) > 1 and "category" in vals[1].lower():
            head = vals
            continue
        if head is None or low.startswith(("projects  to add", "projects to add",
                                           "highlighted rows", "some addresses",
                                           "kor structural \u2013", "kor structural -",
                                           "75 projects")):
            continue
        d = dict(zip(head, vals))
        out.append({"name": d.get("Project", ""), "address": d.get("Address", ""),
                    "lat": num(d.get("Latitude")), "lng": num(d.get("Longitude")),
                    "comment": d.get("Comments by JD", ""),
                    "decision": (d.get("Keep/Delete") or "").strip()})
    return out


def his_comment(feat, rows):
    """His August note for this pin, where the row is clearly the same project."""
    n = norm(feat.get("name"))
    for r in rows:
        if n and norm(r["name"]) == n:
            return r["comment"]
    fk = street_key(feat.get("address"))
    if fk:
        for r in rows:
            if street_key(r["address"]) == fk:
                return r["comment"]
    ft = tokens(feat.get("name"))
    if ft and feat.get("lat") is not None:
        for r in rows:
            rt = tokens(r["name"])
            if rt and (rt <= ft or ft <= rt) and r["lat"] is not None:
                if haversine_km(r["lat"], r["lng"], feat["lat"], feat["lng"]) <= 0.25:
                    return r["comment"]
    return ""


THIN = Side(style="thin", color="BFBFBF")
BOX = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
HEADERS = ["Project", "Category", "Developer / firm", "Architect", "Address",
           "Region", "Latitude", "Longitude", "Comments by JD", "Keep/Delete"]
WIDTHS = [34, 26, 22, 18, 34, 15, 11, 11, 46, 14]


def sheet(wb, name, feats, rows, subtitle):
    ws = wb.create_sheet(name) if wb.sheetnames != ["Sheet"] else wb.active
    ws.title = name
    ws.cell(1, 1, "KOR Structural \u2013 %s projects, Jim DesRoches" % name).font = \
        Font(bold=True, size=14, color=DARK)
    ws.cell(2, 1, subtitle).font = Font(size=10, italic=True, color="595959")
    ws.merge_cells(start_row=1, start_column=1, end_row=1, end_column=len(HEADERS))
    ws.merge_cells(start_row=2, start_column=1, end_row=2, end_column=len(HEADERS))

    for i, h in enumerate(HEADERS, 1):
        c = ws.cell(4, i, h)
        c.font = Font(bold=True, color="FFFFFF", size=10)
        c.fill = PatternFill("solid", fgColor=DARK)
        c.alignment = Alignment(vertical="center", wrap_text=True)
        c.border = BOX
        ws.column_dimensions[get_column_letter(i)].width = WIDTHS[i - 1]
    ws.row_dimensions[4].height = 28
    ws.freeze_panes = ws.cell(5, 1)
    ws.auto_filter.ref = "A4:%s4" % get_column_letter(len(HEADERS))

    r = 5
    for f in sorted(feats, key=lambda f: norm(f.get("name"))):
        small = bool(f.get("small"))
        vals = [
            f.get("name") or "",
            "KOR Structural" if f.get("era") == "KOR" else "Jim DesRoches \u2013 prior to KOR",
            f.get("developer") or "", f.get("architect") or "",
            f.get("address") or "",
            city_of(f.get("address")) or (f.get("region") or ""),
            f.get("lat"), f.get("lng"),
            his_comment(f, rows) or f.get("description") or "",
            "",
        ]
        for i, v in enumerate(vals, 1):
            c = ws.cell(r, i, v)
            c.alignment = Alignment(vertical="top", wrap_text=(i in (5, 9)))
            c.border = BOX
            c.font = Font(size=10)
            if small:
                c.fill = PatternFill("solid", fgColor=GREY)
        r += 1
    return len(feats)


def main():
    stamp = io.open(STAMP, encoding="utf-8").read().strip() if os.path.exists(STAMP) else "unknown"
    feats = load_map()
    mine = [f for f in feats if JIM in f["_people"]]
    rows = ca_rows()
    print("map: %d features, %d carry Jim (regenerated %s)" % (len(feats), len(mine), stamp))

    van = [f for f in mine if f.get("region") == "vancouver"]
    ca = [f for f in mine if f.get("region") == "united-states"]
    other = [f for f in mine if f not in van and f not in ca]

    wb = Workbook()
    n1 = sheet(wb, "Vancouver", van, rows,
               "%d projects on the map under your name, as of %s. Greyed rows are under $25k "
               "billed \u2013 they show on your bio page, not the public map. "
               "Mark each Keep or Delete in column J." % (len(van), stamp))
    n2 = sheet(wb, "California", ca, rows,
               "%d projects on the map under your name, as of %s \u2013 the %d you deleted in "
               "August are gone. Greyed rows are under $25k billed. "
               "Mark each Keep or Delete in column J."
               % (len(ca), stamp, sum(1 for r in rows if r["decision"].lower() == "delete")))
    wb.save(OUT)
    print("Vancouver %d rows, California %d rows -> %s" % (n1, n2, OUT))
    if other:
        print("not in either tab (%d): %s"
              % (len(other), ", ".join("%s [%s]" % (f.get("name"), f.get("region")) for f in other)))

    # --- for Ian only, deliberately not in Jim's workbook ---
    print("\n--- for Ian, not in the sheet ---")
    def on_map(r_):
        """Same three arms as his_comment, proximity included -- without it this
        counts Radiance, The Grande, Bayside and others as missing when they are
        on the map under a shorter name."""
        n = norm(r_["name"])
        if any(norm(f.get("name")) == n for f in ca):
            return True
        rk = street_key(r_["address"])
        if rk and any(street_key(f.get("address")) == rk for f in ca):
            return True
        rt = tokens(r_["name"])
        if rt and r_["lat"] is not None:
            for f in ca:
                ft = tokens(f.get("name"))
                if ft and (rt <= ft or ft <= rt) and f.get("lat") is not None:
                    if haversine_km(r_["lat"], r_["lng"], f["lat"], f["lng"]) <= 0.25:
                        return True
        return False

    never = [r_["name"] for r_ in rows
             if r_["decision"].lower() == "keep" and not on_map(r_)]
    print("marked Keep in August but no pin exists (%d): %s" % (len(never), "; ".join(never)))
    mirka = [f for f in ca if "mirka" in norm(f.get("name"))]
    if len(mirka) == 2:
        print("Mirka pair still separate: %.0f m apart"
              % (haversine_km(mirka[0]["lat"], mirka[0]["lng"],
                              mirka[1]["lat"], mirka[1]["lng"]) * 1000))


if __name__ == "__main__":
    main()
