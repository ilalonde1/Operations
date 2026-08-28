# The column design loop — what it costs now, and the build

Written 2026-08-28 from the firm's own records, not from anyone's impression. Every number below
came from Deltek timesheets, the projects share, or the files themselves.

## What the evidence says

**Where the hours go.** 49,857 hours booked since Sept 2025 across 31 people and 536 projects.
Construction Documents is the largest identified phase at 15,969 hours; Construction Admin 9,641;
Design Development 3,604.

**What people write on their CD time.** Of the 8,658 hours that carry a description, the largest
recurring identifiable cluster is building design calculators — 221.5 hours across 10 people and 6
projects explicitly mention a spreadsheet, Excel or a calculator. That is a floor, not a ceiling:
most entries carry no description, and nobody books "used the column spreadsheet."

Verbatim, top of the CD list:

```
ACI Column Design Spreadsheet          24.0     Slab Design Spreadsheet- Mat Detailer  10.0
Column Design Spreadsheet              23.0     Bi-Axial Column Code                    9.0
ACI Strip and Pad Footings             13.0     spreadsheet verification                7.0
Bi-Axial Column Bending                11.5     ACI Punching Shear Spreadhseet          6.5
Column transition detail               10.0     Strip footing spreadsheet               5.5

"Trying to understand Mark's seismic excel sheets"                       5.5
"Finalize Column Spreadsheet- it's the best one yet."
"Developing NBC2020 spreadsheet with Henry to suit wood"
"Column Design - Why does S-Concrete have some proprietary shear design??"
```

Five different names for a column design spreadsheet in one quarter.

**There is no library.** SharePoint holds one design spreadsheet, from 2019.
`\\Kor-fs01\Projects\Projects\00 Templates` holds drafting templates, master PDFs, sample models and
wood framing — and no calculators. Every project starts from someone's personal copy.

**And the real work is not the spreadsheet.** `02 Engineering\05 Column Design` on 30961-01 holds:

```
Column Design - AEM      30961-01 SEC ELEMS.xlsm, two .EDB models, S-CONCRETE\,
                         Load Take Down (AA).xlsx, and a folder: "AN's Problematic Ones"
Column Design - AN       MIDRISE\, TOWER\
Parking Columns_SG       27 .SCO files
```

Three engineers' parallel column design on one project. The `.SCO` files are named by hand with the
column marks inside them:

```
12X30 (P1-L1)(All Except C53_C92).SCO
14X36 (L02-L8)(C18 to C21).SCO
15X30 (L02TH - L7)(C52,C74,C249,C250,C251).SCO
```

## The menial task, exactly

An `.SCO` is plain text — `@Object@ / @Table@ / @EndTable@`. Its Sectional Loads table is:

```
LC | Nf | Tf | Vfz | Mfy | Cmy | Vfy | Mfz | Cmz | Pdistr | CheckLC | Load Type | Comment | AutoGen
 1 | -102.3596 | 0 | 15.1116 | 107.2232 | 1 | 5.0946 | 25.42022 | ... | L02TH  C75 -> Grav1, 12X30, 45Mpa, kl 8.8497,Cm-1 | 0
 3 | -364.4055 | 0 | 62.8025 | 506.0379 | 1 | 9.6905 | 56.96675 | ... | L02TH  C75 -> EQX1, 12X30, 45Mpa, kl 8.8497,Cm-1 | 0
```

Six load cases per column — Grav1, Grav2, EQX1, EQX2, EQY1, EQY2 — each carrying axial, two shears
and two moments. The comment states storey, column mark, case, section, concrete strength and
effective length. **`AutoGen` is 0 on every row: none of it was generated.**

So the loop today is:

1. Run the ETABS model.
2. Read per-column, per-storey, per-case forces out of it — or do a hand load take-down in Excel.
3. Group columns by section and level range into batches.
4. Create one S-Concrete file per batch and type the demands in.
5. Record which columns are in which file by typing their marks into the filename.
6. Run the check.
7. Chase the failures by hand.
8. **When the model changes, do it all again.**

Only step 6 is engineering. Steps 1–5, 7 and 8 are transcription and bookkeeping.

## The build

**ETABS results → grouped, named `.SCO` files → results back → one column schedule.**

| step | what it does | what we already own |
|---|---|---|
| read demands | per column, per storey, per load case | `ReflectionCsiOapiDriver` / `EtabsApiExporter` (write-only today; needs a result-read interface) |
| know the columns | mark, section, storey, effective length | `E2kQuantityTakeoff` already parses `FRAME SECTIONS`, `LINE ASSIGNS` and storey rises |
| group them | by section + contiguous level range | new, small — the model states both |
| write `.SCO` | the tab-delimited tables above | new, small — the format is plain text and we already write `.e2k` |
| read results back | capacity, utilisation, pass/fail | new |
| the schedule | every column, demand, capacity, utilisation, flagged failures | `StructuralTakeoffReportGenerator` pattern, branded workbook |

**What is genuinely new is small.** The hard parts — knowing the building, reading the model,
writing a structured text format, producing a branded workbook — are done.

## How it gets proven before anyone trusts it

The same way the Revit importer was: **against work already done by hand.**

`Parking Columns_SG` holds 27 hand-made `.SCO` files with their demands already in them. Generate
those same files from the ETABS model and diff. If every `Nf`, `Vfz`, `Mfy`, `Vfy` and `Mfz` matches
the engineer's own numbers, it is proven. If they do not match, we find out why before a line of it
is used on a live job.

That check becomes a test, skipped where the share is unreachable, like every other test that needs
files it does not own.

## What this is not

It is not an AI sizing columns. S-Concrete still does the code check, the engineer still signs it.
This removes the typing between ETABS and S-Concrete, and the bookkeeping about which column went
where. Nothing about the design decision moves.

## What I still need

1. **One project where the ETABS model and the hand-made `.SCO` files are both current**, so the
   diff is meaningful. 30961-01 looks right — `Column Design - AEM` holds two `.EDB` files — but
   whether they match the `Parking Columns_SG` batch needs confirming.
2. **Which S-Concrete version** — the file says `Version 2022.1`. A newer install may write a
   different table.
3. **Whether the load take-down in `Load Take Down (AA).xlsx` is upstream of the ETABS model or a
   parallel check.** If people are taking loads down by hand *as well*, that is a second menial task
   sitting next to this one.

## Ranked against the alternative

The other candidate was reading unmarked architect prelims into a model. That is weeks of
classification research and its output is probabilistic geometry an engineer must check line by
line. This is deterministic, provable against files that already exist, and it removes work that
happens on every concrete project the firm runs.
