# Kor whole, round 2 — Codex response, 21 November 2026

Both W doors now allow, including when judged independently from their complete transcript prefixes without `Comply`. All 57 existing WholeGate cases remain unchanged; 54 added cases pass. The full Framework comparison has **zero new failing names**, under the deliberately database-blocked environment described below. This is not a claim that every remaining gate decision is correct.

Work started on `kor-whole-narrow` at **7914eeed**, the brief-only commit after 810604ed. Production changes are confined to `WholeGate.cs` and `WholeTranscript.cs`. No push, installation, database query/write, remote execution, holdout access, settings change, or installed hook change was performed.

The shared post-commit hook runs installed kor's `capture-git`, which writes the database. Each commit therefore used command-local `-c core.hooksPath=Kor.Operations.Framework.Tests/obj/whole-narrow-2/no-hooks`. No persistent Git configuration was changed.

**Cause and evidence**

The brief's “first program decides” explanation is wrong. The old `IsReader` already walked stages and accepted `cd`, simple assignments, chains and pipes; an existing test already proved `cd ... && grep ... | head` was a reader. The relevant failure class is **losing the origin of quoted text when command syntax, script indirection or path spelling changes**. The review also identified a separate class: crediting evidence to an unrelated population or source.

- **W L281 → L806:** the unquoted `Segment` regex split `awk -F'|'` and quoted awk source at shell-looking punctuation. It did not merely reject `cd`. The output at L282 is a listing of historical GuardRefusal rows, not new pad/test findings.
- **W L1031/1039 → L1047/1048 → L1158:** there are several causes. `$S` must be resolved; `/c/...` and `C:\...` must identify one file; `Out-File` and `(Get-Content ...).Count` are readers; and `recs.sql` was produced by **printf, not Write**. Its `%s` receives a digit-only ID list extracted by `grep -oE`, `tr`, `sort` and `paste`. Resolving a prefix alone cannot repair this case. L1048 contains stored descriptions of 44 records, not fresh results from their historical jobs.
- **Omitted W context:** even after removing the two cited sources, the complete prefixes still refused on L431 and L669. Those calls run `around.py` and `ctx.py`, created by successful quoted heredocs at **L221 and L481**. Their source loads JSONL transcript messages. The brief never mentioned these additional quoted-history flags. They now retain their source provenance on later invocation.
- **Review notes 1–3:** verified against the code and the actual review printed at **B1 L5537**, without querying review rows. Numeric coincidence accounted unrelated populations; TRX basenames conflated directories; assessment wording suppressed a checker's own summary. Each now has refusal counterexamples.
- **Review note 4:** confirmed. Slash replacement and case-insensitive lookup did not resolve `.`/`..`, cwd-relative paths or Git Bash drive paths.
- **Review note 5:** the old test did retain the listing's own count summary, but never ran the preceding real `testdiff` summary. The note is fair about that missing sequence. A new test supplies it before an incomplete listing; the old test remains unchanged.

Prior-art searches covered `tools/`, `docs/codex/` and the Framework CLI for WholeGate, replay, testdiff and refusal inspection. I read the complete gate, transcript parser, tests and CLI replay, both earlier briefs/response, and repository guidance. Existing `kor whole --replay`, `testdiff`, and the prior round's ignored inspection/moment helpers supplied the harness. No database inspection tool was run.

**Cases and commits**

There are **32 new MUST REFUSE and 22 new MUST PASS cases**, parameterized rows counted separately. Some pass cases additionally assert that W3 still refuses because disclosure is not opening. Fixtures are short, credential-free extracts or explicitly constructed counterexamples. All round-1 tests remain byte-for-byte present: the test-file diff has 239 added lines and zero deleted lines.

