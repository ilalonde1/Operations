# BD Director 10e: each morning situation arrives with the knowledge that fits it, and IT gets a doctrine

Bounded brief for Codex. Source edits, one migration file and gates only. You build nothing, run nothing, apply
nothing and deploy nothing. The response goes beside this file as `CODEX-BD-DIRECTOR-10E-KNOWLEDGE-MEETS-THE-DAY-RESPONSE.md`,
with one section per part: what changed, the gate authored and not run, and anything in the code that contradicted
this brief. `git diff --check` must be clean.

## Why

The morning composer (`tools/MorningBrief`) hands the writing session a list of accepted card titles, and nothing
links a situation to the knowledge that fits it. The session has to search for itself, so whether the right card or
passage is found depends on the session. Measured 17 Oct:
- **Doctrine:** 190 accepted cards (BD 135, Operations 55).
- **Claims:** 21,757 at the start of the session, embedded with the local `bge-small-en-v1.5` model. More are being
  added tonight.
- **Relations:** relations between claims are being written now (`knowledge.ClaimRelation`, kinds Supports,
  Contradicts and Refines).
- **IT:** IT has no doctrine at all. The prompt says "IT moves must have cards: []", and the Role CHECKs only allow BD
  and Operations.

This brief does two things. It makes the composer attach, to each concrete situation, the accepted cards and book
claims closest in meaning, with any contradicting or refining claims. It also opens a third role, `IT`, so the
Systems Director can have doctrine the same way the other two roles do.

**Never an API.** Matching uses the same local embedder the books are searched with
(`Kor.Opportunities.Knowledge/Embedding/KnowledgeEmbedder.cs`, ONNX, no network).

## Part 1: situations meet knowledge, in the composer

1. **Reference.** `MorningBrief.csproj` adds a ProjectReference to `Kor.Opportunities.Knowledge`. The **App** must still
   not reference it; MorningBrief is a console tool.
2. **The situations.** Situations are the concrete rows a move could address, taken from facts the composer already
   reads, with these caps:
   - **BD:**
     - up to 10 stale pursuits (the "No touch in 14+ days" fact);
     - Ian's late actions, and then any other late actions, up to 10 in total.
   - **COO:**
     - the first 10 invoice rows of the "AR > 60 Days" KPI;
     - the first 5 rows of "Projects Over Budget";
     - the "Partial timesheet weeks" fact as one situation, if any week is partial.
   - **IT:** the first 10 open-finding groups, by the existing severity order.

   Each situation is the row's own text (the line already printed). Add no new reads.
3. **Matching. Write a pure part and an I/O part.**
   - **Cards.** Embed each accepted card of the role once per run. The text is `Title. Plain. Recognise.`, using the
     columns of `opportunities.DoctrineCard`. For each situation, take the 3 accepted cards of its role with the
     highest cosine score (`KnowledgeSearch.Cosine`). Print each score to 2 decimals. Apply no threshold; the scores
     are shown so the session can judge.
   - **Claims.** Call `KnowledgeSearch.SearchClaimsAsync(situation, take: 3, perSource: 1)`.
   - **Relations.** For the returned claim ids, read their relations with one new store method,
     `ListRelationsAsync(IReadOnlyCollection<long> claimIds)`, on `IKnowledgeStore` and `SqlKnowledgeStore`. It reads
     `knowledge.ClaimRelation` rows where either end is in the set, and joins each other end's statement and source
     title. Use a typed parameter list, chunked as the store already does elsewhere.
4. **Output.** Each role gets one fact, `Knowledge matched to today's situations`, with X of Y coverage, where Y is
   the number of situations. Each situation is one line:
   `<situation, trimmed to 160 chars> → cards #210 (0.71), #209 (0.64), #213 (0.58); claims: [<book>] "<statement>" (0.78) …;
   contested: [<book>] "<statement>" (<note>); refined by: …`.
   A failure sets CouldNotRead and must not stop the other facts. The pure formatter takes the matches and returns
   the lines, so it can be tested.
5. **Prompt** (`MorningPromptComposer.cs:20`):
   - Say the matched cards and claims are **candidates found by meaning**: read a card in full before citing it, and
     other accepted cards of the role may still be cited.
   - Say a "contested" line means the books disagree, and the move should say which side applies and why.
   - Replace "IT moves must have cards: [] because there is no IT doctrine" with "IT moves may cite only accepted IT
     cards below".
   - Change the `doctrine show` hint to list `--role IT` as well.
6. **IT cards.** `GatherItAsync` ends with `await CardsAsync(facts, "IT", ct);`.

## Part 2: a third doctrine role, IT

1. **Migration `Kor.Opportunities.Data/Schema/367_ItDoctrineRole.sql`.** It is guarded and idempotent, in the style
   of 366. It drops and re-creates `CK_DoctrineCard_Role` and `CK_BdPromptRun_Role` (362, lines 32 and 51) as
   `Role IN (N'BD', N'Operations', N'IT')`. It changes no data.
