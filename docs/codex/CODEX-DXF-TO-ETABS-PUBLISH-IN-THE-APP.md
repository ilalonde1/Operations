# CODEX — PUBLISHING MOVES INTO THE APP, AND THE SCRIPT IS DELETED

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations.** **Do not touch the 31168 job share.** **Do not publish anything.**

## What I want

`tools\Publish-EtabsModel.ps1` is **848 lines of PowerShell that re-parse the `.e2k` with regex in
about fifteen places**, decide which job folder and reference to use, build a summary PDF, gate the
explainers, and land files. Beside it sits `takeoff publish` and `JobPublisher.cs` — 230 lines of C#
doing plan → generate → verify → land, and covering perhaps a third of what the script does.

Two implementations of one thing, and the one in use is the one that reads ETABS models with regex
in a shell script.

**Move all of it into the app. `takeoff publish` becomes the command and the script is deleted.**
The owner's instruction, verbatim: *"Gone entirely - takeoff publish is the command."*

This is a PORT, not a redesign. Every behaviour below exists because something went wrong once, and
the comments in the script say what. Carry the reasons across, not just the code.

## The behaviours that must survive

Read `tools\Publish-EtabsModel.ps1` in full first. These are the ones that will be lost if you only
skim, each with the incident that produced it.

**1. Finding the job rather than being told it.** Job folder is the first directory under
`\\Kor-fs01\Projects\Projects\*` starting with the project number; the model folder is the first
`*ETABS Models*` within three levels of it; the DXF folder is the first `*DXF*` inside the model
folder **or its parent** — 31168 keeps it inside, 31138 outside.

**2. Choosing the reference, and refusing to guess.** The reference is an ENGINEER's model, never
ours: candidates are `.e2k`/`.$et` in the model folder, excluding `*FROM-DRAWINGS*`, excluding any
file whose first 40,000 lines contain `"K[WCPFSO]\d+"` — our object names survive a round trip
through ETABS, which is how a generated model was once mistaken for an engineer's own. Prefer a name
containing `reference`. **If more than one candidate remains, throw and list them.** Taking the
largest or the first silently decides which building gets rebuilt, and 31168's folder holds a site
reference and a tower-B rebuild within 66 bytes of each other.

**3. Refuse to publish without KorStandards.** No rules connection, no publish — a model built on
built-in values is not a production model.

**4. Nothing enters the job folder until it has passed.** Generate to a staging folder, run the
invariants against the finished file, copy only what passed. Models used to be generated straight
into the engineer's folder and checked afterwards, which makes every check a note rather than a
gate: eight tower storeys, a 132-inch wall and a site-wide plate all reached that folder with the
checks running after they landed.

**5. The per-job summary PDF, and the one-page rule.** A page written from this job's own model and
report: storeys populated, wall panels, columns, floor plates, headers, openings cut — plus
"storeys with a floor" **shown only when it exceeds the plate count**, because a storey that borrows
a plate makes plates read lower than storeys and looks exactly like a storey that lost its floor.
Then the findings, taken VERBATIM from the report's flag lines rather than summarised, because
summarising is where they soften.

⭐**It is called a one-page summary, so it must be one page.** The findings list is the only part
whose length varies, so it is shortened — 8, 6, 4, 3, 2 — re-rendered, and re-measured with
`pdfinfo` until the page is one page. Whatever is dropped is still counted and named as dropped.
It used to REFUSE instead, which is right for a wrong number and wrong for a long one: 31168's two
towers legitimately produce more findings than the mid-rise, and a publish correct in every other
respect was blocked by its own covering note.

**6. The explainers travel only to jobs they describe.** The dossier and one-pager name particular
buildings. The gate reads the job numbers out of the dossier SOURCE (`\b3\d{4}\b`) and, if this
project is not among them, does not copy them — the model, report and questions still publish,
because those are generated from this job and are always true of it. Copying an explainer beside a
job it does not describe hands the engineer an authoritative-looking document about somebody else's
tower.

**7. The claims gate runs against the SOURCE, before the copy.** Every count the dossier states must
appear in the model it will sit beside, and the check must happen BEFORE landing. It used to copy
first and check after, so the run that discovered the dossier was three model-revisions out of date
had already put it in the engineer's folder: a document claiming 1,119 walls and 63 storeys sat
beside a 349-wall, 15-storey model for nine days. **A source that fails takes its stale copy out of
the folder with it.**

**8. Staleness.** An explainer older than the newest `.cs` under `Kor.Operations.EngineeringTools.Core\Dxf`
is stale and is reported as such by name.

**9. Per-building fan-out**, which `PublishPlan.ForBuildings` already does, plus the `--drop-storeys`
list added on 31 August.

## The rule that decides the shape

⛔**Delete every regex read of the `.e2k`.** The script counts storeys, wall panels, columns, floor
plates, headers, openings and floored storeys by pattern-matching the file it has just written. The
service already returns all of them — `DxfToEtabsReport.Summary`, and `E2kModelContents` from the
saved-model readback. Re-deriving a number the app already knows is exactly how the report and the
file came to disagree, which took four rounds to clean up on 31 August.

Checked before writing this, so you do not have to hunt: `E2kModelContents` (`E2kDocument.cs:20`)
already carries `Storeys`, `Walls`, `Columns`, `Floors`, `Joints`, `MembersByStorey` and
`PlatesByStorey` — and **"storeys with a floor" is just `PlatesByStorey.Count`**, which is the
number the script recovers with two regexes and a join.

The only two the summary needs that nothing currently returns are **headers (`KS`)** and **openings
cut (`KO`)**. Add those to `E2kModelContents` where every other count already lives. Do not re-read
the file for them.

Same for the question count: the script recovers it by regex from the report text (`^Questions for
you:\s*(\d+)`). `ModelQuestionnaire` knows it.

## Shape

Yours to choose, but I expect roughly:

```
JobPublisher            plan → generate → verify → land, as now, plus discovery and the gates
PublishDiscovery        job folder, model folder, DXF folder, reference selection (2 above)
PublishSummary          the summary page and the one-page loop (5)
PublishExplainers       applicability, claims and staleness gates (6, 7, 8)
```

`takeoff publish` gains whatever options the script had that it lacks: `--tower`, `--top-storey`,
`--variant`, `--skip-dossier`, `--per-building`. Keep `--land` meaning what it means now.

**External tools.** `Format-BdWebPdf.ps1` (headless Edge) and `pdfinfo` are the only things that
must still be shelled out to; wrap them in one place with a clear failure if either is missing. The
script's own warning applies: do not delete the temp HTML the instant the renderer returns — it is a
race the renderer loses on a slow run, and what lands is a PDF of the browser's "file not found"
page, which looks like a document until somebody opens it.

**Tests.** Discovery and the gates are the parts that have gone wrong before and the parts a test
can hold: reference selection with two candidates must throw; an explainer naming other jobs must
not be copied; a dossier whose counts disagree with the model must block AND remove a stale copy; a
summary must come out one page. Geometry needs no new tests — this changes none of it.

## What to report

A short response file: the shape you chose, anything in the script whose intent you could not
determine (ask rather than guess — several of those comments are the only record of an incident),
and anything the script does that you deliberately did NOT carry across, with why.

Then apply it. `tools\Publish-EtabsModel.ps1` should be deleted in the same change, and any
documentation naming it updated to `takeoff publish`.

Write to `docs/codex/CODEX-DXF-TO-ETABS-PUBLISH-IN-THE-APP-RESPONSE.md`. Ping when applied.
