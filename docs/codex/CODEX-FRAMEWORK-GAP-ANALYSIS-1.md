# CODEX framework gap analysis 1: which controls are enforced, which are only instructions

inputs changed since #133831: that record is Codex's 16 Nov inventory 1, used here as an INPUT; the new question is grading every bullet of an outside 17-section rubric as enforced or prompt-only, which no earlier report did. Not #133993 (a one-line coordinator note) or #133566 (unrelated).

Prior work searched (18 Nov): `rg -il "gap analysis|prompt-only|technically enforced|enforced versus"` over 1,017 .md files in
Operations/docs and Framework/docs (Codex work folders excluded): 6 hits, none a framework gap analysis (BD module x3,
takeoff scorecard, MCP architecture, engineering tools). Your own 16 Nov reports are the base and are NOT to be redone.

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by the Claude session whose framework is being audited (Framework coordinator 8876d9cf, 18 Nov 2026). Ian
asked ChatGPT for the rubric in the appendix because he does not trust that session. Treat everything below except the
appendix as claims by the audited party: the inputs listed, the claims B1-B6, and the framing. If the brief leaves out an
input or a question that matters, say so. Verify every claim yourself against files.

## THIS BRIEF RUNS IN THREE PARTS (a single run was too big last time)

Ian starts you with "Run PART <n>". Do ONLY that part, and write ONLY its file:
- **PART 1** - rubric sections 1-6, claims B1-B4. Write `docs/codex/CODEX-FRAMEWORK-GAP-ANALYSIS-1-RESPONSE-PART1.md`.
- **PART 2** - rubric sections 7-12, claims B5-B6. Write `docs/codex/CODEX-FRAMEWORK-GAP-ANALYSIS-1-RESPONSE-PART2.md`.
- **PART 3** - rubric sections 13-17, then output items 1, 4, 5 and 6 ACROSS ALL 17 SECTIONS, reading PART1 and PART2's
  files as inputs (re-check any grade you rank; do not just copy). Write `docs/codex/CODEX-FRAMEWORK-GAP-ANALYSIS-1-RESPONSE.md`.
Parts 1 and 2 give output items 2 and 3 for their sections only, plus a short list of the gaps they found for PART 3.
Each part has its own ~40-minute budget. Read only the inputs your sections need.

Edit nothing else, commit nothing, run no build, no database query, nothing on another machine. Read-only `git log`/`git show` and reading files are
allowed; you may run `C:/KOR-Operations/kor/kor.exe --help` (read-only) to see what its verbs claim to do.

## The question