| Test suffix, omitting `Narrow2_` | Refuse | Pass | Source / boundary |
|---|---:|---:|---|
| `W806_ReadingRefusalRowsDoesNotCreateFreshFlags` | 0 | 1 | W L281/282 → L806; actual awk syntax and export shape, shortened scratch path |
| `W1158_TheEarlierPrintfSuppliesTheSqlProvenance` | 0 | 1 | W L1031/1039, L1047/1048 → L1158 |
| `ReaderStagesKeepQuotedSeparators` | 0 | 5 | Generalizations of W281: cd, Bash/PowerShell assignments, reader chains and pipes |
| `RealChecksBehindTheSameSyntaxStillRefuse` | 9 | 0 | Paired real checks after those prefixes, in a pipe, after a JSONL read, and inside substitutions/assignments |
| `UnprovenOrChangedPrintfSqlRemainsACheck` | 4 | 0 | W1031 counterexamples: denied write, arbitrary interpolation, SQL mutation, subsequent overwrite |
| `KnownSqlUsesResolvedPaths` | 0 | 4 | W1047 and review note 4: variables, cd-relative, dot segments and `/c/` spelling |
| `SqlProvenanceDoesNotExcuseOtherChecks` | 3 | 0 | Known read followed by a checker, another program, or a different source directory |
| `RatioDisclosureNeedsTheSameSubject` | 2 | 2 | Review note 1: widgets vs sprockets vs an unnamed count; same-subject disclosure still cannot lift W3 |
| `UnrelatedRatioCannotAccountForATestTotal` | 1 | 0 | Windows with the same numbers cannot account a test runner's failures |
| `BaselineListingMustNameTheSameTrxPaths` | 1 | 2 | Review note 2 / B1 L2457/2769: different directories vs same paths and normalized spellings |
| `RealSummaryThenPartialListCannotOpenAllFailures` | 1 | 0 | Review note 5: real summary first, then 41 of the 42 identities |
| `ACheckersAssessedQuoteIsStillItsOwnResult` | 3 | 0 | Review note 3: checker summaries and unattributed review summaries remain findings |
| `StoredTranscriptReadersKeepTheirProvenance` | 1 | 2 | Omitted W L221/431 and L481/669; appended real checker still refuses |
| `TranscriptScriptProvenanceIsNotJustItsFilename` | 4 | 0 | Unknown/denied/changed helper and a helper executing subprocesses |
| `NumericSqlInterpolationMustStillBeNumericAtTheWrite` | 1 | 0 | W1031 counterexample: replace the ID variable after the numeric pipeline |
| `PreviousSentenceDoesNotRenameTheDisclosedTestPopulation` | 0 | 3 | A2 L12103/L12176: prior sentence and actual bold Markdown must not become the ratio's subject |
| `AttributedCodeReviewExampleIsNotACheckerSummary` | 1 | 1 | B1 L5533/5537: attributed code example from a quoted-path review command vs the same output from a checker |
| `TestdiffDisclosureKeepsItsSubjectDespiteSummaryVocabulary` | 1 | 1 | B1 L5544/5548 → L5658: test disclosure vs another population, despite “VERDICT/NO NEW FAILURES” wording |
| **Total** | **32** | **22** | |

Test-only history, never amended:

| Commit | Cases | Why it exists |
|---|---:|---|
| `f772aa2a` — `kor whole narrow 2A: cases (codex)` | 39 | Committed before any implementation edit; unchanged gate: 75 pass / 21 fail out of 96 |
| `95833563` | 8 | Full-prefix proof exposed the omitted transcript-reader scripts; review also caught interpolation reassignment. Before the subsequent fix: 101 pass / 3 fail out of 104 |
| `ef4a3339` | 4 | Replay caught preceding-sentence attribution and the actual quoted-path code-review example. Before fixing: 105 pass / 3 fail out of 108 |
| `782f3f2f` | 1 | Preserve A2's actual bold Markdown, not just the simplified sentence. Focused pre-fix run: 2 pass / 1 fail |
| `f5a49bfa` | 2 | Preserve B1's actual testdiff disclosure while refusing an unrelated population. Focused pre-fix run: 1 pass / 1 fail |

The supplementary commits are additions, not amendments to A or changes from MUST REFUSE to MUST PASS. They make the full-prefix discoveries reviewable rather than silently changing the original fixtures. The last three additions also document shortcomings in my first implementation: matching the whole ratio window was too strict, and my initial short fixtures omitted actual punctuation or command quoting.

