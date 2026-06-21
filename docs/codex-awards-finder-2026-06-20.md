# Codex prompt — #4 Industry Awards Finder (BD platform)

**Goal:** Add an **Industry Awards Finder** to the BD platform — a scheduled Worker job that
discovers and maintains a catalog of **AEC industry award *programs* KOR could apply for**
(engineering/structural/design-excellence competitions, BC + California), and surfaces
upcoming-deadline awards in the BD module with a light KOR-project match. **Read-only discovery
only — do NOT draft or auto-submit applications** (AI write tools are deferred firm-wide).

**Critical — this is NOT the retired contract-award intel.** `AwardAgentEnrichmentJob` (retired
2026-06-10) scraped who *won public contracts*. This new feature is the opposite: industry
**award competitions KOR enters to win recognition** — e.g. ACEC-BC Awards for Engineering
Excellence, Canadian Consulting Engineering Awards, IStructE Structural Awards, Vancouver Urban
Design Awards, Gold Nugget Awards, BIA/Icon Awards. Keep them entirely separate; do **not** revive
or touch `AwardAgentEnrichmentJob`.

**Pattern to follow (reuse, don't reinvent):**
- New `AwardProgramFinderJob` in `Kor.Opportunities.Worker/Services/`, registered as a
  `ScheduledJobDefinition` in `Kor.Opportunities.Worker/Program.cs` exactly like the existing
  jobs. Weekly cadence is fine (award deadlines move slowly).
- Reuse the existing research-executor path (`AnthropicResearchExecutorService` +
  a `FileSystem*ResearchPromptCatalog`) for the LLM/web discovery of award programs. Cache calls
  — respect the existing ResearchPrompts cost guidance; don't trim prompts below the working size.
- Persist to a new table `opportunities.AwardProgram` (migration = next sequential number in
  `Kor.Opportunities.Data/Schema/`): `Id`, `NaturalKey` (awarding body + program name + cycle
  year — for idempotent upsert), `AwardingBody`, `ProgramName`, `Category`, `Discipline`,
  `Region` (BC/CA/National), `EligibilitySummary`, `SubmissionDeadline` (date), `EntryFee`,
  `Url`, plus the standard `FirstSeen/LastSeen/Retired*` audit columns the other intel tables use.
  Idempotent upserts keyed by `NaturalKey`.
- Surface in the BD module following the existing BdReports/Briefs surfaces: a simple read-only
  **"Awards"** list (soonest deadline first), wired through the existing DI in
  `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs`. A KOR-project match
  (award category/discipline ↔ KorClient project sectors) is a nice-to-have — keep it simple.

**Constraints:**
- Read-only: discover + surface only. No application drafting or submission.
- Idempotent; never duplicate an award program across runs.
- Follow existing job-registration, DI, options, and audit-column conventions; don't break
  existing jobs or the cron scheduler.
- Migration: sequential number; if you add a column AND reference it in an index, split into
  GO-separated batches; include the standard audit columns.
- Do **not** build or run (the Codex env hangs on dotnet build/test) — I'll verify locally.
