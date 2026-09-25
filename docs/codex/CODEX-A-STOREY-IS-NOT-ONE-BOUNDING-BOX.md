# CODEX — a storey holding two towers is scored as one wide building

**One defect. Open only the files named here.**

## The file and the line

`Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs`, the donor-plate block that begins

```csharp
if (options.InferMissingFloors && options.IncludeFloors)
```

and specifically:

```csharp
.Select(x => (x.Storey, x.Plates, Likeness: x.Plates.Max(pl => pl.Where.LikenessTo(standingOn, inch))))
```

where `standingOn` comes from `ownExtents[storey.Name]` and is a single `Extent` — one bounding box
around every member on that storey.

## The rule that has to hold

> A storey may only borrow a floor that its own structure stands on.

## The shape of the failure, measured 2026-09-25

`31065-01` is a **two-tower** site. Both towers appear on the same storey; the storey names carry no
building tag, so `E2kDocument.BuildingTagOf` returns nothing for either.

`standingOn` for `L17` is therefore one wide box spanning **both** towers *and the ground between
them*. Consequences, in order:

1. The site-wide podium plate (~22,646 sq ft) is also a wide rectangle, so `LikenessTo(standingOn)`
   scores it near-perfect.
2. Each tower's own plate (~6,991 and ~671 sq ft) is small against that box and scores poorly.
3. The podium wins. Ten storeys — `L6, L7, L9, L11, L13, L15, L17, L20, ROOF, L5` — each received
   one ~22,646 sq ft plate.
4. The right-hand tower, the one that actually has no floor on those storeys, **still has none.**

Net: about **226,000 sq ft of plate the engineer does not have**, and the real hole untouched. Our
reproduced-area percentage would rise while the model got further from her building.

Rendered evidence, both arms, every storey on one sheet: `31065-before.png` and `31065-after.png`
(produced with `takeoff model-render` from `Baselines/pdf-only-31065-01.e2k` and the six-set run's
`out.e2k`). In BEFORE, `L13/L15/L17` show two towers with the right one carrying columns and no
floor. In AFTER, the same storeys carry one wide plate across the whole site and the right tower is
still bare.

## Why the existing guards do not catch it

- `DonorPlateLikenessMargin` picks among candidates that already scored well. It cannot help when
  the wrong candidate scores best.
- The "a storey that HAS a floor may not be handed one several times bigger" comment describes
  intent; the test is `Where.CoverageOf(standingOn) >= 0.5`, and the podium plate covers that
  combined box handsomely.
- `E2kDocument.BuildingTagOf` is the repo's building discriminator and returns empty here, because
  this set separates its towers by geometry, not by storey name.

**No bounding-box test can fix this.** The podium plate lies *inside* the combined box. The
information that distinguishes it is that a large part of it has no member standing on it.

## The question

Given the storey's members (positions and footprints, not just their bounding box), what is the
cheapest correct predicate that refuses the podium plate for `L17` and still accepts a genuine
typical-floor donor?

Two shapes worth weighing, and a third if you see one:

- **Cluster the storey's members** and require the donor plate to resemble one cluster's extent
  rather than the union's.
- **Coverage the other way round**: require that some fraction of the DONOR PLATE's area has a
  member of this storey standing on or near it. The podium spans the gap between towers, where
  there are none.

State which you would use, why, and what it costs per storey. `31039-01` (35 storeys built, 7
floored) and `30989-01` (27 built, 7 floored) are the sets the fix is FOR; `31065-01` is the set it
must not break.

## What NOT to do

- Do not widen or narrow a threshold. The defect is an ORDER-of-information problem, not a tuning
  one, and this repo has a standing rule for it: **compose the site once, CUT AFTER**. The donor is
  being chosen before the building cut.
- Do not propose turning the feature off. It is already off — opt-in behind `KOR_STEP148_ON`. The
  question is what makes it right.
- Do not judge a rule on one set. The target set is in `docs/pdf-intake/step148-prediction.md`:
  32 sets, 130 unfloored storeys, 112 of them with no drawing placed at all.
- A reasoned refusal is a valid answer. The last three audits each produced one and each was right.
