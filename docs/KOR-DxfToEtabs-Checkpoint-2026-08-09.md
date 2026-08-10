# Checkpoint — what "verified" meant before, and what it means now

Written because the same sentence was said three times — *both models publish clean, all tests
green, nothing outstanding* — and each time something fundamental was found afterwards. This
records what was actually broken, what fixed it, and the structural reason the earlier verification
could not have caught any of it.

---

## The three rounds

### Round 1 — I said it was verified. An outside reviewer found five defects in an hour.

State claimed: 430 tests green, both projects publish clean, ready to send.

| # | What was wrong | How bad |
|---|---|---|
| 1 | 24 duplicate wall placements and 19 duplicate columns on 31168, same points, same section, same storey | doubled stiffness and self-weight |
| 2 | The dossier's counts contradicted the models it shipped beside | the document did not describe the file |
| 3 | **31138's questionnaire asked about 31168** — its stepped block, its storeys, its plate areas | she opens the workbook and is asked to rule on the wrong building |
| 4 | The workbook said headers were 24″ deep; the writer used storey height − opening, 24–60″ | she is asked to approve a number that is not what shipped |
| 5 | `NothingIsModelledTwice` only ever checked floor plates, never walls or columns | the test name was a lie |

All five reproduced independently, all five fixed (`7777b290`). Kept verbatim in
`KOR-DxfToEtabs-IndependentAudit-2026-08-09.md` along with the prompt that produced them.

### Round 2 — I said the audit's findings were all fixed. A second pass found four more.

State claimed: 430 tests green, both publish clean, five questions outstanding.

| # | What was wrong | How bad |
|---|---|---|
| 6 | **The ground floor of both 31168 towers was empty.** A mezzanine reads as the level it sits above, so `LEVEL 1 PLAN` and `LEVEL 1 PLAN MEZZ` both parsed as level 1, and the mezzanine took both sheets — 45 walls and 67 columns built one storey too high | she opens the model and a whole floor is missing |
| 7 | Same fault on 31138: a mezzanine part plan built into L01 | 11 walls and 18 columns on the wrong storey |
| 8 | Two headers of different depths stacked over one opening on A‑LEVEL 33 — headers were the last member still deduplicating on their placement storey | the class the audit had just fixed, in the one member it missed |
| 9 | The dossier's **prose** was two rounds stale while its table was right: 897 walls, 2,418 columns, 124 plates, 233/181 reused against 273/237, and one test suite described as both "410 tests" and "384 tests" | every number she might quote back was wrong |
| 10 | The dossier's HTML source existed **only in a session temp folder** | the shipped document could not be regenerated or corrected |

Fixed in `8d8944e5`. Numbers 6 and 7 are the important ones: **nothing in any count was wrong.**
The geometry was modelled, just on the wrong storey, so every total looked healthy. It was found by
rendering the model and looking at a white band in the elevation.

### Round 3 — I built an audit. It immediately caught my own mistakes.

| # | What was wrong | Whose fault |
|---|---|---|
| 11 | The first shape check **compared the model against the classifier that built it** — two sides, one source. It passed unchanged with round-column detection disabled entirely | mine, in the checking |
| 12 | Capture window required both ends of a segment inside it, producing five confident "drawn inside 0x10" findings | mine, in the checking |
| 13 | Compared a section's length to the drawn box's *side* instead of its *diagonal*, calling an 8×43 turned 45° oversize inside a 36×36 box | mine, in the checking |
| 14 | An 8×38 column — more slender than any column either engineer uses — produced where a footprint paired with nothing | real, caught by the audit |
| 15 | Header depths every one 2–4″ too deep: 84″ assumed for a door where her own spandrels imply 86–88″ | real, found by measuring her model |
| 16 | The decomposer refused any face under 48″, so corner limbs never formed — the wall-versus-column rule leaking into shape decomposition for the second time | real, 47 walls missing on 31138 |
| 17 | My own verification of #15 was wrong — it read a single storey's `HEIGHT` where a header spanning interleaved tower storeys spans their sum | mine, in the checking |
| 18 | My claim "no generated column is more slender than 3:1" was too broad — 42 exist on 31138, all with column-layer linework under them, because drafting drew them | mine, in the claiming |

