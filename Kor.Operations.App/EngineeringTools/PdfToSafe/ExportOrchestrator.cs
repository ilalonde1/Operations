#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Pure orchestration logic for the SAFE OAPI export. Takes an
    /// already-constructed <see cref="ISafeOapiDriver"/> (driving either a
    /// live SAFE COM server or a test fake) and an <see cref="SafeApiExporter.ExportInput"/>,
    /// sequences the OAPI calls needed to build the model, and returns a
    /// structured <see cref="SafeApiExporter.ExportResult"/>.
    ///
    /// Zero reflection, zero COM activation, zero Assembly.LoadFrom. Unit
    /// tests drive this against <c>FakeSafeOapiDriver</c> to verify call
    /// sequence, argument values, unit conversion, idempotency, and failure
    /// handling without touching SAFE.
    /// </summary>
    internal static class ExportOrchestrator
    {
        // ── Unit conversion constants (metric → imperial) ─────────────
        // Internal pipeline is always metric (mm, MPa, kPa). When IsImperial
        // is set, values are converted to kip-in-F at the driver boundary.
        private const double MmToIn        = 1.0 / 25.4;
        private const double MpaToKsi      = 1.0 / 6.895;       // 1 ksi = 6.895 MPa
        private const double KpaToKipPerIn2 = 1.0e-3 / 6.895;   // kPa → N/mm² → kip/in²
        private const double AlphaMetric   = 1.0e-5;             // 1/°C
        private const double AlphaImperial = 5.5e-6;             // 1/°F (concrete)
        public static SafeApiExporter.ExportResult Run(ISafeOapiDriver driver, SafeApiExporter.ExportInput input)
        {
            if (input.Slabs.Count == 0 && input.Columns.Count == 0 && input.Lines.Count == 0)
                return Failed("Nothing to export (no slabs, columns, or lines).");

            int slabsExported = 0, columnsExported = 0, framesExported = 0, loadsApplied = 0, skipped = 0;
            try
            {
                int ret;
                try
                {
                    ret = driver.Start();
                }
                catch (Exception ex) when (
                    InnerMost(ex) is InvalidCastException
                    || (InnerMost(ex) is COMException cex && (uint)cex.ErrorCode == 0x80004002u))
                {
                    // E_NOINTERFACE / InvalidCastException on Start means the
                    // registered SAFE COM server and the loaded SAFEv1.dll
                    // disagree on the cOAPI interface GUID — typically caused
                    // by the registered version differing from the folder whose
                    // DLL we loaded.
                    return Fail(
                        $"SAFE COM server rejected the cOAPI interface. Run RegisterSAFE.exe as Administrator from the SAFE install folder we're loading SAFEv1.dll from, then retry. "
                      + $"Underlying error: {InnerMost(ex).Message}");
                }

                if (ret != 0)
                    return Fail($"ApplicationStart returned {ret}. Likely SAFE not registered via RegisterSAFE.exe, wrong/unlicensed edition, or another SAFE holding the license.");

                try { driver.Unhide(); } catch { /* non-fatal */ }

                bool imp = input.IsImperial;
                ret = driver.InitializeNewModel(imp);
                if (ret != 0) return Fail($"InitializeNewModel returned {ret}.");

                ret = driver.NewBlank();
                if (ret != 0) return Fail($"File.NewBlank returned {ret}.");

                // Merge tolerance in mm so slab / column / wall vertices that
                // come from the same PDF coordinate merge instead of forming
                // disconnected nodes.
                try { driver.SetMergeTol(imp ? 1.0 * MmToIn : 1.0); } catch { /* non-fatal */ }

                // ── Materials ─────────────────────────────────────────────
                var grades = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input.DefaultGradeCode };
                for (int i = 0; i < input.Slabs.Count; i++)
                    grades.Add(SettingsFor(input.SlabColors, input.ColorSettings, input.AnnotatedSlabThicknesses, i, input.DefaultGradeCode, input.DefaultThicknessMm).GradeCode);
                foreach (string g in grades)
                {
                    string matName = NormalizeName(g);
                    // Use the design-code-specific E formula. CSA: 4500√fc,
                    // ACI: 4700√fc, AS/NZS: density-based, EC2: power formula.
                    // Prior to this fix, the OAPI path always used ACI (4700√fc)
                    // regardless of the selected code — a 4.4% error for CSA.
                    var (e, _, _) = StructuralMaterialDatabase.GetGrade(g, input.DesignCode);
                    double eOut = imp ? e * MpaToKsi : e;
                    double aOut = imp ? AlphaImperial : AlphaMetric;
                    ret = driver.SetMaterial(matName, $"KOR Operations — {g}");
                    if (ret != 0) return Fail($"SetMaterial({matName}) returned {ret}.");
                    ret = driver.SetMPIsotropic(matName, eOut, 0.2, aOut);
                    if (ret != 0) return Fail($"SetMPIsotropic({matName}) returned {ret}.");
                }

                // ── Slab properties ───────────────────────────────────────
                var slabPropsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < input.Slabs.Count; i++)
                {
                    var s = SettingsFor(input.SlabColors, input.ColorSettings, input.AnnotatedSlabThicknesses, i, input.DefaultGradeCode, input.DefaultThicknessMm);
                    string propName = SlabPropertyName(s, imp);
                    if (!slabPropsSeen.Add(propName)) continue;
                    double thickness = imp ? s.ThicknessMm * MmToIn : s.ThicknessMm;
                    ret = driver.SetSlabProp(propName, NormalizeName(s.GradeCode), thickness);
                    if (ret != 0) return Fail($"PropArea.SetSlab({propName}) returned {ret}.");

                    // Apply slab stiffness modifiers (CSA A23.3 cracked = 0.25).
                    // Without these, SAFE uses uncracked stiffness → wrong
                    // deflections and moment distribution.
                    try { driver.SetSlabModifiers(propName, input.SlabMembraneModifier, input.SlabBendingModifier, input.SlabShearModifier); }
                    catch { /* non-fatal */ }
                }

                // ── Frame sections: per unique column W×D and per wall W×D ──
                string defaultMat = NormalizeName(input.DefaultGradeCode);
                var frameSecsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (w, depth) in input.ColumnSizes)
                {
                    double rw = Round10(w), rd = Round10(depth);
                    string name = FrameSectionName("C", rw, rd);
                    if (!frameSecsSeen.Add(name)) continue;
                    double dOut = imp ? rd * MmToIn : rd, wOut = imp ? rw * MmToIn : rw;
                    ret = driver.SetFrameRectangleProp(name, defaultMat, dOut, wOut);
                    if (ret != 0) return Fail($"PropFrame.SetRectangle({name}) returned {ret}.");
                }
                foreach (var hint in input.LineSectionHints)
                {
                    if (hint is null) continue;
                    double rw = Round10(hint.Value.WidthMm), rd = Round10(hint.Value.DepthMm);
                    string name = FrameSectionName("B", rw, rd);
                    if (!frameSecsSeen.Add(name)) continue;
                    double dOut = imp ? rd * MmToIn : rd, wOut = imp ? rw * MmToIn : rw;
                    ret = driver.SetFrameRectangleProp(name, defaultMat, dOut, wOut);
                    if (ret != 0) return Fail($"PropFrame.SetRectangle({name}) returned {ret}.");
                }

                // ── Annotation-derived frame sections ─────────────────────
                // Text annotations (e.g., "B300x600", "C500x500") can
                // override the bbox/hint-derived sections. Pre-create any
                // unique sections found in annotations.
                if (input.AnnotatedColumnSections is not null)
                {
                    foreach (var ann in input.AnnotatedColumnSections)
                    {
                        if (ann is null) continue;
                        double rw = Round10(ann.Value.WidthMm), rd = Round10(ann.Value.DepthMm);
                        string name = FrameSectionName("C", rw, rd);
                        if (!frameSecsSeen.Add(name)) continue;
                        double dOut = imp ? rd * MmToIn : rd, wOut = imp ? rw * MmToIn : rw;
                        ret = driver.SetFrameRectangleProp(name, defaultMat, dOut, wOut);
                        if (ret != 0) return Fail($"PropFrame.SetRectangle({name}) returned {ret}.");
                    }
                }
                if (input.AnnotatedLineSections is not null)
                {
                    foreach (var ann in input.AnnotatedLineSections)
                    {
                        if (ann is null) continue;
                        double rw = Round10(ann.Value.WidthMm), rd = Round10(ann.Value.DepthMm);
                        string name = FrameSectionName("B", rw, rd);
                        if (!frameSecsSeen.Add(name)) continue;
                        double dOut = imp ? rd * MmToIn : rd, wOut = imp ? rw * MmToIn : rw;
                        ret = driver.SetFrameRectangleProp(name, defaultMat, dOut, wOut);
                        if (ret != 0) return Fail($"PropFrame.SetRectangle({name}) returned {ret}.");
                    }
                }

                // ── Load patterns ─────────────────────────────────────────
                // SAFE's blank model pre-creates DEAD and LIVE; re-adding
                // returns 1 ("already exists"). Enumerate and skip.
                var existingPats = new HashSet<string>(driver.ListLoadPatternNames(), StringComparer.OrdinalIgnoreCase);
                bool anySdl  = input.ColorSettings?.Values.Any(v => v.SdlKPa  > 0) ?? false;
                bool anyLive = input.ColorSettings?.Values.Any(v => v.LiveKPa > 0) ?? false;
                if (anySdl && !existingPats.Contains("SDL"))
                {
                    ret = driver.AddLoadPattern("SDL", SafeLoadPatternType.SuperDead);
                    if (ret != 0) return Fail($"LoadPatterns.Add(SDL) returned {ret}.");
                }
                if (anyLive && !existingPats.Contains("LIVE"))
                {
                    ret = driver.AddLoadPattern("LIVE", SafeLoadPatternType.Live);
                    if (ret != 0) return Fail($"LoadPatterns.Add(LIVE) returned {ret}.");
                }

                // ── Slabs + their uniform loads ───────────────────────────
                for (int i = 0; i < input.Slabs.Count; i++)
                {
                    var rawPoly = input.Slabs[i];
                    if (rawPoly.Count < 3) { skipped++; continue; }

                    // Remove consecutive duplicate vertices that arise from
                    // PDF subpath junctions. A zero-length polygon edge can
                    // cause SAFE's mesher to degenerate.
                    var poly = DeduplicateConsecutive(rawPoly);
                    if (poly.Count < 3) { skipped++; continue; }

                    var s = SettingsFor(input.SlabColors, input.ColorSettings, input.AnnotatedSlabThicknesses, i, input.DefaultGradeCode, input.DefaultThicknessMm);
                    string propName = SlabPropertyName(s, imp);

                    double lenScale = imp ? MmToIn : 1.0;
                    var ptNames = new string[poly.Count];
                    bool pointFailed = false;
                    for (int j = 0; j < poly.Count; j++)
                    {
                        int rP = driver.AddPoint(poly[j].X * lenScale, poly[j].Y * lenScale, 0.0, out string ptName);
                        if (rP != 0) { pointFailed = true; break; }
                        ptNames[j] = ptName;
                    }
                    if (pointFailed) { skipped++; continue; }

                    int rArea = driver.AddArea(ptNames, propName, out string areaName);
                    if (rArea != 0) { skipped++; continue; }
                    slabsExported++;

                    // Auto edge constraint: ensures SAFE's mesher connects any
                    // frame node (column top, wall end) that lands on or near
                    // the slab edge to the slab's FE mesh. Without this, columns
                    // can be geometrically inside the slab but structurally
                    // disconnected from it.
                    try { driver.SetAreaEdgeConstraint(areaName, true); } catch { /* non-fatal */ }

                    // kPa → database pressure units. Metric: 1 kPa = 1e-3 N/mm².
                    // Imperial: kPa → kip/in² via KpaToKipPerIn2.
                    double loadScale = imp ? KpaToKipPerIn2 : 1e-3;
                    if (s.SdlKPa > 0 && driver.SetAreaLoadUniform(areaName, "SDL", s.SdlKPa * loadScale) == 0)
                        loadsApplied++;
                    if (s.LiveKPa > 0 && driver.SetAreaLoadUniform(areaName, "LIVE", s.LiveKPa * loadScale) == 0)
                        loadsApplied++;
                }

                // ── Drop panels: thickened slab zones near columns ────────
                if (input.DropPanelCandidates is not null && input.DropPanelCandidates.Count > 0 && input.Columns.Count > 0)
                {
                    List<(int ParentIdx, int DropIdx, List<(double X, double Y)> Poly)> dropPanels;
                    try
                    {
                        var slabsAsList = input.Slabs.Select(s => s.ToList()).ToList();
                        var dropsAsList = input.DropPanelCandidates.Select(d => d.ToList()).ToList();
                        dropPanels = PolygonProcessor.DetectDropPanels(slabsAsList, input.Columns, dropsAsList);
                    }
                    catch { dropPanels = new(); }

                    foreach (var (parentSlabIdx, _, dropPoly) in dropPanels)
                    {
                        try
                        {
                            if (dropPoly.Count < 3) continue;

                            // Thickness = parent slab × multiplier.
                            var parentSettings = SettingsFor(input.SlabColors, input.ColorSettings, input.AnnotatedSlabThicknesses, parentSlabIdx, input.DefaultGradeCode, input.DefaultThicknessMm);
                            double dropThickMm = Math.Round(parentSettings.ThicknessMm * input.DropPanelThicknessMultiplier, 1);
                            var dropSettings = new SlabColorSettings
                            {
                                GradeCode   = parentSettings.GradeCode,
                                ThicknessMm = dropThickMm,
                                SdlKPa      = parentSettings.SdlKPa,
                                LiveKPa     = parentSettings.LiveKPa,
                            };
                            string dropProp = SlabPropertyName(dropSettings, imp);
                            if (slabPropsSeen.Add(dropProp))
                            {
                                double thickness = imp ? dropThickMm * MmToIn : dropThickMm;
                                ret = driver.SetSlabProp(dropProp, NormalizeName(dropSettings.GradeCode), thickness);
                                if (ret != 0) continue;
                                try { driver.SetSlabModifiers(dropProp, input.SlabMembraneModifier, input.SlabBendingModifier, input.SlabShearModifier); }
                                catch { /* non-fatal */ }
                            }

                            var dpDeduped = DeduplicateConsecutive(dropPoly);
                            if (dpDeduped.Count < 3) continue;
                            double lenScale = imp ? MmToIn : 1.0;
                            var dpPtNames = new string[dpDeduped.Count];
                            bool dpFailed = false;
                            for (int j = 0; j < dpDeduped.Count; j++)
                            {
                                int rP = driver.AddPoint(dpDeduped[j].X * lenScale, dpDeduped[j].Y * lenScale, 0.0, out string ptName);
                                if (rP != 0) { dpFailed = true; break; }
                                dpPtNames[j] = ptName;
                            }
                            if (dpFailed) continue;

                            int rDp = driver.AddArea(dpPtNames, dropProp, out string dpAreaName);
                            if (rDp != 0) continue;
                            slabsExported++;
                            try { driver.SetAreaEdgeConstraint(dpAreaName, true); } catch { }

                            // Apply same loads as parent slab.
                            double loadScaleDp = imp ? KpaToKipPerIn2 : 1e-3;
                            if (dropSettings.SdlKPa > 0 && driver.SetAreaLoadUniform(dpAreaName, "SDL", dropSettings.SdlKPa * loadScaleDp) == 0)
                                loadsApplied++;
                            if (dropSettings.LiveKPa > 0 && driver.SetAreaLoadUniform(dpAreaName, "LIVE", dropSettings.LiveKPa * loadScaleDp) == 0)
                                loadsApplied++;
                        }
                        catch { /* non-fatal — skip this drop panel, continue with rest */ }
                    }
                }

                // ── Columns: short vertical frame below slab, 6-DOF fixity ─
                double colHmm = input.ColumnHeightMm > 0 ? input.ColumnHeightMm : 3000.0;
                double colH = imp ? colHmm * MmToIn : colHmm;
                for (int i = 0; i < input.Columns.Count; i++)
                {
                    var (x, y) = input.Columns[i];
                    // Annotation-derived section takes priority over bbox.
                    var annCol = (input.AnnotatedColumnSections is not null && i < input.AnnotatedColumnSections.Count)
                        ? input.AnnotatedColumnSections[i] : null;
                    double w     = Round10(annCol?.WidthMm ?? (i < input.ColumnSizes.Count ? input.ColumnSizes[i].WidthMm : 400.0));
                    double depth = Round10(annCol?.DepthMm ?? (i < input.ColumnSizes.Count ? input.ColumnSizes[i].DepthMm : 400.0));
                    string sec = FrameSectionName("C", w, depth);

                    if (frameSecsSeen.Add(sec))
                    {
                        double dOut = imp ? depth * MmToIn : depth, wOut = imp ? w * MmToIn : w;
                        int rSec = driver.SetFrameRectangleProp(sec, defaultMat, dOut, wOut);
                        if (rSec != 0) { skipped++; continue; }
                    }

                    double cx = imp ? x * MmToIn : x, cy = imp ? y * MmToIn : y;
                    int rT = driver.AddPoint(cx, cy, 0.0,   out string topName);
                    if (rT != 0) { skipped++; continue; }
                    int rB = driver.AddPoint(cx, cy, -colH, out string botName);
                    if (rB != 0) { skipped++; continue; }

                    int rF = driver.AddFrame(botName, topName, sec, out string colFrameName);
                    if (rF != 0) { skipped++; continue; }

                    // Full 6-DOF fixity at the base — SAFE floor-model convention.
                    try { driver.SetPointRestraint(botName, new[] { true, true, true, true, true, true }); }
                    catch { /* non-fatal */ }

                    // Insertion point: Bottom Center (cardinal 2) — column hangs
                    // below the slab connection. This is the convention from every
                    // SAFE reference model and makes the 3D extruded view show
                    // columns correctly relative to the slab soffit.
                    try { driver.SetFrameInsertionPoint(colFrameName, 2); }
                    catch { /* non-fatal */ }

                    columnsExported++;
                }

                // ── Walls / beams (polylines, optional W×D hint) ──────────
                for (int i = 0; i < input.Lines.Count; i++)
                {
                    var poly = input.Lines[i];
                    if (poly.Count < 2) { skipped++; continue; }

                    // Annotation section takes priority over wall-reduction hint.
                    var annLine = (input.AnnotatedLineSections is not null && i < input.AnnotatedLineSections.Count)
                        ? input.AnnotatedLineSections[i] : null;
                    var hint = i < input.LineSectionHints.Count ? input.LineSectionHints[i] : null;
                    double w     = Round10(annLine?.WidthMm ?? hint?.WidthMm ?? 0);
                    double depth = Round10(annLine?.DepthMm ?? hint?.DepthMm ?? input.DefaultWallDepthMm);
                    if (w <= 0) w = depth;
                    string sec = FrameSectionName("B", w, depth);
                    if (frameSecsSeen.Add(sec))
                    {
                        double dOut = imp ? depth * MmToIn : depth, wOut = imp ? w * MmToIn : w;
                        int rSec = driver.SetFrameRectangleProp(sec, defaultMat, dOut, wOut);
                        if (rSec != 0) { skipped++; continue; }
                    }

                    double ls = imp ? MmToIn : 1.0;
                    for (int j = 0; j + 1 < poly.Count; j++)
                    {
                        int rA = driver.AddPoint(poly[j].X * ls,     poly[j].Y * ls,     0.0, out string a);
                        if (rA != 0) { skipped++; continue; }
                        int rBp = driver.AddPoint(poly[j + 1].X * ls, poly[j + 1].Y * ls, 0.0, out string b);
                        if (rBp != 0) { skipped++; continue; }

                        int rF = driver.AddFrame(a, b, sec, out _);
                        if (rF != 0) { skipped++; continue; }
                        framesExported++;
                    }
                }

                // ── Grid lines from column positions ──────────────────────
                // Engineers expect A/B/C and 1/2/3 grids in the model. We
                // generate them from column clustering (same logic as F2K)
                // and add via the DatabaseTables API. Coordinates converted
                // to database units (inches if imperial).
                try
                {
                    var colPositions = new List<(double X, double Y)>();
                    for (int i = 0; i < input.Columns.Count; i++)
                        colPositions.Add(input.Columns[i]);
                    var gridLines = StructuralGridGenerator.Generate(colPositions);
                    if (gridLines.Count > 0)
                    {
                        double gs = imp ? MmToIn : 1.0;
                        var converted = gridLines.Select(g =>
                            (g.Label, g.IsAlongX, g.OrdMm * gs)).ToList();
                        driver.AddGridLines(converted);
                    }
                }
                catch { /* non-fatal — model is usable without grids */ }

                string? destDir = Path.GetDirectoryName(input.DestFdbPath);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                ret = driver.SaveModel(input.DestFdbPath);
                if (ret != 0) return Fail($"File.Save returned {ret}.");

                return new SafeApiExporter.ExportResult(true,
                    $"Exported {slabsExported} slab(s), {columnsExported} column(s), {framesExported} wall/beam frame(s), {loadsApplied} area load(s)"
                    + (skipped > 0 ? $", skipped {skipped}" : "")
                    + $" → {input.DestFdbPath}.",
                    input.DestFdbPath, slabsExported, columnsExported, framesExported, loadsApplied, skipped);
            }
            catch (COMException cex)
            {
                return Fail($"COM error 0x{cex.ErrorCode:X8}: {cex.Message} — SAFE rejected a call.");
            }
            catch (SafeDriverException sex)
            {
                return Fail(sex.Message);
            }
            catch (Exception ex)
            {
                return Fail($"{ex.GetType().Name}: {InnerMost(ex).Message}");
            }

            SafeApiExporter.ExportResult Fail(string reason)
                => new SafeApiExporter.ExportResult(false, reason, null, slabsExported, columnsExported, framesExported, loadsApplied, skipped);
        }

        private static SafeApiExporter.ExportResult Failed(string message)
            => new SafeApiExporter.ExportResult(false, message, null, 0, 0, 0, 0, 0);

        /// <summary>
        /// Returns the effective slab settings for index <paramref name="idx"/>.
        /// Priority: text-annotation thickness (if available) overrides the
        /// color-setting thickness. Grade always comes from color settings or default.
        /// </summary>
        private static SlabColorSettings SettingsFor(
            IReadOnlyList<(byte R, byte G, byte B)> colors,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            IReadOnlyList<double?>? annotatedThicknesses,
            int idx,
            string defaultGrade,
            double defaultThickness)
        {
            var baseSettings = (colorSettings != null && idx < colors.Count && colorSettings.TryGetValue(colors[idx], out var s))
                ? s
                : new SlabColorSettings { GradeCode = defaultGrade, ThicknessMm = defaultThickness };

            // Text-annotation thickness overrides color-level default.
            if (annotatedThicknesses != null && idx < annotatedThicknesses.Count && annotatedThicknesses[idx].HasValue)
            {
                return new SlabColorSettings
                {
                    ElementType = baseSettings.ElementType,
                    GradeCode   = baseSettings.GradeCode,
                    ThicknessMm = annotatedThicknesses[idx]!.Value,
                    SdlKPa      = baseSettings.SdlKPa,
                    LiveKPa     = baseSettings.LiveKPa,
                };
            }
            return baseSettings;
        }

        private static string SlabPropertyName(SlabColorSettings s, bool imperial)
        {
            string grade = NormalizeName(s.GradeCode);
            if (imperial)
            {
                double inches = s.ThicknessMm / 25.4;
                return $"S{inches:F1}in-{grade}";
            }
            return $"S{(int)Math.Round(s.ThicknessMm)}-{grade}";
        }

        private static string FrameSectionName(string prefix, double w, double d)
            => $"{prefix}{(int)Round10(w)}x{(int)Round10(d)}";

        /// <summary>
        /// Rounds a dimension to the nearest 10 mm. Applied to both section
        /// names AND the actual W/D values passed to the driver, so SAFE's
        /// section definition matches its label (no "C373x957 with actual
        /// 368.3×952.5" mismatch). Also merges near-duplicate columns whose
        /// bounding-box sizes differ by ≤5 mm (extraction noise).
        /// </summary>
        private static double Round10(double v) => Math.Round(v / 10.0) * 10.0;

        /// <summary>
        /// Removes consecutive duplicate vertices (within 0.01 mm) that occur
        /// at PDF subpath junctions. A polygon with two identical consecutive
        /// vertices has a zero-length edge, which can break SAFE's mesher.
        /// </summary>
        private static List<(double X, double Y)> DeduplicateConsecutive(IReadOnlyList<(double X, double Y)> poly)
        {
            var clean = new List<(double X, double Y)>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                if (clean.Count == 0 ||
                    Math.Abs(clean[^1].X - poly[i].X) > 0.01 ||
                    Math.Abs(clean[^1].Y - poly[i].Y) > 0.01)
                {
                    clean.Add(poly[i]);
                }
            }
            return clean;
        }

        /// <summary>Strips chars SAFE dislikes in property names (spaces, slashes, etc.).</summary>
        public static string NormalizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "C30";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.ToString();
        }

        private static Exception InnerMost(Exception ex)
        {
            while (ex.InnerException is not null) ex = ex.InnerException;
            return ex;
        }
    }
}
