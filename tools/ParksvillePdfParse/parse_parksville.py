"""Parse the City of Parksville development-applications PDF into CSV.

Parksville is the only market in the mid-Island set that publishes a NAMED
APPLICANT per application, and it does it as a quarterly PDF rather than a feed.
That makes it the richest source there and the only one that needs a reader.

    python parse_parksville.py <parksville.pdf> <out.csv>

⚠ WHY THIS USES COORDINATES AND NOT `pdftotext -layout`.
The document's rows are visually STAGGERED: a two-line applicant name pushes the
description down, so in the -layout output the description for one application
sits on the line belonging to the NEXT one. Parsing by character offset silently
attaches the wrong applicant to the wrong address and scope — precisely the class
of error that put a Caribbean retreat fund on our Starlight client record. So
this reads word bounding boxes from `pdftotext -bbox` and clusters them:

  * rows    — words whose vertical centres fall within ROW_TOL of each other
  * columns — the four column x-bands, taken from the header words themselves
              rather than hard-coded, so a re-laid-out issue still parses

A row that opens with a date starts a new application; rows without a date are
continuations and each word is appended to the column its x-position falls in.

Verify after every new issue: `rows parsed` must equal the number of date-led
entries, and spot-check two applicants against the PDF before trusting it.
"""
import csv
import io
import re
import sys

import pdfplumber

FULLDATE_RX = re.compile(r'([A-Z][a-z]+ \d{1,2}, \d{4})')
FILE_RX = re.compile(r'(\d{4}-[A-Z]{2,4}\d+)')
HEADINGS = ('DEVELOPMENT PERMITS', 'REZONING APPLICATIONS', 'SUBDIVISION',
            'DEVELOPMENT VARIANCE PERMITS', 'TEMPORARY USE PERMITS',
            'OFFICIAL COMMUNITY PLAN AMENDMENTS')
ROW_TOL = 6.0


def words_from_pdf(pdf_path):
    """Word boxes with page / x / y.

    pdfplumber rather than `pdftotext -bbox`: the Xpdf build on this machine is
    Glyph & Cog 4.00, which exits 99 on -bbox. Poppler's pdftotext has it; this
    one does not, so do not "simplify" this back to a subprocess call."""
    out = []
    with pdfplumber.open(pdf_path) as pdf:
        for page_no, page in enumerate(pdf.pages):
            for w in page.extract_words(use_text_flow=False, keep_blank_chars=False):
                t = (w.get('text') or '').strip()
                if t:
                    out.append({
                        'page': page_no,
                        'x': float(w['x0']),
                        'y': (float(w['top']) + float(w['bottom'])) / 2.0,
                        'text': t,
                    })
    return out


def cluster_rows(words):
    rows = []
    for w in sorted(words, key=lambda w: (w['page'], w['y'], w['x'])):
        if rows and rows[-1][0]['page'] == w['page'] and abs(rows[-1][0]['y'] - w['y']) <= ROW_TOL:
            rows[-1].append(w)
        else:
            rows.append([w])
    for r in rows:
        r.sort(key=lambda w: w['x'])
    return rows


def find_columns(rows):
    """Take the column x-origins from the header row, so a re-laid-out issue
    still parses instead of silently mis-splitting."""
    for r in rows:
        text = ' '.join(w['text'] for w in r)
        if 'APPLICANT' in text and 'CIVIC' in text:
            xs = {}
            for w in r:
                if w['text'] == 'APPLICANT':
                    xs['applicant'] = w['x']
                if w['text'] == 'CIVIC':
                    xs['address'] = w['x']
                if w['text'] == 'DESCRIPTION':
                    xs['desc'] = w['x']
            if len(xs) == 3:
                return xs
    raise SystemExit('Could not find the header row — the PDF layout changed.')


def main(pdf_path, csv_path):
    rows = cluster_rows(words_from_pdf(pdf_path))
    cols = find_columns(rows)
    # Split midway between column origins so a word that starts slightly left of
    # its header still lands in the right column.
    b1 = (cols['applicant'] + cols['address']) / 2.0
    b2 = (cols['address'] + cols['desc']) / 2.0
    b0 = cols['applicant'] - 4.0

    out = []
    current = None
    section = ''

    for r in rows:
        line = ' '.join(w['text'] for w in r).strip()
        upper = line.upper()

        if any(upper.startswith(h) for h in HEADINGS) and len(line) < 60:
            section = line
            continue
        if 'DATE OF' in upper or 'DEVELOPMENT APPLICATIONS' in upper and 'PERMIT' not in upper:
            continue

        # The date and the applicant are NOT separated by a column gap on a
        # single-line entry — "December 8, 2025 Momentum Design Build" runs
        # straight through — so banding by x alone eats the first word or two of
        # the applicant. Instead: find the date in the row text, consume exactly
        # the words that spell it, and band everything that is left.
        m = FULLDATE_RX.search(line)
        consumed = 0
        if m and r and r[0]['x'] < b1:
            date_tokens = m.group(1).split()
            head = [w['text'] for w in r[:len(date_tokens)]]
            if head == date_tokens:
                consumed = len(date_tokens)
                if current:
                    out.append(current)
                current = {'Section': section, 'DateOfSubmission': m.group(1),
                           'Applicant': [], 'CivicAddress': [], 'Description': []}

        if current is None:
            continue

        for w in r[consumed:]:
            # A continuation line's leading words sit under the applicant column
            # even when they start slightly left of the header.
            key = 'Applicant' if w['x'] < b1 else ('CivicAddress' if w['x'] < b2 else 'Description')
            current[key].append(w['text'])

    if current:
        out.append(current)

    for r in out:
        for k in ('Applicant', 'CivicAddress', 'Description'):
            r[k] = re.sub(r'\s+', ' ', ' '.join(r[k])).strip()
        fm = FILE_RX.search(r['Description'])
        r['FileNo'] = fm.group(1) if fm else ''

    with io.open(csv_path, 'w', encoding='utf-8', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=[
            'Section', 'DateOfSubmission', 'Applicant', 'CivicAddress', 'FileNo', 'Description'])
        w.writeheader()
        w.writerows(out)

    print('rows parsed        :', len(out))
    print('with an applicant  :', sum(1 for r in out if r['Applicant']))
    print('with an address    :', sum(1 for r in out if r['CivicAddress']))
    print('with a file number :', sum(1 for r in out if r['FileNo']))
    print('sections           :', sorted({r['Section'] for r in out if r['Section']}))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