**Implementation**

`WholeGate.cs` now separates shell syntax outside quotes from literal argument text. Literal assignments and cwd changes are resolved within their command; executable assignments/substitutions remain checks unless the enclosed command is itself a reader. The known SQL reader is recognized stage by stage, so a following checker is not exempted.

`WholeTranscript.cs` retains successful quoted-cat heredoc and bounded printf source alongside its existing successful Write/Edit/MultiEdit cache. Failed writes invalidate that provenance. SQL format interpolation is accepted only for the recognized digit-list pipeline in an `Id IN (%s)` slot, and reassignment invalidates it. Known Python transcript-reader source can supply provenance to later calls; unknown helpers and explicit process execution remain checks. This is bounded source recognition, not a Python interpreter or a semantic proof of arbitrary code.

Paths are normalized lexically from the historical cwd and literal variables, including `.`/`..`, slash differences and Git Bash drive notation. TRX opening uses these path identities rather than generic basenames. It still requires an uncut listing of every distinct identity, the matching count, zero new failures and the same source set. W4's existing source-matching logic is unchanged.

Ratio disclosure now also requires matching distinctive subject words. Generic test-summary vocabulary and surrounding sentence/Markdown formatting are excluded; equal counts for an unrelated subject do not account the flag. Review quotation suppression requires a review source and an explicitly attributed document/code file. A checker's identical sentence is still measured.

W1–W4's doors, windows, and requirements are retained. Accounting still does not lift W2/W3. Existing record/replay command exemptions remain bounded by their old limitations; this change is not a wholesale shell-security parser replacement.

**Proof**

All builds/tests were scoped to Framework tests/CLI and their dependencies, in Debug. Builds were **not piped**; output was redirected to a file and the compiler's exit code was captured directly. Thus no pipeline could conceal a compiler failure. Final CLI build: **0 warnings, 0 errors**.

The brief requires the full Framework suite while forbidding database queries/writes. The suite contains live database reads and writes, including rolled-back transactions. For both sides I set **process-local** `KORBRAIN_SQL` and `KOR_ENGINEERINGTOOLS_STANDARDSDB` to a deliberately invalid nonempty value. `KorStandardsConnection.Parse` rejects it before constructing/opening a connection, and the nonempty process value prevents registry fallback. `TEMP`/`TMP` pointed into this worktree's ignored proof directory. No repository settings were changed. This is a full invocation with live dependencies blocked, not a green live integration run.

| Check | Before | Final |
|---|---:|---:|
| Existing WholeGate tests | 57 passed / 0 failed | Existing 57 still pass |
| WholeGate, original A on unchanged gate | 75 passed / 21 failed | — |
| WholeGate, all added cases | — | **111 passed / 0 failed / 0 skipped** |
| Full Framework tests | 952 passed / 244 failed / 0 skipped | **1006 passed / 244 failed / 0 skipped** |
| Full testdiff, by distinct name | 1190 names | 1244 names; **241 failing in both, 0 only in new, 0 missing base tests** |

All **54 added test names pass**. Of the 244 failures on both sides, 187 directly report the deliberate connection-parser rejection. The remaining 57 were individually inspected in the TRX comparison and are unchanged: EnrollCheck, DrafterPythonGuard and Recall cases, including downstream assertions on blocked database output. They must not be represented as failures caused by this change. Both full invocations completed; no blame sequence was requested, so `testdiff` correctly reports hung-case measurement as unavailable.

Commands run (proof directory abbreviated as `P` below):

```text
dotnet test Kor.Operations.Framework.Tests -c Debug --filter FullyQualifiedName~WholeGate -nologo --logger 'trx;LogFileName=whole-final.trx' --results-directory P
dotnet test Kor.Operations.Framework.Tests -c Debug --no-build -nologo --logger 'trx;LogFileName=new.trx' --results-directory P
dotnet build Kor.Operations.Framework.Cli -c Debug -nologo
dotnet run --project Kor.Operations.Framework.Cli -c Debug --no-build -- testdiff P/base.trx P/new.trx
dotnet <built-cli-directory>/kor.dll whole --replay <frozen-transcript>
```

