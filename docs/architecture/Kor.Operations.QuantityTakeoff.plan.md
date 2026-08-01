# Quantity Takeoff & Issue Delta — Build Plan

**Status:** Proposed (pre-build). Nothing in this plan is implemented yet.
**Author:** Drafted with Claude for Ian Lalonde, 2026-06-24. Rev 2 (post measure-twice review).
**Audience:** Read this first, approve it, then we build it one Codex prompt at a time.

---

## 1. What we're building (plain language)

A new tool inside the app's **Engineering Tools** window. You point it at a project's
drawings for two different issues — e.g. **IFT Addendum** and **IFC** — and it produces a
**level-by-level table of how much concrete and rebar changed** between them.

The business reason: when a client (e.g. Rize on 5380 Heather) asks *"why did my concrete
and rebar budget jump?"*, we hand them defensible **quantities** (m³ of concrete, tonnes of
rebar, m² of formwork) per level, instead of a vague written list. We give quantities only —
the client's estimator applies dollar rates.

It reuses the PDF-reading engine we already built and trust for SAFE/ETABS import
(`EngineeringTools/PdfToSafe`). **We are not changing that engine.** We add a separate module
that *reads its output* and turns geometry into volumes, weights, snapshots, and a comparison.

---

## 2. What success looks like

- Engineer opens **Engineering Tools → Quantity Takeoff**, picks the Deltek project (WBS1).
- Loads the prepared drawing sheets for an issue (one plate per level), confirms scale and
  per-slab thickness, enters a storey height per level, confirms grades / rebar densities,
  clicks **Compute**.
- Gets a per-level table of concrete m³ (split by grade), rebar t (estimated), formwork m² —
  **with a confidence flag on every line**. Reviews the flagged lines, saves it as a
  **snapshot** tagged to WBS1 + issue label + date + the method/basis used.
- Repeats for the second issue.
- Picks the two snapshots → gets a **delta table** (added/removed levels surfaced explicitly)
  and a one-click client report (.docx + .xlsx).
- A human reviews and signs off before anything goes to a client. The tool accelerates and
  standardises the work; it does not replace engineering judgement.

---

## 3. The zero-regression contract

Regression risk is controlled **structurally**, not by hope. The feature is its own module;
existing code is read-only to us.

### 3a. Files we ADD (new — cannot break anything that exists)
```
Kor.Operations.App/EngineeringTools/QuantityTakeoff/
    TakeoffMeasurement.cs            (PUBLIC DTO: element area/length/section/thickness/grade)
    TakeoffModels.cs                 (records: snapshot, line, density table, confidence)
    VolumeCalculator.cs              (pure math: TakeoffMeasurement[] -> m3 / kg / m2)
    TakeoffExtractionAdapter.cs      (ExtractedGeometry -> TakeoffMeasurement[]; read-only reuse)
    TakeoffDiffService.cs            (compare two snapshots; match/added/removed levels)
    TakeoffReportGenerator.cs        (HTML + .docx + .xlsx output)
    QuantityTakeoffWindow.xaml(.cs)  (the UI)
    QuantityTakeoffViewModel.cs
Kor.Operations.App/EngineeringTools.Tests/QuantityTakeoff/
    VolumeCalculatorTests.cs         (hand-calc golden tests — pure, no PDF dependency)
    TakeoffDiffServiceTests.cs
Kor.Operations.Data/
    SqlTakeoffSnapshotStore.cs       (mirrors SqlFinancialPortfolioSnapshotStore)
```

**Key separation:** `VolumeCalculator` operates on the **public `TakeoffMeasurement` DTO**, not
on the internal `ExtractedGeometry`. The adapter is the only thing that touches the PdfToSafe
types. This makes the math trivially unit-testable and fully decouples the new feature from the
extraction engine.

### 3b. Files we MODIFY — additive only, 3 tiny edits, done LAST
1. `EngineeringTools/EngineeringToolsWindow.xaml` — add one new card button (alongside the
   existing `PdfToSafeCard`).
2. `EngineeringTools/EngineeringToolsWindow.xaml.cs` — add one `OpenQuantityTakeoff_Click`
   handler (copy of the existing `OpenPdfToSafe_Click`, 4 lines).
3. `CompositionModules/AppModule.cs` — register the new window + services in DI.

Each is purely additive. No existing line's behaviour changes.

