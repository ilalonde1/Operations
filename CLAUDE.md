# Working rules for this repo

These exist because each one was broken, at cost, on 2026-08-15. They are gates, not advice.

## 1. Search before you build. Say what you searched.

Before writing any script, tool, or command that walks files, reads models, or talks to the share,
grep for prior art and **state in your reply what you searched and what you found**. Start with:

    tools/                          scripts that already do this
    ../KOR.Drafter/db/tools/        corpus and verification tooling
    docs/                           what has already been measured

"I built X" without a sentence naming what you checked first is a defect, whatever X does.

What this cost: a second corpus walker was built while `tools/Measure-EtabsCorpusRules.ps1` sat in
this repo, having already produced the 1,126-model analysis it was duplicating. Ten minutes of VPN
walk, one wedged process, and a lesson re-learned that `db/tools/verify_e2k_claims.ps1` states in
its header: *Get-ChildItem -Recurse over SMB is unusably slow.*

## 2. Read the artifact you were given before generating a new one.

If the user has handed over a report, a scan, a measurement — read it and say what it already
answers before running anything. Most "we should measure X" turns out to be measured.

What this cost: a full-volume scan was started to find out whether reference models declare their
units. The answer was in the analysis the user had linked an hour earlier: 936 in inches, 159 in
feet, 29 in millimetres, 2 in metres — all 1,126 of them.

## 3. Never hand over a command you have not run or verified.

Before giving the user anything to paste: confirm the path exists **on the machine that will run
it**, and that its dependencies are there. If you have access to that machine, run it yourself
instead of handing it over.

You usually do have access. `\\Kor-fs01\C$`, `E$` and `Projects` are reachable. WinRM is blocked,
but `Invoke-CimMethod -ComputerName <host> -ClassName Win32_Process -MethodName Create` starts a
process remotely over RPC and works.

What this cost: a command with a repo-relative path, handed to the user on a file server with no
repo and no .NET.

## 4. Push filters to the filesystem, not to LINQ.

`Directory.EnumerateFiles(root, "*.e2k", ...)` lets the OS filter. `EnumerateFiles(root, "*.*")`
followed by a `.Where()` enumerates every file on the volume. On the projects share that is the
difference between seconds and never.

## 5. Verify the artifact, not your reading of it.

Check the shipped PDF as text, not the HTML it came from. Check the workbook by opening it, not by
reading the writer. Check that an edit landed by reading the file back — a scripted replace that
silently no-matches will pass an aggregate "did anything change" test while the thing you meant to
change did not.

What this cost: a stale claim survived a correction because the text it targeted had been reworded;
a rebuilt PDF still carried a sentence the source had already fixed, because Edge caches file://
URLs.

## 6. When told to stop, stop everything.

Background test runs, watchers and scans count. "I'll stop after this finishes" is not stopping.

## 7. Never write a C# regex through a non-raw Python string.

`\b` in a Python string is a BACKSPACE, U+0008. It converts silently — no warning, unlike `\s`
and `\d`, which at least raise a SyntaxWarning. The C# then holds an invisible control character
where a word boundary was meant, the pattern matches nothing, and every part of it tests fine in
isolation.

What this cost: a diaphragm check that never fired, an hour hunting an innocent `LinesOf` call,
and a Codex brief written around a hypothesis that was wrong. Also a mangled `\bin\Debug\net8.0`
path in a PowerShell script, from the same cause.

Use the Edit tool for anything containing a regex or a Windows path. If a script must generate
it, use a raw string (`r"..."`) and read the file back to confirm what landed.

## 8. One artefact, one place, one name. Every time.

A Codex brief goes in `docs/codex/` and is called `CODEX-<TOPIC>.md`. Nothing else, nowhere else,
no exceptions for "this one is different".

The same holds for anything produced more than once: pick the convention the repo already has,
find where the previous ones went before writing a new one, and put it there.

What this cost: forty Codex artefacts across SIX conventions — seventeen as `CODEX-*.txt` in the
repo root, seventeen as `docs/codex-*.md`, one `docs/audit-2026-08/codex/BRIEF-*.md`, one
`docs/map-audit/CODEX-PROMPT.md`, one `docs/KOR-BD-Enrichment-Fix/Phase1-Codex-Brief.md`, and
responses landing on the Desktop as `.md` or `.txt` depending on the day.

The filing is not the point. The point is what it proves: on the single most repeated artefact in
this repo there was **no procedure** — every instance was decided fresh, from nothing. A method
that cannot hold a convention it has used forty times will not hold one under pressure, and the
same session that scattered these chased one symptom at a time for four days instead of
characterising the fault once. Consistency on the small, boring things is the evidence that there
is a method at all.

Before creating any recurring artefact: `find . -iname "*<thing>*"` first, and match what is there.

## 9. Before the first fix: ground truth, a harness, and a picture.

On any problem expected to take more than an hour, three things exist BEFORE the first code change,
and the reply that starts the work names all three:

1. **The data is local.** Nothing that gets read on every iteration is read over SMB or the VPN.
2. **One command measures EVERY deliverable.** Not the one being worked on — all of them, and the
   other project too. If a fix can break something without the harness saying so, the harness is
   not finished.
