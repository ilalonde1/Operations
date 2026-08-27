# Research helpers for the MVE pipeline work

Five small scripts that did the work described in `../P4-session2-resolutions.md §1`. They are here
rather than in the repo's top-level `tools/` because they serve this research, not production.
Run them from a working directory holding the downloaded artifacts.

| Script | What it does |
|---|---|
| `sdq.py` | Queries a local copy of San Diego's `approvals_active_datasd.csv` by regex over project title, scope and address; prints stage, valuation, storeys, floor area and permit holder. Expects the CSV as `sd_active.csv` in the cwd |
| `buildsd_meta.py` | Harvests `og:title` / `og:description` from all 147 BuildSD project pages (their detail is client-rendered, but the status line is in the server-rendered `<head>`). Expects `slugs.txt` |
| `titleblocks.py` | Renders the bottom-right title block of each sheet of a raster-only plan set and stacks them into one contact sheet, so a 36-sheet consultant roster is readable in one look. `python titleblocks.py <pdf> 11,14,17,20 out.png` |
| `flat.py` | Flattens an HTML filing (SEC, city page) to plain text and greps it with context. `python flat.py <file.htm> "<regex>" [span]` |
| `scan_avb.py` | Flattens an SEC HTML exhibit table-by-table to one line per `<tr>`, for reading supplemental attachments. `python scan_avb.py <file.htm> "<regex>"` |

## Getting the inputs

```
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36"

# San Diego permits (272 MB; stream-grep first if you only need one address)
curl -s -A "$UA" https://seshat.datasd.org/development_permits/approvals_active_datasd.csv -o sd_active.csv

# BuildSD slugs
curl -s -A "$UA" https://buildsd.org/projects | grep -oE 'href="/projects/[^"]+"' \
  | sed 's|href="/projects/||;s/"//' | sort -u > slugs.txt

# SEC needs a UA carrying a contact email
curl -s -A "$UA ilalonde@korstructural.com" https://data.sec.gov/submissions/CIK0000915912.json
```

Requires `pdftotext` / `pdftoppm` (poppler) and Pillow, both already installed on KOR-1001.
