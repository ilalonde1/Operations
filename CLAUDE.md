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

## Environment facts worth not rediscovering

- Rules live in `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis`. A missing rule stops a
  production run by design; there is no fallback value.
- Migrations live in `C:\VIsual Studio Projects\KOR.Drafter\db\`, not in this repo.
- The full test suite takes 10–14 minutes because ~20 of its tests rebuild both reference buildings
  from real drawings over SMB. Do not start one and then keep editing.
- `\\Kor-fs01\Projects\Projects` is `E:\Projects\Projects` on the server itself.
- FS01 has no .NET runtime. A self-contained single-file publish
  (`-r win-x64 --self-contained -p:PublishSingleFile=true`) runs there without installing anything.
