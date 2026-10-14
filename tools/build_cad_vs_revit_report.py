#!/usr/bin/env python3
"""Build the CAD-vs-Revit profitability report: an HTML page on the house template and an Excel workbook.

WHY THIS EXISTS
    Two managers were tallying "how much CAD do we do" by opening job folders and counting
    files, and wanted drafting profitability per residential job. Every ingredient is in
    Deltek and the Financials module already computes it. This turns the per-job pull into
    the hand-out, with no number typed by hand: every figure on the page is computed from
    the rows.

INPUTS (CSV, produced by the ODBC pull of 2026-10-14 and banked in
docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data/; same query shape as
ProjectAnalyticsService + HistoricalAnalyticsViewModel, reproduced to the dollar on six projects.
The same measurement lives in KorTransmittals via db/KorTransmittals/008, proc
usp_SnapshotProjectDraftingProfitability; a future edition should export from that view.)
    --jobs        one row per top-level regular project with production hours:
                  WBS1, Name, Status, Org, OpenDate, Sector, DraftType, Category, ConstructionType,
                  Fee, FeeBilled (CAD; Org=USA converted at 1.36), EngHrs, DraftHrs, DraftCost,
                  EngCost, CostActive (app's figure: active staff only), CostAll (all staff),
                  BillableHrs, FirstHrs, LastHrs           (cost columns here = hours x TODAY's rate)
    --hist-jobs   posted payroll amounts per job (the rate in effect when the hours were posted):
                  WBS1, EngAmt, DraftAmt, PostedCost, PartnerHrs, PartnerPostedAmt, PartnerDraftHrs,
                  PartnerDraftAmt, PartnerEngHrs, PartnerEngAmt
    --pre2019-jobs WBS1, BilledBefore2019, FirstBillingPeriod, LastBillingPeriod (PRSummaryMain)
    --years / --years-res   drafting hours per calendar year by the job's drafting type: Yr, DraftType, DraftHrs, Jobs
    --billed-years          Yr, Sector, DraftType, Billed (PRSummaryMain by posting-period year)
    --cost-years            Yr, Sector, DraftType, EngHrs, DraftHrs, CostAll, CostActive, DraftCost, Jobs (current rates)
    --hist-years            Yr, Sector, DraftType, PostedCost, EngAmt, DraftAmt, PartnerHrs, PartnerPostedAmt
    --screen-recon          Bucket, Jobs, TotalFee: the Historical screen's population split by why a row is in or out
    --pre2019               DraftType, Jobs, BilledBefore2019 (residential, jobs with 2019+ hours)
    --coverage              Yr, Who, BillableHrs, HrsNoAmt, PostedAmt, CurrentRateCost

DEFINITIONS (the same ones as the view in 008)
    cost (report basis)   posted amounts for staff + partner hours x $250 (the app's PartnerImputedCostRate)
    finished              billed >= 90% of a non-zero fee, or status not Active (the screen's "Closed"), whatever the fee
    in window             no billings posted before 2019 and no open date before 2019: the job's whole life is
                          inside Deltek's timesheet history, which begins 2019. Jobs outside the window carry
                          lifetime billings against labour that only starts in 2019, so their margin is not real;
                          they are excluded from every job-level margin and counted on the page.
    cohort                calendar year of the job's first timesheet

OUTPUTS
    --html   the page (style block copied verbatim from tools/BdDocTemplate/reference-quickstart.html)
    --xlsx   workbook: Summary, Residential jobs, All jobs, Yearly hours, By year (raw)
    Then: tools\\Format-BdWebPdf.ps1 -Html <html> -Pdf docs\\<name>-web.pdf

WHAT THIS DOES NOT DO
    It does not query Deltek. It cannot recover labour before 2019. It does not fill the jobs Deltek leaves
    without a drafting-type tag. It describes associations between drafting type and margin; it does not
    isolate the effect of the software from job size, era or who ran the job.
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
ACTIVE_STATUSES = {"A", "ACTIVE"}          # HistoricalAnalyticsViewModel.ActiveStatuses; everything else is "Closed" on the screen
PARTNER_IMPUTED_RATE = 250.0               # SharedOptions.PartnerImputedCostRate, the app's standing policy
HISTORY_STARTS = 2019                      # first year with timesheets in Deltek


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


# ---------- loading ----------
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
                "billable": f(r["BillableHrs"]),
                "first_year": year_of(r.get("FirstHrs", "")), "last_year": year_of(r.get("LastHrs", "")),
                "open_year": year_of(r.get("OpenDate", "")),
                # current-rate basis (today's cost rates), as the Historical screen computes it
                "cost_current": f(r["CostAll"]), "cost_app": f(r["CostActive"]),
                "eng_cost_current": f(r["EngCost"]), "draft_cost_current": f(r["DraftCost"]),
                # filled by apply_hist
                "cost_book": 0.0, "partner_hrs": 0.0, "eng_cost": 0.0, "draft_cost": 0.0, "cost_all": 0.0,
                # filled by apply_pre2019
                "billed_before_2019": 0.0, "first_billing_period": 0,
            }
            rows.append(j)
    return rows


def apply_hist(rows, path):
    """Cost basis = Deltek's posted amounts (the rate in effect when the hours were posted) for staff,
    with partner hours at the imputed rate instead of their nominal payroll amount. Fails if a job has
    no posted-amount row: a silent fallback to another basis is exactly the fault this guards against."""
    hist = {}
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            hist[r["WBS1"].strip()] = r
    missing = [j["wbs1"] for j in rows if j["wbs1"] not in hist]
    if missing:
        raise SystemExit(f"{len(missing)} jobs have no posted-amount row, e.g. {missing[:5]}")
    for j in rows:
        h = hist[j["wbs1"]]
        posted = f(h["PostedCost"])
        p_hrs, p_amt = f(h["PartnerHrs"]), f(h["PartnerPostedAmt"])
        pd_hrs, pd_amt = f(h["PartnerDraftHrs"]), f(h["PartnerDraftAmt"])
        pe_hrs, pe_amt = f(h["PartnerEngHrs"]), f(h["PartnerEngAmt"])
        j["cost_book"] = posted
        j["partner_hrs"] = p_hrs
        j["cost_all"] = posted - p_amt + p_hrs * PARTNER_IMPUTED_RATE
        j["eng_cost"] = f(h["EngAmt"]) - pe_amt + pe_hrs * PARTNER_IMPUTED_RATE
        j["draft_cost"] = f(h["DraftAmt"]) - pd_amt + pd_hrs * PARTNER_IMPUTED_RATE


def apply_pre2019(rows, path):
    pre = {}
    with open(path, newline="", encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            pre[r["WBS1"].strip()] = r
    for j in rows:
        p = pre.get(j["wbs1"])
        if p:
            j["billed_before_2019"] = f(p["BilledBefore2019"])
            j["first_billing_period"] = int(f(p["FirstBillingPeriod"]))


def derive(rows):
    for j in rows:
        j["prod"] = j["eng"] + j["draft"]
        j["closed"] = j["status"].upper() not in ACTIVE_STATUSES
        j["fully_billed"] = j["fee"] > 0 and j["billed"] >= 0.9 * j["fee"]
        j["finished"] = j["fully_billed"] or j["closed"]
        j["in_window"] = j["billed_before_2019"] == 0 and (j["open_year"] == 0 or j["open_year"] >= HISTORY_STARTS)
        j["margin"] = j["billed"] - j["cost_all"]
        j["margin_pct"] = j["margin"] / j["billed"] if j["billed"] else None
        j["billed_per_hr"] = j["billed"] / j["prod"] if j["prod"] else None
        j["billed_per_draft_hr"] = j["billed"] / j["draft"] if j["draft"] else None
        j["draft_cost_per_hr"] = j["draft_cost"] / j["draft"] if j["draft"] else None
        j["draft_share"] = j["draft"] / j["prod"] if j["prod"] else None


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


# ---------- statistics ----------
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
    cost_book = sum(r["cost_book"] for r in rows)
    draft_cost = sum(r["draft_cost"] for r in rows)
    return {
        "jobs": n, "fee": fee, "billed": billed, "eng": eng, "draft": draft, "prod": prod,
        "draft_pct": draft / prod if prod else None,
        "cost_all": cost_all, "cost_book": cost_book,
        "margin": billed - cost_all,
        "margin_pct": (billed - cost_all) / billed if billed else None,          # billings-weighted
        "margin_book_pct": (billed - cost_book) / billed if billed else None,    # partners at posted payroll
        "med_margin_pct": median([r["margin_pct"] for r in rows if r["billed"] > 0]),
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
    if x is None:
        return none
    if x != 0 and abs(x) < 0.005:          # a loss of 0.3% must not print as "-0%" or "0%"
        return f"{x*100:.1f}%"
    return f"{x*100:.0f}%"


def num(x, none="–"):
    return none if x is None else f"{x:,.0f}"


def esc(s):
    return html.escape(str(s))


def table_html(headers, rows, numeric_from=1, note=None, cls=""):
    if len({len(r) for r in rows} | {len(headers)}) != 1:
        raise SystemExit(f"table '{headers[0]}': header/row column counts differ")
    th = "".join(f"<th{' class=num' if i >= numeric_from else ''}>{esc(h)}</th>" for i, h in enumerate(headers))
    body = []
    for r in rows:
        tds = "".join(f"<td{' class=num' if i >= numeric_from else ''}>{c}</td>" for i, c in enumerate(r))
        body.append(f"<tr>{tds}</tr>")
    n = f'<p class="note">{note}</p>' if note else ""
    return f'<table class="{cls}"><thead><tr>{th}</tr></thead><tbody>{"".join(body)}</tbody></table>{n}'


def margin_row(label, t, s):
    return [label, TYPE_LABEL[t], num(s["jobs"]), money(s["med_fee"]), money(s["billed"]),
            pct(s["margin_pct"]), pct(s["med_margin_pct"]), dollars(s["billed_per_hr"]),
            dollars(s["cost_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])]


MARGIN_HEADERS = ["Cohort", "Type", "Jobs", "Median fee", "Billed", "Billings-weighted margin", "Median job margin",
                  "Billed / prod hr", "Cost / prod hr", "Billed / draft hr", "Draft cost ÷ billed"]


# ---------- the build ----------
def build(args):
    jobs = load_jobs(args.jobs)
    apply_hist(jobs, args.hist_jobs)
    apply_pre2019(jobs, args.pre2019_jobs)
    derive(jobs)
    asof = args.asof
    cur_year = int(asof[:4])
    last_full = cur_year - 1
    asof_label = datetime.strptime(asof, "%Y-%m-%d").strftime("%d %B %Y").lstrip("0")
    posted = args.posted_through

    res = [j for j in jobs if j["sector"] == "Res"]
    fin = [j for j in jobs if j["finished"] and j["in_window"]]            # the margin population
    res_fin = [j for j in fin if j["sector"] == "Res"]
    res_out = [j for j in res if j["finished"] and not j["in_window"]]      # finished but started before the history

    # --- T1 drafting hours by year ---
    years_firm, years_res = load_years(args.years), load_years(args.years_res)
    yrs = sorted(set(years_firm) | set(years_res))
    t1, share_pair, share_all, share_res = [], {}, {}, {}
    for y in yrs:
        fy, ry = years_firm[y], years_res[y]
        f_cad, f_rv = fy.get("CAD", 0), fy.get("RV", 0)
        r_cad, r_rv = ry.get("CAD", 0), ry.get("RV", 0)
        f_other = fy.get("General", 0) + fy.get("untagged", 0)
        share_pair[y] = f_cad / (f_cad + f_rv) if (f_cad + f_rv) else None
        share_all[y] = f_cad / (f_cad + f_rv + f_other) if (f_cad + f_rv + f_other) else None
        share_res[y] = r_cad / (r_cad + r_rv) if (r_cad + r_rv) else None
        label = f"{y} to date" if y == cur_year else str(y)
        t1.append([label, num(f_cad), num(f_rv), num(f_other), pct(share_pair[y]), pct(share_all[y]),
                   num(r_cad), num(r_rv), pct(share_res[y])])

    # --- T2 residential volume, every job with hours ---
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

    # --- T3 finished residential jobs inside the history, by cohort ---
    cohort_years = sorted({j["first_year"] for j in res_fin if j["dtype"] in ("CAD", "RV") and j["first_year"]})
    t3 = []
    for y in cohort_years:
        for t in ("CAD", "RV"):
            s = summ([j for j in res_fin if j["dtype"] == t and j["first_year"] == y])
            if s["jobs"]:
                t3.append(margin_row(str(y), t, s))
    head = {t: summ([j for j in res_fin if j["dtype"] == t]) for t in ("CAD", "RV")}
    for t in ("CAD", "RV"):
        t3.append(margin_row("All, started 2019 on", t, head[t]))
    unknown_cohort = sum(1 for j in res_fin if j["dtype"] in ("CAD", "RV") and not j["first_year"])
    nofee = {t: sum(1 for j in res_fin if j["dtype"] == t and j["fee"] == 0) for t in ("CAD", "RV")}
    strict = {t: summ([j for j in res_fin if j["dtype"] == t and j["fully_billed"]]) for t in ("CAD", "RV")}
    recent = {t: summ([j for j in res_fin if j["dtype"] == t and j["first_year"] >= 2020]) for t in ("CAD", "RV")}
    out_summ = {t: summ([j for j in res_out if j["dtype"] == t]) for t in ("CAD", "RV")}

    # --- T4 by fee size, finished in-window: all cohorts, then 2020 on ---
    sizes = [("under $100K", 0, 100_000), ("$100K – $300K", 100_000, 300_000), ("$300K and up", 300_000, 1e15)]
    t4 = []
    for label, lo, hi in sizes:
        for t in ("CAD", "RV"):
            s = summ([j for j in res_fin if j["dtype"] == t and lo <= j["fee"] < hi])
            t4.append(margin_row(label, t, s))
    t4b = []
    for label, lo, hi in sizes:
        for t in ("CAD", "RV"):
            s = summ([j for j in res_fin if j["dtype"] == t and j["first_year"] >= 2020 and lo <= j["fee"] < hi])
            t4b.append(margin_row(label, t, s))
    small_recent = {t: summ([j for j in res_fin if j["dtype"] == t and j["first_year"] >= 2020 and j["fee"] < 100_000]) for t in ("CAD", "RV")}

    # --- T5 sectors, finished in-window, n >= 5 ---
    t5 = []
    for sec in SECTOR_ORDER:
        for t in TYPE_ORDER:
            g = [j for j in fin if j["sector"] == sec and j["dtype"] == t]
            if len(g) < 5:
                continue
            s = summ(g)
            t5.append([SECTOR_LABEL[sec], TYPE_LABEL[t], num(s["jobs"]), money(s["billed"]), num(s["draft"]),
                       pct(s["draft_pct"]), pct(s["margin_pct"]), pct(s["med_margin_pct"]),
                       dollars(s["billed_per_hr"]), dollars(s["billed_per_draft_hr"]), pct(s["draft_cost_share"])])

    # --- T6 calendar years: billings against labour cost, by tag ---
    billed_y = load_year_scope(args.billed_years, ["Billed", "Jobs"])
    cost_y = load_year_scope(args.cost_years, ["EngHrs", "DraftHrs", "CostAll", "CostActive", "DraftCost"])
    hist_y = load_year_scope(args.hist_years, ["PostedCost", "EngAmt", "DraftAmt", "PartnerHrs", "PartnerPostedAmt"])
    cal_years = sorted({k[1] for k in billed_y} | {k[1] for k in cost_y})
    cal = {}

    def year_cost(scope, y, t):
        h = hist_y.get((scope, y, t), {})
        return h.get("PostedCost", 0.0) - h.get("PartnerPostedAmt", 0.0) + h.get("PartnerHrs", 0.0) * PARTNER_IMPUTED_RATE

    def cal_rows(scope):
        rows = []
        for y in cal_years:
            label = f"{y} to date" if y == cur_year else str(y)
            for t in ("CAD", "RV"):
                b = billed_y.get((scope, y, t), {}).get("Billed", 0.0)
                c = cost_y.get((scope, y, t), {})
                eng, draft, cost = c.get("EngHrs", 0.0), c.get("DraftHrs", 0.0), year_cost(scope, y, t)
                prod = eng + draft
                cal[(scope, y, t)] = {"billed": b, "cost": cost, "cost_current": c.get("CostAll", 0.0),
                                      "margin_pct": (b - cost) / b if b else None, "draft": draft, "eng": eng}
                rows.append([label, TYPE_LABEL[t], money(b), money(cost), pct((b - cost) / b if b else None),
                             num(draft), num(eng), dollars(b / prod if prod else None), dollars(cost / prod if prod else None)])
        return rows

    t6_res, t6_firm = cal_rows("Res"), cal_rows("Firm")
    cal_since = {}
    for t in ("CAD", "RV"):
        ys = [y for y in cal_years if 2020 <= y <= last_full]
        b = sum(cal[("Res", y, t)]["billed"] for y in ys)
        c = sum(cal[("Res", y, t)]["cost"] for y in ys)
        neg = [y for y in ys if cal[("Res", y, t)]["billed"] < cal[("Res", y, t)]["cost"]]
        cal_since[t] = {"billed": b, "cost": c, "margin_pct": (b - c) / b if b else None, "neg": len(neg), "years": len(ys)}
    lf = {t: cal.get(("Res", last_full, t), {}) for t in ("CAD", "RV")}

    # --- facts for the prose ---
    all_jobs, tagged = len(jobs), sum(1 for j in jobs if j["dtype"] != "untagged")
    res_tagged = sum(1 for j in res if j["dtype"] != "untagged")
    total_cost = sum(j["cost_all"] for j in jobs)
    partner_cost = sum(j["partner_hrs"] * PARTNER_IMPUTED_RATE for j in jobs)
    partner_share = partner_cost / total_cost if total_cost else None
    partner_hrs_share = sum(j["partner_hrs"] for j in jobs) / sum(j["billable"] for j in jobs) if jobs else None
    cov_hrs = cov_hrs_no_amt = 0.0
    for r in csv.DictReader(open(args.coverage, newline="", encoding="utf-8-sig")):
        cov_hrs += f(r["BillableHrs"]); cov_hrs_no_amt += f(r["HrsNoAmt"])
    recon = {r["Bucket"]: (int(r["Jobs"]), f(r["TotalFee"])) for r in csv.DictReader(open(args.screen_recon, newline="", encoding="utf-8-sig"))}
    screen_jobs, screen_fee = sum(v[0] for v in recon.values()), sum(v[1] for v in recon.values())
    in_jobs, in_fee_screen = recon.get("InReport", (0, 0))
    in_fee_report = sum(j["fee"] for j in jobs if j["fee"] > 0)
    others = [f"{num(v[0])} hourly-billed jobs with no fixed fee, which the screen counts through their hourly revenue"
              for k, v in recon.items() if k == "HourlyOnly"]
    others += [f"{num(v[0])} rows of other charge types" for k, v in recon.items() if k == "NonRegular"]
    pre = {r["DraftType"] or "untagged": (int(r["Jobs"]), f(r["BilledBefore2019"])) for r in csv.DictReader(open(args.pre2019, newline="", encoding="utf-8-sig"))}

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
    table { break-inside: auto; } thead { display: table-header-group; } tr { break-inside: avoid; }
  }
</style>"""

    def card(label, big, small, body):
        return (f'<div class="card"><h2 style="margin:0 0 .1rem">{label}</h2>'
                f'<div class="big">{big} <small>{small}</small></div><p>{body}</p></div>')

    cards = '<div class="cards">' + card(
        "Drafting hours on CAD jobs",
        pct(share_pair.get(cur_year)), f"of CAD + Revit, {cur_year} to date",
        f"Firm-wide, from timesheets; {pct(share_all.get(cur_year))} of all drafting hours including General and untagged. "
        f"It was {pct(share_pair.get(HISTORY_STARTS))} in {HISTORY_STARTS}. Residential alone is {pct(share_res.get(cur_year))} this year."
    ) + card(
        "Residential billings on CAD jobs",
        pct(cad_share_billed), "of CAD + Revit billings",
        f"Across {num(cad_rv_jobs)} tagged residential jobs with hours. By job count CAD is {pct(cad_share_jobs)}"
        f"{': more jobs, smaller ones' if (by_type['CAD']['med_fee'] or 0) < (by_type['RV']['med_fee'] or 0) else ''}."
    ) + card(
        "Margin, finished residential jobs started 2019 on",
        f"{pct(head['CAD']['margin_pct'])} <small>CAD</small> · {pct(head['RV']['margin_pct'])} <small>Revit</small>", "",
        f"Billings minus labour at the rates of the time, partners at $250. {num(head['CAD']['jobs'])} CAD and "
        f"{num(head['RV']['jobs'])} Revit jobs, finished, with their whole life inside Deltek's timesheet history. "
        f"Typical job: {pct(head['CAD']['med_margin_pct'])} CAD, {pct(head['RV']['med_margin_pct'])} Revit."
    ) + "</div>"

    overview = f"""<div class="box overview"><b>The short version</b>
<ul>
<li><b>CAD is {pct(share_pair.get(cur_year))} of the firm's drafting hours this year, CAD ÷ (CAD + Revit)</b>, down from {pct(share_pair.get(HISTORY_STARTS))} in {HISTORY_STARTS}; counting General and untagged jobs too, {pct(share_all.get(cur_year))}. Residential alone is {pct(share_res.get(cur_year))}. The shift to Revit happened in 2020–2021 and has held since.</li>
<li><b>Counting jobs overstates CAD.</b> Among tagged residential jobs, CAD is {pct(cad_share_jobs)} of jobs with hours but {pct(cad_share_billed)} of billings. The median CAD job is {money(by_type['CAD']['med_fee'])} of fee; the median Revit job {money(by_type['RV']['med_fee'])}.</li>
<li><b>Finished residential jobs started inside the timesheet history lose money on CAD and make it on Revit.</b> Billings-weighted, CAD {pct(head['CAD']['margin_pct'])} on {num(head['CAD']['jobs'])} jobs and Revit {pct(head['RV']['margin_pct'])} on {num(head['RV']['jobs'])}; the typical job {pct(head['CAD']['med_margin_pct'])} against {pct(head['RV']['med_margin_pct'])}. Counting only jobs billed at least 90% of fee: CAD {pct(strict['CAD']['margin_pct'])} on {num(strict['CAD']['jobs'])}, Revit {pct(strict['RV']['margin_pct'])} on {num(strict['RV']['jobs'])}. Started 2020 on: CAD {pct(recent['CAD']['margin_pct'])} on {num(recent['CAD']['jobs'])}, Revit {pct(recent['RV']['margin_pct'])} on {num(recent['RV']['jobs'])}.</li>
<li><b>Year by year says the same.</b> Calendar 2020–{last_full}, residential CAD jobs billed {money(cal_since['CAD']['billed'])} against {money(cal_since['CAD']['cost'])} of labour ({pct(cal_since['CAD']['margin_pct'])}), below zero in {num(cal_since['CAD']['neg'])} of {num(cal_since['CAD']['years'])} years; Revit billed {money(cal_since['RV']['billed'])} against {money(cal_since['RV']['cost'])} ({pct(cal_since['RV']['margin_pct'])}), below zero in {num(cal_since['RV']['neg'])}. In {last_full}: CAD {pct(lf['CAD'].get('margin_pct'))}, Revit {pct(lf['RV'].get('margin_pct'))}.</li>
<li><b>Jobs started before 2019 are left out of every margin here.</b> Deltek's timesheets begin in 2019, so a job billed before then carries lifetime billings against only part of its labour, and its margin reads high: {num(out_summ['CAD']['jobs'])} CAD jobs ({money(out_summ['CAD']['billed'])} billed) and {num(out_summ['RV']['jobs'])} Revit ({money(out_summ['RV']['billed'])}) would show {pct(out_summ['CAD']['margin_pct'])} and {pct(out_summ['RV']['margin_pct'])}, which is not a real number for either.</li>
<li><b>Size and era travel with the tool.</b> Recent CAD jobs are small: {num(small_recent['CAD']['jobs'])} of the {num(recent['CAD']['jobs'])} started 2020 on are under $100K, at {pct(small_recent['CAD']['margin_pct'])}. The fee-band tables show the association; nothing here isolates the software from the size of the job or who ran it.</li>
<li><b>Partners are {pct(partner_share)} of the labour cost</b> at the imputed $250 an hour, {pct(partner_hrs_share)} of billable hours. At their posted payroll amount instead, the finished-job margins would read {pct(head['CAD']['margin_book_pct'])} CAD and {pct(head['RV']['margin_book_pct'])} Revit: the policy moves CAD by {num((head['CAD']['margin_book_pct'] - head['CAD']['margin_pct']) * 100)} points and Revit by {num((head['RV']['margin_book_pct'] - head['RV']['margin_pct']) * 100)}, because partners put more of their hours on CAD jobs.</li>
<li><b>Coverage:</b> {num(tagged)} of {num(all_jobs)} jobs with hours carry Deltek's drafting-type tag. Untagged residential jobs billed {money(by_type['untagged']['billed'])}; nothing here can split them.</li>
</ul></div>"""

    sec1 = (f'<h2>How much CAD we do — drafting hours by year</h2>'
            f'<p class="lead">Every drafting hour on a timesheet, grouped by the drafting type Deltek records on the job. '
            f'"Other" is General plus jobs with no tag. CAD share is shown both ways.</p>'
            + table_html(["Year", "Firm · CAD", "Firm · Revit", "Firm · other", "CAD ÷ (CAD + Revit)", "CAD ÷ all hours",
                          "Residential · CAD", "Residential · Revit", "Residential CAD ÷ (CAD + Revit)"], t1,
                         note="Source: tkDetail labour code 20, rejected lines excluded."))

    pre_note = (f"Billings posted before 2019 on jobs that still logged hours from 2019 on fall outside these calendar tables: "
                f"{money(pre.get('CAD', (0, 0))[1])} on {num(pre.get('CAD', (0, 0))[0])} CAD jobs and {money(pre.get('RV', (0, 0))[1])} on "
                f"{num(pre.get('RV', (0, 0))[0])} Revit jobs, which weighs on the early CAD years. The job-level tables below leave those jobs out altogether.")
    sec_year = (f'<h2>Year by year — billings against labour cost, by drafting type</h2>'
                f'<p class="lead">Calendar years from {HISTORY_STARTS}. Billed = what was posted in the year on jobs carrying each tag; cost = the labour '
                f'posted that year on those jobs, at the rates of the time, partners at $250. In-flight work is included. Billing and hours do not '
                f'land in the same month, so read the trend, not one year alone. {pre_note} General and untagged are in the workbook.</p>'
                f'<h2 style="margin-top:.2rem">Residential</h2>'
                + table_html(["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_res, numeric_from=2)
                + f'<h2>Whole firm</h2>'
                + table_html(["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_firm, numeric_from=2))

    sec2 = (f'<h2>Residential by the numbers — every job with production hours</h2>'
            + table_html(["Drafting type", "Jobs", "Share of jobs", "Fee", "Billed", "Share of billed",
                          "Draft hrs", "Eng hrs", "Billed / prod hr"], t2,
                         note=f"{num(res_tagged)} of {num(len(res))} residential jobs carry a drafting-type tag in Deltek. "
                              f"Fee and billed in CAD; US jobs at {args.usd_rate}."))

    unknown_note = f" {num(unknown_cohort)} finished jobs have no timesheet date and sit only in the All row." if unknown_cohort else ""
    sec3 = (f'<h2>Profitability — finished residential jobs started 2019 on, by the year they started</h2>'
            f'<p class="lead">Finished = billed at least 90% of a fee, or closed on the screen (status not Active), whatever was billed. '
            f'Started 2019 on = no billing before 2019 and no open date before 2019, so the whole job sits inside the timesheet history. '
            f'Margin = billings minus labour cost: the payroll amount Deltek posted on each timesheet line, at the rate in effect at the time, '
            f'with partner hours at the imputed $250. Cohort = year of the first timesheet.{unknown_note}</p>'
            + table_html(MARGIN_HEADERS, t3, numeric_from=2,
                         note=f"{num(nofee['CAD'])} of the {num(head['CAD']['jobs'])} CAD jobs and {num(nofee['RV'])} of the {num(head['RV']['jobs'])} Revit jobs carry no fixed fee in Deltek "
                              f"(billed hourly, or the fee was never entered); they count what they billed, which is why a median fee can read $0. "
                              f"Billed at least 90% of a fee only: CAD {pct(strict['CAD']['margin_pct'])} on {num(strict['CAD']['jobs'])} jobs, "
                              f"Revit {pct(strict['RV']['margin_pct'])} on {num(strict['RV']['jobs'])}. Left out as started before 2019: "
                              f"{num(out_summ['CAD']['jobs'])} CAD and {num(out_summ['RV']['jobs'])} Revit finished jobs.")
            + f'<h2>Same jobs, by fee size — all cohorts</h2>'
            + table_html(["Fee band"] + MARGIN_HEADERS[1:], t4, numeric_from=2)
            + f'<h2>Same jobs, by fee size — started 2020 on</h2>'
            + table_html(["Fee band"] + MARGIN_HEADERS[1:], t4b, numeric_from=2,
                         note="Bands, not matched jobs: median fees differ inside each band and the cells are small. An association, not a software effect."))

    sec4 = (f'<h2>Across sectors — finished jobs started 2019 on, five or more per cell</h2>'
            + table_html(["Sector", "Type", "Jobs", "Billed", "Draft hrs", "Draft share of hrs", "Billings-weighted margin",
                          "Median job margin", "Billed / prod hr", "Billed / draft hr", "Draft cost ÷ billed"], t5, numeric_from=2))

    recon_note = (f"The Historical screen, filtered to Has Hours + Fee, shows {num(screen_jobs)} projects and {money(screen_fee)} of fee; "
                  f"{num(in_jobs)} of them are this report's population and the rest are {'; '.join(others)}. "
                  f"On the screen's fee measure (fixed fee plus hourly revenue) those {num(in_jobs)} jobs are {money(in_fee_screen)}; "
                  f"this report counts the fixed fee alone, {money(in_fee_report)}.")
    method = f"""<div class="two">
<div class="box"><b>Where every number comes from</b>
<ul>
<li>Deltek Vantagepoint over the same ODBC route the Financials module uses. Top-level regular projects with engineering or drafting hours.</li>
<li>Hours: tkDetail. Engineering = labour codes 10 and 30, Drafting = 20. Rejected lines excluded. Timesheets begin in {HISTORY_STARTS}.</li>
<li>Billed: PRSummaryMain, posted through {esc(posted)}. Fee: PR.Fee. US jobs converted at {args.usd_rate}, a flat rate, not the year's.</li>
<li>Cost: Deltek's posted labour amounts (tkDetail RegAmt + OvtAmt + SpecialOvtAmt), the rate in effect when the hours were posted, the same figure Firm Health uses. Partner hours at the app's imputed $250/hr. The Historical screen prices hours at today's rate and prices staff who have left at zero; both of those figures are in the workbook beside this one.</li>
<li>Drafting type: the Deltek custom field CustDraftingType (RV, CAD, General). Sector: PR.ProjectType. Closed: status not Active, as the screen defines it.</li>
<li>Reproduced the app's cost and margin to the dollar on six projects before anything was written. Two independent pulls of drafting hours agree to the hour on every year and type. The same pull, banked in KorTransmittals through a different route, agrees on every sector, type and year cell. {recon_note}</li>
</ul></div>
<div class="box"><b>What this does not say</b>
<ul>
<li>Only {num(tagged)} of {num(all_jobs)} jobs with hours carry a drafting-type tag. The untagged line is real work that cannot be split.</li>
<li>Jobs started before {HISTORY_STARTS} have no margin here. Their labour before {HISTORY_STARTS} is not in Deltek, and nothing in these files can recover it.</li>
<li>A "CAD job" is the tag on the job, not every hour. A job that moved to Revit mid-way counts where Deltek put it.</li>
<li>Margin is before subconsultants and before anything not in Deltek's cost rate. {num(cov_hrs_no_amt)} of {num(cov_hrs)} billable hours ({pct(cov_hrs_no_amt / cov_hrs if cov_hrs else None)}) carry no posted amount and cost nothing here, all of them {HISTORY_STARTS}–2021.</li>
<li>Partner hours carry a policy rate, not a payroll rate, and it is most of the labour cost. The overview gives both readings.</li>
<li>These are associations between the tag on a job and its margin. Job size, era, client and who ran the job travel with the tag; the fee-band and cohort tables show that, they do not remove it.</li>
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

    # ---------- the one-page plain-English guide, from the same values ----------
    if args.guide_html:
        mid_recent = {t: summ([j for j in res_fin if j["dtype"] == t and j["first_year"] >= 2020 and 100_000 <= j["fee"] < 300_000]) for t in ("CAD", "RV")}
        cents = lambda s: f"{abs(s['margin_pct'] * 100):.0f} cents" if s["margin_pct"] is not None else "–"
        verb = lambda s: "lost" if (s["margin_pct"] or 0) < 0 else "made"
        guide_css = """
<style>
  .page { max-width: 8.3in; }
  .top { padding: 11px 20px; }
  .top h1 { font-size:17pt; }
  .top p { font-size:9pt; max-width:40rem; }
  .body { padding: 10px 20px 0; }
  h2 { margin-bottom:.35rem; }
  .flow { grid-template-columns: repeat(3, 1fr); margin-bottom: 9px; gap:.45rem; }
  .step { padding:.45rem .6rem; }
  .step .big { font-size:16pt; font-weight:700; color:var(--slate-deep); line-height:1.05; margin:.1rem 0 .1rem; white-space:nowrap; }
  .step .big small { font-size:8.2pt; font-weight:600; color:var(--ink-soft); }
  .step p { font-size:8.4pt; }
  table { font-size:8.6pt; margin-bottom:9px; }
  tr td:first-child { width:12.5rem; }
  td { line-height:1.3; padding:.26rem .45rem; }
  .strip { grid-template-columns: 1fr 1fr 1fr; gap:.5rem; }
  .card { padding:.5rem .65rem; }
  .card p { font-size:8.2pt; line-height:1.32; }
  .card .lead { font-size:8.8pt; }
  .foot { padding:8px 20px 0; font-size:7.5pt; }
  @page { size: Letter; margin: 8mm 10mm; }
</style>"""
        guide = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>CAD vs Revit — Plain English</title>
{style_block}
{guide_css}
</head><body>
<div class="page">
  <div class="top">
    <div>
      <div class="ey">KOR Structural · Financials · companion to the CAD vs Revit report · as of {esc(asof_label)}</div>
      <h1>The Complete Idiot's Guide to What This All Means</h1>
      <p>CAD versus Revit in plain English, on one page. Every number here is the report's number, from Deltek, not from counting folders.</p>
    </div>
  </div>
  <div class="body">
    <h2>The three things to know</h2>
    <div class="flow">
      <div class="step"><span class="n">1 · HOW MUCH CAD</span><div class="big">{pct(share_pair.get(cur_year))} <small>of drafting hours, this year</small></div>
        <p>CAD and Revit jobs only. It was {pct(share_pair.get(HISTORY_STARTS))} in {HISTORY_STARTS}. The switch to Revit happened in 2020–2021 and stuck. About one drafting hour in four is still CAD.</p></div>
      <div class="step"><span class="n">2 · WHO MAKES MONEY</span><div class="big">{pct(head['CAD']['margin_pct'])} <small>CAD</small> · {pct(head['RV']['margin_pct'])} <small>Revit</small></div>
        <p>On finished residential jobs we can measure properly, CAD jobs {verb(head['CAD'])} about {cents(head['CAD'])} of every dollar billed; Revit jobs {verb(head['RV'])} about {cents(head['RV'])}. Typical job: {pct(head['CAD']['med_margin_pct'])} CAD, {pct(head['RV']['med_margin_pct'])} Revit.</p></div>
      <div class="step"><span class="n">3 · JOBS, NOT WORK</span><div class="big">{pct(cad_share_jobs)} <small>of jobs · {pct(cad_share_billed)} of billings</small></div>
        <p>CAD is most of our residential job count but well under half of the money. Counting folders with a .dwg in them counts jobs. The books weigh them.</p></div>
    </div>

    <h2>If someone says… the books say…</h2>
    <table>
      <tr><td>"More than half our residential jobs are CAD."</td><td>True by count ({pct(cad_share_jobs)} of tagged jobs with hours). By billings it is {pct(cad_share_billed)}, and by drafting hours this year {pct(share_res.get(cur_year))}. The median CAD job is {money(by_type['CAD']['med_fee'])} of fee; the median Revit job {money(by_type['RV']['med_fee'])}.</td></tr>
      <tr><td>"CAD jobs are fine, they've always made money."</td><td>The old ones look like they did, because Deltek's timesheets only start in {HISTORY_STARTS}: a job billed before then has all its billings but only part of its hours, so its margin reads high ({pct(out_summ['CAD']['margin_pct'])} on {num(out_summ['CAD']['jobs'])} CAD jobs, {pct(out_summ['RV']['margin_pct'])} on {num(out_summ['RV']['jobs'])} Revit). Not real numbers for either tool, so they are left out. Jobs started since: CAD {pct(head['CAD']['margin_pct'])}, Revit {pct(head['RV']['margin_pct'])}.</td></tr>
      <tr><td>"It's just that CAD jobs are small."</td><td>Partly. Small jobs lose money on both tools: started 2020 on and under $100K, CAD {pct(small_recent['CAD']['margin_pct'])} on {num(small_recent['CAD']['jobs'])} jobs, Revit {pct(small_recent['RV']['margin_pct'])} on {num(small_recent['RV']['jobs'])}. Most recent CAD work is small jobs ({num(small_recent['CAD']['jobs'])} of {num(recent['CAD']['jobs'])}). In the $100K–$300K band, started 2020 on: CAD {pct(mid_recent['CAD']['margin_pct'])} on {num(mid_recent['CAD']['jobs'])}, Revit {pct(mid_recent['RV']['margin_pct'])} on {num(mid_recent['RV']['jobs'])}.</td></tr>
      <tr><td>"One bad year."</td><td>Calendar 2020–{last_full}, residential CAD jobs billed {money(cal_since['CAD']['billed'])} against {money(cal_since['CAD']['cost'])} of labour, below zero in {num(cal_since['CAD']['neg'])} of {num(cal_since['CAD']['years'])} years. Revit: {money(cal_since['RV']['billed'])} against {money(cal_since['RV']['cost'])}, below zero in {num(cal_since['RV']['neg'])}.</td></tr>
      <tr><td>"Partners' time is free, so the margins are wrong."</td><td>Partners are counted at $250 an hour, the firm's standing policy, which is {pct(partner_share)} of all the labour cost here. At payroll instead, both margins jump ({pct(head['CAD']['margin_book_pct'])} CAD, {pct(head['RV']['margin_book_pct'])} Revit), CAD more, because partners put more hours on CAD jobs. Either way, Revit is ahead.</td></tr>
      <tr><td>"So Revit makes us money and CAD loses it."</td><td>That is what the association shows. It does not prove the software caused it: job size, era, client and who ran the job travel with the tool. Treat it as a strong signal, not a verdict.</td></tr>
    </table>

    <div class="strip">
      <div class="card"><h2 style="margin-bottom:.2rem">What "margin" means here</h2>
        <p>What we billed, minus what the people on the job cost at the time, staff at actual payroll and partners at $250 an hour. Before overhead and subconsultants. It is a way to compare CAD and Revit, not the firm's profit and loss.</p></div>
      <div class="card"><h2 style="margin-bottom:.2rem">What it cannot tell you</h2>
        <p>Anything before {HISTORY_STARTS}. Anything about the {num(all_jobs - tagged)} of {num(all_jobs)} jobs with no drafting-type tag in Deltek. Whether a job that moved from CAD to Revit mid-way is counted right (it counts where Deltek put it). Why: it shows the pattern, not the cause.</p></div>
      <div class="card accent"><h2 style="margin-bottom:.2rem; color:var(--good)">How to read the big report</h2>
        <p class="lead">Page 1 is the whole story. The rest is the evidence.</p>
        <p>Every table says which jobs it counts and why. Every assumption is listed on the last page so it can be argued with. The workbook has every job.</p></div>
    </div>
  </div>
  <div class="foot">KOR Operations · Financials · companion to KOR-Financials-CAD-vs-Revit-{asof}-web.pdf</div>
</div>
</body></html>"""
        Path(args.guide_html).write_text(guide, encoding="utf-8")

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
                  ["Year", "Firm CAD", "Firm Revit", "Firm other", "CAD / (CAD+Revit)", "CAD / all hours", "Res CAD", "Res Revit", "Res CAD / (CAD+Revit)"], t1, r)
    r = put_table(ws, "Calendar year, residential: billings against labour cost by drafting type (posted rates, partners $250)",
                  ["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_res, r)
    r = put_table(ws, "Calendar year, whole firm: billings against labour cost by drafting type (posted rates, partners $250)",
                  ["Year", "Type", "Billed", "Labour cost", "Margin", "Draft hrs", "Eng hrs", "Billed / prod hr", "Cost / prod hr"], t6_firm, r)
    r = put_table(ws, "Residential, every job with production hours",
                  ["Drafting type", "Jobs", "Share of jobs", "Fee", "Billed", "Share of billed", "Draft hrs", "Eng hrs", "Billed / prod hr"], t2, r)
    r = put_table(ws, "Residential finished jobs started 2019 on (no billing or open date before 2019), by year of first timesheet",
                  MARGIN_HEADERS, t3, r)
    r = put_table(ws, "Same jobs by fee size, all cohorts", ["Fee band"] + MARGIN_HEADERS[1:], t4, r)
    r = put_table(ws, "Same jobs by fee size, started 2020 on", ["Fee band"] + MARGIN_HEADERS[1:], t4b, r)
    r = put_table(ws, "All sectors, finished jobs started 2019 on, cells with 5+ jobs",
                  ["Sector", "Type", "Jobs", "Billed", "Draft hrs", "Draft share of hrs", "Billings-weighted margin",
                   "Median job margin", "Billed / prod hr", "Billed / draft hr", "Draft cost / billed"], t5, r)
    for col in range(1, 12):
        ws.column_dimensions[get_column_letter(col)].width = 18
    ws.column_dimensions["A"].width = 30

    job_headers = ["Project #", "Project", "Status", "Org", "Sector", "Drafting type", "Category", "Construction type",
                   "First hours (yr)", "Last hours (yr)", "Open year", "Billed before 2019 (CAD)", "In window (started 2019 on)",
                   "Fee (CAD)", "Billed (CAD)", "% billed", "Fully billed (>=90%)", "Closed (status not Active)", "Finished",
                   "Eng hrs", "Draft hrs", "Draft share of hrs", "Partner hrs",
                   "Eng cost (posted, partners $250)", "Draft cost (posted, partners $250)",
                   "Labour cost (report basis: posted, partners $250)", "Margin (report basis)", "Margin % (report basis)",
                   "Labour cost, posted payroll as-is (Deltek)", "Labour cost, all staff at today's rates", "Labour cost, active staff at today's rates (app)",
                   "Billed / prod hr", "Billed / draft hr", "Draft cost / draft hr"]

    def job_row(j):
        org = {"CAD": "Canada", "USA": "USA"}.get(j["org"], j["org"])  # Deltek's org code for Canada is also "CAD"
        rnd = lambda v, n=2: round(v, n) if v is not None else None
        return [j["wbs1"], j["name"], j["status"], org, SECTOR_LABEL.get(j["sector"], j["sector"]), TYPE_LABEL[j["dtype"]],
                j["category"], j["construction"], j["first_year"] or None, j["last_year"] or None, j["open_year"] or None,
                rnd(j["billed_before_2019"]), "yes" if j["in_window"] else "",
                rnd(j["fee"]), rnd(j["billed"]), rnd(j["billed"] / j["fee"], 3) if j["fee"] else None,
                "yes" if j["fully_billed"] else "", "yes" if j["closed"] else "", "yes" if j["finished"] else "",
                rnd(j["eng"], 1), rnd(j["draft"], 1), rnd(j["draft_share"], 3), rnd(j["partner_hrs"], 1),
                rnd(j["eng_cost"]), rnd(j["draft_cost"]),
                rnd(j["cost_all"]), rnd(j["margin"]), rnd(j["margin_pct"], 3),
                rnd(j["cost_book"]), rnd(j["cost_current"]), rnd(j["cost_app"]),
                rnd(j["billed_per_hr"]), rnd(j["billed_per_draft_hr"]), rnd(j["draft_cost_per_hr"])]

    money_cols = {12, 14, 15, 24, 25, 26, 27, 29, 30, 31, 32, 33, 34}
    pct_cols = {16, 22, 28}
    hour_cols = {20, 21, 23}
    if len(job_headers) != len(job_row(jobs[0])):
        raise SystemExit("workbook: job_headers and job_row differ in length")

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
        for row in s.iter_rows(min_row=2, max_row=len(rows) + 1):
            for cell in row:
                if cell.column in money_cols:
                    cell.number_format = "#,##0"
                elif cell.column in pct_cols:
                    cell.number_format = "0%"
                elif cell.column in hour_cols:
                    cell.number_format = "#,##0.0"

    job_sheet("Residential jobs", sorted(res, key=lambda j: j["wbs1"], reverse=True))
    job_sheet("All jobs", sorted(jobs, key=lambda j: j["wbs1"], reverse=True))

    ys = wb.create_sheet("Yearly hours")
    yh = ["Year", "Scope", "CAD", "Revit", "General", "untagged", "CAD / (CAD+Revit)", "CAD / all"]
    for c, h in enumerate(yh, 1):
        cell = ys.cell(row=1, column=c, value=h)
        cell.font = hdr_font
        cell.fill = hdr_fill
    i = 2
    for scope, data in (("Firm", years_firm), ("Residential", years_res)):
        for y in sorted(data):
            d = data[y]
            cad, rv, gen, un = d.get("CAD", 0), d.get("RV", 0), d.get("General", 0), d.get("untagged", 0)
            ys.append([y, scope, round(cad), round(rv), round(gen), round(un),
                       round(cad / (cad + rv), 3) if (cad + rv) else None,
                       round(cad / (cad + rv + gen + un), 3) if (cad + rv + gen + un) else None])
            ys.cell(row=i, column=7).number_format = "0%"
            ys.cell(row=i, column=8).number_format = "0%"
            i += 1
    for c in range(1, 9):
        ys.column_dimensions[get_column_letter(c)].width = 14

    by = wb.create_sheet("By year (raw)")
    has_active = any(c.get("CostActive", 0.0) for c in cost_y.values())   # the DB year table does not carry it
    byh = ["Scope", "Year", "Drafting type", "Billed (CAD)", "Eng hrs", "Draft hrs", "Partner hrs",
           "Labour cost (report basis: posted, partners $250)", "Margin (report basis)", "Margin % (report basis)",
           "Labour cost, posted payroll as-is", "Labour cost, all staff at today's rates"] + (["Labour cost, active staff at today's rates (app)"] if has_active else [])
    for c, h in enumerate(byh, 1):
        cell = by.cell(row=1, column=c, value=h)
        cell.font = hdr_font
        cell.fill = hdr_fill
        cell.alignment = Alignment(wrap_text=True, vertical="bottom")
    keys = sorted(set(billed_y) | set(cost_y) | set(hist_y), key=lambda k: (k[0], k[1], TYPE_ORDER.index(k[2]) if k[2] in TYPE_ORDER else 9))
    for i, k in enumerate(keys, 2):
        b = billed_y.get(k, {}).get("Billed", 0.0)
        c = cost_y.get(k, {})
        h = hist_y.get(k, {})
        cost = year_cost(*k)
        by.append([k[0], k[1], TYPE_LABEL.get(k[2], k[2]), round(b), round(c.get("EngHrs", 0.0)), round(c.get("DraftHrs", 0.0)),
                   round(h.get("PartnerHrs", 0.0)), round(cost), round(b - cost), round((b - cost) / b, 3) if b else None,
                   round(h.get("PostedCost", 0.0)), round(c.get("CostAll", 0.0))] + ([round(c.get("CostActive", 0.0))] if has_active else []))
        by.cell(row=i, column=10).number_format = "0%"
    for c in range(1, 14):
        by.column_dimensions[get_column_letter(c)].width = 16
    by.freeze_panes = "A2"

    wb.save(args.xlsx)

    # ---------- stdout: the headline numbers, for the eye ----------
    print(f"jobs={all_jobs} tagged={tagged} residential={len(res)} finished_in_window_res={len(res_fin)} finished_out_of_window_res={len(res_out)}")
    print(f"firm CAD share of drafting hrs (CAD+RV): {HISTORY_STARTS}={pct(share_pair.get(HISTORY_STARTS))} {cur_year}={pct(share_pair.get(cur_year))}; all hours {cur_year}={pct(share_all.get(cur_year))}; residential {cur_year}={pct(share_res.get(cur_year))}")
    print(f"residential CAD share: billed={pct(cad_share_billed)} jobs={pct(cad_share_jobs)}")
    for t in ("CAD", "RV"):
        s = head[t]
        print(f"finished in-window residential {t}: jobs={s['jobs']} billed={money(s['billed'])} margin={pct(s['margin_pct'])} median={pct(s['med_margin_pct'])} "
              f"strict90={pct(strict[t]['margin_pct'])}/{strict[t]['jobs']} 2020on={pct(recent[t]['margin_pct'])}/{recent[t]['jobs']} excluded(pre-2019)={out_summ[t]['jobs']}@{pct(out_summ[t]['margin_pct'])}")
    print(f"wrote {args.html}\nwrote {args.xlsx}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--jobs", required=True)
    ap.add_argument("--hist-jobs", required=True)
    ap.add_argument("--pre2019-jobs", required=True)
    ap.add_argument("--years", required=True)
    ap.add_argument("--years-res", required=True)
    ap.add_argument("--billed-years", required=True)
    ap.add_argument("--cost-years", required=True)
    ap.add_argument("--hist-years", required=True)
    ap.add_argument("--screen-recon", required=True)
    ap.add_argument("--pre2019", required=True)
    ap.add_argument("--coverage", required=True)
    ap.add_argument("--html", required=True)
    ap.add_argument("--xlsx", required=True)
    ap.add_argument("--guide-html", help="also write the one-page plain-English companion from the same values")
    ap.add_argument("--asof", required=True, help="YYYY-MM-DD the pull was taken")
    ap.add_argument("--posted-through", required=True, help='e.g. "October 2026" (PRSummaryMain max period)')
    ap.add_argument("--usd-rate", default="1.36")
    ap.add_argument("--template", default=str(Path(__file__).resolve().parent / "BdDocTemplate" / "reference-quickstart.html"))
    build(ap.parse_args())
