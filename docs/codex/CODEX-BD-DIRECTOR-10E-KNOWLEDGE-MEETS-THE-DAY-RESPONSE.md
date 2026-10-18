# BD Director 10e response

Source edits, one migration and offline gates authored. No build, test, composer, migration, database read, book read, APP01 access or deployment was performed. Match quality is unmeasured; the live review for the 19 October situations remains for Claude after apply.

## Part 1 — Situations meet knowledge

`MorningBrief` now references `Kor.Opportunities.Knowledge`. `MorningFacts` retains situation text and accepted card rows from its existing reads. Selection is capped at ten stale pursuits plus ten late actions (Ian first), ten AR invoice rows plus five over-budget projects and one partial-timesheet situation, and ten finding groups in the existing severity order. No extra situation or card queries were added beyond the requested IT card read.

`MorningKnowledge.cs` separates pure ranking/formatting from the reader. One local `KnowledgeEmbedder` and one cached `KnowledgeSearch` serve the run. Accepted cards are embedded once per role using `Title. Plain. Recognise.`; the three highest cosine scores have no cutoff. Claim search requests three hits with one per source. A role with fewer than three accepted cards shows those available; claims can still match when IT has no accepted cards yet.

`ListRelationsAsync` uses typed BIGINT parameters in batches of 500, reads either endpoint, joins both statements and book titles, removes duplicate relations across batches, and excludes retired endpoints. The formatter shows contradictions with notes and distinguishes incoming `refined by` from outgoing `refines`. Situation text is limited to 160 characters; scores use invariant two-decimal formatting. Each role receives one knowledge fact. X counts situations with at least one candidate and no failed knowledge read; Y counts all selected situations. Partial results remain visible with `CouldNotRead`, and other facts and roles continue. Error messages expose exception types, not configuration values.

The prompt explains candidate matches, reading full cards, other accepted cards, disagreements, and IT citations. IT collection ends with its accepted-card read.

Gates authored, **not run**: `MorningKnowledgeTests` covers formatting, relation direction/notes, coverage, exact card input, fixed-vector cosine ranking, no threshold, caps, printed-row reuse, claim diversity, shared index, and read-failure isolation. SQL text checks cover both endpoints, parameters, chunks and joins. The existing `MorningBriefTests` reference gate now checks that App projects have no direct Knowledge reference. Both existing Knowledge test stores implement the expanded interface. These gates do not validate live SQL, model availability, runtime dependencies, production source freshness or semantic match quality.

Code differences from the brief: late actions were printed only inside owner-group lines, so a capped individual late-action fact was added using the same rows. Partial weeks were separate lines; they now share one printed line used verbatim as the single situation. `KnowledgeSearch.Cosine` requires 384-dimensional vectors, so fixed-vector gates use that size. The existing claim-search default minimum remains unchanged; the no-threshold rule applies to cards.

## Part 2 — IT doctrine

`367_ItDoctrineRole.sql` guards both tables and both constraint drops, and recreates the two role CHECKs with BD, Operations and IT inside a transaction. It changes no data. CLI validation/help, synthesis wording, morning imports, duplicate checks, and the doctrine view now support IT. Duplicate checks include both other roles. `ValidateCards` accepts only accepted cards of the section's role and retains COO → Operations. Hour kinds and existing card text are unchanged.

Gates authored, **not run**: the updated citation gate accepts an accepted IT card and rejects a BD card in IT; source gates inspect both guarded CHECKs, CLI role validation/import/duplicate wiring and prompt rules; a mocked view-model gate exercises selection and both other-role reads for all three roles. They do not apply SQL, execute the CLI, render WPF, validate persisted cards or measure match quality.

Code differences from the brief: CLI line 270 was the morning import card pool, not the duplicate check. It also needed IT to make the new citation rule usable. The actual `doctrine similar` branch read only the selected role; it now reads all three. Migration 366 is transactional but has unguarded constraint drops; 367 adds the requested guards. One existing gate explicitly prohibited IT doctrine and was updated.

## Part 3 — NetworkOps connection on APP01

`NetworkRowsAsync` resolves WATCHDB first and falls back to DB only when WATCHDB is absent. Coverage names the selected variable without printing its value. Its three SQL statements remain SELECT-only; the watch login and its grants are unchanged.

Gate authored, **not run**: source text checks cover fallback order, variable-name coverage, and all three `NetworkRowsAsync` SQL literals, rejecting INSERT, UPDATE, DELETE, MERGE and EXEC. This does not establish actual environment values, login rights, connectivity or live query results. No additional contradiction was found in this part.

Final review: `git diff --check` is clean, including separate whitespace checks for the newly authored files. Pre-existing unrelated changes were left untouched. Compilation and all authored gates remain unverified by execution, as requested.