### 3c. Files we DO NOT TOUCH (load-bearing — read-only reuse only)
- `PdfToSafe/PdfToSafeWindow.*` (the 127 KB interactive tool engineers rely on)
- `PdfGeometryExtractor.cs`, `PolygonProcessor.cs`, `ThicknessAnnotationParser.cs`,
  `ColumnSectionParser.cs`, `BeamSectionParser.cs`, `F2kModelPrep.cs`
- All export code: `F2kWriter`, `EtabsE2kExporter`, `SafeApiExporter`, `Sap2000ApiExporter`,
  `ExportOrchestrator`, CSI OAPI drivers, `DxfExporter`
- `StructuralMaterialDatabase.cs`

We **call** the static methods on these. We never edit them.

### 3d. Test gates (every prompt must pass before the next)
- Full solution **builds** clean.
- Existing **test suite stays green** (proves we changed nothing existing).
- New **unit tests pass** — including hand-calc goldens (e.g. 10 m × 10 m slab @ 200 mm =
  exactly 20.0 m³; with a 3 m × 3 m opening = 18.2 m³; a 1.5× drop panel adds its delta).

---

## 4. Reuse map (existing code we call, read-only) — all verified present

| Need | Existing API we call | File |
|---|---|---|
| Extract geometry from a PDF page | `PdfGeometryExtractor.Extract(path, scale, page, slabMin, lineMin, excludeGridLines)` | PdfGeometryExtractor.cs |
| Apply colour classification | `PdfGeometryExtractor.ReclassifyByColor(...)` | same |
| Detect drawing scale | `PdfGeometryExtractor.DetectScale(path)` | same |
| Slab thickness from text | `PdfGeometryExtractor.ExtractThicknessHints(...)` + `ThicknessAnnotationParser.AssignToSlabs` | same / ThicknessAnnotationParser.cs |
| Polygon area (mm²) | `PolygonProcessor.PolygonAreaMm2(poly)` | PolygonProcessor.cs:41 |
| Openings (voids to subtract) | `PolygonProcessor.DetectOpenings(...)` | PolygonProcessor.cs:190 |
| **Drop panels / thickenings** | `PolygonProcessor.DetectDropPanels(slabs, columns, candidates)` + `DropPanelThicknessMultiplier` | PolygonProcessor.cs / ExportSettings.cs:77 |
| Per-sheet setup (colours, scale, overrides) | `PdfToSafeProject.Load(path)` (the saved `.ktsafe` json) | PdfToSafeProject.cs:36 |
| Concrete grades | `StructuralMaterialDatabase` | StructuralMaterialDatabase.cs |
| **Project picker (WBS1)** | reuse `ProposalProjectPickerDialog` / `DeltekLookupService` | Crm/DeltekLookupService.cs |
| Tabular report shape | `HtmlReportBuilder`, `ExportSummaryRow` | HtmlReportBuilder.cs |
| **.xlsx output** | `ClosedXML` 0.105 (already referenced) | — |
| .docx output | `DocumentFormat.OpenXml` 3.5.1 (already referenced) | — |
| SQL snapshot pattern | copy `SqlFinancialPortfolioSnapshotStore` | Kor.Operations.Data |

**`ExtractedGeometry` shape (confirmed):** `Slabs` (mm polygons), `Columns` + `ColumnSizes`
(W×D mm), `Lines` (mm polylines) + `LineSectionHints` (W×D mm), `DropPanelCandidates`. All mm.
**Tests:** `InternalsVisibleTo("Kor.Operations.EngineeringTools.Tests")` is set, so the adapter
can be tested against internal types if needed — but the math layer never needs it.

---

## 5. The volume math (exact, units explicit, with the accuracy guards)

All geometry is in **mm**. Volume in mm³ ÷ 1e9 → **m³**. Area in mm² ÷ 1e6 → **m²**.

**Concrete**
- **Slab:** `(PolygonAreaMm2(slab) − Σ openingAreas) × thicknessMm`
- **Drop panel / thickening:** added separately via `DetectDropPanels`, using the parent
  thickness × `DropPanelThicknessMultiplier` (default 1.5) over the drop footprint — **minus**
  the base slab already counted there, so we add only the *incremental* concrete. **This is
  essential: transfer slabs / drop panels are the likely cost driver and uniform-thickness
  would undercount them.**
