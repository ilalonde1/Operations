#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Imports a concrete schedule (Revit export or any CSV) for the absolute single-issue
    /// Structural Quantity Takeoff. Unlike <see cref="TakeoffCsvImporter"/> this reads the optional
    /// <c>Variant</c> column (slab type / wall kind / mat thickness) that drives the per-variant
    /// reinforcing density, and leaves the volume in whatever unit the caller selected — no
    /// conversion here (the density table and the volumes must share one <see cref="UnitSystem"/>).
    ///
    /// Columns (case-insensitive, order-independent; "(m³)" style suffixes ignored):
    ///   Level            — required (e.g. "P3", "L1", "Roof")
    ///   Element          — Slab | Wall | Beam | Column | Foundation | DropPanel (default Slab)
    ///   Variant          — optional (e.g. "parking", "residential", "shear", "84in")
    ///   ConcreteVolume   — required (also accepts ConcreteM3 / ConcreteYd3 / Volume)
    ///   FormworkArea     — optional (also accepts FormworkM2 / FormworkSqFt / Formwork)
    ///   Grade            — optional (e.g. "C30")
    /// </summary>
    public static class StructuralTakeoffCsvImporter
    {
        public static IReadOnlyList<StructuralTakeoffInput> Import(string csvText)
        {
            ArgumentNullException.ThrowIfNull(csvText);

            var rows = csvText
                .Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Where(l => l.Trim().Length > 0)
                .ToList();
            if (rows.Count < 2) return Array.Empty<StructuralTakeoffInput>();

            var header = ParseLine(rows[0]).Select(Normalize).ToList();
            int levelCol    = IndexOf(header, "level");
            int elementCol  = IndexOf(header, "element", "elementtype", "type");
            int variantCol  = IndexOf(header, "variant", "subtype", "kind", "slabtype", "thickness");
            int volumeCol   = IndexOf(header, "concretevolume", "concretem3", "concreteyd3", "concrete", "volume", "volumem3");
            int formworkCol = IndexOf(header, "formworkarea", "formworkm2", "formworksqft", "formwork");
            int gradeCol    = IndexOf(header, "grade", "gradecode", "concretegrade");

            if (levelCol < 0 || volumeCol < 0)
                throw new ArgumentException("CSV must have at least 'Level' and a concrete volume column.");

            var result = new List<StructuralTakeoffInput>();
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = ParseLine(rows[r]);
                string Cell(int i) => (i >= 0 && i < cells.Count) ? cells[i].Trim() : string.Empty;

                if (!TryParseDouble(Cell(volumeCol), out double volume) || volume < 0)
                    continue; // skip blank/invalid rows; never invent a quantity

                string variantRaw = Cell(variantCol);
                result.Add(new StructuralTakeoffInput(
                    Cell(levelCol),
                    ParseElement(Cell(elementCol)),
                    string.IsNullOrWhiteSpace(variantRaw) ? null : variantRaw,
                    volume,
                    TryParseDouble(Cell(formworkCol), out double fw) ? fw : 0,
                    Cell(gradeCol)));
            }
            return result;
        }

        private static TakeoffElementType ParseElement(string raw) => Normalize(raw) switch
        {
            "wall" => TakeoffElementType.Wall,
            "beam" or "framing" => TakeoffElementType.Beam,
            "column" => TakeoffElementType.Column,
            "foundation" or "footing" or "foundations" or "mat" => TakeoffElementType.Foundation,
            "droppanel" or "drop" => TakeoffElementType.DropPanel,
            _ => TakeoffElementType.Slab,
        };

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
                if (names.Contains(header[i])) return i;
            return -1;
        }

        private static bool TryParseDouble(string s, out double value) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

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
