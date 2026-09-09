#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kor.Operations.EngineeringTools.Dxf;   // PlanClassificationOptions: the layer vocabulary

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    public static class DxfExporter
    {
        // Encoding.GetEncoding(1252) in Export needs the code-page provider, which .NET does not
        // register on its own. It used to work only because some other library already loaded in
        // the process (AngleSharp, MsgReader) had registered it first — an ordering accident, not a
        // dependency this class owned. Registering twice is harmless.
        static DxfExporter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        /// <summary>
        /// Writes the geometry as a DXF.
        ///
        /// <paramref name="layerByColour"/> changes what a LAYER means, and it exists because of a
        /// request this exporter could not serve: "I would like to get a dxf for this pdf. The tower
        /// outline, the red markups I did and the balcony outline." Not one of those three is a
        /// beam, a column or a slab, so the classifier's layering — the default below — merges all
        /// three into SLAB and BEAM and hands back something no one can pick apart.
        ///
        /// All three ARE colours. A draughtsman separates his drawing by colour and pen, the parser
        /// keeps that per shape, and this discarded it on the way out. With this set, each source
        /// colour becomes its own layer (PDF-F00000 and so on), so the separation the drawing was
        /// made with survives into AutoCAD and the engineer picks what he wants by turning layers
        /// off. On Parcel 11 that is eleven layers, and his red markup is exactly one of them.
        ///
        /// Off by default: every existing caller wants the structural layering and gets it unchanged.
        /// </summary>
        public static void Export(
            ExtractedGeometry geometry,
            string outputPath,
            HashSet<int>? excludedSlabs = null,
            HashSet<int>? excludedLines = null,
            HashSet<int>? excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null,
            bool layerByColour = false,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            bool korLayers = false,
            PlanClassificationOptions? classification = null)
        {
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var pts in geometry.Slabs)
            {
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            foreach (var (x, y) in geometry.Columns) { sumX += x; sumY += y; totalWeight += 1.0; }
            foreach (var pts in geometry.Lines)
            {
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            foreach (var wall in geometry.Walls)
            {
                var pts = wall.Outline.ToList();
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            // footings weigh too: a foundation plan whose only structure is footings is a plan (audit F6)
            foreach (var footing in geometry.Footings)
            {
                var pts = footing.Outline.ToList();
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight;
            double cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = PdfToSafeConstants.MinVertexDistanceMm;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            static string TextForDxf(string text) =>
                text.Replace('\r', ' ').Replace('\n', ' ').Trim();

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || PolygonProcessor.Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            var xSlabs = new List<List<(double X, double Y)>>();
            var xWalls = new List<List<(double X, double Y)>>();
            var xWallColours = new List<(byte R, byte G, byte B)>();
            var xWallMarkup = new List<bool>();
            var xLines = new List<List<(double X, double Y)>>();
            // Parallel to xLines: was this line wall-hinted?
            var xLineIsWall = new List<bool>();
            var xColumns = new List<(double X, double Y)>();
            // Parallel to xColumns: bounding-box sizes for footprint rectangles.
            var xColumnSizes = new List<(double W, double D)>();
            // Parallel to each of the three, the colour the shape was drawn in. Carried whether or
            // not it is used, so the three lists cannot fall out of step with it.
            var xSlabColours = new List<(byte R, byte G, byte B)>();
            var xLineColours = new List<(byte R, byte G, byte B)>();
            var xColumnColours = new List<(byte R, byte G, byte B)>();
            // And whether each came from a markup annotation rather than the architect's page.
            var xSlabMarkup = new List<bool>();
            var xLineMarkup = new List<bool>();
            var xColumnMarkup = new List<bool>();
            var black = ((byte)0, (byte)0, (byte)0);

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.SlabColors.Count && excludedColors.Contains(geometry.SlabColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Slabs[i]));
                if (pts.Count >= 3)
                {
                    xSlabs.Add(pts);
                    xSlabColours.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : black);
                    xSlabMarkup.Add(i < geometry.SlabIsAnnotation.Count && geometry.SlabIsAnnotation[i]);
                }
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.LineColors.Count && excludedColors.Contains(geometry.LineColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Lines[i]));
                if (pts.Count >= 2)
                {
                    xLines.Add(pts);
                    xLineIsWall.Add(i < geometry.LineSectionHints.Count && geometry.LineSectionHints[i] is not null);
                    xLineColours.Add(i < geometry.LineColors.Count ? geometry.LineColors[i] : black);
                    xLineMarkup.Add(i < geometry.LineIsAnnotation.Count && geometry.LineIsAnnotation[i]);
                }
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.ColumnColors.Count && excludedColors.Contains(geometry.ColumnColors[i])) continue;
                var (colX, colY) = geometry.Columns[i];
                double px = colX - cx, py = colY - cy;
                if (Ok(px, py))
                {
                    xColumns.Add((px, py));
                    xColumnSizes.Add(i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (400.0, 400.0));
                    xColumnColours.Add(i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : black);
                    xColumnMarkup.Add(i < geometry.ColumnIsAnnotation.Count && geometry.ColumnIsAnnotation[i]);
                }
            }

            for (int i = 0; i < geometry.Walls.Count; i++)
            {
                if (excludedColors != null && i < geometry.WallColors.Count && excludedColors.Contains(geometry.WallColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Walls[i].Outline.ToList()));
                if (pts.Count >= 3)
                {
                    xWalls.Add(pts);
                    xWallColours.Add(i < geometry.WallColors.Count ? geometry.WallColors[i] : black);
                    xWallMarkup.Add(i < geometry.WallIsAnnotation.Count && geometry.WallIsAnnotation[i]);
                }
            }
            // Footings: the box each dashed outline closed, on a FOOTING layer. No KOR layer pattern
            // names footings, so the layer is FOOTING under --kor-layers too, and visibly unread by
            // the DXF-to-ETABS classifier rather than mistaken for something it does read.
            var xFootings = new List<List<(double X, double Y)>>();
            foreach (var footing in geometry.Footings)
            {
                var pts = FilterPts(Ctr(footing.Outline.ToList()));
                if (pts.Count >= 3) xFootings.Add(pts);
            }

            var xText = new List<(string Text, double X, double Y, double HeightMm)>();
            foreach (var annotation in geometry.TextAnnotations)
            {
                string clean = TextForDxf(annotation.Text);
                double px = annotation.LeftX - cx, py = annotation.BottomY - cy;
                if (clean.Length > 0 && Ok(px, py))
                    xText.Add((clean, px, py, annotation.HeightMm));
            }

            double bMinX = double.MaxValue, bMinY = double.MaxValue;
            double bMaxX = double.MinValue, bMaxY = double.MinValue;
            void Expand(double ex, double ey)
            {
                if (ex < bMinX) bMinX = ex; if (ex > bMaxX) bMaxX = ex;
                if (ey < bMinY) bMinY = ey; if (ey > bMaxY) bMaxY = ey;
            }
            foreach (var s in xSlabs) foreach (var (ex, ey) in s) Expand(ex, ey);
            foreach (var wall in xWalls) foreach (var (ex, ey) in wall) Expand(ex, ey);
            foreach (var footing in xFootings) foreach (var (ex, ey) in footing) Expand(ex, ey);
            foreach (var l in xLines) foreach (var (ex, ey) in l) Expand(ex, ey);
            foreach (var (ex, ey) in xColumns) Expand(ex, ey);
            if (bMinX > bMaxX) { bMinX = -1000; bMaxX = 1000; bMinY = -1000; bMaxY = 1000; }

            var ic = CultureInfo.InvariantCulture;

            // WINDOWS-1252, NOT ASCII, AND THE FILE SAYS SO.
            //
            // A drawing's words are not ASCII. Written as ASCII, every character above 127 becomes a
            // question mark, so the architect's "ft²" and "m²" arrived as "ft?" and "m?" — 22 of
            // them on Parcel 11's plan, in an area schedule where the unit is the point. Degrees,
            // diameters, dashes and accented names go the same way.
            //
            // A DXF states its own code page in $DWGCODEPAGE, so this declares ANSI_1252 and writes
            // to match, which is what AutoCAD has assumed by default since R12. ² is 0xB2 in it.
            var codePage = Encoding.GetEncoding(1252);
            using var sw = new StreamWriter(outputPath, false, codePage);
            void G(int code, string val) { sw.WriteLine(code); sw.WriteLine(val); }
            void Num(int code, double v) => G(code, v.ToString("F4", ic));

            G(0, "SECTION"); G(2, "HEADER");
            G(9, "$ACADVER"); G(1, "AC1009");
            G(9, "$DWGCODEPAGE"); G(3, "ANSI_1252");
            G(9, "$INSUNITS"); G(70, "4");
            G(9, "$EXTMIN"); Num(10, bMinX); Num(20, bMinY); G(30, "0.0000");
            G(9, "$EXTMAX"); Num(10, bMaxX); Num(20, bMaxY); G(30, "0.0000");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "TABLES");
            G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
            G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); G(40, "0.0");
            G(0, "ENDTAB");
            // A layer per source colour, or the four structural ones. Named PDF-RRGGBB so the layer
            // says which pen it came off, and given the nearest AutoCAD colour index so it still
            // LOOKS like the drawing when it opens.
            //
            // AND A SEPARATE LAYER WHERE THE SAME PEN WAS USED BY TWO PEOPLE. On Parcel 11 the
            // engineer's shear walls and the architect's property line are both #F00000, so colour
            // alone hands an engineer his own markup welded to a site boundary. Origin separates
            // them exactly and without a guess — his are Bluebeam ANNOTATIONS, the boundary is page
            // content — and on that sheet it splits 5 shapes and 37 wall segments from 488 lines.
            static string ColourLayer((byte R, byte G, byte B) c, bool markup) =>
                $"PDF-{c.R:X2}{c.G:X2}{c.B:X2}" + (markup ? "-MARKUP" : "");

            // KOR's drafting vocabulary, BUILT FROM THE CLASSIFIER'S OWN PATTERNS rather than
            // spelled out again here.
            //
            // A DXF the Revit bridge exports and one this writes are read by the same
            // StructuralPlanClassifier, and it matches layers by SUBSTRING: SLABEDG, _COL, WALL.
            // Naming a layer by concatenating the pattern it must match means the two cannot drift
            // -- change the pattern and the emitted name follows it. Spelling "KOR_C_SLABEDG" as a
            // literal here would be a second copy of the vocabulary, and the copy would be the one
            // that goes stale.
            //
            // The prefixes mirror the Revit export's own shape (JBP_C_SLABEDG, JBP_V_COL,
            // JBP_V-WALL) so a drafter opening either file sees the same thing.
            //
            // BEAM is deliberately absent and stays unmatched. Our Lines collection is whatever did
            // not close -- on 31130 page 12 that is 2,420 subpaths, mostly grid, dimension and
            // leader work. Naming those WALL would put 2,420 walls into the model. A caller who
            // knows a colour IS a wall says so through colorSettings, and that answer is honoured
            // below; nothing is promoted to structure by guessing.
            // ⚠ THE PATTERNS MUST BE THE ONES THAT WILL READ THIS FILE, NOT THE COMPILED DEFAULTS.
            // dxf.wall-layer-patterns, dxf.column-layer-patterns and dxf.slab-layer-patterns are all
            // overridable per job in KorStandards, so a job that states its own vocabulary would get
            // a file named for the default one and a classifier looking for something else. The
            // first version of this built from `new PlanClassificationOptions()` and had exactly
            // that bug: drift removed against the default, left against the effective rules.
            var patterns = classification ?? new PlanClassificationOptions();

            // ⚠ A VOCABULARY IS A LIST, AND TAKING [0] IS A GUESS. KorStandards banks
            // dxf.column-layer-patterns as "_COL;-COL;S-COL" — THREE patterns — and the first
            // version of this concatenated element [0], which worked only because `_COL` happens to
            // be first and happens to build an unambiguous name. A reordered list, a different
            // practice's vocabulary, or an empty one (which threw) all break that silently.
            //
            // So every pattern is tried, and the first that yields a name matching ITS OWN
            // vocabulary and no other is used. If none does, the plain kind name is emitted rather
            // than an ambiguous one: the classifier will not read it, which is visible, where a
            // layer read as the wrong element is not.
            string KorLayerName(string kind)
            {
                var (prefix, own) = kind switch
                {
                    "SLAB"   => ("KOR_C_", patterns.SlabLayerPatterns),
                    "COLUMN" => ("KOR_V",  patterns.ColumnLayerPatterns),
                    "WALL"   => ("KOR_V-", patterns.WallLayerPatterns),
                    _        => (null, null),                                // BEAM, deliberately unmatched
                };
                if (prefix is null || own is null) return kind;

                foreach (string pattern in own)
                {
                    if (string.IsNullOrWhiteSpace(pattern)) continue;
                    string candidate = prefix + pattern;
                    if (Unambiguous(candidate, own)) return candidate;
                }
                return kind;
            }

            // Matches its own vocabulary and neither of the other two. The trap is recorded in
            // ModelQuestionnaire in KOR's own convention: "V_COL-WALL is a column layer, and testing
            // walls first would take it for a wall."
            bool Unambiguous(string layer, IReadOnlyList<string> own)
            {
                bool Hits(IReadOnlyList<string> vocab) =>
                    vocab.Any(p => !string.IsNullOrWhiteSpace(p)
                                && layer.Contains(p, StringComparison.OrdinalIgnoreCase));

                if (!Hits(own)) return false;

                int families = 0;
                if (Hits(patterns.SlabLayerPatterns))   families++;
                if (Hits(patterns.ColumnLayerPatterns)) families++;
                if (Hits(patterns.WallLayerPatterns))   families++;
                return families == 1;
            }

            string StructuralLayer(string baseLayer, bool markup)
            {
                string name = korLayers ? KorLayerName(baseLayer) : baseLayer;
                return markup ? name + "-MARKUP" : name;
            }

            string StructuralBaseLayer(string fallback, (byte R, byte G, byte B) colour)
            {
                if (colorSettings is null || !colorSettings.TryGetValue(colour, out var settings))
                    return fallback;

                return settings.ElementType.Trim().ToUpperInvariant() switch
                {
                    "SLAB" => "SLAB",
                    "BEAM" => "BEAM",
                    "COLUMN" => "COLUMN",
                    "WALL" => "WALL",
                    _ => fallback
                };
            }

            static int NearestAci((byte R, byte G, byte B) c)
            {
                (int Aci, byte R, byte G, byte B)[] basics =
                {
                    (1, 255, 0, 0), (2, 255, 255, 0), (3, 0, 255, 0), (4, 0, 255, 255),
                    (5, 0, 0, 255), (6, 255, 0, 255), (7, 255, 255, 255), (8, 128, 128, 128),
                    (9, 192, 192, 192), (250, 51, 51, 51),
                };
                int best = 7;
                double bestDistance = double.MaxValue;
                foreach (var (aci, r, g, b) in basics)
                {
                    double d = (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b);
                    if (d < bestDistance) { bestDistance = d; best = aci; }
                }
                return best;
            }

            var colourLayers = new List<((byte R, byte G, byte B) Colour, bool Markup)>();
            if (layerByColour)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                void Note((byte R, byte G, byte B) c, bool m)
                {
                    if (seen.Add(ColourLayer(c, m))) colourLayers.Add((c, m));
                }
                for (int i = 0; i < xSlabColours.Count; i++) Note(xSlabColours[i], xSlabMarkup[i]);
                for (int i = 0; i < xColumnColours.Count; i++) Note(xColumnColours[i], xColumnMarkup[i]);
                for (int i = 0; i < xLineColours.Count; i++) Note(xLineColours[i], xLineMarkup[i]);
                for (int i = 0; i < xWallColours.Count; i++) Note(xWallColours[i], xWallMarkup[i]);
            }

            bool hasText = xText.Count > 0;
            string textLayer = layerByColour ? "PDF-TEXT" : "TEXT";
            // the named grid axes, one LINE and two TEXTs each, on a layer the ETABS side recognises
            // as a grid by name (GridAlignment.LooksLikeAGridLayer); never mapped to a KOR layer
            bool hasGrid = geometry.GridAxes.Count > 0;
            const string gridLayer = "GRID";
            bool hasFootings = xFootings.Count > 0;
            string footingLayer = korLayers ? KorLayerName("FOOTING") : "FOOTING";
            int layerCount = (layerByColour ? colourLayers.Count + 1 : 9) + (hasText ? 1 : 0) + (hasGrid ? 1 : 0) + (hasFootings ? 1 : 0);

            G(0, "TABLE"); G(2, "LAYER"); G(70, layerCount.ToString(ic));
            void WL(string n, int c) { G(0, "LAYER"); G(2, n); G(70, "0"); G(62, c.ToString()); G(6, "CONTINUOUS"); }
            WL("0", 7);
            if (layerByColour)
            {
                foreach (var (c, markup) in colourLayers) WL(ColourLayer(c, markup), NearestAci(c));
            }
            else
            {
                // Declared through the same StructuralLayer() the entities are written with, so the
                // table and the entities cannot name layers differently.
                foreach (var (kind, aci) in new[] { ("SLAB", 3), ("BEAM", 4), ("COLUMN", 2), ("WALL", 1) })
                {
                    WL(StructuralLayer(kind, markup: false), aci);
                    WL(StructuralLayer(kind, markup: true), aci);
                }
            }
            if (hasText) WL(textLayer, 7);
            if (hasGrid) WL(gridLayer, 8);
            // declared like the entities are written (audit F6: FOOTING polylines had no table entry)
            if (hasFootings) WL(footingLayer, NearestAci(black));
            G(0, "ENDTAB");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "ENTITIES");

            void WritePolyline(string layer, int aci, List<(double X, double Y)> pts, bool closed)
            {
                G(0, "POLYLINE"); G(8, layer);
                G(62, aci.ToString(ic));
                G(66, "1");
                G(70, closed ? "1" : "0");
                Num(10, 0); Num(20, 0); Num(30, 0);
                foreach (var (vx, vy) in pts)
                {
                    G(0, "VERTEX"); G(8, layer);
                    G(62, aci.ToString(ic));
                    Num(10, vx); Num(20, vy); Num(30, 0);
                }
                G(0, "SEQEND"); G(8, layer);
                G(62, aci.ToString(ic));
            }

            for (int i = 0; i < xSlabs.Count; i++)
                WritePolyline(
                    layerByColour
                        ? ColourLayer(xSlabColours[i], xSlabMarkup[i])
                        : StructuralLayer(StructuralBaseLayer("SLAB", xSlabColours[i]), xSlabMarkup[i]),
                    NearestAci(xSlabColours[i]),
                    xSlabs[i],
                    true);

            for (int i = 0; i < xWalls.Count; i++)
                WritePolyline(
                    layerByColour
                        ? ColourLayer(xWallColours[i], xWallMarkup[i])
                        : StructuralLayer(StructuralBaseLayer("WALL", xWallColours[i]), xWallMarkup[i]),
                    NearestAci(xWallColours[i]),
                    xWalls[i],
                    true);

            foreach (var footing in xFootings)
                WritePolyline(footingLayer, NearestAci(black), footing, true);

            // Columns: footprint rectangles from the parallel xColumnSizes list.
            for (int i = 0; i < xColumns.Count; i++)
            {
                var (px, py) = xColumns[i];
                double hw = xColumnSizes[i].W / 2.0;
                double hd = xColumnSizes[i].D / 2.0;
                var rect = new List<(double X, double Y)>
                {
                    (px - hw, py - hd), (px + hw, py - hd),
                    (px + hw, py + hd), (px - hw, py + hd)
                };
                WritePolyline(
                    layerByColour
                        ? ColourLayer(xColumnColours[i], xColumnMarkup[i])
                        : StructuralLayer(StructuralBaseLayer("COLUMN", xColumnColours[i]), xColumnMarkup[i]),
                    NearestAci(xColumnColours[i]),
                    rect,
                    true);
            }

            // Lines: WALL or BEAM layer from the parallel xLineIsWall list.
            for (int i = 0; i < xLines.Count; i++)
            {
                string structuralLayer = StructuralLayer(
                    StructuralBaseLayer(xLineIsWall[i] ? "WALL" : "BEAM", xLineColours[i]),
                    xLineMarkup[i]);
                WritePolyline(
                    layerByColour ? ColourLayer(xLineColours[i], xLineMarkup[i]) : structuralLayer,
                    NearestAci(xLineColours[i]),
                    xLines[i],
                    false);
            }

            void WriteText(string text, double x, double y, double heightMm)
            {
                G(0, "TEXT"); G(8, textLayer);
                G(62, "7");
                Num(10, x); Num(20, y); Num(30, 0);
                Num(40, heightMm > 0 ? heightMm : 250.0);
                G(1, text);
            }

            foreach (var (text, x, y, heightMm) in xText)
                WriteText(text, x, y, heightMm);

            // Each named axis is a line across the drawn extent, recentred with everything else, with
            // its name at both ends — what a drafter draws, and what DxfToEtabsService aligns by.
            if (hasGrid)
            {
                var drawn = geometry.Slabs.SelectMany(p => p)
                    .Concat(geometry.Lines.SelectMany(p => p))
                    .Concat(geometry.Walls.SelectMany(w => w.Outline))
                    .Concat(geometry.Footings.SelectMany(f => f.Outline))
                    .Concat(geometry.Columns)
                    .Where(p => Ok(p.X, p.Y)).ToList();
                if (drawn.Count > 0)
                {
                    double minX = drawn.Min(p => p.X) - cx, maxX = drawn.Max(p => p.X) - cx;
                    double minY = drawn.Min(p => p.Y) - cy, maxY = drawn.Max(p => p.Y) - cy;
                    double pad = Math.Max(1000.0, 0.05 * Math.Max(maxX - minX, maxY - minY));
                    const double nameHeightMm = 300.0;
                    foreach (var axis in geometry.GridAxes)
                    {
                        double at = axis.Vertical ? axis.AtMm - cx : axis.AtMm - cy;
                        if (!Ok(at, at)) continue;
                        var (x0, y0, x1, y1) = axis.Vertical ? (at, minY - pad, at, maxY + pad) : (minX - pad, at, maxX + pad, at);
                        G(0, "LINE"); G(8, gridLayer); G(62, "8");
                        Num(10, x0); Num(20, y0); Num(30, 0); Num(11, x1); Num(21, y1); Num(31, 0);
                        foreach (var (tx, ty) in new[] { (x0, y0), (x1, y1) })
                        {
                            G(0, "TEXT"); G(8, gridLayer); G(62, "8");
                            Num(10, tx); Num(20, ty); Num(30, 0); Num(40, nameHeightMm);
                            G(1, TextForDxf(axis.Name));
                        }
                    }
                }
            }

            G(0, "ENDSEC");
            G(0, "EOF");
        }
    }
}