The logger arguments were quoted in PowerShell. `testdiff` exited 0 with `NO NEW FAILURES`; both full test invocations exited 1 with the unchanged failures above. The CLI uses the `testdiff` verb, not `--testdiff`.

`P` is `Kor.Operations.Framework.Tests/obj/whole-narrow-2`. It retains the frozen manifest, TRXs, complete test/build/replay logs, individual-decision JSON and local inspection helpers. The unchanged CLI was copied to `P/baseline-bin` before implementation; final replay used this worktree's `Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll`, never installed kor. All twelve final before/after replay invocations exited 0. The final Framework assembly contains the new `cat transcript-record-text` string as UTF-16LE bytes.

```text
Framework DLL SHA-256, before:
4cc3be8e5e04a153364901dae3795241c17199b476de794e6289cee833bdec7f
Framework DLL SHA-256, final:
ca44aafbad776857d4cbb824db7f5a46b08e92fd3b3df9bf2862ba36de80388f
```

Sources A1–B2 are the same original paths listed in the round-1 response. W is `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\b49d2807-d400-4747-b0b9-bcc5e7e7af50.jsonl`. All six were frozen once for this round; B1 had grown since round 1.

| Transcript | Frozen lines | Doors | Flags/holes before → after | Refusals before → after |
|---|---:|---:|---:|---:|
| A1 / e7287342 | 7359 | 171 | 33 → 30 | 11 → 11 |
| A2 / 6b3b880f | 16897 | 226 | 80 → 81 | 17 → 17 |
| A3 / 4f85ab91 | 2783 | 90 | 23 → 11 | 9 → 7 |
| B1 / b6568d08 | 5762 | 111 | 20 → 17 | 2 → 2 |
| B2 / ee8754e5 | 4436 | 120 | 44 → 42 | 6 → 4 |
| W / b49d2807 | 1383 | 11 | 225 → 0 | 2 → 0 |
| **Total** | | **729** | **425 → 181** | **47 → 41** |

Every replay refusal line:

| Transcript | Before | After |
|---|---|---|
| A1 | 523, 2159, 2565, 2852, 2922, 3058, 3140, 4170, 4619, 5069, 6470 | 523, 2008, 2159, 2852, 2922, 3058, 3140, 4170, 4619, 5069, 6470 |
| A2 | 397, 608, 1017, 1929, 3906, 5343, 6019, 6693, 7041, 8817, 9354, 10072, 10681, 12885, 15175, 15376, 15716 | Same |
| A3 | 323, 561, 695, 1219, 1312, 1488, 2171, 2672, 2693 | 323, 561, 1219, 1488, 2171, 2672, 2693 |
| B1 | 1952, 2601 | Same |
| B2 | 296, 833, 930, 1212, 2158, 2390 | 296, 1212, 2158, 2390 |
| W | 806, 1158 | None |

I inspected the changed decisions, not just totals. A3's removed grounds were reader outputs at L457/L624; B2's were reader outputs at L718/L893/L907. A1's old L4122 flag also came from reading a commit message. **A1 L2008 is a conservative false refusal introduced by stricter label matching:** its evidence says `model-audit 6 of 14 PASS`, while the outstanding keys use `ACCEPTANCE RESULT NOT ACCEPTED`. The distinct words do not match. An exact-key disclosure would account it; neither disclosure would open the eight outstanding items. Replay then calls `Comply`, so those same findings no longer refuse at L2565. This is a remaining lexical-attribution limitation, not evidence of a newly repaired model fault.

Replay forgives named findings after refusal. I separately evaluated these **20 individual moments without Comply**, each with a fresh ledger from the complete prefix and the compiled Framework DLL beside the built CLI. Board-door SQL was supplied from its preceding historical Write, rather than today's file. Hook logging and database calls were not invoked.