The appendix is a 17-section rubric for an AI-agent framework (core: "AI proposes. Deterministic systems enforce. Evidence
determines completion."). For EVERY control bullet in sections 1-15, and every mechanism in section 16 and state in section
17, say what the KOR framework has today and grade it:

- **ENFORCED** - code refuses the forbidden action whatever the agent says, and the agent cannot switch it off.
- **ENFORCED, AGENT-BYPASSABLE** - code refuses, but the agent can edit it, disable it, or set its override itself.
- **PARTLY** - enforced for some routes or actions only (name the uncovered route).
- **PROMPT-ONLY** - a rule in CLAUDE.md, a memory/record, a reminder or advisory hook output; nothing refuses.
- **ABSENT** - nothing found (name your search words and scope; an incomplete search is not absence).

Each grade with file:line evidence. Then rank the gaps.

## Inputs (read these; search, do not read whole, where marked)

Your earlier work - build on it, do not repeat it:
- `docs/codex/CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md` (all 153 hook entries in three settings.json files) and its `-work` folder.
- `docs/codex/CODEX-FRAMEWORK-INVENTORY-2-RESPONSE.md` (17 guards judged; verdicts) and its `-work` folder.
- `docs/codex/CODEX-FLAWS-AND-FRAMEWORK-REBUILD-RESPONSE.md` (two FATALs, three smallest changes).
- `docs/codex/CODEX-FRAMEWORK-STEP5-PLAN-AUDIT-RESPONSE.md` and `CODEX-ANALYSIS-MODEL-FIXTURES-RESPONSE.md` (step 5, 18 Nov).

The framework as written and as wired:
- `C:\VIsual Studio Projects\Framework\docs\RULES-TO-CHECKS.md` (27 of 210 rules command-checkable; lessons pass) - search.
- `C:\VIsual Studio Projects\Framework\docs\RESET-PLAN.md`, `GUARDS-BAR.md`, `ACCEPTANCE-CONTRACT.md`, `DONE-LIST-ANALYSIS-MODEL.md`, `HANDOVER.md` (STATE NOW only, top ~30 lines).
- `C:\VIsual Studio Projects\Operations\docs\HOW-WE-BUILD.md`; CLAUDE.md in Framework and Operations (rules as prose).
- The three `.claude/settings.json` (Framework, Operations, KOR.Drafter) - re-check against INVENTORY-1, note any change.
- Guard scripts, headers first (each states what it does NOT catch): `Operations/tools/claude_*_guard.py`,
  `Operations/tools/git_hooks/pre_push.py`, `Framework/tools/*guard*.py`, `Framework/tools/git_hooks/pre_push_review.py`,
  `Framework/tools/kor_review.py`, `Framework/tools/kor_task.py`, `Framework/tools/kor_handover.py`.
- Git hook shims: `.git/hooks/pre-push` in each of the three repos (uncommitted; read them on disk).
- Product gates for comparison: KOR.Drafter `Kor.Operations.EngineeringTools.TakeoffCli/TakeoffVerbs.cs` (verb list),
  `Verbs/ModelAuditVerb.cs` header, `Verbs/HandoffGateVerb.cs` header, `tools/virtual_drafter.py` lines ~770-800 (sample-first stop).

## Claims from the audited party to verify (TRUE / FALSE / PARTLY / CANNOT TELL from files, with file:line)

- B1. The agent can grant itself every push-gate override: `KOR_REVIEW_SKIP_OK` (pre_push_review.py) and `KOR_PUSH_CARRY_OK`
  (pre_push.py) are environment variables the pushing session sets itself; on 18 Nov the coordinator set them three times
  (Operations push of 16 'changes' commits; KOR.Drafter 41ca5ef3; Operations f8a65150). Git history and the review table
  are the evidence; the table is not readable to you - say what the files alone show.
- B2. Every hook is defined in a `.claude/settings.json` and a `.git/hooks/pre-push` the agent's account can edit, so no
  guard is beyond the agent's reach (rubric section 6). The 17 Nov coordinator edited the look/absence guards to log-only
  (Operations 1e66e0f0) with Ian's yes, but nothing would have stopped it without.
- B3. There is no capability registry: nothing a session or reviewer can query that lists existing commands/gates and what
  each checks. On 18 Nov a new `takeoff` verb was briefed while `takeoff model-audit` already covered most of it; the plan
  reviewer said GO with "the repo-wide Grep ... timed out". Framework 38bf916 changed the reviewer (plan_review_guard.py settle()).
- B4. A task's definition is not frozen: board rows (KorStandards oversight.Commitment) can have What/DoneWhen rewritten by
  the session at any time (DB rows are not visible to you: grade from the code that writes the board, e.g. kor_task.py,
  and say what cannot be told).
- B5. Completion evidence on the board is free text the closing session writes itself; only `kor_task.py accept` (helper
  rows) puts a fresh judge between a claim and 'done'.
- B6. State outside sessions (brain records, LaneState, board, HANDOVER, context stop at 850k tokens) is the strongest area.

## Output (the response file)

1. One paragraph: how much of the rubric the framework ENFORCES today, as counts per grade over all bullets graded.
2. A table per rubric section: bullet | grade | existing mechanism (path) | bypass route | evidence (file:line).
3. Claims B1-B6 with verdicts.
4. The ranked gap list: for each gap, what escapes (cite a real mistake or 18 Nov event where the inputs show one), the
   smallest change ON AN EXISTING PATH that would close it, and what it costs Ian. Top 5 first. Do NOT propose a new
   framework, a new service, or one mechanism per bullet: the 16 Nov review found that design-then-not-implement is FATAL
   here. Where the rubric's section 16/17 mechanisms would only re-create something that exists, say so.
5. Controls the rubric asks for that would cost more than they catch here (and why), and existing controls that already
   meet a bullet better than the rubric's wording.
6. What you could not judge from these inputs.

## Limits

Facts from files you read, each with file:line; a search that found nothing names its words and scope. Budget: about 40
minutes per part. If going past it, stop and write what you have marked INCOMPLETE, with what is left - a correct partial grading
beats a complete one that guesses.

## Appendix - the rubric (ChatGPT, given by Ian 18 Nov 2026, verbatim)

AI Agent Framework — Required Controls & Operating Principles
Objective: Build a framework that systematically mitigates LLM weaknesses (hallucination, scope creep, destructive changes, context loss, overengineering, false confidence, premature completion) while amplifying strengths (reasoning, parallelism, creativity, code generation, analysis, and problem-solving).

Core principle: AI proposes. Deterministic systems enforce. Evidence determines completion.
1. Task Understanding & Definition
- Restate the user's request in plain language.
- Explicitly define the deliverable.
- Define what successful completion looks like.
- Establish measurable acceptance criteria.
- Identify what is explicitly out of scope.
- Identify assumptions and uncertainties.
- Ask for clarification when ambiguity materially affects implementation.
- Distinguish user requirements from agent-generated suggestions.
- Prevent agents from silently redefining objectives.
- Freeze the approved task definition before execution.
- Require authorization for material changes to scope.
2. Existing System Awareness
- Maintain a complete, searchable inventory of existing functionality, tools, services, libraries, APIs, agents, hooks, and utilities.
- Search existing implementations before proposing new code.
- Identify existing functionality that can be reused or extended.
- Identify dependencies and downstream consumers before modification.
- Maintain a system capability registry, not merely a file inventory.
- Record what each component does, where it lives, and who depends on it.
- Detect duplicate or overlapping functionality.
- Require justification before introducing new abstractions, services, frameworks, or dependencies.
- Prefer reuse over replacement.
- Prefer minimal modification over rewriting.
- Never equate unfamiliar code with unnecessary code.
- Require evidence before declaring functionality obsolete.
3. Planning Before Execution
- Produce a task-specific implementation plan.
- Identify affected components and files.
- Identify existing tools and services to reuse.
- Identify risks, dependencies, and possible side effects.
- Define required tests before modifying code.
- Identify protected functionality and invariants.
- Estimate scope and complexity.
- Break complex work into independently verifiable steps.
- Identify opportunities for safe parallel execution.
- Define checkpoints and rollback points.
- Require plan approval for high-risk changes.
- Prevent execution until mandatory preconditions are satisfied.
4. Scope Discipline
- Every action must trace back to an approved requirement.
- Maintain explicit permitted and prohibited change boundaries.
- Prevent unrelated refactoring.
- Prevent opportunistic cleanup.
- Prevent unsolicited feature development.
- Prevent architectural changes unrelated to the deliverable.
- Detect modifications outside the approved scope.
- Require separate authorization for discovered problems outside scope.
- Maintain a backlog for unrelated discoveries instead of immediately addressing them.
- Prevent agents from creating new tasks merely to sustain momentum.
- Finishing the task is the objective—not continuously finding more work.
5. Preservation of Existing Functionality
- Treat existing working functionality as protected by default.
- Capture pre-change behavior and test results.
- Identify business-critical invariants.
- Require dependency and impact analysis before modification.
- Prevent deletion of functionality without explicit justification and authorization.
- Protect public interfaces, database contracts, and integration behavior.
- Require regression testing against existing behavior.
- Use golden-master testing where appropriate.
- Compare before-and-after outputs for critical business logic.
- Detect unexpected changes in behavior, not merely compilation failures.
- Never accept "the build succeeds" as proof that functionality was preserved.
6. Destructive Action Prevention
- Classify operations by risk before execution.
- Restrict filesystem, database, Git, and deployment permissions.
- Prohibit direct modification of protected branches.
- Prohibit destructive database operations without explicit authorization.
- Block mass deletion and large-scale rewrites by default.
- Require explicit approval for destructive migrations.
- Use isolated worktrees and disposable databases.
- Establish protected backups outside agent-controlled environments.
- Make recovery checkpoints mandatory.
- Enforce restrictions outside the LLM through operating-system permissions, credentials, and independent policy controls.
- Prevent agents from modifying or disabling their own enforcement mechanisms.
- No agent should possess the technical authority to perform actions it is prohibited from performing.
7. Context, Memory & Compaction Reliability
- Maintain persistent, structured task state outside conversational context.
- Separate architectural knowledge, task state, decisions, and historical lessons.
- Persist objectives, constraints, assumptions, and acceptance criteria.
- Record decisions with rationale and supporting evidence.
- Track unresolved questions and known risks.
- Restore authoritative task state after compaction or session restart.
- Verify that required context has been successfully restored.
- Detect contradictions between retrieved context and current repository state.
- Attach source references and version information to important knowledge.
- Distinguish verified facts from agent assumptions.
- Expire or invalidate stale knowledge.
- Prevent execution when critical context is missing.
- Never rely on conversational memory as the sole source of truth.
8. Hallucination & Assumption Controls
- Require evidence for claims about existing code or system behavior.
- Require file references for architectural conclusions.
- Prohibit inventing APIs, dependencies, configuration, or existing functionality.
- Distinguish observed facts, inferences, and speculation.
- Require verification before acting on assumptions.
- Introduce explicit uncertainty reporting.
- Escalate unresolved high-impact uncertainty.
- Prevent confident assertions from being treated as verified evidence.
- Use independent tools to validate claims whenever possible.
- An agent's confidence is not evidence of correctness.
9. Implementation Discipline
- Implement the smallest change that satisfies the task.
- Prefer incremental changes over sweeping rewrites.
- Preserve established architectural conventions unless change is authorized.
- Avoid unnecessary abstractions.
- Avoid speculative generalization.
- Avoid adding dependencies without demonstrated need.
- Require justification for introducing new services or infrastructure.
- Validate after each meaningful implementation step.
- Stop when unexpected behavior appears.
- Prevent repeated speculative fixes without diagnosis.
- Require root-cause analysis before changing code to address failures.
- Limit automated retry loops.
- Prevent agents from repeatedly modifying code merely to make tests pass.
10. Independent Verification
- Separate implementation from verification authority.
- Prevent builders from altering protected acceptance tests.
- Maintain pre-change regression tests.
- Require independent review of significant changes.
- Provide reviewers with original requirements and baseline evidence.
- Do not rely solely on the builder's explanation.
- Validate functionality, not just code quality.
- Check for missing, removed, or degraded behavior.
- Verify database and external integration contracts.
- Detect unauthorized scope expansion.
- Require evidence for every acceptance criterion.
- Treat reviewer agreement as supporting evidence, not proof.
- Require human approval where automated verification is insufficient.
11. Multi-Agent Coordination
- Establish clear agent roles and authority boundaries.
- Assign one accountable orchestrator per task.
- Give each agent a bounded objective.
- Use isolated workspaces for parallel execution.
- Prevent conflicting simultaneous modifications.
- Establish ownership of shared resources.
- Require structured handoffs between agents.
- Maintain a shared authoritative task record.
- Prevent agents from silently changing another agent's requirements.
- Require independent verification rather than consensus.
- Detect circular delegation and redundant work.
- Prevent agents from generating unnecessary subtasks.
- Limit parallelism where dependencies create integration risk.
- Ensure the orchestrator cannot override independent enforcement gates.
12. Momentum & Runaway Behavior Controls
- Establish explicit stopping conditions.
- Stop when the approved deliverable is complete.
- Stop when required evidence is unavailable.
- Stop when actions exceed authorized scope.
- Stop when repeated attempts fail without new diagnostic evidence.
- Stop when critical assumptions are invalidated.
- Stop when unexpected destructive changes are detected.
- Limit tool calls, retries, execution time, and change volume.
- Require renewed authorization when risk or scope materially increases.
- Prevent endless cycles of planning, refactoring, reviewing, and improving.
- Prevent agents from interpreting available time or tokens as permission to continue.
- Doing nothing is preferable to making an unjustified change.
13. Testing & Quality Assurance
- Define tests before implementation where practical.
- Separate preservation tests from new-feature tests.
- Require meaningful regression coverage.
- Test negative cases and failure conditions.
- Test integration boundaries.
- Validate database behavior against representative datasets.
- Detect tests that merely encode the agent's implementation assumptions.
- Prevent unauthorized weakening or deletion of tests.
- Require reproducible test environments.
- Record test evidence with the exact code revision.
- Distinguish untested from tested and failed.
- Never report success when mandatory verification was skipped.
14. Completion & Evidence
- Compare final output directly against the original task definition.
- Verify every acceptance criterion.
- Confirm no unauthorized changes occurred.
- Confirm protected functionality remains intact.
- Produce a concise change summary.
- Provide exact files and components modified.
- Provide tests executed and their results.
- Identify unresolved risks.
- Identify assumptions that remain unverified.
- Provide rollback information.
- Distinguish implemented, verified, approved, and deployed states.
- Prohibit unsupported claims of completion.
- Completion is determined by evidence, not by the agent declaring success.
15. Continuous Learning & Framework Improvement
- Record failures and near misses.
- Perform root-cause analysis on destructive incidents.
- Identify which control failed or was missing.
- Convert recurring failures into deterministic safeguards.
- Convert confirmed defects into regression tests.
- Maintain a versioned incident and lessons-learned registry.
- Measure unauthorized changes, regressions, rework, and recovery time.
- Track false-positive blocks that unnecessarily impede development.
- Evaluate framework changes against historical failure scenarios.
- Prevent unverified agent-generated lessons from becoming authoritative rules.
- Require approval for modifications to the framework's safety mechanisms.
- Periodically remove redundant or ineffective controls.
- The framework must become more reliable through evidence, not merely accumulate more instructions.

16. Additional Architectural Mechanisms Worth Building
These are the components I would prioritize beyond ordinary prompting, hooks, and stored context.
Mechanism    Purpose
Task Contract Engine    Formalizes objective, deliverable, scope, and acceptance criteria
Capability Registry    Catalogs existing functionality and prevents redundant implementations
Dependency & Impact Analyzer    Identifies what could break before modifications
Policy Enforcement Engine    Blocks unauthorized actions independently of the LLM
Risk Classification Engine    Determines required permissions, verification, and approvals
Protected Invariant Registry    Records behavior that must not change
Task State Store    Preserves authoritative state across sessions and compaction
Evidence Ledger    Links claims and completion criteria to actual verification results
Change Scope Validator    Detects unrelated or unauthorized modifications
Independent Verification Pipeline    Tests proposed changes without trusting the builder
Execution Budget Controller    Prevents runaway retries, excessive changes, and endless work
Recovery Controller    Ensures checkpoints, backups, and rollback mechanisms exist
Agent Performance Evaluator    Measures quality, reliability, and recurring failure patterns
Framework Regression Suite    Replays historical agent failures to validate safeguards

These mechanisms should be implemented incrementally. Some can initially be simple scripts and structured records rather than new services.
17. Mandatory Agent Execution Lifecycle
Every development task should follow this state machine:
1. UNDERSTAND

Restate the request and intended deliverable


2. DEFINE

Establish acceptance criteria and scope


3. DISCOVER

Inventory existing capabilities and dependencies


4. ASSESS

Classify risk and protected functionality


5. PLAN

Select minimal implementation and verification strategy


6. AUTHORIZE

Confirm permissions and mandatory approvals


7. BASELINE

Capture recovery state and preservation tests


8. EXECUTE

Implement bounded changes in isolation


9. VERIFY

Run independent tests and policy checks


10. RECONCILE

Compare results against original task contract


11. COMPLETE

Produce evidence-backed completion report


12. LEARN

Record verified lessons and framework improvements



Each transition should have machine-checkable preconditions where possible. Failed checks must block progression or require an explicitly authorized exception. The agent must not be able to mark its own failed gate as passed.
The most important principle to give your Claude Brain
The purpose of this framework is not to make LLMs follow instructions more reliably. It is to design an engineering environment in which their predictable weaknesses cannot silently cause unacceptable damage.

Agents should be free to reason, explore, propose, create, and solve problems—but their authority to modify, delete, integrate, or deploy must be independently constrained.

Optimize for verified delivery of the requested outcome, preservation of existing functionality, minimal unnecessary work, and recoverability.

Never confuse activity with progress, consensus with correctness, memory with knowledge, or successful execution with successful delivery.

My suggested first implementation: Have Claude Brain audit your existing framework against these 17 sections, identify controls that are merely prompt instructions versus technically enforced, and produce a prioritized gap analysis. That will show where additional engineering delivers genuine protection rather than another layer of complexity.
