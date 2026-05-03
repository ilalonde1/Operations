#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Minimal RFC 4180 CSV parser. Handles quoted fields, escaped double-quotes,
/// CRLF/LF line endings. Auto-detects the delimiter (', ; \t |') by trying each
/// and picking the one that produces the widest first row — robust against
/// CanadaBuys (which has used both ',' and ';' in different exports).
/// </summary>
public static class CsvParser
{
    private static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };

    /// <summary>Parses an entire CSV string into a list of rows.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string csv, char? preferredDelimiter = null)
    {
        if (string.IsNullOrEmpty(csv))
        {
            return System.Array.Empty<IReadOnlyList<string>>();
        }

        var delimiters = preferredDelimiter.HasValue
            ? new[] { preferredDelimiter.Value }.Concat(CandidateDelimiters).Distinct().ToArray()
            : CandidateDelimiters;

        IReadOnlyList<IReadOnlyList<string>>? best = null;
        var bestCols = 0;
        foreach (var d in delimiters)
        {
            var rows = ParseWithDelimiter(csv, d);
            if (rows.Count == 0)
            {
                continue;
            }

            var cols = rows[0].Count;
            if (cols > bestCols)
            {
                bestCols = cols;
                best = rows;
            }
        }

        return best ?? System.Array.Empty<IReadOnlyList<string>>();
    }

    /// <summary>
    /// Looks up a value from <paramref name="row"/> by trying each candidate
    /// header name in order. Header matching is case-insensitive, ignores
    /// punctuation/whitespace, and accepts substring matches in either direction
    /// (CanadaBuys headers are bilingual hyphenated, e.g. <c>noticeTitle-titreAvis</c>).
    /// Returns null if no header matches or the row is shorter than the header index.
    /// </summary>
    public static string? FirstValue(
        IReadOnlyList<string> row,
        IReadOnlyList<string> normalizedHeaders,
        params string[] candidateHeaders)
    {
        foreach (var candidate in candidateHeaders)
        {
            var target = NormalizeToken(candidate);
            if (target.Length == 0)
            {
                continue;
            }

            for (var i = 0; i < normalizedHeaders.Count; i++)
            {
                var h = normalizedHeaders[i];
                if (h == target || h.Contains(target) || target.Contains(h))
                {
                    if (i < row.Count && !string.IsNullOrWhiteSpace(row[i]))
                    {
                        return row[i].Trim();
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Lowercase, alphanumeric-only — used for fuzzy header matching.</summary>
    public static string NormalizeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Strips a UTF-8 BOM from the leading header cell, then trims.</summary>
    public static IReadOnlyList<string> NormalizeHeaderRow(IReadOnlyList<string> headers)
    {
        var result = new List<string>(headers.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            var v = headers[i];
            if (i == 0)
            {
                v = v.TrimStart('﻿');
            }

            result.Add(NormalizeToken(v.Trim()));
        }

        return result;
    }

    private static List<IReadOnlyList<string>> ParseWithDelimiter(string csv, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                continue;
            }

            if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
                continue;
            }

            field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
