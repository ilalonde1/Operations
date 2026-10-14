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
            j["cost_current"] = j["cost_all"]      # hours x today's cost rate, all staff, partners $250
            j["cost_book"] = None                  # Deltek's posted amounts as-is (set by apply_hist)
            j["partner_hrs"] = 0.0
            derive(j)
            rows.append(j)
    return rows


def derive(j):
    """Derived per-job figures. `cost_all` is the report's cost basis; `draft_cost` its drafting share."""
    j["prod"] = j["eng"] + j["draft"]
    j["completed"] = j["fee"] > 0 and j["billed"] >= 0.9 * j["fee"]
    j["margin_all"] = j["billed"] - j["cost_all"]
    j["margin_app"] = j["billed"] - j["cost_app"]
    j["margin_all_pct"] = j["margin_all"] / j["billed"] if j["billed"] else None
    j["billed_per_hr"] = j["billed"] / j["prod"] if j["prod"] else None
    j["billed_per_draft_hr"] = j["billed"] / j["draft"] if j["draft"] else None
    j["draft_cost_per_hr"] = j["draft_cost"] / j["draft"] if j["draft"] else None
    j["draft_share"] = j["draft"] / j["prod"] if j["prod"] else None


PARTNER_IMPUTED_RATE = 250.0  # SharedOptions.PartnerImputedCostRate, the app's standing policy


def apply_hist(rows, path):
    """Switch the cost basis to Deltek's posted amounts (the rate in effect when the hours were posted)
    for staff, with partner hours at the app's imputed $250 instead of their nominal payroll amount.
    Returns the number of jobs matched."""
    hist = {}
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            hist[r["WBS1"].strip()] = r
    n = 0
    for j in rows:
        h = hist.get(j["wbs1"])
        if h is None:
            continue
        n += 1
        posted = f(h["PostedCost"])
        p_hrs, p_amt = f(h["PartnerHrs"]), f(h["PartnerPostedAmt"])
        pd_hrs, pd_amt = f(h.get("PartnerDraftHrs", 0)), f(h.get("PartnerDraftAmt", 0))
        j["cost_book"] = posted
        j["partner_hrs"] = p_hrs
        j["cost_all"] = posted - p_amt + p_hrs * PARTNER_IMPUTED_RATE
        j["eng_cost"] = f(h["EngAmt"])
        j["draft_cost"] = f(h["DraftAmt"]) - pd_amt + pd_hrs * PARTNER_IMPUTED_RATE
        derive(j)
    return n


def load_years(path):
    out = defaultdict(lambda: defaultdict(float))
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            out[int(r["Yr"])][r["DraftType"].strip() or "untagged"] += f(r["DraftHrs"])
    return out


