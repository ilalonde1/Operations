#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>A row the schedule carried that could not be priced — surfaced, never dropped.</summary>
    public sealed record RevitScheduleResidual(string Source, string Row, string Note);

    /// <summary>What a set of Revit schedule exports came to, and everything inferred on the way.</summary>
    public sealed record RevitScheduleImportResult(
        IReadOnlyList<StructuralTakeoffInput> Inputs,
        IReadOnlyList<RevitScheduleResidual> Residual,
        IReadOnlyList<string> Notes,
        UnitSystem Unit,
        int RowsRead,
        // Concrete the schedules state that this takeoff could NOT place on a storey. A count of
        // skipped rows tells an estimator nothing; 76 m³ tells them whether to chase it.
        double UnplacedVolume);

    /// <summary>
    /// Reads the CSVs Revit actually exports, rather than the tidy one somebody has to make first.
    ///
    /// A Revit schedule export is not a data file. It opens with the schedule's own title, puts the
    /// header on the second row, leaves the third blank, names the level three different ways
    /// depending on the category — <c>Level</c> on floors and foundations, <c>Base Constraint</c> on
    /// walls, <c>Base Level</c> on columns — orders the columns differently in each, and writes the
    /// unit inside every cell ("489.03 m³"). Neither existing importer reads any of that, so four
    /// exports have been hand-assembled into one clean sheet every time a takeoff is done.
    ///
    /// The element type is not in the rows at all. It is in the TITLE — "Floor Schedule",
    /// "Wall Schedule 2", "Structural Column Schedule 2" — which is why the whole file has to be
    /// read as a document rather than a table.
    ///
    /// HONEST SCOPE: it prices what the schedule states and nothing else. Volume is Revit's own,
    /// so it is exact and includes every thickening and drop the model carries. Formwork is not in
    /// a volume schedule and is left at zero rather than estimated. A material of <c>&lt;varies&gt;</c>
    /// is Revit saying the rows disagree, and is recorded as no grade rather than a made-up one.
    /// Every inference — the element type from a title, the unit from a cell — is reported.
    /// </summary>
    public static class RevitScheduleImporter
    {
        /// <param name="files">Name and text of each export. The name is used only in reporting.</param>
        public static RevitScheduleImportResult Import(IEnumerable<(string Name, string Text)> files)
        {
            ArgumentNullException.ThrowIfNull(files);

            var inputs = new List<StructuralTakeoffInput>();
            var residual = new List<RevitScheduleResidual>();
            var notes = new List<string>();
            var unitsSeen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int rowsRead = 0;
            double unplaced = 0;

            foreach (var (name, text) in files)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;

                var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                if (lines.Length > 0) lines[0] = lines[0].TrimStart('﻿');

                // The header is the first row that names both a level and a volume. Everything
                // above it is the schedule's title, and the title is where the element type lives.
                int headerRow = -1;
                List<string> header = new();
                for (int i = 0; i < lines.Length && i < 10; i++)
                {
                    var cells = ParseLine(lines[i]).Select(Normalize).ToList();
                    if (LevelIndex(cells) >= 0 && VolumeIndex(cells) >= 0) { headerRow = i; header = cells; break; }
                }

                if (headerRow < 0)
                {
                    residual.Add(new RevitScheduleResidual(name, "(whole file)",
                        "no row names both a level and a volume, so this is not a Revit quantity schedule. Export the schedule with its Level and Volume fields."));
                    continue;
                }

                string title = string.Join(" ", lines.Take(headerRow).Select(l => ParseLine(l).FirstOrDefault() ?? "")).Trim();
                var element = ElementFromTitle(title);

                if (element is null)
                {
                    residual.Add(new RevitScheduleResidual(name, title.Length > 0 ? title : "(no title row)",
                        "the schedule's title does not say what the elements are, so nothing was priced from it. Name the schedule for its category — Floor, Wall, Structural Column, Structural Foundation, Structural Framing."));
                    continue;
                }

                notes.Add($"{name}: read as {element} from the schedule title \"{(title.Length > 0 ? title : "(none)")}\".");

                int levelCol = LevelIndex(header), volumeCol = VolumeIndex(header);
                int gradeCol = IndexOf(header, "structuralmaterial", "material", "grade", "concretegrade");
                int variantCol = IndexOf(header, "type", "family", "familyandtype", "typemark", "structuralusage");

                double fileSum = 0;
                double? statedTotal = null;

                for (int r = headerRow + 1; r < lines.Length; r++)
                {
                    if (lines[r].Trim().Trim(',').Length == 0) continue;      // Revit's blank separator row

                    var cells = ParseLine(lines[r]);
                    string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : "";

                    string level = Cell(levelCol);
                    string rawVolume = Cell(volumeCol);

                    // REVIT'S OWN GRAND TOTAL IS A DUPLICATE OF THE CATEGORY, NOT A LEVEL.
                    //
                    // A schedule with totals switched on ends "Grand total: 140,10135.60 m³". Priced
                    // as a row it adds the whole category a second time — on 31065 that was
                    // 10,135.6 m³ against a real 15,990, so the building came out 63% too big and
                    // looked like a plausible number. It is not skipped quietly either: it is
                    // exactly what the rows should sum to, so it is kept and checked against them.
                    if (IsTotalRow(level))
                    {
                        var (t, _) = ReadVolume(rawVolume);
                        if (t is not null) statedTotal = t;
                        continue;
                    }

                    if (level.Length == 0)
                    {
                        var (orphan, _) = ReadVolume(rawVolume);
                        if (orphan is not null) unplaced += orphan.Value;
                        residual.Add(new RevitScheduleResidual(name, lines[r].Trim(),
                            orphan is not null
                                ? $"the row states no level, so its {orphan.Value:N1} of concrete belongs to no storey. Set the element's level in Revit, or add it by hand."
                                : "the row states no level and no volume."));
                        continue;
                    }

                    var (volume, unit) = ReadVolume(rawVolume);
                    if (volume is null)
                    {
                        // A totals row Revit appends is not a defect; it is a duplicate, and pricing
                        // it would double the building.
                        residual.Add(new RevitScheduleResidual(name, lines[r].Trim(),
                            $"'{rawVolume}' is not a volume."));
                        continue;
                    }
                    if (unit is not null) unitsSeen.Add(unit);

                    rowsRead++;
                    fileSum += volume.Value;
                    inputs.Add(new StructuralTakeoffInput(
                        level,
                        element.Value,
                        Variant(Cell(variantCol)),
                        volume.Value,
                        FormworkArea: 0,
                        Grade: Grade(Cell(gradeCol))));
                }

                // The schedule's own total is a free check on our reading of it.
                if (statedTotal is double stated)
                {
                    double drift = Math.Abs(stated - fileSum);
                    notes.Add(drift <= Math.Max(0.5, stated * 0.001)
                        ? $"{name}: the rows sum to {fileSum:N1}, matching the schedule's own grand total of {stated:N1}. The total row itself is not priced."
                        : $"WARNING — {name}: the rows sum to {fileSum:N1} but the schedule's grand total says {stated:N1}, a difference of {drift:N1}. Rows are being missed or misread; the total row itself is not priced.");
                }
            }

            // The unit is read off the cells, not assumed. Revit writes it in every one.
            var system = unitsSeen.Any(u => u.StartsWith("yd", StringComparison.OrdinalIgnoreCase)
                                         || u.Equals("CY", StringComparison.OrdinalIgnoreCase))
                ? UnitSystem.Imperial
                : UnitSystem.Metric;

            notes.Add(unitsSeen.Count switch
            {
                0 => "No unit was printed in any volume cell; the schedule's numbers are taken as cubic metres. Check the Revit project units if that is wrong.",
                1 => $"Volumes are in {unitsSeen.First()}, read from the cells.",
                _ => $"WARNING — the exports do not agree on a unit ({string.Join(", ", unitsSeen)}). Every number is being treated as {system}. Re-export them from one project unit setting.",
            });

            if (unplaced > 0)
                notes.Add($"WARNING — {unplaced:N1} of concrete sits on rows that state no level, and is NOT in any figure here. "
                    + "It is real concrete the model carries: set those elements' level in Revit, or add it by hand.");

            notes.Add("Formwork is not in a volume schedule and is left at zero rather than estimated. Concrete is Revit's own volume, so every thickening and drop the model carries is already in it.");

            var grouped = inputs
                .GroupBy(i => (i.Level, i.Element, i.Variant, i.Grade))
                .Select(g => new StructuralTakeoffInput(
                    g.Key.Level, g.Key.Element, g.Key.Variant, g.Sum(x => x.ConcreteVolume), 0, g.Key.Grade))
                .OrderBy(i => i.Level, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Element)
                .ToList();

            return new RevitScheduleImportResult(grouped, residual, notes, system, rowsRead, unplaced);
        }

        // ---- what the schedule is -------------------------------------------------------------

        /// <summary>The element type, from the schedule's own title. Revit names a schedule for its
        /// category by default — "Floor Schedule", "Structural Column Schedule 2" — and that name is
        /// the only place in the file that says what the rows are.</summary>
        private static TakeoffElementType? ElementFromTitle(string title)
        {
            string t = title.ToUpperInvariant();

            // Foundation before column and wall: "Structural Foundation" contains neither, but a
            // renamed "Column Footing Schedule" contains both and is a foundation.
            if (t.Contains("FOUNDATION") || t.Contains("FOOTING") || t.Contains("PILE") || t.Contains("PILECAP")) return TakeoffElementType.Foundation;
            if (t.Contains("DROP PANEL") || t.Contains("DROPPANEL")) return TakeoffElementType.DropPanel;
            if (t.Contains("COLUMN")) return TakeoffElementType.Column;
            if (t.Contains("WALL")) return TakeoffElementType.Wall;
            if (t.Contains("BEAM") || t.Contains("FRAMING")) return TakeoffElementType.Beam;
            if (t.Contains("FLOOR") || t.Contains("SLAB")) return TakeoffElementType.Slab;
            return null;
        }

        /// <summary>Revit's total rows, which name a count where a level belongs: "Grand total: 140",
        /// "Total", "Sum". No storey is called any of these.</summary>
        private static bool IsTotalRow(string level)
        {
            string t = level.Trim().ToUpperInvariant();
            return t.StartsWith("GRAND TOTAL", StringComparison.Ordinal)
                || t.StartsWith("TOTAL", StringComparison.Ordinal)
                || t.StartsWith("SUM", StringComparison.Ordinal);
        }

        private static int LevelIndex(IReadOnlyList<string> header) =>
            IndexOf(header, "level", "baseconstraint", "baselevel", "referencelevel", "schedulelevel", "basestory", "story", "storey");

        private static int VolumeIndex(IReadOnlyList<string> header) =>
            IndexOf(header, "volume", "concretevolume", "netvolume", "concretem3", "concreteyd3");

        /// <summary>The number and the unit Revit printed beside it — "489.03 m³" is both.</summary>
        private static (double? Volume, string? Unit) ReadVolume(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell)) return (null, null);

            string s = cell.Replace(",", "").Trim();
            var m = Regex.Match(s, @"^(-?\d+(?:\.\d+)?)\s*([^\d\s]*)$");
            if (!m.Success) return (null, null);
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0)
                return (null, null);

            string unit = m.Groups[2].Value.Trim();
            return (v, unit.Length > 0 ? unit : null);
        }

        /// <summary>Revit writes &lt;varies&gt; when the rows behind a total disagree. That is the
        /// model declining to answer, and it is recorded as no grade rather than guessed.</summary>
        private static string Grade(string cell) =>
            cell.Length == 0 || cell.StartsWith("<", StringComparison.Ordinal) ? "" : cell;

        private static string? Variant(string cell) =>
            cell.Length == 0 || cell.StartsWith("<", StringComparison.Ordinal) ? null : cell.ToLowerInvariant();

        // ---- csv -------------------------------------------------------------------------------

        private static string Normalize(string cell) =>
            new string(cell.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static int IndexOf(IReadOnlyList<string> header, params string[] names)
        {
            for (int i = 0; i < header.Count; i++)
                if (names.Contains(header[i], StringComparer.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static List<string> ParseLine(string line)
        {
            var cells = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }
}