3. **The output has been LOOKED AT**, rendered, not counted. `docs/etabs-handoff/plan_sheet.py`
   draws every storey on one sheet in a second.

What this cost: on 2026-08-27 each iteration re-read 139 DXFs across the VPN — four minutes a
turn, for hours, until Ian said "PULL THE DXFs LOCAL". Copying them took twenty-six seconds and
made a full two-model regeneration take thirty-seven. In the same session every change was measured
against ONE of the two deliverables, so four separate fixes each repaired one model and silently
broke the other. And every fault for two days was found by the engineer opening the file or by Ian
sending a screenshot, because the checking was count tables — while a rule in this file already
said to render it and look.

## 10. Two regressions means STOP and characterise.

A fix that repairs one thing and breaks another is information the first time. The second time, the
model in your head is wrong, and every further patch is a coin flip that costs an hour.

Stop touching the code. Write down what the system actually is — the entities, how they relate, and
the single rule that has to hold — and find every place the code contradicts it. Then fix it once.

What this cost: four days on ONE question — which storey a member belongs to and whose building it
is — as a sequence of single-symptom patches. Three rules that were each defensible alone and
mutually inconsistent together; fixing any one of them broke the other two. The characterisation
that should have come on day one was finally written as
`docs/codex/CODEX-DXF-TO-ETABS-WHOLE-SYSTEM-AUDIT.md`, and only because Ian demanded it.

## 11. On the SECOND instance, stop fixing and build the thing that finds them all.

Rule 10 says stop and characterise. This is the part that was still missing: the characterisation
is not a paragraph, it is **a test that fails on every instance at once**. Until that exists, no
more fixes.

The trigger is mechanical, so there is nothing to judge:

> Two symptoms with the same shape → name the class in one sentence → write the check that would
> have caught both → only then fix.

If the sentence will not come out, that IS the finding: the class is not understood yet, and the
next fix is a guess.

What this cost: on 2026-08-31 the stack merge gave every member one label its whole height, which
breaks **every reader keyed on an object name**. That sentence was knowable at the second instance.
Instead it was found EIGHT times, one symptom at a time, over two days — the report counts, the
publish gate refusing a correct model, the coverage audit, the benchmark against her own model, the
plausibility heights reading a wall at 454ft, the sheet ledger crediting one drawing with 32
storeys, the baseline counts, and finally building attribution, where the merge ran before the
building cut and let 2,591 of the towers' members into the YMCA's model. Every one was green until
somebody happened to look.

The check that finds them all is `StackMergeChangesNothingButLabelsTests`: build the same job with
the merge on and off, and assert everything the engineer can see is identical — every (kind,
position, storey, section) placement, the counts she reads, how many members the cut removed as
somebody else's, and which drawing each storey came from. Object names and object counts are the
only things allowed to differ.

It took ten minutes and found a NINTH instance within seconds of existing, on the other building,
which nobody had looked at. Built at the second instance it would have replaced two days of this.

**A differential harness is the tool.** Where a change is supposed to alter one property and nothing
else, run it both ways and diff everything else. That applies well past this merge: a cut, a rename,
a reorder, a unit change.

## Environment facts worth not rediscovering

- Rules live in `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis`. A missing rule stops a
  production run by design; there is no fallback value.
- Migrations live in `C:\VIsual Studio Projects\KOR.Drafter\db\`, not in this repo.
- The full test suite takes about **7 minutes** because ~20 of its tests rebuild both reference
  buildings from real drawings. Do not start one and then keep editing.

  Measured 2026-08-29, and it is faster than the 10–14 minutes this said before — the drawings are
  mirrored locally now (`DrawingCache`) instead of being read over SMB every run:

      dotnet test --filter "Speed!=Slow"    every edit           22 s   (678 tests)
      dotnet test                           geometry + publish  6m56s   (731 tests)
      …App/EngineeringTools.Tests           the WPF side         15 s   (443 tests)

  So the iterate-and-check loop is under forty seconds for both suites, and the seven-minute run is
  only owed on geometry changes and before a publish.

  **Geometry work always runs the full suite.** The coverage ratchets in `ModelCoverageTests` are
  the only thing that catches a member read on one build and lost on the next, and they may only
  come down.

  A needless full run costs more than its seven minutes: it holds the build output lock, so the next
  edit cannot compile — and nothing else can even be committed while it runs. On 2026-08-24 roughly
  a third of the runs were for PowerShell and document changes that no C# test touches, and one of
  them wedged a testhost.

  ⚠ But the full run finds things the filter cannot, and not only geometry. On 2026-08-29 it failed
  on `ATaggedSheetOnlyMatchesItsOwnBuilding`, which passes on its own: `PlanSheetNaming.Vocabulary`
  is a public mutable STATIC, xUnit runs test classes in PARALLEL, and another class was setting it
  to a different practice's words mid-test. It had failed twice before and been written off as
  unexplained. **A test that passes alone and fails in the suite is shared state, not luck** — look
  for a static before looking anywhere else.
- `\\Kor-fs01\Projects\Projects` is `E:\Projects\Projects` on the server itself.
- FS01 has no .NET runtime. A self-contained single-file publish
  (`-r win-x64 --self-contained -p:PublishSingleFile=true`) runs there without installing anything.
