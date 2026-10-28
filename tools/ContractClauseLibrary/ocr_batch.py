"""OCR every standalone PDF that came out of extraction with fewer than 30 words (scanned agreements).

Usage: python -I ocr_batch.py <pulled-root> <ext-dir> <ocr-dir> <win_ocr.ps1>
For each such PDF: render pages to PNG (PyMuPDF, 200 dpi, first 40 pages), run the Windows OCR
script on the folder, save the text as <ocr-dir>/<sha12>.txt where sha12 is the first 12 hex of the
file's SHA-1 (the same key extract_clauses.py uses for DocId). A rerun of extract_clauses.py with the
ocr-dir then picks these texts up in place of the empty ones.
"""
import csv, hashlib, os, shutil, subprocess, sys, time
import fitz

root, ext_dir, ocr_dir, ps1 = sys.argv[1:5]
os.makedirs(ocr_dir, exist_ok=True)
docs = list(csv.DictReader(open(os.path.join(ext_dir, "documents.csv"), encoding="utf-8", newline="")))
targets = [d for d in docs if d["Kind"] == "pdf" and int(d["Words"]) < 30 and not d["Part"]]
print(f"targets: {len(targets)}")
done = skipped = failed = 0; pages_total = 0
t0 = time.time()
for d in targets:
    path = os.path.join(root, d["RelPath"])
    if not os.path.exists(path):
        skipped += 1; continue
    out = os.path.join(ocr_dir, d["DocId"] + ".txt")
    if os.path.exists(out) and os.path.getsize(out) > 0:
        skipped += 1; continue
    work = os.path.join(ocr_dir, "_work")
    shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    try:
        doc = fitz.open(path)
        n = 0
        for i, page in enumerate(doc):
            if i >= 40: break
            page.get_pixmap(dpi=200).save(os.path.join(work, f"p{i+1:03d}.png")); n += 1
        r = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps1, "-Folder", work],
                           capture_output=True, text=True, timeout=600)
        if r.returncode != 0 or not os.path.exists(os.path.join(work, "ocr.txt")):
            failed += 1; print("FAIL", d["RelPath"][-80:], r.stderr[:200].replace("\n", " ")); continue
        shutil.move(os.path.join(work, "ocr.txt"), out)
        pages_total += n; done += 1
    except Exception as e:
        failed += 1; print("FAIL", d["RelPath"][-80:], str(e)[:120])
shutil.rmtree(os.path.join(ocr_dir, "_work"), ignore_errors=True)
print(f"done={done} skipped={skipped} failed={failed} pages={pages_total} in {time.time()-t0:.0f}s -> {ocr_dir}")
