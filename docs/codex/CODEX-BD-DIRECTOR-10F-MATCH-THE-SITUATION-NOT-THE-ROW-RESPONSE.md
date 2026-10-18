Implemented the 10f source changes and authored the gates. No build, test, composer run, database/book access, APP01 access, publish or deployment was performed. Real match quality remains unverified.

Before editing, searched the existing `MorningKnowledge` implementation, `MorningFacts` situation selection, morning-test usages and the 10e/10f filenames under `docs/codex`. The existing code already supplied selection caps, card embedding, claim search, relation formatting and coverage accounting. Those paths were extended. Small dependency/signature reads established the action and financial row fields, claim-search contract and one additional typed-situation test usage.

Changes:

- `tools/MorningBrief/MorningKnowledge.cs`: added the pure `MorningSituation(Kind, Query, Printed, Situations)` record and identifier scrubber. Matching embeds/searches `Query`; formatting uses `Printed`. Cards retain their `appliesTo` terms and rank through `BdDoctrine.AppliesTo` with moment `hour`. Matching non-`any` terms take priority, ordered by cosine; remaining places come from cosine ranking. A mixed `any, situation:...` card receives priority only when its specific term matches. Formatting preserves that order.
- `tools/MorningBrief/MorningFacts.cs`: builds queries from the six kinds' fields, preserving printed rows, selection ordering and caps. Pursuits retain the opening timestamp already read so `never-touched` is distinguishable from a later touch. Invoice amounts become the requested three bands; project percentages use engineering-budget overrun and posted fee billed. IT queries use the existing finding-title field with the device name removed. The private NetworkOps row reader now accepts typed rows; SQL and connection fallback are unchanged.
- Claims search six candidates with one per source, then retain at most three scoring at least 0.55. Each claim ID can appear on at most three selected situations within the role's matching call. Claims still visible after a relation-read failure consume their repeat allowance. Other roles get their own allowance while sharing the claim index. Empty results print `no claim above 0.55`.
- Prompt text, role wiring, coverage calculation and relation-line direction/wording are unchanged.

Gates authored in `MorningKnowledgeTests.cs`:

- Six kind fixtures check field-derived phrases and absence of the specified identifiers, plus doctrine terms, known-name removal, IT device removal and preserved printed text.
- A scrubber fixture checks emails, IDs, WBS, invoice numbers, ISO dates/timestamps, currency amounts and retained action meaning. It also covers a supplied name that is an email's local part. Invoice fixtures cover both band boundaries.
- Fake vectors put a 0.50 going-cold card ahead of a 0.70 generic card, preserve priority through formatting, check mixed `any` terms and moment `hour`, and retain cosine ordering without a specific match.
- Five situations check the three-appearance cap and backfill from candidates four through six, including a relation-read failure. Another role starts with a fresh allowance. Separate fixtures check 0.50 rejection and inclusive 0.55 acceptance.
- Existing 10e assertions remain, adapted for typed situations and pre-ranked formatter input. One assertion in `MorningBriefTests.cs` now accesses `.Printed`; this is required by the `MorningFact.Situations` type change.

Limits: name removal uses names supplied by the row (action owner name/key and IT device name). It does not infer unlabelled third-party names in action prose. Repeat tracking is by claim ID, not quotation text duplicated under different IDs. Synthetic gates do not establish semantic match quality or live source correctness.

Validation: `git diff --check` is clean. Gates were authored, not run; no build or test success is claimed. Unrelated pre-existing working-tree changes were left untouched.

The brief's live follow-up remains for Claude: compose the same 19 October situations, check #159 on the 140-day-quiet pursuits, inspect AR claims for collection/receivables relevance, and confirm no claim appears on more than three situations in a role.
