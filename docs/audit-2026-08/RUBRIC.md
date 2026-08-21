# Module Audit Rubric — KOR Suite, August 2026

**Read this in full before auditing. Every module audit follows it exactly.**

## Context

KOR Structural is a structural engineering firm (~40 staff; British Columbia, Canada + Southern
California). Over the last 8 months it built an in-house software suite, ~364,500 lines of C#
across 11 repositories. It grew organically in three layers:

1. **A Newforma replacement** — file emails to project folders, search filed email, send file
   transmittals, track transfers. Outlook add-in + WPF desktop app, SharePoint instead of
   Newforma Info Exchange, plus a self-hosted transmittal tracking/redirector server.
2. **A Deltek Vantagepoint reporting layer** — real-time views over Deltek data (WIP, AR, cash,
   backlog, utilization, PM/DM performance, YoY, earned vs invoiced, billed P&L, collections
   exposure, at-risk projects), with a conversational AI "virtual CFO" on top.
3. **The BD Brain** — public procurement ingestion, entity resolution, enrichment, scoring, AI
   research agents, dossier generation, pursuit lifecycle.

Plus engineering tools: PDF→SAFE, quantity takeoff, rebar change detection, DXF→ETABS model
generation, Revit tooling.

**Why this audit exists.** In under two weeks KOR demos this suite to **MVE**, a large SoCal
architecture partner, whose technical lead will push back hard. The owner has had his head down
building for 8 months and does not currently know, module by module, what is finished, what is
half-built, and what will break on screen. This audit is the answer.

**Today is 2026-08-20.** Triage mode. The output is a decision aid, not a code review.

## Non-negotiable rules

**1. Every claim carries an evidence tier. No exceptions.**

- `RUN` — you executed it and observed the output (build, test, CLI, service, script).
- `QUERIED` — you read live state (SQL SELECT, HTTP endpoint, service status, file share).
- `READ` — you read the source and reasoned about it. You did not observe it working.
- `DOC` — a document, comment, or README asserts it. **Lowest trust. Never present as fact.**

Write the tier inline, e.g. `[RUN]`, `[READ]`. A finding with no tier is invalid.

**2. Documents are hypotheses, not evidence.** This repo's docs are heavily stale. Before citing
any doc, compare its date to the last commit touching the code it describes
(`git log -1 --date=short --format=%ad -- <path>`). If the code is newer, the doc is suspect —
say so. A known example: `AGENTS.md` claims `EmailFiler/EmailFilerv2` fails to build; the owner
confirms it installs and runs. Do not repeat doc claims uncritically.

**3. Read-only. Absolutely no writes to any server, database, service, or share.** SELECT only.
GET only. Never restart a service, never change config, never write to a network path. You may
write files only under `docs/audit-2026-08/`.

**4. Do not run the full test suite.** It takes 10–14 minutes because ~20 tests rebuild reference
buildings over SMB. Run targeted tests only, with `--filter`. Per `AGENTS.md`: build/test in
Debug, single project at a time. WPF app tests hang headless — always use `--filter`.

**5. Prefer filesystem-level filtering.** `Get-ChildItem -Recurse` over SMB is unusably slow.
Never enumerate a network share broadly.

**6. Say what you searched.** Open your report with the paths, greps, commands and queries you
ran. An unsourced conclusion is a defect.

**7. Do not speculate about what you could not check.** Write "could not verify — here is the
command that would" instead. The owner has been burned by confident-sounding wrong answers.

## Required output structure

Write your report to the file path given in your task. Use exactly these sections:

### 1. What I searched
Paths, greps, builds, queries, endpoints. Be specific enough to reproduce.

### 2. What this module is
Two paragraphs, plain language, demo-facing. What problem does it solve for the firm? What
would a user actually see and do? Assume the reader is technical but has not opened this code
in months.

### 3. How you would demo it
The concrete click-path or command sequence. Entry point, prerequisites (VPN? service running?
data present?), and what appears on screen. If it cannot currently be demoed, say so plainly.

### 4. Completeness
A table: capability | state | evidence tier.
State is one of: `WORKING` · `PARTIAL` · `STUBBED` · `DEAD` · `UNKNOWN`.
Count and list: `TODO`/`FIXME`/`HACK`/`NotImplementedException`/empty catch blocks/
`throw new NotSupportedException`. Name the significant ones with file:line.

### 5. What is broken or risky
Concrete defects with `file.cs:line`. Swallowed exceptions, hardcoded paths, hardcoded
credentials or connection strings, missing null guards on user input, unbounded queries,
network calls with no timeout, anything that would fail on a machine other than the developer's.

### 6. Dependencies
External systems it needs: SQL databases (name them), Deltek ODBC, Microsoft Graph, SharePoint,
network shares, HTTP services, AI providers, licensed desktop software. Note which are reachable
from a laptop off the KOR LAN — this matters if the demo is done remotely or at MVE's office.

### 7. Test reality
Test project, test count, what is actually covered vs. what matters. Did you run them? Result?
Be blunt where coverage is theatre.

### 8. Demo risk
Ranked list. What would break, look embarrassing, or invite an awkward question in front of
MVE's technical lead. Include "looks unfinished" risks, not just crashes.

### 9. To-do register
A table: item | size (S ≤2h / M ≤1d / L >1d) | tag | why it matters.
Tag is one of:
- `BEFORE-DEMO` — must be fixed or deliberately hidden before MVE sees it
- `SOON` — matters within a quarter
- `LATER` — real but not urgent

Be ruthless about `BEFORE-DEMO`. Under a two-week runway, most things are not.

### 10. Verdict
Three to five sentences. Is this module demo-ready, demo-able with care, or should it stay off
the screen? What is the single most important thing to fix?

## Environment facts (verify before relying on any of these)

- SQL: `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis`. A missing rule stops a
  production run by design — there is no fallback value.
- The Opportunities/BD database connection string lives in the `KOR_OPPORTUNITIES_OPPORTUNITIESDB`
  environment variable on KOR-APP01.
- The MCP AI service is reported to serve `/ask` on `kor-app01:5500`. Verify, do not assume.
- Deltek is reached over ODBC via a DSN, using four-part catalog names. Never issue `USE`.
- `\Kor-fs01\Projects\Projects` is `E:\Projects\Projects` on the server itself.
- FS01 has no .NET runtime installed.
- WinRM is blocked. `Invoke-CimMethod -ComputerName <host> -ClassName Win32_Process -MethodName
  Create` starts a remote process over RPC and works.
- `sqlcmd -i` requires a Windows-style path and UTF-8 BOM input.
- Migrations for KorStandards live in `C:\VIsual Studio Projects\KOR.Drafter\db\`.

## What to return to the orchestrator

**At most 40 lines.** Structure:
- Verdict: demo-ready / demo-able with care / keep off screen
- Completeness in one line (e.g. "7 of 9 capabilities WORKING, 1 PARTIAL, 1 DEAD")
- The 3 most important findings, each with its evidence tier
- Every `BEFORE-DEMO` item, one line each
- Anything that contradicts an existing document or memory (flag as STALE-DOC with the filename)

**Do not paste the report body into your reply.** The report lives in the file. Your reply is a
summary for an orchestrator assembling 18 of these.
