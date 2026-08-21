# DXF → ETABS — what changed on 2026-08-21, and what it means for your session

**You are picking this up in the Andrea-guided ETABS session. Read this before you touch the module;
another session changed four things in it today. Nothing here changes the geometry pipeline.**

Single commit: **`8341372b`** — *"The one-pager stops being an error page, and the gate starts reading
the artifact"*. Everything below is in that commit unless noted.

Full context lives in `docs/audit-2026-08/` — `START-HERE.md`, then `04-TODO-REGISTER.md`, which
carries a `status` on all 182 audit items and is the single arbiter of what is done. Evidence for
every status change is in `WORKLOG.md`.

---

## 0 · READ THIS FIRST — two things collide with what you are doing

Written after reading where your session actually is: the `exportdxf` verb on `KOR.Drafter.Bridge`,
500 Foster (30783-01) vs Andrea's finished model, per-job layer flags, and the workbook front page.

**Your git view is stale.** Your last message said *"both repos are still clean at `6ce3e428`"*.
Operations `HEAD` is now **`1151fcf5`** — thirteen commits later, same working tree, same `develop`.
Run `git log --oneline -15` before any git operation. Nothing of yours was touched; every commit
staged named paths in the App, FileSync, TakeoffCli and EngineeringTools, plus `docs/`.

**Collision 1 — the front-page test is now stricter, and it is on your side.**
You said *"five tests walk every question against the front page, which no longer holds them all."*
`TheFrontPageCarriesOnlyWhatSheHasToRead` used to assert only `Assert.NotNull(rules)`. It now asserts
that **every `ForTheRecord` question's setting keys appear in *Rules in force* with provenance
`question <code>`**. That is exactly the design you described — register moves off the front page and
into *Rules in force* — so the test now **enforces** the thing you were about to build rather than
fighting it. But it will fail loudly if a question leaves the front page without landing in *Rules in
force*. Suite is `483/483` as of `8341372b`.

**Collision 2 — the three layer-pattern keys are now REQUIRED.**
`DxfToEtabsService.cs:350` now passes `RequiredRuleKeys` (35) instead of `builtIn.Keys` (32). The
three additions are `dxf.wall-layer-patterns`, `dxf.column-layer-patterns`, `dxf.slab-layer-patterns`
— the same three your per-job layer flags work touches. They are confirmed present in `KorStandards`
`[QUERIED]`, so nothing breaks today. **But if per-job flags introduce a new rule key, it must be in
`RequiredRuleKeys` or a run will stop** — that is now the guarantee, not a warning.

---

## 1 · Four things changed

| # | change | why it matters to you |
|---|---|---|
| **26** | `DxfToEtabsService.cs:350` — `RuleSettings.LoadRequired(...)` now receives **`RequiredRuleKeys`** (35 keys), not `builtIn.Keys` (32) | The three missing keys were `dxf.wall-layer-patterns`, `dxf.column-layer-patterns`, `dxf.slab-layer-patterns` — **the rules that decide what counts as a wall**. They now sit inside the "a missing rule stops the run, there is no fallback" guarantee. Confirmed present in `KorStandards` on `KOR-APP01\SQLEXPRESS`, so this cannot stop a run `[QUERIED]`. |
| **27** | 3 `ModelQuestionnaireTests` that had been red since `72c1a2ca` are green. Suite is **483/483** | All three were stale tests, not product defects — checked one by one, not assumed. Two came back **stronger**: `TheFrontPageCarriesOnlyWhatSheHasToRead` was asserting `Assert.NotNull(rules)` and now asserts every `ForTheRecord` question's setting keys appear in *Rules in force* with provenance `question <code>`. **If you are changing which questions are `ForTheRecord`, or the front-page filter, that test will now catch you.** |
| **24** | `docs/KOR-DxfToEtabs-onepager-web.pdf` re-rendered | It was **59 KB of Edge "File not found"**, and `Publish-EtabsModel.ps1:231` copies it into client job folders. It had been shipping. |
| **25** | `Publish-EtabsModel.ps1:432-441` — the one-pager count gate now parses **the shipped PDF**, not the source HTML | It also fails on browser-error markers, and fails when it finds **zero** count claims — an error-page PDF yields no claims, so a gate that only checked claims passed it in silence. Proven: the old PDF pulled back out of git trips both guards; the new one trips neither. |

## 2 · One change outside the module that you should know about

`tools/Format-BdWebPdf.ps1` — the house HTML→PDF renderer — had the actual root cause of §24. It
waited only for the output file to **exist**, so re-rendering over an existing PDF saw the stale file
immediately, deleted the temporary source, and shipped the old bytes. It now waits for the output's
`LastWriteTime` to pass the render start and **throws** if the render never landed.

**If you re-render any dossier or one-pager, this is why it now takes a moment longer, and why it can
now fail instead of silently succeeding.**

## 3 · What did NOT change

- No change to geometry, classification, composition, or any `.e2k` emission.
- No change to the rule values themselves.
- The job folders were **not** re-published — see below.

## 4 · Still open in this module — with the two that matter most first

| # | item | who |
|---|---|---|
| **130** | **Have an engineer import a generated `.e2k` into ETABS and sign off.** The register calls this *the largest untested surface in the module*, and its tier is `[DOC]` — an assertion, never observed. **Nobody has done it.** | Ian |
| **129** | Put one **unfamiliar** building through end to end. Two buildings, both used to tune it, is not evidence about a third. Named as the single largest gap | either |
| 43 | Re-publish 31168 and 31138 from HEAD — both job folders are 5 commits stale; 31138's workbook is missing its *Rules in force* sheet, 31168 has no summary PDF | Ian (share write) |
| 175 | Make the seven keyless DECIDED workbook rows (`C1 F2 M1 M2 O1 P1 S2`) legible | either |
| 176 | Tighten `LiveProjectBaselineTests` tolerance below 10%, or delete it | either |
| 177 | Make the publish gate fail loudly when `pdftotext` is absent | either |
| 178 | Read `CIRCLE` entities, or name the skipped circle-drawn column in the report | either |
| 128 | Move `KOR_ENGINEERINGTOOLS_STANDARDSDB` off a personal env var | Ian |
| 131 | Schedule `Measure-EtabsCorpusRules.ps1` / set `KOR_PORTFOLIO_CHECK` | Ian |

## 5 · Standing answer 7, now confirmed from the database

A Revit-exported DXF yields **walls but no columns and no floors**. Revit writes `A-WALL` / `S-COLS` /
`A-FLOR`; the rules expect `WALL` / `_COL` / `SLABEDG`. Queried live on 2026-08-21 `[QUERIED]`:

    dxf.wall-layer-patterns    WALL
    dxf.column-layer-patterns  _COL
    dxf.slab-layer-patterns    SLABEDG

Because these are database rules rather than C# constants (since 2026-08-15), **closing this is a
settings change, not a code change** — add the Revit layer names to those three rules.

## 6 · If the engineer gives you feedback, it belongs in the register

Item 130 exists precisely because this module has never had engineer observation. Feedback that stays
in a chat window does not close it. Put it in `docs/audit-2026-08/04-TODO-REGISTER.md` with an
evidence tier — `[RUN]` if they ran it, `[DOC]` if they only said it — and the evidence in
`WORKLOG.md`. **That is the single most valuable input this module can receive.**