| Actual moment | Before | After |
|---|---|---|
| W L806 | W1 | **Allow** |
| W L1158 | W1 | **Allow** |
| A1 L6470 | W2 | W2 |
| A1 L6490 | W2+W1 | W2+W1 |
| A1 L6516 | W3 | W3 |
| A2 L10567 | W4 | W4 |
| A2 L10681 | W4+W1 | W4+W1 |
| A2 L10873 | Allow | Allow |
| A3 L2763 | W1 | W1 |
| B1 L2601 / 9150 | W3+W1 | W3+W1 |
| B1 L2830 / 9195 | Allow | Allow |
| B1 L3438 / 9201 | Allow | Allow |
| B2 L3412, L3428, L3445 / 9198–9200 | Allow each | Allow each |
| B2 L3832, L3843, L3863, L3884, L3893 / 9202–9206 | Allow each | Allow each |

Frozen input SHA-256 values:

```text
A1 18b8a2a00cce7924bd88d3990ee4b394893d69071ae3edf1c9e7062f80f027ba
A2 01c9dbe2aef91f97f58c9831a92227a26992e1faa6e92ac6897c52f5867abdd9
A3 1e0b485e14d72d270889727bfb1ffef0a8681ac1162c0c93eba4671934530cb3
B1 aa9b75c8aec79ae2d5e4dc52a1333a60eacd14fea0a0e322973290ef2252a7d0
B2 b33813035b6c0b016577fa629b98dc6d738e23690848bc8f99af9bb320c4b2eb
W  98fd16470dfb0705dc555e49459e4546e15ff4dd8c6751bc6a35b1a40ab67a52
```

**Rule 11: coverage, limits and a same-class miss**

The harness compares actual gate decisions, retained findings, disclosure versus opening, quoted shell separators, known-script provenance, lexical path identity, and unrelated-population/source counterexamples. Its data is local; the complete WholeGate suite is one command, and full replay plus independent moment decisions inspect the actual output. There is no graphical deliverable here to render. The full Framework comparison additionally checks every test name and identifies missing/new failures.

It does **not** establish arbitrary shell/Python/SQL semantics, script honesty, filesystem identity through links, content identity of TRX files, omitted inputs never mentioned, complete source inventories, unrecognized failure wording, cross-session state, live database integration, installation/hook logging, or unseen holdouts. Successful recognized writes are the cache's evidence; arbitrary external edits or unrecognized shell overwrites can leave that cache incomplete or stale. Variables are resolved only within their command. Generic record/replay exemptions still have command-wide blind spots. Python transcript recognition uses source conventions and rejects explicit execution/write forms; it cannot prove a dynamically constructed program is only a reader.

Subject matching is lexical, not semantic. A1 L2008 demonstrates the resulting false refusal. Conversely, an author could reuse the same label for different populations, and the gate would not know. An uncut unrelated read can still satisfy the older OPENED line-count test. W4 still matches source basenames. General CLI replay can still consult today's SQL file for board-door text; the individual historical decisions avoid that ambiguity.

**Same-class fault still missed:** a checker measures only five pads, prints the five measurements with no population count or recognized failure wording, and the session says “all pads are four-sided.” There is no recognized flag or numbered sampled-range claim, so the partial look still passes as the whole. `4 pads irregular` is another concrete unrecognized flag form. Nothing in the proof upgrades the gate into a completeness proof.

**What the brief gets wrong or omits**

The prefix explanation mistakes a quote-blind splitter for a first-program classifier. The SQL file was created by printf, and W contained additional transcript-reader flags. The branch was at 7914eeed, not 810604ed. The review notes were real, but a full Framework test run cannot exercise its live database tests while honoring the database prohibition; the blocked-dependency comparison is the explicit compromise. Replay's automatic compliance is not independent evidence at every later door. The held-back evidence cannot be assessed from this brief, and no claim is made about it.

Process: one Codex session, no subagents, no remote/model runs or permission questions. Additional local iterations were driven by failed cases and full-prefix/replay regressions. The response and both production files accompany the B commit. The repeated lesson in this round is concrete: preserving real punctuation, script creation history and the full prefix found faults that shortened fixtures alone missed. Those discoveries were committed separately rather than rewriting A.