def load_year_scope(path, fields):
    """Sum numeric `fields` into dict[(scope, year, dtype)] for scope 'Firm' (all sectors) and 'Res'."""
    out = defaultdict(lambda: defaultdict(float))
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            y, t = int(r["Yr"]), (r["DraftType"].strip() or "untagged")
            scopes = ["Firm"] + (["Res"] if r["Sector"].strip() == "Res" else [])
            for sc in scopes:
                for fld in fields:
                    out[(sc, y, t)][fld] += f(r[fld])
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
    posted_basis = bool(args.hist_jobs)
    if posted_basis:
        matched = apply_hist(jobs, args.hist_jobs)
        print(f"posted-amount basis: {matched} of {len(jobs)} jobs matched")
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

    # T3 completed residential by cohort (year of first timesheet) x CAD/RV
    # Deltek timesheet history begins in 2019: a first timesheet in 2019 means "2019 or earlier".
    cur_year = int(asof[:4])
    cohort_years = sorted({j["first_year"] for j in res_done if j["dtype"] in ("CAD", "RV") and j["first_year"]})
    t3 = []
    cohort_summ = {}
    for y in cohort_years:
        label = "2019 or earlier" if y <= 2019 else str(y)
        for t in ("CAD", "RV"):
            s = summ([j for j in res_done if j["dtype"] == t and j["first_year"] == y])
            cohort_summ[(y, t)] = s
            if s["jobs"] == 0:
                continue
            t3.append([label, TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
                       pct(s["margin_all_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
                       dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])
    recent_done = {t: summ([j for j in res_done if j["dtype"] == t]) for t in ("CAD", "RV")}
    for t in ("CAD", "RV"):
        s = recent_done[t]
        t3.append(["All cohorts together", TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
                   pct(s["margin_all_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
                   dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])

    # T6 calendar year: billings against labour cost, by drafting type, firm and residential
    billed_y = load_year_scope(args.billed_years, ["Billed", "Jobs"])
    cost_y = load_year_scope(args.cost_years, ["EngHrs", "DraftHrs", "CostAll", "CostActive", "DraftCost"])
    hist_y = load_year_scope(args.hist_years, ["PostedCost", "EngAmt", "DraftAmt", "PartnerHrs", "PartnerPostedAmt"]) if args.hist_years else None
    cal_years = sorted({k[1] for k in billed_y} | {k[1] for k in cost_y})
    cal = {}

    def year_cost(scope, y, t):
        if hist_y is not None:
            h = hist_y.get((scope, y, t), {})
            return h.get("PostedCost", 0.0) - h.get("PartnerPostedAmt", 0.0) + h.get("PartnerHrs", 0.0) * PARTNER_IMPUTED_RATE
        return cost_y.get((scope, y, t), {}).get("CostAll", 0.0)

    def cal_rows(scope):
        rows = []
        for y in cal_years:
            label = f"{y} to date" if y == cur_year else str(y)
            for t in ("CAD", "RV"):
                b = billed_y.get((scope, y, t), {}).get("Billed", 0.0)
                c = cost_y.get((scope, y, t), {})
                eng, draft, cost = c.get("EngHrs", 0.0), c.get("DraftHrs", 0.0), year_cost(scope, y, t)
                prod = eng + draft
                m = b - cost
                cal[(scope, y, t)] = {"billed": b, "cost": cost, "margin_pct": m / b if b else None, "draft": draft, "eng": eng}
                rows.append([label, TYPE_LABEL[t], money(b), money(cost), pct(m / b if b else None),
                             num(draft), num(eng), dollars(b / prod if prod else None), dollars(cost / prod if prod else None)])
        return rows

    t6_res, t6_firm = cal_rows("Res"), cal_rows("Firm")
    last_full = cur_year - 1

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
  .box.overview { border-left-color:var(--accent); background:var(--surface); border:1px solid var(--line); border-left:3px solid var(--accent); font-size:9.6pt; }
  .box.overview li { margin:.2rem 0; }
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
        f"Billed minus labour cost{' at the rates of the time, partners at $250' if posted_basis else ' at current cost rates'}, {num(recent_done['CAD']['jobs'])} CAD and "
        f"{num(recent_done['RV']['jobs'])} Revit jobs billed to completion. Typical job: "
        f"{pct(recent_done['CAD']['med_margin_pct'])} CAD, {pct(recent_done['RV']['med_margin_pct'])} Revit."
    ) + "</div>"

    # executive summary, every figure from the rows above
    mid = {t: summ([j for j in res_done if j["dtype"] == t and 100_000 <= j["fee"] < 300_000]) for t in ("CAD", "RV")}
    lf_cad, lf_rv = cal.get(("Res", last_full, "CAD"), {}), cal.get(("Res", last_full, "RV"), {})
    untagged_billed = by_type["untagged"]["billed"]
    total_cost = sum(j["cost_all"] for j in jobs)
    partner_cost = sum(j["partner_hrs"] * PARTNER_IMPUTED_RATE for j in jobs)
    partner_share = partner_cost / total_cost if total_cost else None
    # sensitivity: partners at their posted payroll amount instead of $250
    book = {}
    for t in ("CAD", "RV"):
        g = [j for j in res_done if j["dtype"] == t and j["cost_book"] is not None]
        b = sum(j["billed"] for j in g)
        book[t] = (b - sum(j["cost_book"] for j in g)) / b if b else None
    # since 2020: completed cohorts and calendar years, the figures that reconcile page 1 with the year table
    since = {t: summ([j for j in res_done if j["dtype"] == t and j["first_year"] >= 2020]) for t in ("CAD", "RV")}
    cal_since = {}
    for t in ("CAD", "RV"):
        b = sum(cal[("Res", y, t)]["billed"] for y in cal_years if 2020 <= y <= last_full)
        c = sum(cal[("Res", y, t)]["cost"] for y in cal_years if 2020 <= y <= last_full)
        neg_years = [y for y in cal_years if 2020 <= y <= last_full and (cal[("Res", y, t)]["margin_pct"] or 0) < 0]
        cal_since[t] = {"billed": b, "cost": c, "margin_pct": (b - c) / b if b else None, "neg_years": neg_years}
    old = {t: summ([j for j in res_done if j["dtype"] == t and j["first_year"] <= 2019]) for t in ("CAD", "RV")}

    # the gap to the Historical screen's population, by cause (screen_recon.csv), and pre-2019 billings (pre2019_billed.csv)
    recon = {r["Bucket"]: (int(r["Jobs"]), f(r["TotalFee"])) for r in csv.DictReader(open(args.screen_recon, newline="", encoding="utf-8-sig"))} if args.screen_recon else {}
    screen_jobs = sum(v[0] for v in recon.values())
    screen_fee = sum(v[1] for v in recon.values())
    recon_note = ""
    if recon:
        parts = []
        if "HourlyOnly" in recon: parts.append(f"{num(recon['HourlyOnly'][0])} hourly-billed jobs with no fixed fee, which the screen counts through their hourly revenue")
        if "NonRegular" in recon: parts.append(f"{num(recon['NonRegular'][0])} non-regular charge-type rows")
        if "Wbs3" in recon: parts.append(f"{num(recon['Wbs3'][0])} phase-level rows")
        in_jobs, in_fee_screen = recon.get("InReport", (0, 0))
        in_fee_report = sum(j["fee"] for j in jobs if j["fee"] > 0)
        recon_note = (f"The Historical screen, filtered to Has Hours + Fee, shows {num(screen_jobs)} projects and {money(screen_fee)} of fee; "
                      f"{num(in_jobs)} of them are this report's population and the rest are {'; '.join(parts)}. "
                      f"On the screen's fee measure (fixed fee plus hourly revenue) those {num(in_jobs)} jobs are {money(in_fee_screen)}; "
                      f"this report counts the fixed fee alone, {money(in_fee_report)}.")
    pre = {r["DraftType"] or "untagged": (int(r["Jobs"]), f(r["BilledBefore2019"])) for r in csv.DictReader(open(args.pre2019, newline="", encoding="utf-8-sig"))} if args.pre2019 else {}
    pre2019_note = ""
    if pre:
        pre2019_note = (f"Billings posted before 2019 on jobs that still logged hours from 2019 on are outside the window: "
                        f"{money(pre.get('CAD', (0, 0))[1])} on {num(pre.get('CAD', (0, 0))[0])} CAD jobs and {money(pre.get('RV', (0, 0))[1])} on "
                        f"{num(pre.get('RV', (0, 0))[0])} Revit jobs, which weighs on the early CAD years.")
    cov_hrs_no_amt = cov_hrs = 0.0
    if args.coverage:
        for r in csv.DictReader(open(args.coverage, newline="", encoding="utf-8-sig")):
            cov_hrs += f(r["BillableHrs"]); cov_hrs_no_amt += f(r["HrsNoAmt"])

    if posted_basis:
        cost_sentence = ("Cost = the payroll amount Deltek posted on each timesheet line, at the rate in effect at the time, "
                         f"with partner hours at the firm's imputed $250 instead of their nominal payroll figure.")
        cost_bullet = (f"Cost: Deltek's posted labour amounts (tkDetail RegAmt + OvtAmt + SpecialOvtAmt), the rate in effect when the hours "
                       f"were posted, the same figure Firm Health uses. Partner hours at the app's imputed $250/hr, which is "
                       f"{pct(partner_share)} of all labour cost here. With partners at their posted payroll amount instead, completed "
                       f"residential margins would read {pct(book['CAD'])} CAD and {pct(book['RV'])} Revit.")
        rate_caveat = ("Partner hours are a third of all billable hours and carry a policy rate, not a payroll rate. It moves CAD and Revit "
                       "margins by about the same amount; the workbook carries the posted-payroll figure beside it.")
    else:
        cost_sentence = ("Cost = every hour on the job at that person's current cost rate, partners imputed at $250.")
        cost_bullet = ("Cost: hours × the person's current cost rate (EMCompany), partners at $250/hr. The Historical screen in the app "
                       "prices hours of staff who have left at zero; this report prices them. Both figures are in the workbook.")
        rate_caveat = ("Cost rates are today's, applied to every year. Old jobs look costlier than they were; it hits CAD and Revit alike "
                       "within an era, which is why the eras are kept apart.")
    overview = f"""<div class="box overview"><b>The short version</b>
<ul>
<li><b>CAD is {pct(firm_now)} of the firm's drafting hours this year</b>, down from {pct(firm_2019)} in 2019. Residential alone is {pct(res_now)}. The shift to Revit happened in 2020–2021 and has held since.</li>
<li><b>Counting jobs overstates CAD.</b> In residential, CAD is {pct(cad_share_jobs)} of jobs with hours but {pct(cad_share_billed)} of billings. The median CAD job is {money(by_type['CAD']['med_fee'])} of fee; the median Revit job is {money(by_type['RV']['med_fee'])}.</li>
<li><b>Billed to completion, residential margins are level overall</b>: {pct(recent_done['CAD']['margin_all_pct'])} CAD against {pct(recent_done['RV']['margin_all_pct'])} Revit, fee-weighted. On the typical job Revit is ahead, {pct(recent_done['RV']['med_margin_pct'])} against {pct(recent_done['CAD']['med_margin_pct'])}.</li>
<li><b>The level figure is the old book.</b> Jobs started 2019 or earlier: CAD {pct(old['CAD']['margin_all_pct'])}, Revit {pct(old['RV']['margin_all_pct'])}. Jobs started 2020 on and billed to completion: CAD {pct(since['CAD']['margin_all_pct'])} on {num(since['CAD']['jobs'])} jobs, Revit {pct(since['RV']['margin_all_pct'])} on {num(since['RV']['jobs'])}.</li>
<li><b>Year by year it is plainer.</b> Calendar 2020–{last_full}, residential CAD jobs billed {money(cal_since['CAD']['billed'])} against {money(cal_since['CAD']['cost'])} of labour ({pct(cal_since['CAD']['margin_pct'])}), below zero in {num(len(cal_since['CAD']['neg_years']))} of {num(last_full - 2019)} years; Revit billed {money(cal_since['RV']['billed'])} against {money(cal_since['RV']['cost'])} ({pct(cal_since['RV']['margin_pct'])}), below zero in {num(len(cal_since['RV']['neg_years']))}. In {last_full}: CAD {pct(lf_cad.get('margin_pct'))}, Revit {pct(lf_rv.get('margin_pct'))}.</li>
<li><b>Size, not tool, explains part of it.</b> In the $100K–$300K band across all cohorts CAD runs {pct(mid['CAD']['margin_all_pct'])} at {dollars(mid['CAD']['billed_per_hr'])} billed per production hour and Revit {pct(mid['RV']['margin_all_pct'])} at {dollars(mid['RV']['billed_per_hr'])}; the CAD losses sit in the under-$100K band, where most recent CAD jobs are.</li>
<li><b>Coverage:</b> {num(tagged)} of {num(all_jobs)} jobs with hours carry Deltek's drafting-type tag. Untagged residential jobs billed {money(untagged_billed)}; nothing here can split them.</li>
</ul></div>"""

    sec_year = (f'<h2>Year by year — billings against labour cost, by drafting type</h2>'
                f'<p class="lead">Calendar years. Billed = what was posted in the year on jobs carrying each tag; cost = the labour '
                f'posted that year on those jobs{", at the rates of the time, partners at $250" if posted_basis else " at current cost rates"}. '
                f'Billing and hours do not land in the same month, so read the trend, not one year alone. {pre2019_note} '
                f'General and untagged are in the workbook.</p>'
                f'<h2 style="margin-top:.2rem">Residential</h2>'
                + table_html(["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_res, numeric_from=2)
                + f'<h2>Whole firm</h2>'
                + table_html(["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_firm, numeric_from=2))

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

    sec3 = (f'<h2>Profitability — residential jobs billed to completion, by the year they started</h2>'
            f'<p class="lead">Completed = billed at least 90% of the fee. Margin = billed minus labour cost. {cost_sentence} '
            f'Cohort = year of the first timesheet.</p>'
            + table_html(["Started", "Type", "Jobs", "Median fee", "Billed", "Margin", "Median job margin",
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
<li>{cost_bullet}</li>
<li>Drafting type: the Deltek custom field CustDraftingType (RV, CAD, General). Sector: PR.ProjectType.</li>
<li>Reproduced the app's cost and margin to the dollar on six projects before anything was written. Two independent pulls of drafting hours agree to the hour on every year and type. {recon_note}</li>
</ul></div>
<div class="box"><b>What this does not say</b>
<ul>
<li>Only {num(tagged)} of {num(all_jobs)} jobs with hours carry a drafting-type tag. The untagged line is real work that cannot be split.</li>
<li>{rate_caveat}</li>
<li>A "CAD job" is the tag on the job, not every hour. A job that moved to Revit mid-way counts where Deltek put it.</li>
<li>In-flight jobs are excluded from margin: cost lands before billing does. Closed jobs that never billed 90% of their fee are excluded too; including them changes no comparison by more than two points.</li>
<li>Assumptions, stated so they can be challenged: US jobs at a flat 1.36, not the year's rate; margin is before subconsultants and anything not in Deltek's cost rate; {num(cov_hrs_no_amt)} of {num(cov_hrs)} billable hours ({pct(cov_hrs_no_amt / cov_hrs if cov_hrs else None)}) carry no posted amount and cost nothing here, all of them 2019–2021; "completed" means billed at least 90% of fee.</li>
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
    {overview}
    {cards}
    {sec1}
    {sec_year}
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
    r = put_table(ws, "Calendar year, residential: billings against labour cost by drafting type",
                  ["Year", "Type", "Billed", "Labour cost (all staff)", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_res, r)
    r = put_table(ws, "Calendar year, whole firm: billings against labour cost by drafting type",
                  ["Year", "Type", "Billed", "Labour cost (all staff)", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_firm, r)
    r = put_table(ws, "Residential billed to completion (billed >= 90% of fee), by year of first timesheet",
                  ["Started", "Type", "Jobs", "Median fee", "Billed", "Margin (all staff cost)", "Median job margin",
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
                   "Labour cost (report basis)", "Labour cost, active staff at current rates (app)", "Margin (report basis)", "Margin % (report basis)",
                   "Margin, app basis", "Billed / prod hr", "Billed / draft hr", "Draft cost / draft hr",
                   "Labour cost, posted payroll as-is (Deltek)", "Labour cost, all staff at current rates", "Partner hrs"]

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
                round(j["draft_cost_per_hr"], 2) if j["draft_cost_per_hr"] is not None else None,
                round(j["cost_book"], 2) if j["cost_book"] is not None else None,
                round(j["cost_current"], 2), round(j["partner_hrs"], 1)]

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
        money_cols = [11, 12, 18, 19, 20, 21, 22, 24, 25, 26, 27, 28, 29]
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

    by = wb.create_sheet("By year (raw)")
    byh = ["Scope", "Year", "Drafting type", "Billed (CAD)", "Eng hrs", "Draft hrs", "Labour cost, all staff",
           "Labour cost, active staff (app)", "Draft cost", "Margin, all staff", "Margin %, all staff"]
    for c, h in enumerate(byh, 1):
        cell = by.cell(row=1, column=c, value=h)
        cell.font = hdr_font
        cell.fill = hdr_fill
    keys = sorted(set(billed_y) | set(cost_y), key=lambda k: (k[0], k[1], TYPE_ORDER.index(k[2]) if k[2] in TYPE_ORDER else 9))
    for i, k in enumerate(keys, 2):
        b = billed_y.get(k, {}).get("Billed", 0.0)
        c = cost_y.get(k, {})
        cost_all = c.get("CostAll", 0.0)
        by.append([k[0], k[1], TYPE_LABEL.get(k[2], k[2]), round(b), round(c.get("EngHrs", 0.0)), round(c.get("DraftHrs", 0.0)),
                   round(cost_all), round(c.get("CostActive", 0.0)), round(c.get("DraftCost", 0.0)), round(b - cost_all),
                   round((b - cost_all) / b, 3) if b else None])
        by.cell(row=i, column=11).number_format = "0%"
    for c in range(1, 12):
        by.column_dimensions[get_column_letter(c)].width = 16
    by.freeze_panes = "A2"

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
    ap.add_argument("--billed-years", required=True, help="Yr, Sector, DraftType, Billed (CAD), Jobs: PRSummaryMain by period year")
    ap.add_argument("--cost-years", required=True, help="Yr, Sector, DraftType, EngHrs, DraftHrs, CostAll, CostActive, DraftCost, Jobs: tkDetail by year")
    ap.add_argument("--hist-jobs", help="WBS1, EngAmt, DraftAmt, PostedCost, PartnerHrs, PartnerPostedAmt, PartnerDraftHrs, PartnerDraftAmt: posted amounts per job")
    ap.add_argument("--hist-years", help="Yr, Sector, DraftType, PostedCost, EngAmt, DraftAmt, PartnerHrs, PartnerPostedAmt: posted amounts by year")
    ap.add_argument("--screen-recon", help="Bucket, Jobs, TotalFee: the Historical screen's population split by why a row is or is not in this report")
    ap.add_argument("--pre2019", help="DraftType, Jobs, BilledBefore2019: residential billings posted before 2019 on jobs with 2019+ hours")
    ap.add_argument("--coverage", help="Yr, Who, BillableHrs, HrsNoAmt, PostedAmt, CurrentRateCost: posted-amount coverage by year")
    ap.add_argument("--html", required=True)
    ap.add_argument("--xlsx", required=True)
    ap.add_argument("--asof", required=True, help="YYYY-MM-DD the pull was taken")
    ap.add_argument("--posted-through", required=True, help='e.g. "October 2026" (PRSummaryMain max period)')
    ap.add_argument("--usd-rate", default="1.36")
    ap.add_argument("--template", default=str(Path(__file__).resolve().parent / "BdDocTemplate" / "reference-quickstart.html"))
    build(ap.parse_args())
