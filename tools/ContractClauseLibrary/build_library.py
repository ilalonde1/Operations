"""Build the clause library from the extraction output.

Usage: python -I build_library.py <inv-dir> <ext-dir> <out-dir>
  inv-dir : inventory.csv, jobs.csv (from inventory_classify.py)
  ext-dir : documents.csv, clauses.csv (from extract_clauses.py)
Outputs (out-dir):
  library_clauses.csv  one row per operative clause: job, client, project, paper (KOR/client), role
                       (executed/markup/draft/proposal/email-attachment), date, file, section, heading,
                       topics, words, text
  job_topic_matrix.csv one row per job: which topics have text, by role (E=executed, M=markup, D=draft,
                       P=KOR proposal)
  positions.csv        per topic: jobs with an executed clause, with a markup, with a draft only
  summary.txt          the counts and the rules that produced them
Roles come from the file name (inventory Kinds) plus text signals for execution (DocuSign, "signed by",
"/s/", "executed on"). This is bookkeeping, not legal judgement: every row carries its source path.
"""
import csv, os, re, sys
from collections import Counter, defaultdict

inv_dir, ext_dir, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out_dir, exist_ok=True)
csv.field_size_limit(10_000_000)

def norm(p):
    p = p.replace("\\", "/")
    p = re.sub(r"^[A-Za-z]:/", "", p)
    return p.lower()

inventory = {}
for r in csv.DictReader(open(os.path.join(inv_dir, "inventory.csv"), encoding="utf-8", newline="")):
    inventory[norm(r["FullName"])] = r

docs = list(csv.DictReader(open(os.path.join(ext_dir, "documents.csv"), encoding="utf-8", newline="")))
clauses = list(csv.DictReader(open(os.path.join(ext_dir, "clauses.csv"), encoding="utf-8", newline="")))

# Only signals that do not appear in unsigned boilerplate. "IN WITNESS WHEREOF" and "executed as of the
# date first above written" are in every draft, so they prove nothing (10-28 test: both Starlight versions
# and the Matthews draft were promoted to 'executed' by them).
EXEC_TEXT = re.compile(r"docusign envelope id|docusigned by|electronically signed by|adobe sign|signed via", re.I)
KIND_RX = {
    "executed": re.compile(r"\b(executed|fully[- ]executed|signed|countersigned|final signed|execution copy)\b", re.I),
    "markup": re.compile(r"\b(mark[- ]?ups?|redline|red[- ]line|comments|cmp|compare|comparison|tracked|track changes|kor rev|clean|kor edits|kor comments)\b", re.I),
    "proposal": re.compile(r"fee proposal|proposal|\bquote\b|quotation|fee letter", re.I),
    "agreement": re.compile(r"agreement|contract|\bpsa\b|\bpsc\b|terms (and|&) conditions|engagement|letter of intent|\bloi\b|purchase order|sub-?consultant|subcontract|\bmsa\b|work order", re.I),
}

def text_of(doc):
    try:
        return open(os.path.join(ext_dir, "text", doc["DocId"] + ".txt"), encoding="utf-8", errors="replace").read()
    except OSError:
        return ""