- **Wall:** `polylineLengthMm × thicknessMm(=Width) × storeyHeightMm`
- **Beam/band:** `polylineLengthMm × WidthMm × DepthMm`
- **Column:** `WidthMm × DepthMm × storeyHeightMm`

**Formwork (m²)** — nearly free from the same geometry:
- Slab soffit = net slab area; wall faces = length × height × 2; column faces = perimeter ×
  height; (beam sides as applicable).

**Concrete by grade** — group every element's volume by `GradeCode`. A grade change between
issues is a cost driver invisible to total volume; the report shows m³ per grade.

**Rebar (estimate)** — `concreteVolumeM3 × densityKgPerM3` → kg → t, per element type from a
**configurable density table** (flat slab 80–120, walls 90–150, columns 200–350, transfer
slabs 250–400+). Every rebar value is labelled `source = density`. If a modelled/scheduled bar
source is ever confirmed, `source = modeled`.

### Accuracy guards (baked into the calculator/snapshot, not optional)
1. **Double-count convention:** slabs counted gross; walls/columns counted separately. The
   small slab/column overlap is documented and consistent, so it largely cancels in a *delta*.
   The convention is recorded on the snapshot.
2. **No silent thickness defaults.** A slab with no resolved thickness is **flagged
   `Unresolved`**, not silently set to 200 mm. Unresolved lines are excluded from totals until
   the engineer sets a value.
3. **Scale must be confirmed.** `DetectScale` is a suggestion; the UI requires the engineer to
   confirm per sheet (scale error squares into area). Snapshot records the scale used.
4. **Confidence flag per line:** `{thicknessResolved, scaleConfirmed, openingsDetected}` →
   `High | Review`. The engineer reviews the `Review` lines; totals show how much volume is in
   `Review` state. This is what makes the output defensible.
5. **Same-basis comparison.** Each snapshot stores its method/basis (conventions, density
   table, scale handling). The diff **warns** if two snapshots were taken on different bases.
6. **Coverage checklist.** Each issue records which levels/sheets were included; the diff shows
   any level present in one issue and missing in the other (added/removed), never drops it.

---

## 6. Data model

**Public DTO** (`TakeoffMeasurement.cs`) — what the math consumes, decoupled from PDF types:
- `ElementType` (Slab/Wall/Beam/Column/DropPanel), `Level`, `AreaMm2?`, `LengthMm?`,
  `WidthMm?`, `DepthMm?`, `ThicknessMm?`, `StoreyHeightMm?`, `GradeCode`, plus QA inputs.

**Core records** (`TakeoffModels.cs`):
- `TakeoffSnapshot` — Id, ProjectWbs1, IssueLabel, IssueDateUtc, BasisJson (conventions +
  density table + scale handling), CoverageJson (levels/sheets included), CreatedUtc, CreatedBy, Notes
- `TakeoffLine` — SnapshotId, Level, ElementType, GradeCode, ConcreteM3, FormworkM2, RebarKg,
  RebarSource (`density`|`modeled`), Confidence (`High`|`Review`), SourcePdf, SheetPage
- `RebarDensityTable` — element type → kg/m³ (editable; stored with the snapshot for audit)

**SQL** (`SqlTakeoffSnapshotStore`, KorTransmittalsDb, mirrors FinancialPortfolioSnapshot;
`EnsureSchemaAsync` idempotent `CREATE TABLE IF OBJECT_ID(...) IS NULL`):
- `dbo.TakeoffSnapshot` (header) · `dbo.TakeoffLine` (FK → snapshot)

Audit lineage on every snapshot: source PDF, scale, densities, conventions, operator,
timestamp — that is what makes the number stand up if challenged.

---

## 7. Honest limitations (so nobody oversells it)

- Extraction is **semi-interactive per sheet** (colour mapping, reclassification, exclusions,
  nearest-annotation thickness). Accurate **with engineer QA**; not unattended batch.
- Quality depends on drawings being cleanly layered/coloured. Varies by set.
- Storey heights, scale confirmation, and grade/density are human inputs.
- Rebar is an **estimate** until a modelled source is confirmed.
- Preparing a full tower × two issues is **front-loaded labor** the first time (see fast-follow
  on prepared-sheet reuse). Set expectations on the first 31065 run.
- A human signs off. The tool standardises and accelerates; it does not replace judgement.

---