2. **Every hard-coded role list** (searched 17 Oct). Allow `IT` in each:
   - `tools/BdKnowledgeIngest/Program.cs:270`, the duplicates check, so it lists IT cards as well;
   - `Program.cs:680` and `:686`, the `--role` validation;
   - `Kor.Opportunities.Data/Crm/BdPromptComposer.cs:57`, the role phrase. IT becomes "a systems and IT director for a
     sixty-person engineering firm".
   - `Kor.Opportunities.Data/Crm/DirectorHour.cs:203-208`, `ValidateCards`. Remove the IT exclusion, so an IT section
     may cite accepted IT cards and nothing else. Keep the COO → Operations mapping.
   - `Kor.Operations.App/BusinessDevelopment/Director/BdDoctrineViewModel.cs:52` and `:103`. Roles become
     `BD, Operations, IT`. Line 103's "other role" duplicate check must list **both** other roles, not swap between two.
   - `BdRhythmStore.HourKinds` and `CK_DirectorHour_Kind` are hour kinds, not doctrine roles. Leave them.
3. Change no card text and no existing card.

## Part 3: the IT read on APP01

**Measured 17 Oct.**
- **The watch login:** `networkops_watch`, the login in `KOR_NETWORKOPS_WATCHDB` on FS01, can SELECT exactly 1
  object, `NetworkOps.ServiceHeartbeat`. That is by design: `db/KorNetworkOps/003_WatchdogLogin.sql` keeps the
  credential on FS01 "worth as little as possible".
- **APP01:** it has no `KOR_NETWORKOPS_WATCHDB`. It does have `KOR_NETWORKOPS_DB`, the service's own login on the same
  server as the database.

So the composer's IT reads (Findings, Devices, Actions) fail on APP01. Do not widen the watch login.

**Fix** (`tools/MorningBrief/MorningFacts.cs`): the NetworkOps connection becomes
`OptionalConnection("KOR_NETWORKOPS_WATCHDB") ?? Connection("KOR_NETWORKOPS_DB")`.
- Keep the composer's SQL strictly SELECT, as it is now.
- Its coverage line names which variable was used. Print the variable's name only, never its value.

**Gate.** As source text: the IT connection resolves WATCHDB first, then DB. No `INSERT`, `UPDATE`, `DELETE`, `MERGE`
or `EXEC` appears in any `NetworkRowsAsync` SQL.

## Gates (authored, not run)

1. **Pure formatter.** Synthetic situations, cards, claims and relations give:
   - exactly 3 cards per situation, in score order, with 2-decimal scores;
   - contested and refined lines that carry their notes;
   - the long situation text trimmed to 160 characters;
   - coverage X of Y.
2. **Card text and ranking.** The embedded text is `Title. Plain. Recognise.`. The top-3 ranking uses
   `KnowledgeSearch.Cosine`. A fake embedder returning fixed vectors fixes the order.
3. **Store SQL, as text.** `ListRelationsAsync` filters `FromClaimId IN` or `ToClaimId IN` a parameter list, with no
   string-built ids.
4. **IT role.**
   - `ValidateCards` accepts an IT section citing an accepted IT card and refuses a BD card in an IT section.
   - The migration text has the three-value CHECK for both constraints and is guarded.
   - The CLI accepts `--role IT`.
   - The prompt text no longer contains "there is no IT doctrine".
5. **App reference.** No App csproj references `Kor.Opportunities.Knowledge`. Extend the existing reference gate if
   there is one; otherwise add a text check.

**Each gate's summary says what it does not cover.** None of these gates measures match quality. A situation can
score its best card at 0.40 and still print. After apply, Claude runs the composer live, reads every matched line for
the 19 Oct situations, and records which matches were right.

## Read bound (about 40 KB)

| File | Range |
|---|---|
| `tools/MorningBrief/MorningFacts.cs` | all (about 420 lines) |
| `tools/MorningBrief/MorningPromptComposer.cs` | all |
| `Kor.Opportunities.Knowledge/Search/KnowledgeSearch.cs` | all (about 115 lines) |
| `Kor.Opportunities.Knowledge/Store/IKnowledgeStore.cs`, `SqlKnowledgeStore.cs` | the interface; 290–340 and one existing chunked read |
| `Kor.Opportunities.Data/Crm/DirectorHour.cs` | 195–210 |
| `Kor.Opportunities.Data/Crm/BdPromptComposer.cs` | 50–60 |
| `Kor.Opportunities.Data/Schema/362_BdDoctrineAndPromptRuns.sql`, `366_MorningHour.sql` | all |
| `tools/BdKnowledgeIngest/Program.cs` | 260–275, 675–690 |
| `Kor.Operations.App/BusinessDevelopment/Director/BdDoctrineViewModel.cs` | 45–110 |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/MorningBriefTests.cs` | the 10d gates, for conventions |

Do not open databases, the books or APP01.
