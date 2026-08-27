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

## Environment facts worth not rediscovering

- Rules live in `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis`. A missing rule stops a
  production run by design; there is no fallback value.
- Migrations live in `C:\VIsual Studio Projects\KOR.Drafter\db\`, not in this repo.
- The full test suite takes 10–14 minutes because ~20 of its tests rebuild both reference buildings
  from real drawings over SMB. Do not start one and then keep editing.

  Those tests are tagged, so most edits do not need them:

      dotnet test --filter "Speed!=Slow"    every edit — seconds
      dotnet test                           geometry changes, and before any publish

  **Geometry work always runs the full suite.** The coverage ratchets in `ModelCoverageTests` are
  the only thing that catches a member read on one build and lost on the next, and they may only
  come down.

  A needless full run costs more than its ten minutes: it holds the build output lock, so the next
  edit cannot compile. On 2026-08-24 roughly a third of the runs were for PowerShell and document
  changes that no C# test touches, and one of them wedged a testhost.
- `\\Kor-fs01\Projects\Projects` is `E:\Projects\Projects` on the server itself.
- FS01 has no .NET runtime. A self-contained single-file publish
  (`-r win-x64 --self-contained -p:PublishSingleFile=true`) runs there without installing anything.

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
