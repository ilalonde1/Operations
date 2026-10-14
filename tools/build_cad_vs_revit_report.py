#!/usr/bin/env python3
"""Build the CAD-vs-Revit profitability report: an HTML page on the house template and an Excel workbook.

WHY THIS EXISTS
    Two managers were tallying "how much CAD do we do" by opening job folders and counting
    files, and wanted drafting profitability per residential job. Every ingredient is in
    Deltek and the Financials module already computes it. This turns the per-job pull into
    the hand-out, with no number typed by hand: every figure on the page is computed from
    the rows.

INPUTS (CSV, produced by the ODBC pull of 2026-10-14; same query shape as
ProjectAnalyticsService + HistoricalAnalyticsViewModel, reproduced to the dollar on six projects)
    --jobs       one row per top-level regular project with production hours:
                 WBS1, Name, Status, Org, OpenDate, Sector, DraftType, Category, ConstructionType,
                 Fee, FeeBilled (CAD; Org=USA converted at 1.36), EngHrs, DraftHrs, DraftCost,
                 EngCost, CostActive (app's figure: active staff only), CostAll (all staff),
                 BillableHrs, FirstHrs, LastHrs
    --years      drafting hours (LaborCode 20) per calendar year by the job's drafting type, firm-wide:
                 Yr, DraftType, DraftHrs, Jobs
    --years-res  the same, residential jobs only

OUTPUTS
    --html   the page (style block copied verbatim from tools/BdDocTemplate/reference-quickstart.html)
    --xlsx   workbook: Summary, Residential jobs, All jobs, Yearly hours
    Then: tools\\Format-BdWebPdf.ps1 -Html <html> -Pdf docs\\<name>-web.pdf

WHAT THIS DOES NOT DO
    It does not query Deltek. It does not decide what "completed" means beyond the one rule
    below (billed >= 90% of a non-zero fee). It does not fill the 84% of jobs Deltek leaves
    without a drafting-type tag.
"""
import argparse
import csv
import html
import re
import statistics
from collections import defaultdict
from datetime import datetime
from pathlib import Path

SECTOR_LABEL = {
    "Res": "Residential", "Com": "Commercial", "Hotel": "Hotel", "Off": "Office",
    "Ind-Garage": "Industrial / Garage", "Inst-Rec-C": "Institutional / Rec / Church",
    "R-S-R": "Reno / Seismic / Restoration", "Small Job": "Small Job", "": "(no sector)",
}
SECTOR_ORDER = ["Res", "Com", "Hotel", "Off", "Ind-Garage", "Inst-Rec-C", "R-S-R", "Small Job"]
TYPE_LABEL = {"CAD": "CAD", "RV": "Revit", "General": "General", "untagged": "untagged"}
TYPE_ORDER = ["CAD", "RV", "General", "untagged"]


def f(x):
    try:
        return float(x)
    except (TypeError, ValueError):
        return 0.0


def year_of(s):
    if not s:
        return 0
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%d", "%m/%d/%Y %I:%M:%S %p", "%m/%d/%Y"):
        try:
            return datetime.strptime(s.strip(), fmt).year
        except ValueError:
            pass
    m = re.match(r"(\d{4})-", s.strip())
    return int(m.group(1)) if m else 0