## 8. Codex prompt sequence (one at a time)

Each prompt is one PR-sized, independently buildable + testable chunk. Risky UI/wiring is
**last**, after all logic is proven by tests. Do not start a prompt until the prior one is
green.

1. **Core + math.** `TakeoffMeasurement.cs` (public DTO) + `TakeoffModels.cs` +
   `VolumeCalculator.cs` (slab/opening/drop-panel/wall/beam/column volume, formwork, by-grade,
   rebar-by-density, confidence flags, no-silent-default) + `VolumeCalculatorTests.cs` with
   hand-calc goldens. No DB, no UI, no edits to existing files.
2. **Persistence.** `SqlTakeoffSnapshotStore` (idempotent schema, insert, load, list-by-project)
   with Basis + Coverage stored, mirroring `SqlFinancialPortfolioSnapshotStore`.
3. **Diff.** `TakeoffDiffService` + tests: snapshot A vs B → delta by level/element/grade,
   added/removed levels surfaced, different-basis warning.
4. **Report.** `TakeoffReportGenerator` → HTML + `.docx` (OpenXml) + `.xlsx` (ClosedXML)
   matching the client-response layout (quantities only, caveats, confidence summary).
5. **Extraction adapter.** `TakeoffExtractionAdapter`: load `PdfToSafeProject` + PDF, call the
   existing static `PdfGeometryExtractor` / `PolygonProcessor` methods, map to
   `TakeoffMeasurement[]`. Read-only reuse; no edits to PdfToSafe. One tiny sample-PDF fixture
   for a characterization test.
6. **UI.** `QuantityTakeoffWindow` + view model: project picker (reuse existing), load sheets,
   confirm scale/thickness, enter storey heights/densities, compute, review flagged lines,
   save snapshot, pick two issues, diff, export.
7. **Wire-in (the only edits to existing files).** Engineering Tools card + handler + DI
   registration. Smoke-test end to end.

After each prompt: confirm build green, existing tests green, new tests green, and that no
files outside the prompt's stated scope were modified.

---

## 9. Fast-follows (Phase 1.5 — after v1 ships, separate prompts)

- **Category attribution:** tag each delta line to the change-list category (City Comments /
  RFI / coordination) so the report shows what drove cost *and who owns it*.
- **Prepared-sheet reuse:** persist the per-sheet `.ktsafe` setups per project/issue so the
  next comparison is incremental — turns this into the repeatable service Rory wants.

## 10. AI policy — where AI is allowed, and where it is banned

**Banned: the measurement path.** Areas, volumes, weights are computed by deterministic
geometry math (`VolumeCalculator`). No LLM ever produces or adjusts a number. Same inputs →
same m³, every time. That reproducibility is the whole basis of "defensible."

**Allowed: the setup / interpretation path, human-confirmed.** Exactly the pattern PdfToSafe
already uses — `PdfGeometryAnalysisService` sends the rendered page to Claude Vision only to
*suggest which colours are slabs / walls / columns*; PdfPig still provides the precise
geometry. The takeoff adapter can **reuse that same existing service** to speed sheet setup.
AI may later also assist with sheet→level mapping and thickness-annotation fallback. In every
case AI only proposes; the engineer confirms; deterministic code measures.

**Sequencing: deterministic core first, AI second.** Prompts 1–7 ship a complete, AI-free
tool. Layering AI afterward is mostly *wiring in the existing vision service* via the adapter —
not new AI work — so deferring it costs nothing and keeps the trustworthy core clean.

## 11. Optional later (not core — and explicitly NOT the COO card)

- MCP read tool `get_material_takeoff_delta` — lets the in-app AI answer "summarise the
  concrete/rebar delta for project X between issues" over stored snapshots. Read-only, low
  risk, purely additive. A takeoff is a per-project, on-demand, client-facing artifact — not a
  firm-level daily exec metric, so it does **not** belong on the COO card.

## 12. Decisions still open (do not block prompt 1)

1. **Concrete source of truth** for the *delta*: PDF-geometry engine (works from archived
   issued PDFs — no dependency on old Revit models) vs. Revit schedules. Plan assumes the PDF
   path; revisit before prompt 5 if Revit is preferred.
2. **Rebar fidelity:** confirm whether bar is modelled to weight anywhere. Until then, rebar
   stays a labelled density estimate (handled by `RebarSource`).