doc_meta = {}
for d in docs:
    inv = inventory.get(norm(d["RelPath"]), {})
    kinds = set((inv.get("Kinds") or "").split("|")) - {""}
    label = d["Part"] or ""
    if label and label != "body":            # an email attachment: judge it by its own name
        kinds = {k for k, rx in KIND_RX.items() if rx.search(label)}
        if not kinds: kinds = {"agreement?"}
    txt = text_of(d) if d["Kind"] in ("pdf", "docx") else ""
    head = txt[:2500]
    exec_signal = bool(EXEC_TEXT.search(txt)) if txt else False
    topics = set((d.get("Topics") or "").split("|")) - {""}
    # Is this document actually a contract-shaped text? The folder gate let drawings, forms, portfolios
    # and submittals through (10-28 look: 'FLOOR PLAN.pdf', 'Seismic Form B.pdf' were 'latest on file').
    contract_topics = topics & {"indemnity", "insurance", "termination", "limitation_of_liability", "intellectual_property",
                                "payment", "dispute", "standard_of_care", "confidentiality", "consequential_damages", "suspension"}
    words = int(d["Words"] or 0)
    agreement_text = words >= 500 and len(contract_topics) >= 3 and bool(re.search(
        r"\bagreement\b|terms (and|&) conditions|purchase order|work order|conditions of engagement|general conditions", head, re.I))
    # KOR traded as Bryson Markulin Zickmantel (BMZ) before the rename; pre-2020 paper names BMZ.
    names_kor = bool(re.search(r"kor structural|bryson markulin|\bBMZ\b", txt[:8000], re.I))
    kor_paper_text = bool(re.search(r"kor structural|bryson markulin|\bBMZ\b", head, re.I)) and bool(re.search(r"fee proposal|proposal for|pleased to (submit|provide|present)|our fee", head, re.I))
    kor_paper = ("proposal" in kinds and "agreement" not in kinds) or kor_paper_text   # KOR's own fee-proposal paper
    name = os.path.basename(d["RelPath"]) if not label or label == "body" else label
    drawing_issue = bool(re.search(r"issued? for (contract|construction|tender|permit)|\bifc\b|stick file|floor plans?|pricing plans?|submittal|power plan|general notes", name, re.I))
    lease = len(re.findall(r"\b(landlord|tenant|premises)\b", txt[:20000], re.I)) >= 8
    # a contract KOR is party to: contract-shaped text that names KOR, or a contract-named file with 3+ topics
    named_contract = bool(kinds & {"executed", "agreement"}) and not drawing_issue and len(contract_topics) >= 3 and words >= 300
    is_contract = (agreement_text and (names_kor or named_contract)) or (named_contract and names_kor)
    if d["Kind"] == "email":
        role = "email"
    elif "executed" in kinds or exec_signal:
        role = "executed" if (is_contract or kor_paper or named_contract) else "other"
    elif kor_paper:
        role = "proposal"
    elif not is_contract or lease or drawing_issue:
        role = "other"          # in a contracts folder but not KOR's contract: drawing, form, lease, another firm's terms
    elif "markup" in kinds:
        role = "markup"
    else:
        role = "draft"
    if (d["Job"] or inv.get("Job", "")).startswith("99999"):
        role = "other"          # KOR's own internal job numbers (employer programs, admin), not client work
    job = d["Job"] or inv.get("Job", "")
    if job:
        key = job
    else:
        mo = re.search(r"Project Opportunities[\\/][^\\/]+[\\/]([^\\/]+)", d["RelPath"], re.I)
        key = "OPP:" + mo.group(1)[:60] if mo else ""
    doc_meta[d["DocId"]] = {
        "Key": key,
        "Job": job, "ClientName": inv.get("ClientName", ""), "ProjectName": inv.get("ProjectName", ""),
        "Paper": "KOR" if kor_paper else "client", "Role": role, "ExecSignal": "yes" if exec_signal else "",
        "Date": (inv.get("LastWrite") or "")[:10], "Area": inv.get("Area", ""), "File": d["RelPath"], "Part": label,
        "Kind": d["Kind"], "Words": d["Words"], "VersionIndex": "", "Latest": "", "NamesKor": "yes" if names_kor else "",
    }

# Version order within a job: client-paper agreement documents (not emails, not KOR proposals) sorted by
# file date then name. The last one is the latest version ON FILE — which is what KOR ended up with when
# no file is named 'executed'. Starlight's executed copy is named '... May 8 2026 KOR.pdf', nothing more.
by_job = defaultdict(list)
for did, m in doc_meta.items():
    if m["Role"] in ("draft", "markup", "executed") and m["Kind"] in ("pdf", "docx", "pdf-ocr", "doc-converted") and m["Key"]:
        by_job[m["Key"]].append(did)
for job, dids in by_job.items():
    dids.sort(key=lambda x: (doc_meta[x]["Date"], doc_meta[x]["File"], doc_meta[x]["Part"]))
    for i, did in enumerate(dids, 1):
        doc_meta[did]["VersionIndex"] = f"{i}/{len(dids)}"
    # Latest = the newest document that names KOR as a party when any does, else the newest of all.
    naming = [x for x in dids if doc_meta[x]["NamesKor"] == "yes"]
    doc_meta[(naming or dids)[-1]]["Latest"] = "yes"

with open(os.path.join(out_dir, "documents_roles.csv"), "w", encoding="utf-8", newline="") as f:
    cols = ["DocId", "Key", "Job", "ClientName", "Paper", "Role", "ExecSignal", "NamesKor", "VersionIndex", "Latest", "Date", "Area", "Kind", "Words", "File", "Part"]
    w = csv.DictWriter(f, fieldnames=cols); w.writeheader()
    for did, m in doc_meta.items():
        w.writerow({**{k: m.get(k, "") for k in cols}, "DocId": did})