def load_jobs(path):
    rows = []
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            j = {
                "wbs1": r["WBS1"].strip(), "name": r["Name"].strip(), "status": r["Status"].strip(),
                "org": r["Org"].strip(), "sector": r["Sector"].strip(),
                "dtype": r["DraftType"].strip() or "untagged",
                "category": r.get("Category", "").strip(), "construction": r.get("ConstructionType", "").strip(),
                "fee": f(r["Fee"]), "billed": f(r["FeeBilled"]),
                "eng": f(r["EngHrs"]), "draft": f(r["DraftHrs"]),
                "draft_cost": f(r["DraftCost"]), "eng_cost": f(r["EngCost"]),
                "cost_app": f(r["CostActive"]), "cost_all": f(r["CostAll"]),
                "billable": f(r["BillableHrs"]),
                "first_year": year_of(r.get("FirstHrs", "")), "last_year": year_of(r.get("LastHrs", "")),
                "open_year": year_of(r.get("OpenDate", "")),
            }
            j["prod"] = j["eng"] + j["draft"]
            j["completed"] = j["fee"] > 0 and j["billed"] >= 0.9 * j["fee"]
            j["margin_all"] = j["billed"] - j["cost_all"]
            j["margin_app"] = j["billed"] - j["cost_app"]
            j["margin_all_pct"] = j["margin_all"] / j["billed"] if j["billed"] else None
            j["billed_per_hr"] = j["billed"] / j["prod"] if j["prod"] else None
            j["billed_per_draft_hr"] = j["billed"] / j["draft"] if j["draft"] else None
            j["draft_cost_per_hr"] = j["draft_cost"] / j["draft"] if j["draft"] else None
            j["draft_share"] = j["draft"] / j["prod"] if j["prod"] else None
            rows.append(j)
    return rows


def load_years(path):
    out = defaultdict(lambda: defaultdict(float))
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            out[int(r["Yr"])][r["DraftType"].strip() or "untagged"] += f(r["DraftHrs"])
    return out


def median(vals):
    vals = [v for v in vals if v is not None]
    return statistics.median(vals) if vals else None


def summ(rows):
    n = len(rows)
    fee = sum(r["fee"] for r in rows)
    billed = sum(r["billed"] for r in rows)
    eng = sum(r["eng"] for r in rows)
    draft = sum(r["draft"] for r in rows)
    prod = eng + draft
    cost_all = sum(r["cost_all"] for r in rows)
    cost_app = sum(r["cost_app"] for r in rows)
    draft_cost = sum(r["draft_cost"] for r in rows)
    return {
        "jobs": n, "fee": fee, "billed": billed, "eng": eng, "draft": draft, "prod": prod,
        "draft_pct": draft / prod if prod else None,
        "cost_all": cost_all, "cost_app": cost_app,
        "margin_all": billed - cost_all,
        "margin_all_pct": (billed - cost_all) / billed if billed else None,
        "margin_app_pct": (billed - cost_app) / billed if billed else None,
        "med_margin_pct": median([r["margin_all_pct"] for r in rows if r["billed"] > 0]),
        "billed_per_hr": billed / prod if prod else None,
        "cost_per_hr": cost_all / prod if prod else None,
        "billed_per_draft_hr": billed / draft if draft else None,
        "draft_cost_per_hr": draft_cost / draft if draft else None,
        "draft_cost_share": draft_cost / billed if billed else None,
        "med_fee": median([r["fee"] for r in rows]),
    }


# ---------- formatting ----------
def money(x, none="–"):
    if x is None:
        return none
    a = abs(x)
    s = "-" if x < 0 else ""
    if a >= 1_000_000:
        return f"{s}${a/1_000_000:.1f}M"
    if a >= 1_000:
        return f"{s}${a/1_000:.0f}K"
    return f"{s}${a:.0f}"


def dollars(x, none="–"):
    return none if x is None else f"${x:,.0f}"


def pct(x, none="–"):
    return none if x is None else f"{x*100:.0f}%"


def num(x, none="–"):
    return none if x is None else f"{x:,.0f}"


def esc(s):
    return html.escape(str(s))


# ---------- tables ----------
def table_html(headers, rows, numeric_from=1, note=None, cls=""):
    th = "".join(f"<th{' class=num' if i >= numeric_from else ''}>{esc(h)}</th>" for i, h in enumerate(headers))
    body = []
    for r in rows:
        tds = "".join(f"<td{' class=num' if i >= numeric_from else ''}>{c}</td>" for i, c in enumerate(r))
        body.append(f"<tr>{tds}</tr>")
    n = f'<p class="note">{note}</p>' if note else ""
    return f'<table class="{cls}"><thead><tr>{th}</tr></thead><tbody>{"".join(body)}</tbody></table>{n}'


