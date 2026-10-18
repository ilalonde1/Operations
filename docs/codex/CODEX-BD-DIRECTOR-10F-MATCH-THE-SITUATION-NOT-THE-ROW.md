# BD Director 10f: match knowledge to the situation, not to the data row

Bounded brief for Codex. Source edits and gates only. You build nothing, run nothing and deploy nothing. The response
goes beside this file as `CODEX-BD-DIRECTOR-10F-MATCH-THE-SITUATION-NOT-THE-ROW-RESPONSE.md`. `git diff --check` must be
clean.

## What 10e does, measured live 18 Oct for 19 Oct

10e (commit a124597a) attaches cards and book claims to every morning situation: 20 of 20 BD, 16 of 16 COO and 10 of 10
IT. Match quality depends on the role:

- **IT is good.** A "disk missing" finding pulls RAID, restore-time and array-log claims.
- **COO is mixed.** AR invoice rows get the right card (#210, chase receivables by age), but the claims are noise:
  "2/10 net 30 discount terms", a real-estate tax law claim, and mortgage weighted-average maturity.
- **BD is weak.** A pursuit 140 days quiet gets #129, #245 and #183. The card written for exactly this case, #159
  "Revive a quiet relationship by naming the silence" (`appliesTo` includes `situation:going-cold`), is not in the top
  three. The same two Trusted Advisor claims appear on every one of the 10 stale pursuits.

**The cause.** `MorningKnowledgeReader` embeds the printed row: client names, WBS numbers, invoice numbers, emails,
dates and dollar amounts. The local model then matches on those tokens instead of on what is happening. IT rows match
well because their text is already the situation ("A drive has disappeared").

## The fix, in `tools/MorningBrief/MorningKnowledge.cs` and the situation selection in `MorningFacts.cs`

1. **A situation is a kind plus a meaning, not a row.**

   Add a pure `MorningSituation(string Kind, string Query, string Printed, IReadOnlyCollection<string> Situations)`:
   - `Printed` is the row text exactly as today. It is shown to the reader.
   - `Query` is what gets embedded and searched.
   - `Situations` holds the doctrine situation terms used by `BdDoctrine.AppliesTo`.

   Build `Query` per kind from the row's fields, never from its printed text:

   | Kind | Query (example) | Situations |
   |---|---|---|
   | stale pursuit | "A client pursuit has gone quiet: no contact for {N} days{, no proposed fee}. Revive a quiet relationship, or decide to stop pursuing." | `going-cold` (+ `never-touched` when there was no touch after opening) |
   | late action | "A promised follow-up is overdue by {N} days: {action text with names, ids and dates removed}." | `late-action` |
   | AR invoice | "An invoice to a developer client is {N} days unpaid ({under 5k / 5–25k / over 25k}). Collect old receivables, or decide on a write-off." | none (`any` cards still rank) |
   | over budget | "A project is {P}% over its engineering-hours budget with {B}% of the fee billed. Diagnose the overrun and recover fee through change orders." | none |
   | partial timesheets | "Timesheets are incomplete for recent weeks; utilization and billing are understated." | none |
   | IT finding | the finding title(s) as today, without device names | none |

   Removing identifiers is a pure helper with a gate. It strips emails, `#digits`, WBS patterns (`\d{5}-\d{2}`),
   8-digit invoice numbers, ISO dates and currency amounts.

2. **Rank cards with the doctrine's own vocabulary first.**
   - Cards whose `appliesTo` matches the situation (`BdDoctrine.AppliesTo` with a `DoctrineContext` built from
     `Situations`, moment `hour`) come first, ordered by cosine score.
   - Fill the remaining places, up to 3, from the cosine ranking of all accepted cards for the role.
   - `any` cards rank by cosine only. They must not crowd out a specific match.
3. **Claims carry a floor and a repeat cap.**
   - Keep `SearchClaimsAsync(query, take: 6, perSource: 1)`, and show at most 3 claims scoring 0.55 or higher.
   - A claim already shown on 3 situations in the same role is replaced on later situations by the next-best claim,
     so the same quote does not appear ten times.
   - Print "no claim above 0.55" rather than nothing.
4. **Leave unchanged:** the 10e output format, the coverage counts, the relations lines ("contested" and "refined by"),
   the prompt text, the IT role and the NetworkOps fallback.

## Gates (authored, not run), in `MorningKnowledgeTests.cs`

1. **Query builder.** One fixture row per kind. Each query contains its kind's phrase. None contains an email, WBS
   number, invoice number, ISO date, dollar amount or `#id`.
2. **Doctrine-first ranking.** Use a fake embedder in which a generic card scores 0.70 and a `situation:going-cold`
   card scores 0.50. For a stale-pursuit situation the going-cold card ranks first. For an AR situation, ranking is
   by cosine.
3. **Claim floor and repeat cap.** One claim that scores highest for 5 situations is shown on the first 3 only. A
   situation whose best claim scores 0.50 prints "no claim above 0.55".
4. **The 10e gates stay green.** Update them only where the query text changed.

**These gates do not measure real match quality.** After apply, Claude runs the composer live for the same 19 October
situations and checks:
- **BD:** #159 appears for the 140-day-quiet pursuits.
- **COO:** AR claims are about collection and receivables.
- **Repeats:** no claim appears on more than 3 situations in a role.

## Read bound (about 25 KB)

| File | Range |
|---|---|
| `tools/MorningBrief/MorningKnowledge.cs` | all |
| `tools/MorningBrief/MorningFacts.cs` | the situation selection added in 10e (search `MorningKnowledge`) and the row records it reads |
| `Kor.Opportunities.Data/Crm/BdDoctrine.cs` | 30–95 (`DoctrineContext`, `AppliesTo`, `Terms`) |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/MorningKnowledgeTests.cs` | all |

Do not open databases, the books or APP01.
