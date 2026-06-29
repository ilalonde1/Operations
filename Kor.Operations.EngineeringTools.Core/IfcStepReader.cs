#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// One parsed IFC (ISO-10303-21 / STEP) instance: its <c>#id</c>, entity keyword (e.g. IFCSLAB) and the
    /// top-level argument tokens exactly as written. Lists, references (<c>#42</c>), strings, enums (<c>.FLOOR.</c>),
    /// numbers and <c>$</c>/<c>*</c> are kept as raw strings — the takeoff resolves them on demand, so the reader
    /// stays a dumb, total tokenizer that never has to understand every IFC entity in the schema.
    /// </summary>
    public sealed record IfcEntity(int Id, string Type, IReadOnlyList<string> Args);

    /// <summary>
    /// A minimal, dependency-free reader for the STEP physical file an IFC export is. It does ONE thing:
    /// turn the DATA section into <c>#id → IfcEntity</c> with top-level args split correctly (respecting
    /// quotes, nested parens and the <c>''</c> string escape). It deliberately does not model the IFC schema —
    /// that keeps it robust across IFC2x3 / IFC4 and across exporters (Revit, Tekla, ArchiCAD). The quantity
    /// takeoff layered on top is where structural meaning lives.
    /// </summary>
    public static class IfcStepReader
    {
        /// <summary>Parse the whole STEP text into an id-indexed entity map. Lines outside DATA (HEADER,
        /// ISO markers) are skipped. Multi-line instances are handled — the parser scans to the terminating
        /// <c>;</c>, not to the end of a physical line.</summary>
        public static IReadOnlyDictionary<int, IfcEntity> Parse(string stepText)
        {
            ArgumentNullException.ThrowIfNull(stepText);
            var map = new Dictionary<int, IfcEntity>();

            int i = 0, n = stepText.Length;
            bool inData = false;
            while (i < n)
            {
                // Find the next '#'. Cheap scan; quotes outside an instance don't occur in well-formed STEP.
                int hash = stepText.IndexOf('#', i);
                if (hash < 0) break;

                if (!inData)
                {
                    int dataAt = IndexOfToken(stepText, "DATA;", 0, hash);
                    if (dataAt < 0) { i = hash + 1; if (!LooksLikeAssignment(stepText, hash)) continue; }
                    inData = true;
                }

                // Parse "#<id> = <KEYWORD> ( <args> ) ;"
                int p = hash + 1;
                int id = ReadInt(stepText, ref p);
                if (id < 0) { i = hash + 1; continue; }
                SkipWs(stepText, ref p);
                if (p >= n || stepText[p] != '=') { i = hash + 1; continue; }
                p++;
                SkipWs(stepText, ref p);

                int kwStart = p;
                while (p < n && (char.IsLetterOrDigit(stepText[p]) || stepText[p] == '_')) p++;
                if (p == kwStart) { i = hash + 1; continue; }
                string keyword = stepText.Substring(kwStart, p - kwStart).ToUpperInvariant();
                SkipWs(stepText, ref p);
                if (p >= n || stepText[p] != '(') { i = hash + 1; continue; }

                var args = SplitArgs(stepText, ref p);     // consumes through the matching ')'
                map[id] = new IfcEntity(id, keyword, args);

                // Advance past the trailing ';'
                int semi = stepText.IndexOf(';', p);
                i = semi < 0 ? p : semi + 1;
            }

            return map;
        }

        /// <summary>Split the top-level, comma-separated argument list starting at the opening '('. On return
        /// <paramref name="p"/> points just past the matching ')'. Nested parens, 'strings' (with '' escape)
        /// and references are preserved verbatim as single tokens.</summary>
        private static List<string> SplitArgs(string s, ref int p)
        {
            var args = new List<string>();
            int n = s.Length;
            p++;                                   // past '('
            var cur = new StringBuilder();
            int depth = 0;
            bool inStr = false;

            while (p < n)
            {
                char c = s[p];
                if (inStr)
                {
                    if (c == '\'')
                    {
                        if (p + 1 < n && s[p + 1] == '\'') { cur.Append("''"); p += 2; continue; } // escaped quote
                        inStr = false; cur.Append(c); p++; continue;
                    }
                    cur.Append(c); p++; continue;
                }

                switch (c)
                {
                    case '\'': inStr = true; cur.Append(c); p++; break;
                    case '(': depth++; cur.Append(c); p++; break;
                    case ')':
                        if (depth == 0) { p++; args.Add(cur.ToString().Trim()); return args; }
                        depth--; cur.Append(c); p++; break;
                    case ',':
                        if (depth == 0) { args.Add(cur.ToString().Trim()); cur.Clear(); p++; }
                        else { cur.Append(c); p++; }
                        break;
                    default: cur.Append(c); p++; break;
                }
            }
            args.Add(cur.ToString().Trim());       // unterminated — return what we have
            return args;
        }

        /// <summary>The comma-split members of a STEP list token like "(#1,#2,#3)" — or empty for "$"/"()".</summary>
        public static IReadOnlyList<string> ListItems(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token == "$") return Array.Empty<string>();
            string t = token.Trim();
            if (t.Length < 2 || t[0] != '(' || t[^1] != ')') return new[] { t };
            int p = 0;
            return SplitArgs(t, ref p);
        }

        /// <summary>Reference id in a token like "#42", or null if the token is not a single reference.</summary>
        public static int? RefId(string token)
        {
            if (string.IsNullOrEmpty(token) || token[0] != '#') return null;
            int p = 1;
            int id = ReadInt(token, ref p);
            return id >= 0 && p == token.Length ? id : (int?)null;
        }

        /// <summary>Unquote a STEP string token ("'A''B'" → "A'B"); pass through $ as empty.</summary>
        public static string Text(string token)
        {
            string t = token.Trim();
            if (t.Length >= 2 && t[0] == '\'' && t[^1] == '\'')
                return t.Substring(1, t.Length - 2).Replace("''", "'");
            return t == "$" ? "" : t;
        }

        /// <summary>Parse a STEP real/number token, or null. Accepts the IFC "1.23E-4" / "1." forms.</summary>
        public static double? Number(string token)
        {
            string t = token.Trim();
            if (t.Length == 0 || t == "$" || t == "*") return null;
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        /// <summary>A STEP enum token ".FLOOR." → "FLOOR" (upper-cased), else "".</summary>
        public static string Enum(string token)
        {
            string t = token.Trim();
            return t.Length >= 2 && t[0] == '.' && t[^1] == '.' ? t.Substring(1, t.Length - 2).ToUpperInvariant() : "";
        }

        // ---- small scanners ----
        private static void SkipWs(string s, ref int p)
        { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; }

        private static int ReadInt(string s, ref int p)
        {
            int start = p;
            while (p < s.Length && char.IsDigit(s[p])) p++;
            return p > start ? int.Parse(s.Substring(start, p - start), CultureInfo.InvariantCulture) : -1;
        }

        private static bool LooksLikeAssignment(string s, int hash)
        {
            int p = hash + 1;
            while (p < s.Length && char.IsDigit(s[p])) p++;
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            return p < s.Length && s[p] == '=';
        }

        private static int IndexOfToken(string s, string token, int from, int before)
        {
            int at = s.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
            return at >= 0 && at < before ? at : -1;
        }
    }
}
