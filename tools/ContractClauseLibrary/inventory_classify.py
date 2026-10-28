"""Classify the FS01 contract-file inventory (kor-contracts-enum.csv) and pick what to pull local.

Input : CSV with FullName, Length, LastWriteTime, CreationTime (written ON FS01 by fs01-contracts-enum.ps1)
Output: inventory.csv  (every RELEVANT row + job, kind labels, year, area)
        jobs.csv       (one row per job: counts by kind, trail flag, newest date, client from Deltek)
        pull-list.txt  (server paths to zip)
        summary.txt    (the counts, with the rule that produced them)
        dropped.csv    (what the gate excluded, with the reason — so the exclusions can be checked)

Gate (set from the 10-28 profile of 13,371 rows): a file is contract-relevant when it is a document
(pdf/docx/doc/msg/eml), 25 MB or smaller, and EITHER sits in a contracts folder (a path segment named
'Proposals & Contracts', 'Contracts', 'Agreements', 'Contract Documents' or 'Legal', directly or in a
sub-folder whose own name carries a contract token) OR its file name carries a contract token.
'markup', 'signed' and 'csa' on their own are NOT tokens: the profile showed 6,186 'markup' names that
are engineering markups of general notes, and 'csa' is mostly CSA standards.
Run with python -I. Paths come in as arguments; nothing here walks the share.
"""
import csv, re, sys, os
from collections import defaultdict, Counter
from datetime import datetime

src, outdir = sys.argv[1], sys.argv[2]
deltek_csv = sys.argv[3] if len(sys.argv) > 3 else None
CUTOFF = datetime(2024, 10, 1)
MAX_BYTES = 25_000_000
EMAIL_MAX_BYTES = 8_000_000
UNNAMED_DOC_MAX_BYTES = 12_000_000
os.makedirs(outdir, exist_ok=True)

JOB = re.compile(r"(?<!\d)(\d{5}-\d{2})(?!\d)")
DOC_EXT = {".pdf", ".docx", ".doc", ".msg", ".eml"}
CONTRACT_FOLDER = re.compile(r"\\(proposals?\s*(&|and)\s*contracts?|contracts?|agreements?|contract documents|legal)\\", re.I)
CONTRACT_FOLDER_LEAF = re.compile(r"^(proposals?\s*(&|and)\s*contracts?|contracts?|agreements?|contract documents|legal)$", re.I)
NAME_TOKEN = re.compile(
    r"agreement|contract|\bpsa\b|\bpsc\b|service agreement|consulting services|professional services|"
    r"terms (and|&) conditions|\bt&c\b|engagement|letter of intent|\bloi\b|purchase order|\bp\.?o\.?\s*\d|"
    r"sub-?consultant|subcontract|master services|\bmsa\b|work order|fee proposal|proposal|\bquote\b|quotation|"
    r"certificate of insurance|\bcoi\b|insurance cert", re.I)
CSA_STD = re.compile(r"\bcsa\b\s*[a-z]?\s*\d", re.I)          # CSA A23.3, CSA S16 → a standard, not an agreement
CSA_AGR = re.compile(r"\bcsa\b", re.I)
KINDS = {
    "insurance": re.compile(r"certificate of insurance|\bcoi\b|insurance cert|liability cert", re.I),
    "executed":  re.compile(r"\b(executed|fully[- ]executed|signed|countersigned|final signed|execution copy)\b", re.I),
    "markup":    re.compile(r"\b(mark[- ]?ups?|redline|red[- ]line|comments|cmp|compare|comparison|tracked|track changes|kor rev|clean|kor edits|kor comments)\b", re.I),   # 'v12'/'rev 2' are versions, not markups (10-28 look)
    "agreement": re.compile(r"agreement|contract|\bpsa\b|\bpsc\b|terms (and|&) conditions|\bt&c\b|engagement|letter of intent|\bloi\b|purchase order|\bp\.?o\.?\s*\d|sub-?consultant|subcontract|\bmsa\b|work order", re.I),
    "proposal":  re.compile(r"fee proposal|proposal|\bquote\b|quotation|\brfp\b|\brfq\b|\brfpq\b|\beoi\b|tender|\bbid\b|fee letter", re.I),
}
TEMPLATES = re.compile(r"\\00 Templates\\", re.I)
PROP_OPP = re.compile(r"\\Project Opportunities\\", re.I)
NOISE_LEAF = re.compile(r"^(IMG_|DSC_|S\d{2}\b|A\d{3}\b)", re.I)

