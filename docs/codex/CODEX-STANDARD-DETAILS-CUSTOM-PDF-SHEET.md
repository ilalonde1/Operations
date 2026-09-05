# CODEX — Standard Details: Revit-free "Create PDF sheet" for engineers

## Goal

Let any engineer compose a personal working sheet from approved details **without Revit**. Today the
Sheet Composer has one output — `Save_Click` → `_composer.ComposeAsync(...)` — which writes a governed
ViewSheet into the AUTHORING Revit model through the bridge. That is correct for the gatekeeper, but a
senior engineer with no Revit installed still needs to lay details out and get a usable sheet.

Add a second, parallel output: **"Create PDF sheet"** that stamps the already-captured per-detail
vector PDFs onto a page laid out exactly as the on-screen canvas shows, writes a PDF to disk, and opens
it. No bridge, no Revit, no governance write. It is an **uncontrolled personal working copy**, clearly
stamped as such so it can never be mistaken for the governed master.

Everything needed already exists: the 600+ per-view vector PDFs live in `detail.RenderedImage.Pdf`
(served by `KorStandardsReadRepository.LoadRenderedPdfAsync("detail", detailNumber)` — the same store
the static Open-PDF path uses), and the composer canvas already holds each placement's position, size
and aspect. This feature is pure PDF assembly bolted onto the existing layout state.

## The layout state you build from (all in `SheetComposerWindow.xaml.cs`, do not re-derive)

- Sheet is `SheetWidthMm = 914.4` × `SheetHeightMm = 609.6` (a 36"×24" sheet). Canvas origin is
  **top-left, Y increasing downward** — the same convention PdfSharp's `XGraphics` uses, so there is
  **no Y flip**.
- `_placements` is `ObservableCollection<ComposerPlacementDisplayRow>`. Each row has `DetailNumber`,
  `CanonicalViewName`, `ImageAspect` (source width/height), and the editable `XmmText` / `YmmText`
  (placement **center**, in mm) / `ScaleText`.
- The on-sheet box for a row is already computed by the existing private helpers — **reuse them, do
  not reinvent**:
  - `ReadPlacementCenter(row)` → `(X, Y)` centre in mm.
  - `ComputePlacementSizeMm(row)` → `(Width, Height)` in mm (width from scale, height = width /
    ImageAspect, with the same 0.9·sheet-height cap the canvas applies).
  - So the box, top-left origin: `left = X - Width/2`, `top = Y - Height/2`, size `Width × Height` mm.
    This is byte-for-byte the rectangle the canvas draws the thumbnail into, so the PDF matches the
    canvas by construction.

## The design

### 1. PDF assembly — new pure class `CustomPdfSheetComposer` (StandardDetails, no WPF types)

Add package **`PdfSharp` 6.1.1** (MIT, net8-compatible; `XGraphics`/`XPdfForm`/`XPdfForm` live in
`PdfSharp.Drawing`, PDF page import in the core package) to `Kor.Operations.App.csproj`. Do **not** pull
in `PdfSharpCore` or `System.Drawing.Common`.

Signature (keep it UI-free and testable):

```csharp
internal sealed record CustomSheetPlacementSpec(
    string DetailNumber,
    byte[]? DetailPdf,     // null => art not captured; stamp a labelled placeholder box
    double LeftMm, double TopMm, double WidthMm, double HeightMm);

internal sealed record CustomSheetSpec(
    double SheetWidthMm, double SheetHeightMm,
    string? SheetNumber, string? SheetName,
    string AuthorLabel, DateTime GeneratedUtc,
    IReadOnlyList<CustomSheetPlacementSpec> Placements);

internal static class CustomPdfSheetComposer
{
    // Returns the composed PDF bytes. Pure: no disk, no dialogs, no bridge.
    internal static byte[] Build(CustomSheetSpec spec);
}
```

`Build`:
- `mm → pt` is `mm * 72.0 / 25.4`. Page: one page sized `SheetWidthMm×SheetHeightMm` in points (36"×24"
  → 2592×1728 pt). Draw a thin full-page border rectangle.
- For each placement with `DetailPdf` non-empty: load it as an `XPdfForm` (prefer `XPdfForm.FromStream`
  if available in the referenced PdfSharp version; otherwise spool the bytes to a temp file, load with
  `XPdfForm.FromFile`, and delete the temp files after `Save`). Draw it with
  `gfx.DrawImage(form, leftPt, topPt, widthPt, heightPt)`. The box already carries the source aspect
  (height = width/ImageAspect), so it will not distort; **but** if the form's own MediaBox aspect
  differs from `widthPt/heightPt` by more than ~2%, fit-inside the box preserving the form's aspect and
  centre it (letterbox) rather than stretching.
