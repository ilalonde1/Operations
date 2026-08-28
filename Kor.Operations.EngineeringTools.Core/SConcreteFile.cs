#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.ColumnDesign
{
    /// <summary>One demand on one column: a load case, and the forces that go with it.</summary>
    /// <param name="Storey">The storey the column stands on, as ETABS names it.</param>
    /// <param name="Mark">The column mark — C75, C104.</param>
    /// <param name="Case">The engineer's name for the combination: Grav1, EQX1, WY2.</param>
    /// <param name="Section">The section as written in the file, e.g. 12X30.</param>
    /// <param name="Strength">Concrete strength as written, e.g. 45Mpa.</param>
    /// <param name="EffectiveLength">kl, in the file's own units.</param>
    /// <param name="Nf">Axial. Negative is compression, as S-Concrete writes it.</param>
    public sealed record ColumnDemand(
        string Storey, string Mark, string Case, string Section, string Strength, double? EffectiveLength,
        double Nf, double Tf, double Vfz, double Mfy, double Vfy, double Mfz)
    {
        public string Key => $"{Storey}|{Mark}|{Case}";

        /// <summary>
        /// True when S-Concrete's 60-character Comment limit cut the identity short before the
        /// effective length.
        ///
        /// It happens whenever the section name is long, and the current Excel route writes a round
        /// column's equivalent square at full float precision —
        /// <c>15.9520846581496X15.9520846581496</c> for what the filename calls D18. Thirty-three
        /// characters of noise, and kl falls off the end: on 30961-01 that is 225 of 1,665 demands
        /// whose effective length is not recorded anywhere in the file.
        /// </summary>
        public bool IdentityTruncated => EffectiveLength is null;
    }

    /// <summary>A table inside an S-Concrete file, kept as read so a file can be round-tripped.</summary>
    public sealed record SConcreteTable(string Object, IReadOnlyList<string> Header, IReadOnlyList<string[]> Rows);

    /// <summary>
    /// Reads S-Concrete <c>.SCO</c> files.
    ///
    /// An .SCO is plain text: a sequence of <c>@Object@name@</c> / <c>@Table@n@</c> / header row /
    /// tab-separated rows / <c>@EndTable@</c>, with CRLF line endings. The one that matters is
    /// "S-CONCRETE Sectional Loads", whose Comment column carries the identity of the demand:
    ///
    ///     L02TH  C75 -&gt; Grav1, 12X30, 45Mpa, kl 8.8497,Cm-1, Slen Min-N
    ///
    /// storey, column mark, load case, section, concrete strength and effective length. Every row of
    /// every file examined carries <c>AutoGen 0</c> — none of it was generated; it was typed.
    ///
    /// WHY THIS EXISTS. On 30961-01 alone, three engineers kept 66 of these files between them, one
    /// per section per level range per group of column marks, with the marks recorded by typing them
    /// into the filename. Reading them is the first half of generating them, and generating them is
    /// the point: the demands come out of an ETABS run, and when the model changes every file has to
    /// be made again by hand.
    /// </summary>
    public static class SConcreteFile
    {
        public const string SectionalLoads = "S-CONCRETE Sectional Loads";

        private static readonly Regex ObjectLine = new(@"^@Object@(.*)@\s*$", RegexOptions.Compiled);

        /// <summary>
        /// The identity S-Concrete keeps in the Comment column, which is the only place the storey,
        /// mark and case are recorded.
        ///
        /// Read in pieces rather than as one pattern, because the field is capped at about sixty
        /// characters and a long section name pushes the tail of it off the end. Demanding the whole
        /// thing dropped 225 of 1,665 demands on 30961-01 without a word, which is the one behaviour
        /// a reader of someone else's structural design must never have.
        /// </summary>
        private static readonly Regex Identity = new(
            @"^\s*(?<storey>\S+)\s+(?<mark>C\d+)\s*->\s*(?<case>[^,]+)(?:,\s*(?<section>[^,]+))?(?:,\s*(?<fc>[^,]+))?",
            RegexOptions.Compiled);

        private static readonly Regex EffectiveLengthIn = new(@"\bkl\s*(?<kl>[\d.]+)", RegexOptions.Compiled);

        public static IReadOnlyList<SConcreteTable> ReadTables(IEnumerable<string> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            var all = lines as IList<string> ?? lines.ToList();
            var tables = new List<SConcreteTable>();

            for (int i = 0; i < all.Count; i++)
            {
                var m = ObjectLine.Match(all[i]);
                if (!m.Success) continue;

                string name = m.Groups[1].Value;
                if (++i >= all.Count || !all[i].StartsWith("@Table@", StringComparison.Ordinal)) continue;
                if (++i >= all.Count) break;

                var header = all[i].Split('\t').Select(h => h.Trim()).ToList();
                var rows = new List<string[]>();
                for (i++; i < all.Count && !all[i].StartsWith("@EndTable@", StringComparison.Ordinal); i++)
                    rows.Add(all[i].Split('\t'));

                tables.Add(new SConcreteTable(name, header, rows));
            }

            return tables;
        }

        public static IReadOnlyList<SConcreteTable> ReadTables(string path) =>
            ReadTables(File.ReadAllLines(path, Encoding.Latin1));

        /// <summary>
        /// Every column demand the file states. Rows whose Comment is not an identity — S-Concrete's
        /// own generated alternates, "** Alt. LC # 1" — are skipped: they are the program's output,
        /// not the engineer's input.
        /// </summary>
        public static IReadOnlyList<ColumnDemand> ReadDemands(IEnumerable<string> lines)
        {
            var result = new List<ColumnDemand>();

            foreach (var table in ReadTables(lines))
            {
                if (!table.Object.Contains("Sectional Loads", StringComparison.OrdinalIgnoreCase)) continue;

                int comment = table.Header.FindIndexOf("Comment");
                if (comment < 0) continue;

                foreach (var row in table.Rows)
                {
                    if (row.Length <= comment) continue;

                    var id = Identity.Match(row[comment]);
                    if (!id.Success) continue;

                    // Column order is fixed by the format: LC, Nf, Tf, Vfz, Mfy, Cmy, Vfy, Mfz, Cmz…
                    if (!(Num(row, 1) is double nf && Num(row, 2) is double tf && Num(row, 3) is double vfz
                          && Num(row, 4) is double mfy && Num(row, 6) is double vfy && Num(row, 7) is double mfz))
                        continue;

                    var kl = EffectiveLengthIn.Match(row[comment]);

                    result.Add(new ColumnDemand(
                        id.Groups["storey"].Value,
                        id.Groups["mark"].Value,
                        id.Groups["case"].Value.Trim(),
                        id.Groups["section"].Success ? id.Groups["section"].Value.Trim() : "",
                        id.Groups["fc"].Success ? id.Groups["fc"].Value.Trim() : "",
                        kl.Success ? double.Parse(kl.Groups["kl"].Value, CultureInfo.InvariantCulture) : null,
                        nf, tf, vfz, mfy, vfy, mfz));
                }
            }

            return result;
        }

        public static IReadOnlyList<ColumnDemand> ReadDemands(string path) =>
            ReadDemands(File.ReadAllLines(path, Encoding.Latin1));

        /// <summary>The identity line S-Concrete keeps in the Comment column, written the way the
        /// engineers write it — so a generated file is indistinguishable from a typed one.</summary>
        public static string Comment(ColumnDemand d) =>
            $"{d.Storey}  {d.Mark} -> {d.Case}, {d.Section}, {d.Strength}"
            + (d.EffectiveLength is double kl
                ? $", kl {kl.ToString("0.####", CultureInfo.InvariantCulture)},Cm-1"
                : "");

        private static double? Num(string[] row, int i) =>
            i < row.Length && double.TryParse(row[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : null;

        private static int FindIndexOf(this IReadOnlyList<string> header, string name)
        {
            for (int i = 0; i < header.Count; i++)
                if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
