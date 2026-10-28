"""Extract text from pulled contract files and cut them into topic-tagged clauses.

Usage: python -I extract_clauses.py <pulled-root> <outdir>
  <pulled-root> is the unzipped tree (job folders preserved). PDFs go through pdftotext -layout,
  .docx through python-docx, .msg through extract_msg (body + every attachment, recursively),
  .doc is recorded as unreadable. Each document becomes <outdir>/text/<hash>.txt and one row in
  documents.csv; every numbered section that matches a topic regex becomes a row in clauses.csv.
Nothing here is a legal judgement: it locates and copies text, and records what it could not read.
"""
import csv, hashlib, os, re, subprocess, sys, json
from collections import Counter

root, outdir = sys.argv[1], sys.argv[2]
ocrdir = sys.argv[3] if len(sys.argv) > 3 else None     # optional: <sha12>.txt OCR texts for scanned PDFs
textdir = os.path.join(outdir, "text")
os.makedirs(textdir, exist_ok=True)

JOB = re.compile(r"(?<!\d)(\d{5}-\d{2})(?!\d)")
TOPICS = {
    "limitation_of_liability": re.compile(r"limitation of (the )?(consultant'?s |engineer'?s )?liabilit|limitation on liabilit|amount of damages|aggregate liability|cumulative liability|total liability|liability (of the (consultant|engineer) )?(is|shall be|will be) limited|shall not exceed the (fee|price|total|amount|lesser|greater)|in no event shall the (consultant|engineer)'?s? (total |aggregate )?liability", re.I),
    "indemnity": re.compile(r"indemnif|hold harmless|save harmless|held harmless", re.I),
    "consequential_damages": re.compile(r"consequential|indirect (damages|loss)|punitive|loss of (profit|revenue|use)", re.I),
    "intellectual_property": re.compile(r"intellectual property|copyright|ownership of (the )?(documents|deliverables|drawings|instruments)|instruments of service|moral rights|licen[cs]e (to|for) (use|the)|work product", re.I),
    "insurance": re.compile(r"insurance|additional insured|subrogation|errors and omissions|professional liability|general liability|certificate of insurance", re.I),
    "termination": re.compile(r"terminat", re.I),
    "suspension": re.compile(r"suspen|stand-?down|stop[- ]work", re.I),
    "payment": re.compile(r"withh[eo]ld|set-?off|back-?charge|payment terms|net \d+ days|within (30|45|60|90) days|interest on (overdue|late)|invoic", re.I),
    "standard_of_care": re.compile(r"standard of care|skill, care|degree of (care|skill)|reasonable (care|skill)|professional (skill|standard)", re.I),
    "permits_approvals": re.compile(r"permit|approvals? (from|of) (the )?(authorit|municipal)|building code|applicable laws", re.I),
    "dispute": re.compile(r"arbitrat|mediat|dispute resolution|governing law|jurisdiction", re.I),
    "confidentiality": re.compile(r"confidential|non-?disclosure", re.I),
    "ai_tools": re.compile(r"artificial intelligence|\bAI\b|machine learning|large language model|generative", re.I),
    "warranty_reperform": re.compile(r"warrant|re-?perform|rectif|remed(y|ial) (the )?(defect|deficien)", re.I),
    "limitation_period": re.compile(r"limitation period|limitation act|within (two|2|six|6) years", re.I),
    "assignment_subconsultants": re.compile(r"assign(ment)? of (this )?agreement|sub-?consultant|sub-?contract", re.I),
    "lien": re.compile(r"\blien", re.I),
    "field_review": re.compile(r"field review|site (visit|review)|construction (review|administration)", re.I),
    "schedule_delay": re.compile(r"time is of the essence|liquidated damages|delay", re.I),
}
SECTION = re.compile(r"^\s*(?:(?:ARTICLE|Article|SECTION|Section)\s+)?(\d{1,2}(?:\.\d{1,2}){0,3})[\.\)]?\s+(?=\S)")