def parse_dt(s):
    s = (s or "").strip()
    for fmt in ("%m/%d/%Y %I:%M:%S %p", "%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S", "%d/%m/%Y %I:%M:%S %p"):
        try:
            return datetime.strptime(s[:22], fmt)
        except Exception:
            continue
    return None

deltek = {}
if deltek_csv and os.path.exists(deltek_csv):
    for r in csv.DictReader(open(deltek_csv, encoding="utf-8-sig", newline="")):
        deltek[r["WBS1"].strip()] = {"ClientName": r.get("ClientName", ""), "ProjectName": r.get("Name", ""),
                                     "Stage": r.get("Stage", ""), "CreateDate": r.get("CreateDate", ""), "Fee": r.get("Fee", "")}

rows, dropped = [], []
with open(src, encoding="utf-8-sig", newline="") as f:
    for r in csv.DictReader(f):
        p = r["FullName"]
        name = os.path.basename(p)
        folder = os.path.dirname(p)
        ext = os.path.splitext(name)[1].lower()
        size = int(r["Length"]) if str(r["Length"]).isdigit() else 0
        reason = ""
        if ext not in DOC_EXT:
            reason = "not a document type"
        elif size > MAX_BYTES:
            reason = "over 25 MB"
        else:
            in_folder = bool(CONTRACT_FOLDER.search(folder + "\\"))
            leaf_ok = bool(CONTRACT_FOLDER_LEAF.match(os.path.basename(folder))) or bool(NAME_TOKEN.search(os.path.basename(folder)))
            tok = bool(NAME_TOKEN.search(name)) or (bool(CSA_AGR.search(name)) and not CSA_STD.search(name))
            if not (tok or (in_folder and leaf_ok) or (in_folder and ext in (".msg", ".eml"))):
                reason = "no contract token and not in a contracts folder" if not in_folder else "contracts folder but a drawing/photo sub-folder"
            elif NOISE_LEAF.match(name) and not tok:
                reason = "photo/sheet name"
        if not reason and ext in (".msg", ".eml") and size > EMAIL_MAX_BYTES:
            reason = "email over 8 MB (drawings attached, not a contract)"
        if not reason and ext in (".pdf", ".docx", ".doc") and size > UNNAMED_DOC_MAX_BYTES and not KINDS["agreement"].search(name):
            reason = "document over 12 MB without an agreement token (renderings/proposal graphics)"
        if reason:
            dropped.append({"FullName": p, "Reason": reason, "Length": size})
            continue
        m = JOB.search(p)
        job = m.group(1) if m else ""
        kinds = [k for k, rx in KINDS.items() if rx.search(name)]
        if ext in (".msg", ".eml"):
            kinds.append("email")
        if not any(k in kinds for k in ("agreement", "proposal", "insurance", "email")):
            kinds.append("agreement?")   # in a contracts folder, name says nothing: read it to find out
        dt = parse_dt(r["LastWriteTime"])
        area = "templates" if TEMPLATES.search(p) else ("opportunities" if PROP_OPP.search(p) else "projects")
        d = deltek.get(job, {})
        rows.append({
            "FullName": p, "Name": name, "Folder": folder, "Job": job, "ClientName": d.get("ClientName", ""),
            "ProjectName": d.get("ProjectName", ""), "Kinds": "|".join(kinds), "Ext": ext, "Length": size,
            "LastWrite": dt.isoformat() if dt else r["LastWriteTime"], "Year": dt.year if dt else "", "Area": area,
            "_dt": dt,
        })