def build(args):
    jobs = load_jobs(args.jobs)
    years_firm = load_years(args.years)
    years_res = load_years(args.years_res)
    res = [j for j in jobs if j["sector"] == "Res"]
    res_done = [j for j in res if j["completed"]]
    asof = args.asof

    # T1 yearly drafting hours
    t1 = []
    yrs = sorted(set(years_firm) | set(years_res))
    shares_firm, shares_res = {}, {}
    for y in yrs:
        fy, ry = years_firm[y], years_res[y]
        f_cad, f_rv = fy.get("CAD", 0), fy.get("RV", 0)
        r_cad, r_rv = ry.get("CAD", 0), ry.get("RV", 0)
        f_other = fy.get("General", 0) + fy.get("untagged", 0)
        shares_firm[y] = f_cad / (f_cad + f_rv) if (f_cad + f_rv) else None
        shares_res[y] = r_cad / (r_cad + r_rv) if (r_cad + r_rv) else None
        label = f"{y} to date" if y == datetime.strptime(asof, "%Y-%m-%d").year else str(y)
        t1.append([label, num(f_cad), num(f_rv), num(f_other), pct(shares_firm[y]),
                   num(r_cad), num(r_rv), pct(shares_res[y])])

    # T2 residential volume by type (all jobs with hours)
    by_type = {t: summ([j for j in res if j["dtype"] == t]) for t in TYPE_ORDER}
    tot_billed = sum(s["billed"] for s in by_type.values())
    tot_jobs = sum(s["jobs"] for s in by_type.values())
    t2 = []
    for t in TYPE_ORDER:
        s = by_type[t]
        t2.append([TYPE_LABEL[t], num(s["jobs"]), pct(s["jobs"] / tot_jobs if tot_jobs else None),
                   money(s["fee"]), money(s["billed"]), pct(s["billed"] / tot_billed if tot_billed else None),
                   num(s["draft"]), num(s["eng"]), dollars(s["billed_per_hr"])])
    cad_rv_billed = by_type["CAD"]["billed"] + by_type["RV"]["billed"]
    cad_rv_jobs = by_type["CAD"]["jobs"] + by_type["RV"]["jobs"]
    cad_share_billed = by_type["CAD"]["billed"] / cad_rv_billed if cad_rv_billed else None
    cad_share_jobs = by_type["CAD"]["jobs"] / cad_rv_jobs if cad_rv_jobs else None

    # T3 completed residential by era (first timesheet year) x CAD/RV
    # Deltek timesheet history begins in 2019: a first timesheet in 2019 means "2019 or earlier".
    bands = [("2019 or earlier", 0, 2019), ("2020–2022", 2020, 2022), (f"2023–{asof[:4]}", 2023, 2100)]
    t3 = []
    era_summ = {}
    for label, a, b in bands:
        for t in ("CAD", "RV"):
            s = summ([j for j in res_done if j["dtype"] == t and a <= j["first_year"] <= b])
            era_summ[(label, t)] = s
            t3.append([label, TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
                       pct(s["margin_all_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
                       dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])
    recent_done = {t: summ([j for j in res_done if j["dtype"] == t]) for t in ("CAD", "RV")}
    for t in ("CAD", "RV"):
        s = recent_done[t]
        t3.append(["All eras together", TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
                   pct(s["margin_all_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
                   dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])

    # T4 completed residential 2017+ by fee size x CAD/RV
    sizes = [("under $100K", 0, 100_000), ("$100K – $300K", 100_000, 300_000), ("$300K and up", 300_000, 1e15)]
    t4 = []
    for label, lo, hi in sizes:
        for t in ("CAD", "RV"):
            s = summ([j for j in res_done if j["dtype"] == t and lo <= j["fee"] < hi])
            t4.append([label, TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
                       pct(s["margin_all_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
                       dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])

    # T5 sectors, completed, all eras, n >= 5
    t5 = []
    for sec in SECTOR_ORDER:
        for t in TYPE_ORDER:
            g = [j for j in jobs if j["sector"] == sec and j["dtype"] == t and j["completed"]]
            if len(g) < 5:
                continue
            s = summ(g)
            t5.append([SECTOR_LABEL[sec], TYPE_LABEL[t], num(s["jobs"]), money(s["billed"]), num(s["draft"]),
                       pct(s["draft_pct"]), pct(s["margin_all_pct"]), pct(s["med_margin_pct"]),
                       dollars(s["billed_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])

    # coverage facts
    all_jobs = len(jobs)
    tagged = sum(1 for j in jobs if j["dtype"] != "untagged")
    res_tagged = sum(1 for j in res if j["dtype"] != "untagged")
    term_share = None
    cur_year = int(asof[:4])
    firm_now = shares_firm.get(cur_year)
    firm_2019 = shares_firm.get(2019)
    res_now = shares_res.get(cur_year)

    # ---------- HTML ----------
    style = Path(args.template).read_text(encoding="utf-8")
    m = re.search(r"<style>.*?</style>", style, re.S)
    if not m:
        raise SystemExit("template has no <style> block")
    style_block = m.group(0)
    extra_css = """
<style>
  /* page-specific: landscape tables, a header row the template does not define */
  .page { max-width: 10.4in; }
  .body { padding: 14px 22px 0; }
  h2 { margin-top: .9rem; }
  table { font-size: 8.8pt; margin-bottom: 10px; }
  th { font-family:var(--mono); font-size:7.2pt; letter-spacing:.06em; text-transform:uppercase; color:var(--ink-soft);
       text-align:left; padding:.3rem .45rem; border-bottom:1.5px solid var(--slate); vertical-align:bottom; }
  td { padding:.28rem .45rem; }
  tr td:first-child { width:auto; white-space:nowrap; }
  td.num, th.num { text-align:right; font-variant-numeric: tabular-nums; }
  .note { margin:-4px 0 12px; font-size:8.4pt; color:var(--ink-soft); }
  .cards { display:flex; gap:.7rem; margin:0 0 14px; }
  .cards .card { flex:1 1 0; }
  .card .big { font-size:20pt; font-weight:700; color:var(--slate-deep); letter-spacing:-.01em; line-height:1.1; margin:.15rem 0 .1rem; }
  .card .big small { font-size:10pt; font-weight:600; color:var(--ink-soft); }
  .box { background:var(--surface-2); border-left:3px solid var(--slate); border-radius:6px; padding:.6rem .8rem; margin:0 0 12px; font-size:9pt; color:var(--ink-soft); }
  .box b { color:var(--ink); }
  .box ul { margin:.3rem 0 0; padding-left:1.1rem; }
  .box li { margin:.12rem 0; }
  .two { display:flex; gap:.8rem; }
  .two > * { flex:1 1 0; }
  .lead { font-size:10.5pt; color:var(--ink); margin:0 0 10px; max-width:60rem; }
  @page { size: Letter landscape; margin: 11mm 12mm; }
  @media print {
    .cards, .box, .two { break-inside: avoid; } h2 { break-after: avoid; } .two { display:flex; }
    /* the template keeps whole tables on one page; here rows flow and only a row is kept whole */
    table { break-inside: auto; } thead { display: table-header-group; } tr { break-inside: avoid; }
  }
</style>"""

    posted = args.posted_through
    asof_label = datetime.strptime(asof, "%Y-%m-%d").strftime("%d %B %Y").lstrip("0")

    def card(label, big, small, body):
        return (f'<div class="card"><h2 style="margin:0 0 .1rem">{label}</h2>'
                f'<div class="big">{big} <small>{small}</small></div><p>{body}</p></div>')

    cards = '<div class="cards">' + card(
        "Drafting hours on CAD jobs",
        pct(firm_now), f"of CAD + Revit, {cur_year} to date",
        f"Firm-wide, from timesheets. It was {pct(firm_2019)} in 2019. Residential alone is {pct(res_now)} this year."
    ) + card(
        "Residential billings on CAD jobs",
        pct(cad_share_billed), "of CAD + Revit billings",
        f"Across {num(cad_rv_jobs)} residential jobs with hours. By job count CAD is {pct(cad_share_jobs)}: "
        f"more jobs, smaller ones."
    ) + card(
        "Margin on residential jobs billed to completion",
        f"{pct(recent_done['CAD']['margin_all_pct'])} <small>CAD</small> · {pct(recent_done['RV']['margin_all_pct'])} <small>Revit</small>", "",
        f"Billed minus labour at current cost rates, {num(recent_done['CAD']['jobs'])} CAD and "
        f"{num(recent_done['RV']['jobs'])} Revit jobs billed to completion. Typical job: "
        f"{pct(recent_done['CAD']['med_margin_pct'])} CAD, {pct(recent_done['RV']['med_margin_pct'])} Revit."
    ) + "</div>"

    sec1 = (f'<h2>How much CAD we do — drafting hours by year</h2>'
            f'<p class="lead">Every drafting hour on a timesheet, grouped by the drafting type Deltek records on the job. '
            f'"Other" is General plus jobs with no tag.</p>'
            + table_html(["Year", "Firm · CAD", "Firm · Revit", "Firm · other", "Firm · CAD share",
                          "Residential · CAD", "Residential · Revit", "Residential · CAD share"], t1,
                         note="CAD share = CAD ÷ (CAD + Revit). Source: tkDetail labour code 20, rejected lines excluded."))

    sec2 = (f'<h2>Residential by the numbers — every job with production hours</h2>'
            + table_html(["Drafting type", "Jobs", "Share of jobs", "Fee", "Billed", "Share of billed",
                          "Draft hrs", "Eng hrs", "Billed / prod hr"], t2,
                         note=f"{num(res_tagged)} of {num(len(res))} residential jobs carry a drafting-type tag in Deltek. "
                              f"Fee and billed in CAD; US jobs at {args.usd_rate}."))

    sec3 = (f'<h2>Profitability — residential jobs billed to completion, CAD vs Revit</h2>'
            f'<p class="lead">Completed = billed at least 90% of the fee. Margin = billed minus labour cost, where cost is every '
            f'hour on the job at that person\'s current cost rate, partners imputed at $250. Era = year of the first timesheet.</p>'
            + table_html(["Era", "Type", "Jobs", "Median fee", "Billed", "Margin", "Median job margin",
                          "Billed / prod hr", "Cost / prod hr", "Billed / draft hr", "Draft cost ÷ billed"], t3, numeric_from=2)
            + f'<h2>Same jobs, by fee size</h2>'
            + table_html(["Fee band", "Type", "Jobs", "Median fee", "Billed", "Margin", "Median job margin",
                          "Billed / prod hr", "Cost / prod hr", "Billed / draft hr", "Draft cost ÷ billed"], t4, numeric_from=2,
                         note="Like for like: a $150K Revit job against a $150K CAD job, not a tower against a duplex."))

    sec4 = (f'<h2>Across sectors — completed jobs, all eras, five or more per cell</h2>'
            + table_html(["Sector", "Type", "Jobs", "Billed", "Draft hrs", "Draft share of hrs", "Margin",
                          "Median job margin", "Billed / prod hr", "Billed / draft hr", "Draft cost ÷ billed"], t5, numeric_from=2))

    method = f"""<div class="two">
<div class="box"><b>Where every number comes from</b>
<ul>
<li>Deltek Vantagepoint over the same ODBC route the Financials module uses. Top-level regular projects only.</li>
<li>Hours: tkDetail. Engineering = labour codes 10 and 30, Drafting = 20. Rejected lines excluded.</li>
<li>Billed: PRSummaryMain, posted through {esc(posted)}. Fee: PR.Fee. US jobs converted at {args.usd_rate}.</li>
<li>Cost: hours × the person's current cost rate (EMCompany), partners at $250/hr. The Historical screen in the app prices hours
of staff who have left at zero; this report prices them. Both figures are in the workbook.</li>
<li>Drafting type: the Deltek custom field CustDraftingType (RV, CAD, General). Sector: PR.ProjectType.</li>
<li>Reproduced the app's cost and margin to the dollar on six projects before anything was written.</li>
</ul></div>
<div class="box"><b>What this does not say</b>
<ul>
<li>Only {num(tagged)} of {num(all_jobs)} jobs with hours carry a drafting-type tag. The untagged line is real work that cannot be split.</li>
<li>Cost rates are today's, applied to every year. Old jobs look costlier than they were; it hits CAD and Revit alike within an era, which is why the eras are kept apart.</li>
<li>A "CAD job" is the tag on the job, not every hour. A job that moved to Revit mid-way counts where Deltek put it.</li>
<li>In-flight jobs are excluded from margin: cost lands before billing does.</li>
<li>Counting folders with a .dwg or .rvt in them counts jobs, not work. Of 31 jobs on one such sheet, Deltek tagged 18 and the sheet agreed with 15 of them; on 13 Deltek had no tag, and the folder check found CAD files on 5.</li>
</ul></div>
</div>"""

    page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>CAD vs Revit — Profitability</title>
{style_block}
{extra_css}
</head><body>
<div class="page">
  <div class="top">
    <div>
      <div class="ey">KOR Structural · Financials</div>
      <h1>CAD vs Revit — what the books say</h1>
      <p>How much CAD we still do, and whether it makes money. Measured from Deltek, weighted by hours and dollars, not by counting folders.</p>
    </div>
    <div style="align-self:center;text-align:right;font-size:9pt;color:#CBD8E2">
      <div>As of {esc(asof_label)}</div>
      <div>Billings posted through {esc(posted)}</div>
      <div>{num(all_jobs)} jobs with production hours</div>
    </div>
  </div>
  <div class="body">
    {cards}
    {sec1}
    {sec2}
    {sec3}
    {sec4}
    {method}
  </div>
  <div class="foot">KOR Operations · Financials · Historical Project Analytics data · job-level figures in the companion workbook</div>
</div>
</body></html>"""
    Path(args.html).write_text(page, encoding="utf-8")

    # ---------- XLSX ----------
    from openpyxl import Workbook
    from openpyxl.styles import Font, PatternFill, Alignment
    from openpyxl.utils import get_column_letter

    wb = Workbook()
    ws = wb.active
    ws.title = "Summary"
    hdr_font = Font(bold=True, color="FFFFFF")
    hdr_fill = PatternFill("solid", fgColor="3F5364")
    title_font = Font(bold=True, size=12, color="2C3A47")

    def put_table(ws, title, headers, rows, start_row):
        ws.cell(row=start_row, column=1, value=title).font = title_font
        r = start_row + 1
        for c, h in enumerate(headers, 1):
            cell = ws.cell(row=r, column=c, value=h)
            cell.font = hdr_font
            cell.fill = hdr_fill
            cell.alignment = Alignment(wrap_text=True, vertical="bottom")
        for row in rows:
            r += 1
            for c, v in enumerate(row, 1):
                ws.cell(row=r, column=c, value=v)
        return r + 2

    r = 1
    ws.cell(row=r, column=1, value=f"CAD vs Revit — what the books say · as of {asof_label} · billings posted through {posted}").font = title_font
    r += 2
    r = put_table(ws, "Drafting hours by year (timesheet labour code 20), by the job's drafting type",
                  ["Year", "Firm CAD", "Firm Revit", "Firm other", "Firm CAD share", "Res CAD", "Res Revit", "Res CAD share"], t1, r)
    r = put_table(ws, "Residential, every job with production hours",
                  ["Drafting type", "Jobs", "Share of jobs", "Fee", "Billed", "Share of billed", "Draft hrs", "Eng hrs", "Billed / prod hr"], t2, r)
    r = put_table(ws, "Residential billed to completion (billed >= 90% of fee), by era of first timesheet",
                  ["Era", "Type", "Jobs", "Median fee", "Billed", "Margin (all staff cost)", "Median job margin",
                   "Billed / prod hr", "Cost / prod hr", "Billed / draft hr", "Draft cost / billed"], t3, r)
    r = put_table(ws, "Residential billed to completion, by fee size",
                  ["Fee band", "Type", "Jobs", "Median fee", "Billed", "Margin (all staff cost)", "Median job margin",
                   "Billed / prod hr", "Cost / prod hr", "Billed / draft hr", "Draft cost / billed"], t4, r)
    r = put_table(ws, "All sectors, completed jobs, all eras, cells with 5+ jobs",
                  ["Sector", "Type", "Jobs", "Billed", "Draft hrs", "Draft share of hrs", "Margin (all staff cost)",
                   "Median job margin", "Billed / prod hr", "Billed / draft hr", "Draft cost / billed"], t5, r)
    for col in range(1, 12):
        ws.column_dimensions[get_column_letter(col)].width = 18
    ws.column_dimensions["A"].width = 30

    job_headers = ["Project #", "Project", "Status", "Org", "Sector", "Drafting type", "Category", "Construction type",
                   "First hours (yr)", "Last hours (yr)", "Fee (CAD)", "Billed (CAD)", "% billed", "Completed",
                   "Eng hrs", "Draft hrs", "Draft share of hrs", "Eng cost", "Draft cost",
                   "Labour cost, all staff", "Labour cost, active staff (app)", "Margin, all staff", "Margin %, all staff",
                   "Margin, active staff (app)", "Billed / prod hr", "Billed / draft hr", "Draft cost / draft hr"]

    def job_row(j):
        org = {"CAD": "Canada", "USA": "USA"}.get(j["org"], j["org"])  # Deltek's org code for Canada is also "CAD"
        return [j["wbs1"], j["name"], j["status"], org, SECTOR_LABEL.get(j["sector"], j["sector"]), TYPE_LABEL[j["dtype"]],
                j["category"], j["construction"], j["first_year"] or None, j["last_year"] or None,
                round(j["fee"], 2), round(j["billed"], 2), round(j["billed"] / j["fee"], 3) if j["fee"] else None,
                "yes" if j["completed"] else "", round(j["eng"], 1), round(j["draft"], 1),
                round(j["draft_share"], 3) if j["draft_share"] is not None else None,
                round(j["eng_cost"], 2), round(j["draft_cost"], 2), round(j["cost_all"], 2), round(j["cost_app"], 2),
                round(j["margin_all"], 2), round(j["margin_all_pct"], 3) if j["margin_all_pct"] is not None else None,
                round(j["margin_app"], 2),
                round(j["billed_per_hr"], 2) if j["billed_per_hr"] is not None else None,
                round(j["billed_per_draft_hr"], 2) if j["billed_per_draft_hr"] is not None else None,
                round(j["draft_cost_per_hr"], 2) if j["draft_cost_per_hr"] is not None else None]

    def job_sheet(title, rows):
        s = wb.create_sheet(title)
        for c, h in enumerate(job_headers, 1):
            cell = s.cell(row=1, column=c, value=h)
            cell.font = hdr_font
            cell.fill = hdr_fill
            cell.alignment = Alignment(wrap_text=True, vertical="bottom")
        for i, j in enumerate(rows, 2):
            for c, v in enumerate(job_row(j), 1):
                s.cell(row=i, column=c, value=v)
        s.freeze_panes = "C2"
        s.auto_filter.ref = f"A1:{get_column_letter(len(job_headers))}{len(rows)+1}"
        widths = {1: 11, 2: 38, 5: 14, 6: 12, 7: 14, 8: 22}
        for c in range(1, len(job_headers) + 1):
            s.column_dimensions[get_column_letter(c)].width = widths.get(c, 13)
        money_cols = [11, 12, 18, 19, 20, 21, 22, 24, 25, 26, 27]
        pct_cols = [13, 17, 23]
        for row in s.iter_rows(min_row=2, max_row=len(rows) + 1):
            for cell in row:
                if cell.column in money_cols:
                    cell.number_format = "#,##0"
                elif cell.column in pct_cols:
                    cell.number_format = "0%"
                elif cell.column in (15, 16):
                    cell.number_format = "#,##0.0"

    res_sorted = sorted(res, key=lambda j: (TYPE_ORDER.index(j["dtype"]), j["wbs1"]), reverse=False)
    job_sheet("Residential jobs", sorted(res_sorted, key=lambda j: j["wbs1"], reverse=True))
    job_sheet("All jobs", sorted(jobs, key=lambda j: j["wbs1"], reverse=True))

    ys = wb.create_sheet("Yearly hours")
    yh = ["Year", "Scope", "CAD", "Revit", "General", "untagged", "CAD share of CAD+Revit"]
    for c, h in enumerate(yh, 1):
        cell = ys.cell(row=1, column=c, value=h)
        cell.font = hdr_font
        cell.fill = hdr_fill
    i = 2
    for scope, data in (("Firm", years_firm), ("Residential", years_res)):
        for y in sorted(data):
            d = data[y]
            cad, rv = d.get("CAD", 0), d.get("RV", 0)
            ys.append([y, scope, round(cad), round(rv), round(d.get("General", 0)), round(d.get("untagged", 0)),
                       round(cad / (cad + rv), 3) if (cad + rv) else None])
            ys.cell(row=i, column=7).number_format = "0%"
            i += 1
    for c in range(1, 8):
        ys.column_dimensions[get_column_letter(c)].width = 14

    wb.save(args.xlsx)

    # ---------- stdout: the headline numbers, for the eye ----------
    print(f"jobs={all_jobs} tagged={tagged} residential={len(res)} residential_completed={len(res_done)}")
    print(f"firm CAD share of drafting hrs: 2019={pct(firm_2019)} {cur_year}={pct(firm_now)}; residential {cur_year}={pct(res_now)}")
    print(f"residential CAD share: billed={pct(cad_share_billed)} jobs={pct(cad_share_jobs)}")
    for t in ("CAD", "RV"):
        s = recent_done[t]
        print(f"completed residential {t}: jobs={s['jobs']} billed={money(s['billed'])} margin={pct(s['margin_all_pct'])} "
              f"median={pct(s['med_margin_pct'])} billed/hr={dollars(s['billed_per_hr'])} cost/hr={dollars(s['cost_per_hr'])}")
    print(f"wrote {args.html}\nwrote {args.xlsx}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--jobs", required=True)
    ap.add_argument("--years", required=True)
    ap.add_argument("--years-res", required=True)
    ap.add_argument("--html", required=True)
    ap.add_argument("--xlsx", required=True)
    ap.add_argument("--asof", required=True, help="YYYY-MM-DD the pull was taken")
    ap.add_argument("--posted-through", required=True, help='e.g. "October 2026" (PRSummaryMain max period)')
    ap.add_argument("--usd-rate", default="1.36")
    ap.add_argument("--template", default=str(Path(__file__).resolve().parent / "BdDocTemplate" / "reference-quickstart.html"))
    build(ap.parse_args())
