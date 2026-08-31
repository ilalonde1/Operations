# CODEX — CONSOLIDATE THE VERIFIED DUPLICATION

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side — your runner hangs here for 15+ minutes.
> Apply the edits, grep your own diff, ping. Stop there.
>
> **Do NOT run any destructive git operation, and DELETE NOTHING.** Not a file, not a project, not a
> folder. Two obvious deletions are named at the bottom and both are the user's call, not yours.
>
> **Do NOT regenerate `docs/architecture/architecture.json`.**

The architecture map has been audited twice and its numbers are trustworthy. This is the work it was
built to find: duplication verified by **content**, not by name — the declarations were pulled from
their files and compared, and every item below scored 90%+ identical.

**This is production code** — the BD platform, the desktop app, a Windows service. **Behaviour must
not change.** This is a move-and-reference exercise, not a redesign, and none of it is urgent enough
to be worth a regression.

---

## 1. Credential redaction, copied whole into two projects

    Kor.Operations.App/Logging/CredentialPatterns.cs               6 lines
    Kor.Operations.App/Logging/CredentialRedactingEnricher.cs     14 lines
    Kor.Operations.App/Logging/CredentialRedactingPolicy.cs       17 lines
    Kor.Operations.FileSync.Service/Logging/…  — all three, byte-identical

**Both projects already reference `Kor.Operations.Core`.** One move, three files, no new project and
no new project reference.

This is first because of what it is: code that stops credentials reaching a log, currently free to be
fixed in one copy while the other keeps leaking. Keep the namespace change minimal and leave the
Serilog wiring in each project where it is — only the three types move.

## 2. `MajorProjectRecord` — five copies of a 49-line record, all identical

    Kor.Opportunities.Data/Ingestion/Providers/AbMajorProjectsInventoryProvider.cs
    Kor.Opportunities.Data/Ingestion/Providers/BcMajorProjectsInventoryProvider.cs
    Kor.Opportunities.Data/Ingestion/Providers/CaSocrataMajorProjectsInventoryProvider.cs
    Kor.Opportunities.Data/Ingestion/Providers/CeqanetMajorProjectsInventoryProvider.cs
    tools/BdResearchImport/Program.cs

245 lines to hold one 49-line record. Four of the five are in the same assembly, so most of this is a
lift to a shared file with no reference changes at all. Decide where the fifth should get it from and
say why — a `Compile Include` link is an established pattern in this repo if a project reference is
too heavy for a one-off tool.

## 3. `DeltekClientCandidate` and friends — the copies that agree

`DeltekClientCandidate` (5 lines, 100%, four projects), `CompanyMatch` (4 lines, 100%, three),
`LinkPlan`, `LinkPlanRow`, `ReviewRow`, `DedupCandidateRow`, `CanonicalOrgTarget` (95–97% each,
between `tools/BdDeltekLink` and `Kor.Opportunities.Worker`).

⛔ **Before writing a new home for any of this, find what already exists.** There is a standing
decision on record that the canonical resolver is **`CanonicalOrgResolver` in the BD module**, and a
previous attempt to rewrite that resolver was explicitly rejected. Read what is there first and
extend it rather than starting a parallel one. If the right home already exists, say so and use it.

## 4. `DeltekFuzzyMatch` — ANALYSE, DO NOT MERGE

93 lines, **90%** similar across three copies:

    Kor.Opportunities.Worker/…      tools/BdDeltekLink/Program.cs      tools/BdSeedImport/Program.cs

Everything else on this list is at 100% — copied and left alone. **90% over 93 lines means these have
DRIFTED**, and two versions of the same company-matching logic are behaving differently against a
production database. Which is correct is not yours to decide and not mine.

So for this one: **produce a difference report, change no code.** What differs between the three, what
input would produce a different match result, and which copy looks like the newest. Put it at the
bottom of your response. A merge before that question is answered would silently pick a winner.

## 5. Do not touch — the user's call

Named so you do not helpfully tidy them:

- `docs/map-audit/KorMapSyncRunner.cs` — 371 lines, 100% identical to the live FileSync copy, and in
  no `.csproj`. It is dead, and deleting it is still not your call.
- `tools/BdDeltekLink`, `tools/BdSeedImport`, `tools/ApcInterestBackfill` — prototypes that graduated
  into Worker jobs and were never retired; nothing invokes them. **`tools/BdIntelExtract` is LIVE** —
  `monitor-drains.ps1` calls it. Retiring any of these is a separate decision.

---

## Constraints

- **No behaviour change.** If a consolidation would alter what any code does, stop and say so.
- **No new project.** Everything here has an existing home or an existing sharing mechanism.
- Keep each numbered item a **separate, self-contained edit**, so one can be reverted without the
  others.
- If an item turns out to be harder than it looks — a namespace clash, a type that is only
  *nearly* identical because of something the similarity score smoothed over — **leave it and say
  why.** A stated reason is a better outcome than a forced merge.

When you ping, list what moved where, what you left alone and why, and put the `DeltekFuzzyMatch`
difference report at the end. Write anything long to
`docs/codex/CODEX-DUPLICATION-CONSOLIDATION-RESPONSE.md`.