fields = [k for k in rows[0].keys() if k != "_dt"]
with open(os.path.join(outdir, "inventory.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=fields); w.writeheader()
    for r in rows: w.writerow({k: v for k, v in r.items() if k != "_dt"})
with open(os.path.join(outdir, "dropped.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["FullName", "Reason", "Length"]); w.writeheader(); w.writerows(dropped)

def key_of(r):
    if r["Job"]: return r["Job"]
    if r["Area"] == "opportunities":
        # pursuit folder = first segment after 'Project Opportunities\<Stage>\'
        m = re.search(r"\\Project Opportunities\\[^\\]+\\([^\\]+)", r["FullName"], re.I)
        return "OPP:" + (m.group(1) if m else os.path.basename(r["Folder"]))
    if r["Area"] == "templates": return "TEMPLATES"
    return ""

jobs = defaultdict(lambda: {"files": [], "kinds": Counter(), "newest": None, "folders": set(), "client": "", "pname": ""})
for r in rows:
    key = key_of(r)
    if not key: continue
    j = jobs[key]; j["files"].append(r)
    for k in r["Kinds"].split("|"):
        if k: j["kinds"][k] += 1
    if r["_dt"] and (j["newest"] is None or r["_dt"] > j["newest"]): j["newest"] = r["_dt"]
    j["folders"].add(r["Folder"]); j["client"] = j["client"] or r["ClientName"]; j["pname"] = j["pname"] or r["ProjectName"]

job_rows = []
for key, j in sorted(jobs.items()):
    k = j["kinds"]
    agreement_like = k.get("agreement", 0) > 0 or k.get("agreement?", 0) > 0
    trail = agreement_like and (k.get("markup", 0) > 0 or k.get("executed", 0) > 0 or k.get("email", 0) > 0)
    job_rows.append({
        "Job": key, "ClientName": j["client"], "ProjectName": j["pname"], "Files": len(j["files"]),
        "Agreement": k.get("agreement", 0), "AgreementDir": k.get("agreement?", 0), "Markup": k.get("markup", 0),
        "Executed": k.get("executed", 0), "Email": k.get("email", 0), "Proposal": k.get("proposal", 0),
        "Insurance": k.get("insurance", 0), "Trail": "yes" if trail else "",
        "Newest": j["newest"].date().isoformat() if j["newest"] else "",
        "Recent": "yes" if (j["newest"] and j["newest"] >= CUTOFF) else "",
        "Bytes": sum(int(x["Length"]) for x in j["files"]),
        "ExampleFolder": sorted(j["folders"])[0] if j["folders"] else "",
    })
with open(os.path.join(outdir, "jobs.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(job_rows[0].keys())); w.writeheader(); w.writerows(job_rows)

trail_jobs = {r["Job"] for r in job_rows if r["Trail"] == "yes"}
pull = []
for r in rows:
    kinds = set(r["Kinds"].split("|")) - {""}
    key = key_of(r)
    in_trail = key in trail_jobs
    recent_agreement = bool(kinds & {"agreement", "agreement?", "executed", "markup"}) and r["_dt"] is not None and r["_dt"] >= CUTOFF
    if in_trail or recent_agreement or r["Area"] == "templates":
        pull.append(r["FullName"])
pull = sorted(set(pull))
pullset = set(pull)
with open(os.path.join(outdir, "pull-list.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(pull))
total_bytes = sum(int(r["Length"]) for r in rows if r["FullName"] in pullset)

lines = [
    f"enumerated rows: {len(rows) + len(dropped)}; relevant after gate: {len(rows)}; dropped: {len(dropped)}",
    "  dropped by reason: " + ", ".join(f"{k}={v}" for k, v in Counter(d['Reason'] for d in dropped).most_common()),
    f"  relevant by area: " + ", ".join(f"{k}={v}" for k, v in Counter(r['Area'] for r in rows).most_common()),
    f"  relevant by ext: " + ", ".join(f"{k}={v}" for k, v in Counter(r['Ext'] for r in rows).most_common()),
    f"  with a job number: {sum(1 for r in rows if r['Job'])}; job matched in Deltek: {sum(1 for r in rows if r['ClientName'])}",
    "kind counts (a file can carry several): " + ", ".join(f"{k}={v}" for k, v in Counter(k for r in rows for k in r['Kinds'].split('|') if k).most_common()),
    f"jobs/pursuits keyed: {len(job_rows)}",
    f"  with an agreement-class file: {sum(1 for r in job_rows if r['Agreement'] or r['AgreementDir'])}",
    f"  with a negotiation trail (agreement + markup/executed/email): {len(trail_jobs)}",
    f"  trail AND newest file >= {CUTOFF.date()}: {sum(1 for r in job_rows if r['Trail']=='yes' and r['Recent']=='yes')}",
    f"  relevant files by year: " + ", ".join(f"{k}={v}" for k, v in sorted(Counter(str(r['Year']) for r in rows).items())),
    f"pull list: {len(pull)} files, {total_bytes/1e6:.0f} MB",
    f"rule: relevant = doc type, <=25MB, (contract token in name OR in a contracts folder); trail = agreement-class AND (markup|executed|email); pull = all relevant files of trail jobs + relevant agreement/markup/executed files >= {CUTOFF.date()} + templates",
]
with open(os.path.join(outdir, "summary.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print("\n".join(lines))
