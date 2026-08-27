# Codex prompt — harden the org-brief thin/duplicate guard (prefix false-positives + brittle brand-stem)

One change in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs`, method `FindRicherSameBrandCanonicalAsync` (~line 748) and the auto-redirect decision in `GetOrgBriefAsync` (~line 721). No `dotnet build`/`test` (env hangs); Claude verifies the build, Ian publishes the app.

## Why
The guard auto-redirects a thin org's brief to a "richer same-brand" canonical when one normalized name is a prefix of the other and the shared name is >= 6 chars. Two residual problems:

1. **Coincidental-prefix false positive.** A thin org named exactly a generic word can be a prefix of a *different* company: `concord` -> `concordiauniversity`, `pacific` -> `pacificaproperties`. MinLen>=6 does not catch these (both prefixes are >= 6). It would wrongly auto-redirect to an unrelated firm.
2. **Brittle brand-stem list.** The brand-stem CROSS APPLY hardcodes company-specific suffixes (`developmentwest`, `realestatepartners`) that were clearly fitted to specific orgs (Greystar). This does not generalize and is a code smell.

## Fix — one shared "corporate continuation token" list, used by both checks
Define a single set of corporate words (a derived `VALUES` table inside the query):
`properties, property, developments, development, devcorp, devgroup, group, holdings, capital, homes, residences, construction, contracting, builders, building, realestate, realestatepartners, partners, ventures, investments, equities, communities, land, ltd, inc, incorporated, corp, corporation, llc, llp, lp`

**(A) Auto-redirect must be "safe prefix".** Add a `RedirectSafe` bit to the candidate. It is 1 only when ALL hold:
- it is a prefix match (one normalized name is a leading substring of the other), AND
- `MinLen >= 6`, AND
- the **remainder** of the longer name (the longer normalized name with the shorter prefix removed) **begins with one of the corporate tokens** (i.e. the longer name is literally `<shorter><CorporateWord...>`). Example: `greystar` -> `greystardevelopment` remainder = `development` (corporate) => safe; `concord` -> `concordiauniversity` remainder = `iauniversity` (not corporate) => NOT safe.

**(B) Brand-stem: drop the hardcoded one-offs.** Replace the `WHEN base.TBase LIKE '%development' ...` CASE ladder with: strip the longest matching trailing corporate token (from the shared list) off each base name, then compare the stems (still require stem length >= 6). Remove `developmentwest` and `realestatepartners` special cases entirely.

**(C) Decision in `GetOrgBriefAsync`.** Auto-redirect iff `rc.RedirectSafe`. Any other match (prefix-but-unsafe, or brand-stem) becomes the existing suggestion note ("a likely primary record for this company may be #N ... review for merge") — never a silent redirect. So: add `bool RedirectSafe` to the `RicherCandidate` record struct; compute it in SQL; change the redirect condition from `rc.IsPrefix && rc.MinLen >= 6` to `rc.RedirectSafe`. Keep `IsPrefix`/`MinLen` available if useful for the suggestion text, or drop them if unused.

## Must-pass behavior (verify by reasoning, not a test run)
| requested (thin) | candidate (richer) | outcome |
|---|---|---|
| greystar | greystardevelopment | AUTO-REDIRECT (remainder `development` corporate) |
| anthem | anthemproperties | AUTO-REDIRECT |
| wesgroup | wesgroupproperties | AUTO-REDIRECT |
| concord | concordiauniversity | suggest only (remainder not corporate) |
| pacific | pacificaproperties | suggest only |
| bosa | bosaproperties | suggest only (MinLen 4 < 6) |
| greystardevelopment | greystarrealestatepartners | suggest only (brand-stem `greystar`) |

## Constraints
- Keep it all in the one SQL statement + the small C# decision; do not touch other brief methods. Keep `CommandTimeout`, the recursion guard, and the richness ordering (`ORDER BY RedirectSafe DESC, Richness DESC, r.Id` is fine). ASCII only. Read-only (no DB writes).

After Codex confirms: Claude builds Kor.Opportunities.Data + the App; Ian publishes the WPF app.
