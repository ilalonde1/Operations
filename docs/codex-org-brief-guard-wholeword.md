# Codex prompt — broaden brief-guard safe-redirect to whole-word brand extensions

One change in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs`, `FindRicherSameBrandCanonicalAsync` (the `RedirectSafe` computation, ~line 750-790). No `dotnet build`/`test` (env hangs); Claude verifies the build, Ian publishes the app.

## Why
The current `RedirectSafe` requires the longer name's remainder to begin with a **corporate token** (development/properties/holdings/...). That is too strict for legitimate **brand extensions** where the second word is a distinctive brand word, not a corporate suffix:
- `Concord` -> `Concord Pacific` (same company; "Pacific" is not a corporate token) is wrongly downgraded to suggest-only.

The correct "same company" signal is a **whole-word prefix**: the shorter org's full name is a leading sequence of WHOLE words of the longer org's name. That keeps the safe cases AND still rejects sub-word coincidences:
- `Concord` -> `Concordia University` must stay unsafe ("Concord" is a fragment of the single word "Concordia", not a whole word).
- `Pacific` -> `Pacifica Properties` must stay unsafe.

## Change — broaden the third `RedirectSafe` condition
Keep the first two requirements unchanged: it must be a prefix match (one normalized name is a leading substring of the other) AND `MinLen >= 6`. Replace the third requirement (currently "remainder begins with a corporate token") with an **OR** of two signals:

`RedirectSafe = IsPrefix AND MinLen >= 6 AND ( WholeWordPrefix OR RemainderIsCorporate )`

where:
- **WholeWordPrefix** (the new, primary signal): comparing the two **DisplayName**s case-insensitively and trimmed, the shorter DisplayName followed by a single space is a leading prefix of the longer DisplayName. Compute with LEFT (not LIKE, to avoid wildcard-injection from names containing `%`/`_`/`[`):
  - let `shortDisp` / `longDisp` be the trimmed-lowercased DisplayNames, chosen so `shortDisp` is the one with the smaller LEN;
  - `WholeWordPrefix = (LEN(shortDisp) < LEN(longDisp) AND LEFT(longDisp, LEN(shortDisp) + 1) = shortDisp + N' ')`.
  - `Concord` vs `Concord Pacific`: `LEFT('concord pacific', 8) = 'concord '` => true => safe.
  - `Concord` vs `Concordia University`: `LEFT('concordia university', 8) = 'concord '`? No (`'concordi'`) => false.
- **RemainderIsCorporate** (keep the existing logic): the remainder of the longer NORMALIZED name (longer minus the shorter prefix) begins with one of the shared `CorporateToken` values. This still covers cases where the DisplayName lacks a space but the normalized prefix holds.

Leave the `CorporateToken` CTE, the brand-stem suggestion path, the ordering (`RedirectSafe DESC, Richness DESC, r.Id`), the `RicherCandidate` struct, and the `GetOrgBriefAsync` decision (`if (rc.RedirectSafe)`) unchanged.

## Must-pass (verify by reasoning)
| requested (thin) DisplayName | candidate DisplayName | outcome |
|---|---|---|
| Concord | Concord Pacific Development Corp. | AUTO-REDIRECT (whole-word) |
| Greystar | Greystar Development | AUTO-REDIRECT |
| Anthem | Anthem Properties | AUTO-REDIRECT |
| Wesgroup | Wesgroup Properties | AUTO-REDIRECT |
| Concord | Concordia University | suggest only (sub-word, not whole-word; remainder not corporate) |
| Pacific | Pacifica Properties | suggest only |
| Bosa | Bosa Properties | suggest only (MinLen 4 < 6) |
| Greystar Development | Greystar Real Estate Partners | suggest only (brand-stem, not a prefix) |

## Constraints
Keep it in the one SQL statement + existing C# shape; don't touch other brief methods. Keep `CommandTimeout`, recursion guard, richness ordering. ASCII. Read-only (no DB writes).

After Codex confirms: Claude builds Kor.Opportunities.Data + the App; Ian publishes the WPF app.
