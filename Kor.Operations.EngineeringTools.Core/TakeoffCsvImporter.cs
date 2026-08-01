#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Imports a concrete-quantity schedule (exported from Revit, or any spreadsheet saved
    /// as CSV) into takeoff lines. This is the source-of-truth path Rory described —
    /// "concrete is easy as that's from Revit" — with no PDF extraction and no SAFE coupling.
    ///
    /// Expected columns (case-insensitive, order-independent; "(m³)" style units ignored):
    ///   Level        — required, e.g. "P3", "L1"
    ///   Element      — Slab | Wall | Beam | Column | DropPanel  (default Slab)
    ///   Grade        — e.g. "C30"
    ///   ConcreteM3   — required, concrete volume in m³ (Revit gives this directly)
    ///   RebarKg      — optional; blank => estimated from the density table
    ///   FormworkM2   — optional; blank => 0
    /// </summary>
    public static class TakeoffCsvImporter
    {
        public static IReadOnlyList<TakeoffLineResult> Import(string csvText, RebarDensityTable densities)
        {
            ArgumentNullException.ThrowIfNull(csvText);
            ArgumentNullException.ThrowIfNull(densities);

            var rows = csvText
                .Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Where(l => l.Trim().Length > 0)
                .ToList();

            if (rows.Count < 2)
                return Array.Empty<TakeoffLineResult>();

            var header = ParseLine(rows[0]).Select(Normalize).ToList();
            int levelCol    = IndexOf(header, "level");
            int elementCol  = IndexOf(header, "element", "elementtype", "category", "type");
            int gradeCol    = IndexOf(header, "grade", "gradecode", "concretegrade");
            int concreteCol = IndexOf(header, "concretem3", "concrete", "concretevolume", "volume", "volumem3");
            int rebarCol    = IndexOf(header, "rebarkg", "rebar", "rebarweight");
            int formworkCol = IndexOf(header, "formworkm2", "formwork", "formworkarea");
            int changeCol   = IndexOf(header, "change", "primarychange", "description");
            int categoryCol = IndexOf(header, "category", "cat");

            if (levelCol < 0 || concreteCol < 0)
                throw new ArgumentException("CSV must have at least 'Level' and 'ConcreteM3' columns.");

            var result = new List<TakeoffLineResult>();
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = ParseLine(rows[r]);
                string Cell(int i) => (i >= 0 && i < cells.Count) ? cells[i].Trim() : string.Empty;

                string level = Cell(levelCol);
                var elementType = ParseElement(Cell(elementCol));
                string grade = Cell(gradeCol);
                string change = Cell(changeCol);
                string category = Cell(categoryCol);

                if (!TryParseDouble(Cell(concreteCol), out double concreteM3) || concreteM3 < 0)
                {
                    result.Add(new TakeoffLineResult(
                        elementType, level, grade, 0, 0, 0,
                        RebarSource.Density, TakeoffConfidence.Review, Unresolved: true,
                        $"Concrete volume missing or invalid on row {r + 1} - excluded from totals",
                        Change: change, Category: category));
                    continue;
                }

                double formworkM2 = TryParseDouble(Cell(formworkCol), out double fw) ? fw : 0;

                double rebarKg;
                RebarSource rebarSource;
                if (TryParseDouble(Cell(rebarCol), out double rebar))
                {
                    rebarKg = rebar;
                    rebarSource = RebarSource.Modeled; // a number was supplied (Revit/QS)
                }
                else
                {
                    rebarKg = concreteM3 * densities.For(elementType);
                    rebarSource = RebarSource.Density;  // estimated
                }

                result.Add(new TakeoffLineResult(
                    elementType, level, grade, concreteM3, formworkM2, rebarKg,
                    rebarSource, TakeoffConfidence.High, Unresolved: false, Note: null,
                    Change: change, Category: category));
            }

            return result;
        }

        private static TakeoffElementType ParseElement(string raw)
        {
            switch (Normalize(raw))
            {
                case "wall": return TakeoffElementType.Wall;
                case "beam":
                case "framing": return TakeoffElementType.Beam;
                case "column": return TakeoffElementType.Column;
                case "foundation":
                case "footing":
                case "foundations": return TakeoffElementType.Foundation;
                case "droppanel":
                case "drop": return TakeoffElementType.DropPanel;
                case "slab":
                case "floor":
                case "":
                default: return TakeoffElementType.Slab;
            }
        }

        // Normalise a header/element token: lower-case, drop any "(...)" unit suffix,
        // and strip everything except a-z0-9 so "Concrete (m³)" and "ConcreteM3" both match.
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            int paren = s.IndexOf('(');
            if (paren >= 0) s = s[..paren];
            return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static int IndexOf(List<string> header, params string[] names)
        {
            for (int i = 0; i < header.Count; i++)
                if (names.Contains(header[i]))
                    return i;
            return -1;
        }

        private static bool TryParseDouble(string s, out double value) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        // Minimal CSV line parser: handles double-quoted fields with embedded commas/quotes.
        private static List<string> ParseLine(string line)
        {
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