lib = []
for c in clauses:
    m = doc_meta.get(c["DocId"])
    if not m or m["Role"] in ("email", "other") or c["Kind"] == "email" or c["IsDefinition"] == "yes":
        continue
    if int(c["Words"]) < 15 or int(c["Words"]) > 2500:
        continue
    lib.append({
        "DocId": c["DocId"],
        "Key": m["Key"], "Job": m["Job"], "ClientName": m["ClientName"], "ProjectName": m["ProjectName"], "Paper": m["Paper"], "Role": m["Role"],
        "Version": m["VersionIndex"], "Latest": m["Latest"],
        "Date": m["Date"], "Area": m["Area"], "File": m["File"], "Part": m["Part"], "Section": c["Section"], "Heading": c["Heading"],
        "Topics": c["Topics"], "Words": c["Words"], "Text": c["Text"],
    })
with open(os.path.join(out_dir, "library_clauses.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(lib[0].keys())); w.writeheader(); w.writerows(lib)

TOPICS = sorted({t for r in lib for t in r["Topics"].split("|") if t})
ROLE_CODE = {"executed": "E", "markup": "M", "draft": "D", "proposal": "P"}
matrix = defaultdict(lambda: defaultdict(set))
jobinfo = {}
for r in lib:
    if not r["Key"]: continue
    jobinfo[r["Key"]] = (r["ClientName"], r["ProjectName"])
    for t in r["Topics"].split("|"):
        if t:
            matrix[r["Key"]][t].add(ROLE_CODE.get(r["Role"], "?"))
            if r["Latest"] == "yes": matrix[r["Key"]][t].add("L")
with open(os.path.join(out_dir, "job_topic_matrix.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.writer(f); w.writerow(["Job", "ClientName", "ProjectName"] + TOPICS)
    for job in sorted(matrix):
        w.writerow([job, jobinfo[job][0], jobinfo[job][1]] + ["".join(sorted(matrix[job][t])) for t in TOPICS])

pos = []
for t in TOPICS:
    e = {j for j in matrix if "E" in matrix[j][t]}
    m = {j for j in matrix if "M" in matrix[j][t]}
    d = {j for j in matrix if "D" in matrix[j][t]}
    p = {j for j in matrix if "P" in matrix[j][t]}
    l = {j for j in matrix if "L" in matrix[j][t]}
    pos.append({"Topic": t, "JobsExecuted": len(e), "JobsLatestOnFile": len(l), "JobsMarkup": len(m), "JobsDraftOnly": len(d - e - m), "JobsKorProposal": len(p),
                "ExecutedJobs": " ".join(sorted(e)), "MarkupJobs": " ".join(sorted(m))})
with open(os.path.join(out_dir, "positions.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(pos[0].keys())); w.writeheader(); w.writerows(pos)

role_docs = Counter(m["Role"] for m in doc_meta.values())
lines = [
    f"documents (parts): {len(doc_meta)}; by role: " + ", ".join(f"{k}={v}" for k, v in role_docs.most_common()),
    f"  executed by text signal only (no 'signed/executed' in the name): {sum(1 for m in doc_meta.values() if m['Role']=='executed' and m['ExecSignal']=='yes')}",
    f"  KOR paper (proposals): {sum(1 for m in doc_meta.values() if m['Paper']=='KOR')}; client paper: {sum(1 for m in doc_meta.values() if m['Paper']=='client')}",
    f"  jobs with 2+ client-paper versions on file: {sum(1 for j, d in by_job.items() if len(d) >= 2)} of {len(by_job)}",
    f"operative clause rows: {len(lib)} across {len(matrix)} jobs; topics: {len(TOPICS)}",
    "jobs with an EXECUTED clause, by topic: " + ", ".join(f"{p['Topic']}={p['JobsExecuted']}" for p in pos),
    "jobs with a LATEST-ON-FILE clause, by topic: " + ", ".join(f"{p['Topic']}={p['JobsLatestOnFile']}" for p in pos),
    "rules: role = name kind (executed>markup>proposal>draft); 'executed' also when the text carries a DocuSign/Adobe Sign marker; Latest = last client-paper version by file date within the job; clause rows exclude email bodies, definitions, <15 or >2500 words",
]
with open(os.path.join(out_dir, "summary.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print("\n".join(lines))
