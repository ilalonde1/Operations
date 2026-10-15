# ProposalReview

Scripts promoted from the Leduc West Campus RFP session (2026-10-13 to 15). The process they
belong to is written up in `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md`.

| Script | Does | Run |
|---|---|---|
| `docx_meta.py` | Who has a Word file open (lock-file owner) and, per `.docx` in a folder: creator, last saved by, modified, revision, pages, words, minutes edited, tracked changes | `python -I docx_meta.py "<folder>\~$name.docx" "<folder>"` |
| `inspect_docx.py` | Maps a `.docx`: every non-empty paragraph with style, every table, the fee table's cells and runs, and which strings live inside text boxes | `python -I inspect_docx.py file.docx` |
| `build_leduc_example.py` | Worked example of scripted edits on a copy: logged, fail-loud, cover text box, fee table, inserted paragraphs and a picture. Reuse the helpers (`single_run_replace`, `set_cell`, `new_para_after`, `style_runs_like`), not the Leduc specifics | `python -I build_leduc_example.py src.docx dst.docx orgchart.png` |

Render the result through Word COM, never LibreOffice (the docx skill's `soffice.py` needs
`AF_UNIX`): `ExportAsFixedFormat(pdf, 17)`, then `pdftoppm` and look at the pages.

Python 3.12, `python-docx` 1.2.0, `pandoc` for text diffs between versions.