---

## The model, then and now

| | 31168 at round 1 | 31168 now | 31138 at round 1 | 31138 now |
|---|---|---|---|---|
| Storeys populated | 61 | **63** | 23 | **24** |
| Wall panels | 900 | **925** | 88 | **136** |
| Columns | 2,407 | **2,464** | 165 | **180** |
| Floor plates | 83 | 83 | 11 | 11 |
| Headers | 141 | **139** | 8 | **22** |
| Pier labels | 123 | **129** | 64 | **109** |

Tests 430 → **440**. Questions for the engineer **5 → 3**.

---

## Why the old verification could not have caught any of it

Everything before round 3 checked the model **against itself**:

- `dotnet test` — the generator agreeing with the generator.
- Counts in the report — produced by the same code that produced the model.
- The publish gate — timestamps, and whether a true number appeared *somewhere* in the dossier.
- My reading of the file — the same assumptions that wrote it.

None of that can see a member built on the wrong storey, because the totals stay correct. It cannot
see a wrong size either, because the object exists and is counted. Two of the four ways a model can
be wrong were structurally invisible, and nobody was looking at them.

### What is different now

**1. The four classes are named, so coverage is arguable rather than anecdotal.**

| Class | Visible in a count? | Closed by | State |
|---|---|---|---|
| Dropped | yes | drawn → modelled reconciliation | 36 of 4,229 unaccounted, ratcheted |
| Doubled | yes | `ModelIntegrityTests`, now including headers | 0 |
| **Misplaced** | **no** | modelled → drawn, and no empty storey between populated ones | 0 |
| **Misclassified** | **no** | built size and shape against **raw** DXF entities | 0 |

**2. The checks compare against an independent source.** The drawings, not the model. For shape,
raw column-layer segments with the arc flag off the DXF entity type — never the classifier's own
shape decision. Defect 11 is what happens when that rule is broken: the check agreed with itself
and proved nothing.

**3. Both directions.** *Every drawn member is modelled* catches drops. *Every modelled member
stands on linework from a sheet placed on its own storey* catches inventions. Neither alone is
enough.

**4. Every gate is proven against the defect it exists for, before it is trusted.**

| Gate | With the fix reverted, it says |
|---|---|
| No empty storey between populated ones | `B-LEVEL 1, A-LEVEL 1 sit between populated storeys with nothing on them` |
| Size and shape against raw linework | `KC217 drawn with straight lines only but built round as KOR-D10` |
| Nothing modelled twice | `31168: 2 member(s) modelled on top of another` |
| Dossier counts | ten stale prose claims plus the table row |

**5. Rules come off the engineers' own models instead of being assumed.** 88″ opening measured off
her 29 spandrels, not a standard door. 20–60″ depth range from the same. 3:1 slenderness because her
most slender column is 12×36 and there is nothing beyond it. Panels down to 9″ because hers go that
short.

**6. The audit caught regressions I introduced during this session** — three of them, within
minutes. That is the only real test of a gate.

---

## What is still not covered — read this before trusting it further

- **Wall thickness is not checked against the linework.** Only its centreline and existence are.
  The same treatment as columns would close it.
- **Slab thickness and plate extent are not checked against the drawing at all.**
- **36 drawn members remain unaccounted** (7 on 31168, 29 on 31138). They cluster on sheets the
  report already flags for outlines that will not close. Ratcheted, so they may only come down.
- **Engineering judgement is not checkable** and should not be: whether a stepped block is one pier,
  whether the parkade plate is right, whether a storey needs a floor drawn. Those are the three
  questions left.
- **Neither model has been re-imported into ETABS since these changes.** The last confirmed clean
  import was the version before the mezzanine fix.

## The pattern worth remembering

Of the eighteen defects above, **five were in the checking rather than in the model** (11, 12, 13,
17, 18). A check is not free of the fault it looks for. Every one of those was caught only by
deliberately breaking the code and confirming the check screamed — which is now the rule, not a
habit.