def sha(path):
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def pdf_text(path):
    """PyMuPDF first (it opens any path; poppler's Windows build refused 'Proposals & Contracts'
    folders in the 10-28 test), pdftotext -layout as the fallback. Text is kept whatever the exit code."""
    try:
        import fitz
        doc = fitz.open(path)
        return "\n".join(page.get_text("text") for page in doc)
    except Exception:
        pass
    r = subprocess.run(["pdftotext", "-layout", path, "-"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.stdout or ""

def docx_text(path):
    import docx
    d = docx.Document(path)
    parts = [p.text for p in d.paragraphs]
    for t in d.tables:
        for row in t.rows:
            parts.append(" | ".join(c.text for c in row.cells))
    return "\n".join(parts)

def msg_parts(path, depth=0):
    """Yield (label, text, kind) for the body and every readable attachment."""
    import extract_msg
    m = extract_msg.Message(path)
    hdr = f"FROM: {m.sender}\nTO: {m.to}\nCC: {m.cc}\nDATE: {m.date}\nSUBJECT: {m.subject}\n\n"
    yield ("body", hdr + (m.body or ""), "email")
    for a in m.attachments:
        fn = (a.longFilename or a.shortFilename or "att").strip()
        ext = os.path.splitext(fn)[1].lower()
        if ext not in (".pdf", ".docx", ".msg", ".doc") or a.data is None:
            continue
        tmp = os.path.join(textdir, f"_att_{depth}_{hashlib.sha1(a.data).hexdigest()[:12]}{ext}")
        with open(tmp, "wb") as f:
            f.write(a.data)
        try:
            if ext == ".pdf":
                yield (fn, pdf_text(tmp), "pdf")
            elif ext == ".docx":
                yield (fn, docx_text(tmp), "docx")
            elif ext == ".msg" and depth < 2:
                for lbl, txt, kind in msg_parts(tmp, depth + 1):
                    yield (f"{fn} > {lbl}", txt, kind)
            else:
                yield (fn, "", "doc-unreadable")
        finally:
            try: os.remove(tmp)
            except OSError: pass

NUM_ONLY = re.compile(r"^\s*(\d{1,2}(?:\.\d{1,2}){0,3})\.?\s*$")
XREF = re.compile(r"^\s*\d{1,2}(?:\.\d{1,2}){0,3}\.?\s+(hereof|hereunder|herein|hereto|above|below|of|and|or|to|through|per|days?|years?|months?|hours?|sq|%|\$|business)\b", re.I)

def eml_parts(path, depth=0):
    """Yield (label, text, kind) for an .eml: body (plain, else HTML stripped) and every readable attachment."""
    import email, html as htmlmod
    from email import policy
    with open(path, "rb") as f:
        m = email.message_from_binary_file(f, policy=policy.default)
    hdr = f"FROM: {m.get('From','')}\nTO: {m.get('To','')}\nCC: {m.get('Cc','')}\nDATE: {m.get('Date','')}\nSUBJECT: {m.get('Subject','')}\n\n"
    body = ""
    try:
        b = m.get_body(preferencelist=("plain", "html"))
        if b is not None:
            body = b.get_content()
            if b.get_content_type() == "text/html":
                body = htmlmod.unescape(re.sub(r"<[^>]+>", " ", body))
    except Exception:
        pass
    yield ("body", hdr + body, "email")
    for part in m.iter_attachments():
        fn = (part.get_filename() or "att").strip()
        ext = os.path.splitext(fn)[1].lower()
        if ext not in (".pdf", ".docx", ".msg", ".eml", ".doc"):
            continue
        try:
            data = part.get_payload(decode=True)
        except Exception:
            data = None
        if not data:
            continue
        tmp = os.path.join(textdir, f"_eatt_{depth}_{hashlib.sha1(data).hexdigest()[:12]}{ext}")
        with open(tmp, "wb") as f:
            f.write(data)
        try:
            if ext == ".pdf":
                yield (fn, pdf_text(tmp), "pdf")
            elif ext == ".docx":
                yield (fn, docx_text(tmp), "docx")
            elif ext == ".msg" and depth < 2:
                for lbl, txt, kind in msg_parts(tmp, depth + 1):
                    yield (f"{fn} > {lbl}", txt, kind)
            elif ext == ".eml" and depth < 2:
                for lbl, txt, kind in eml_parts(tmp, depth + 1):
                    yield (f"{fn} > {lbl}", txt, kind)
            else:
                yield (fn, "", "doc-unreadable")
        finally:
            try: os.remove(tmp)
            except OSError: pass

def sections(text):
    """Split text into numbered sections; return list of (number, heading-ish first line, body).
    PyMuPDF puts a heading's number on its own line ("8.4" / "Amount of Damages"), pdftotext keeps
    them together; both shapes start a section. A number followed by a cross-reference word
    ("7.5 hereof:") does not."""
    lines = text.splitlines()
    out, cur, pending = [], None, None   # pending = number seen alone, waiting for its heading line
    for ln in lines:
        s = ln.strip()
        if pending is not None:
            if not s:
                continue
            if cur: out.append(cur)
            cur = [pending, s[:160], [pending + " " + ln]]
            pending = None
            continue
        mo = NUM_ONLY.match(ln)
        if mo:
            pending = mo.group(1)
            continue
        m = SECTION.match(ln)
        if m and len(s) < 400 and not XREF.match(ln):
            if cur: out.append(cur)
            cur = [m.group(1), s[:160], [ln]]
        elif cur:
            cur[2].append(ln)
        else:
            if not out: out.append(["0", "(preamble)", [ln]])
            else: out[-1][2].append(ln)
    if cur: out.append(cur)
    result = [(n, h, "\n".join(b)) for n, h, b in out]
    # Fallback for documents whose numbering is Word auto-numbering (invisible to text extraction) or
    # absent: any section over 1,500 words is re-cut into paragraph chunks of up to ~350 words, so a
    # 4,000-word standard-form agreement still yields clause-sized rows instead of one unusable block.
    fixed = []
    for n, h, body in result:
        if len(body.split()) <= 1500:
            fixed.append((n, h, body)); continue
        paras = [p for p in re.split(r"\n\s*\n", body) if p.strip()]
        chunk, chunk_words, k = [], 0, 0
        for p in paras:
            pw = len(p.split())
            if chunk and chunk_words + pw > 350:
                k += 1
                fixed.append((f"{n}p{k}", chunk[0].strip().splitlines()[0][:160], "\n\n".join(chunk)))
                chunk, chunk_words = [], 0
            chunk.append(p); chunk_words += pw
        if chunk:
            k += 1
            fixed.append((f"{n}p{k}", chunk[0].strip().splitlines()[0][:160], "\n\n".join(chunk)))
    return fixed

docs, clauses = [], []
unreadable = Counter()
for dirpath, _, files in os.walk(root):
    for fn in files:
        path = os.path.join(dirpath, fn)
        ext = os.path.splitext(fn)[1].lower()
        rel = os.path.relpath(path, root)
        job = (JOB.search(rel) or [None, ""])[1] if JOB.search(rel) else ""
        parts = []
        try:
            if ext == ".pdf":
                t = pdf_text(path)
                kind = "pdf"
                if ocrdir and len(t.split()) < 30:
                    ocr_path = os.path.join(ocrdir, sha(path)[:12] + ".txt")
                    if os.path.exists(ocr_path):
                        t = open(ocr_path, encoding="utf-8-sig", errors="replace").read()
                        kind = "pdf-ocr"
                parts = [("", t, kind)]
            elif ext == ".docx":
                parts = [("", docx_text(path), "docx")]
            elif ext == ".msg":
                parts = list(msg_parts(path))
            elif ext == ".eml":
                parts = list(eml_parts(path))
            elif ext == ".doc":
                conv = os.path.join(ocrdir, sha(path)[:12] + ".txt") if ocrdir else None
                if conv and os.path.exists(conv):
                    parts = [("", open(conv, encoding="utf-8-sig", errors="replace").read(), "doc-converted")]
                else:
                    parts = [("", "", "doc-unreadable")]
            else:
                continue
        except Exception as e:
            parts = [("", "", f"error: {e}")]
        for label, text, kind in parts:
            did = sha(path)[:12] + ("-" + hashlib.sha1(label.encode()).hexdigest()[:6] if label else "")
            words = len(text.split())
            if words < 30:
                unreadable[kind] += 1
            with open(os.path.join(textdir, did + ".txt"), "w", encoding="utf-8") as f:
                f.write(text)
            docs.append({"DocId": did, "Job": job, "RelPath": rel, "Part": label, "Kind": kind, "Words": words,
                         "Bytes": os.path.getsize(path), "Topics": ""})
            hit_topics = set()
            for num, head, body in sections(text):
                topics = [t for t, rx in TOPICS.items() if rx.search(body)]
                # an insurance clause quoting policy "limits of liability" / "aggregate" is not a cap on KOR
                if "limitation_of_liability" in topics and re.match(r"^\s*[\d.]*\s*insurance\b", head, re.I):
                    topics.remove("limitation_of_liability")
                if not topics:
                    continue
                hit_topics.update(topics)
                is_def = bool(re.search(r"[\"“”']\s*means\b|\bmeans\b.{0,40}\b(all|any|the)\b", body[:200], re.I))
                clauses.append({"DocId": did, "Job": job, "RelPath": rel, "Part": label, "Kind": kind,
                                "Section": num, "Heading": head, "IsDefinition": "yes" if is_def else "",
                                "Topics": "|".join(topics), "Words": len(body.split()),
                                "Excerpt": re.sub(r"\s+", " ", body)[:700],
                                "Text": re.sub(r"[ \t]+", " ", body).strip()[:20000]})
            docs[-1]["Topics"] = "|".join(sorted(hit_topics))

with open(os.path.join(outdir, "documents.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(docs[0].keys())); w.writeheader(); w.writerows(docs)
with open(os.path.join(outdir, "clauses.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(clauses[0].keys())); w.writeheader(); w.writerows(clauses)
print(f"documents: {len(docs)} (parts incl. email attachments); clause rows: {len(clauses)}")
print("low-text parts by kind (<30 words):", dict(unreadable))
print("docs by kind:", dict(Counter(d['Kind'] for d in docs)))
print("docs with a job number:", sum(1 for d in docs if d['Job']), "of", len(docs))