- For each placement with `DetailPdf` null/empty: draw a light placeholder rectangle at the box with the
  detail number and "art not captured" centred, so the layout is still honest about what is missing.
- Footer band along the bottom margin, small font, muted colour:
  `"UNCONTROLLED WORKING COPY — {SheetNumber} {SheetName} — generated {GeneratedUtc:yyyy-MM-dd} by
  {AuthorLabel} from the KOR Standard Details. Not the governed standard."`
  This stamp is mandatory — it is the only thing preventing a personal PDF being passed off as the
  master.
- Save to a `MemoryStream`, return the bytes.

### 2. Wire it into the window — `CreatePdfSheet_Click`

New handler in `SheetComposerWindow.xaml.cs`, modelled on `Save_Click`/`OpenPdf_Click` but touching
**no** `_composer`/bridge:
1. If `_placements` is empty → info message, return.
2. Build `CustomSheetPlacementSpec` for every placement: `ReadPlacementCenter` + `ComputePlacementSizeMm`
   → the box; `await _catalogRepository.LoadRenderedPdfAsync("detail", row.DetailNumber)` → the bytes
   (may be null). `AuthorLabel` = the app's current user display name if readily available, else the
   Windows username; `GeneratedUtc = DateTime.UtcNow`.
3. If **any** placement came back with no PDF, show a `YesNo` warning listing those detail numbers
   ("These details have no captured art and will appear as labelled placeholders: … Continue?"). On No,
   return.
4. `var bytes = CustomPdfSheetComposer.Build(spec);`
5. Offer a `Microsoft.Win32.SaveFileDialog` (filter `PDF|*.pdf`, default file name from `SheetNumber`
   sanitised, else `KOR-Custom-Sheet-{yyyyMMdd-HHmmss}.pdf`, default dir = the user's Documents). If the
   user cancels, return quietly. Write the bytes to the chosen path.
6. Open it in the default viewer with `ProcessStartInfo { FileName = path, UseShellExecute = true }`
   (same mechanism the static Open-PDF path already uses). Wrap the open in try/catch → on failure, tell
   the user the file was saved at `<path>` but could not be opened.
7. Drive `SummaryText` / `ToggleBusy` exactly like the neighbouring handlers.

Do **not** set `DialogResult = true` here — creating a personal PDF should not close the composer, so the
engineer can keep laying out and export again.

### 3. Button + labels (XAML, `SheetComposerWindow.xaml`)

- Add a **"Create PDF sheet"** button in the footer next to the existing compose/Open-PDF buttons.
  Tooltip: "Builds a personal PDF from the captured detail art. No Revit needed — an uncontrolled
  working copy, not the governed standard."
- To remove the standing ambiguity between the two outputs, relabel the existing compose button
  (`Save_Click`) to **"Save to master"** with tooltip "Writes a governed sheet into the master model.
  Requires Revit with the AUTHORING template open (gatekeeper)." Do not change its behaviour.

## Constraints
- Do not touch `ComposeAsync`, `OpenSheetPdfAsync`, or any bridge/authoring code. The new path must never
  call the bridge — that is the whole point.
- Reuse `ReadPlacementCenter` and `ComputePlacementSizeMm` for the geometry; the PDF must match the
  canvas, so it must use the same numbers the canvas uses.
- The uncontrolled-working-copy footer stamp is not optional.
- `#nullable enable` is on; build gate is warnings-as-errors — no new warnings. Clean up any temp files
  the fallback `XPdfForm.FromFile` path creates.
- No build/test steps.

## Verification (done by the requester — render and LOOK, per repo rule 9)
1. With **Revit closed entirely**, open the Sheet Composer, add three details, drag/resize them on the
   canvas, click **Create PDF sheet**, save, and confirm it opens.
2. Put the opened PDF and the canvas side by side: every detail is in the same position, at the same
   size and aspect, on a 36"×24" page, with the uncontrolled-working-copy footer present. No Revit or
   bridge prompt appeared at any point.
3. Add a detail known to have no captured art (or temporarily one) and confirm the placeholder box with
   its number appears rather than a blank gap.
4. Confirm **Save to master** still composes into Revit exactly as before when the AUTHORING model is up.
