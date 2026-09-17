#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;

namespace Kor.Operations.EngineeringTools.Knowledge;

/// <summary>
/// THE PROFESSION'S KNOWLEDGE, INGESTED (WP7, 2026-09-16, Ian's go: "go to the internal egbc brain"). A clause the
/// app leans on is a row in <c>knowledge.Clause</c> (migration 096) with the clause id as its source numbers it,
/// the page in KOR's copy, the requirement as a value where it is one, and a confidence. Rows written from memory
/// carry <c>from-memory-unverified</c> and no page; this reads KOR's copy of the document, finds the clause, and
/// lifts the row to <c>read-from-source</c> with the page and, where the clause states a number in the units the
/// row expects, the value it states - so a value remembered wrongly is CORRECTED by the source, and the difference
/// is reported. What it stores: the page, the value, and its own name in <c>ReadBy</c>. What it never stores: the
/// source's prose - the NBC, BCBC and CSA texts are licensed (<c>knowledge.Source.Licence</c> says which may be
/// quoted: EGBC's published guidelines and KOR's own PPMP).
/// </summary>
/// <remarks>
/// HOW A CLAUSE IS FOUND: the document is read one text per page (<see cref="PdfPageTextReader"/>, the reader the
/// rebar tools use); a clause ref like <c>9.8.4.2</c> is found where it stands at the head of a sentence - preceded
/// by a space or a page start and followed by a space, a period or a parenthesis - and NOT as part of a longer ref
/// (<c>9.8.4.2</c> is not <c>9.8.4.2.1</c>'s head) and not in the table of contents (a page where more than a
/// tenth of the words are clause refs is an index). The FIRST page where it stands so is the clause's page; where
/// the same ref stands on several pages (a clause continued), the first. The VALUE is the first number followed by
/// the row's units within the clause's first 400 characters. WHAT THIS DOES NOT: read a table (a value in a table
/// stays for a person); tell Division A from Division B (the ref is taken as the caller means it); read scanned
/// PDFs with no text layer.
/// </remarks>
public static class ClauseIngest
{
    public sealed record Found(string ClauseRef, int Page, string? Value, string? Units, string Head);

    /// <summary>Where a clause stands in a document read as one text per page (1-based pages). Null when it stands nowhere.</summary>
    public static Found? Find(IReadOnlyList<string> pagesText, string clauseRef, string? units)
    {
        string esc = Regex.Escape(clauseRef);
        // at the head of a sentence: a page start or whitespace before; whitespace, a period, a colon or a parenthesis after; not a longer ref
        var head = new Regex(@"(?:^|\s)" + esc + @"(?=[\s.:(])(?!\.\d)", RegexOptions.Compiled);
        var anyRef = new Regex(@"(?:^|\s)\d+\.\d+(?:\.\d+)*(?=[\s.:(])", RegexOptions.Compiled);
        for (int i = 0; i < pagesText.Count; i++)
        {
            string text = pagesText[i];
            var m = head.Match(text);
            if (!m.Success) continue;
            // an index page names every clause once: refs are a tenth and more of its words
            int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            int refs = anyRef.Matches(text).Count;
            if (words > 0 && refs * 10 >= words) continue;
            string after = text.Substring(m.Index, Math.Min(400, text.Length - m.Index)).Trim();
            string? value = null;
            if (!string.IsNullOrWhiteSpace(units))
            {
                var v = Regex.Match(after, @"(\d+(?:[.,]\d+)?)\s*" + Regex.Escape(units) + @"\b");
                if (v.Success) value = v.Groups[1].Value.Replace(",", "");
            }
            return new Found(clauseRef, i + 1, value, units, after.Length > 120 ? after[..120] : after);
        }
        return null;
    }

    /// <summary>The document's pages as text, through the reader the rebar tools use.</summary>
    public static IReadOnlyList<string> ReadPages(string pdfPath) => PdfPageTextReader.ReadPages(pdfPath);

    public sealed record Section(string Ref, string Title, int Page);

    /// <summary>
    /// The numbered section headings of a document with the page each stands on: the navigable index of a guideline
    /// (EGBC's Professional Practice Guidelines number their sections 1, 1.1, 1.1.1). A heading is a text line that
    /// starts with a section number and carries a short title (four to eighty characters, starting with a capital); a
    /// page carrying eight or more such lines is the table of contents and is skipped; the first page after it where
    /// the heading stands is the section's page. Headings are titles, not the document's prose - storable whatever
    /// the licence; the requirement column holds the title.
    /// </summary>
    public static IReadOnlyList<Section> IndexSections(string pdfPath)
    {
        // a numbered heading: "3.3.2 DESIGN DEVELOPMENT STAGE" - the number carries a dot (1.0, 1.1, 2.2.1; a bare "2" is a
        // list item, "2018" a year), the title starts with a capital. A two-column page reads two headings on one
        // line ("2.1 COMMON FORMS OF 2.2 RESPONSIBILITIES"): the line is split at every inner number. A heading set
        // in capitals (EGBC's guidelines) ends where the capitals end - what follows is the column's body text.
        var heading = new Regex(@"(?:^|\s)(\d+\.\d+(?:\.\d+){0,2})\.?\s+(?=[A-Z])", RegexOptions.Compiled);
        var perPage = new List<List<(string Ref, string Title)>>();
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            foreach (var page in doc.GetPages())
            {
                var content = VectorPageReader.ReadPage(page);
                var found = new List<(string, string)>();
                var lines = VectorPageReader.ReadTextLines(content).Select(l => l.Text.Trim()).ToList();
                for (int li = 0; li < lines.Count; li++)
                {
                    string text = lines[li];
                    if (text.Contains("....")) continue;   // a contents line
                    var ms = heading.Matches(text);
                    if (ms.Count == 0 || ms[0].Index > 0) continue;   // a heading starts the line
                    for (int k = 0; k < ms.Count; k++)
                    {
                        int start = ms[k].Index + ms[k].Length;
                        int end = k + 1 < ms.Count ? ms[k + 1].Index : text.Length;
                        string title = text[start..end].Trim().TrimEnd('.', ',', ';');
                        if (Regex.IsMatch(title, @"\s\d{1,3}$")) continue;   // a contents line ends in a page number
                        // a heading in capitals ends where the capitals end (a word of one capital is the body's first letter, not the heading's)
                        var caps = Regex.Match(title, @"^(?:[A-Z0-9][A-Z0-9&/,'()-]+\s*|[A&]\s+)+");
                        if (caps.Success && caps.Length >= 4 && caps.Length < title.Length) title = caps.Value.Trim().TrimEnd(',', '&', '-');
                        // a heading in capitals broken over two lines: ends in a joining word and the next line is capitals too
                        if (k == ms.Count - 1 && li + 1 < lines.Count && Regex.IsMatch(title, @"\b(AND|OR|OF|FOR|IN|TO|THE|WITH)$|,$")
                            && Regex.IsMatch(lines[li + 1], @"^[A-Z][A-Z0-9&/,'() -]{2,60}$") && !heading.IsMatch(lines[li + 1]))
                            title = (title + " " + lines[li + 1].Trim()).TrimEnd(',', '&', '-', '.');
                        if (title.Length < 4 || title.Length > 80) continue;
                        found.Add((ms[k].Groups[1].Value, title));
                    }
                }
                perPage.Add(found);
            }
        }
        var sections = new List<Section>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < perPage.Count; i++)
        {
            if (perPage[i].Count >= 8) continue;   // the table of contents
            foreach (var (r, t) in perPage[i])
                if (seen.Add(r)) sections.Add(new Section(r, t, i + 1));
        }
        return sections;
    }

    /// <summary>The document's edition as its first pages state it: "Version 2.1", "V4", a year - the first found.</summary>
    public static string? ReadEdition(IReadOnlyList<string> pagesText)
    {
        foreach (string text in pagesText.Take(3))
        {
            var m = Regex.Match(text, @"\b(?:Version|VERSION|Revision|REVISION)\s*(\d+(?:\.\d+)?)\b");
            if (m.Success) return "v" + m.Groups[1].Value;
            m = Regex.Match(text, @"\b(20\d\d)\b");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Write or refresh a source row (by its code). Returns whether it was new.</summary>
    public static bool RegisterSource(string connectionString, string code, string title, string publisher, string? edition, string licence, string? copyPath, string? publicUrl, string? notes)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM knowledge.Source WHERE Code = @code AND RetiredAtUtc IS NULL)
BEGIN
    UPDATE knowledge.Source SET Title = @title, Publisher = @publisher, Edition = COALESCE(@edition, Edition), Licence = @licence,
           CopyPath = COALESCE(@copy, CopyPath), PublicUrl = COALESCE(@url, PublicUrl), Notes = COALESCE(@notes, Notes)
     WHERE Code = @code AND RetiredAtUtc IS NULL;
    SELECT 0;
END
ELSE
BEGIN
    INSERT INTO knowledge.Source (Code, Title, Publisher, Edition, Licence, CopyPath, PublicUrl, Notes)
    VALUES (@code, @title, @publisher, @edition, @licence, @copy, @url, @notes);
    SELECT 1;
END";
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@publisher", publisher);
        cmd.Parameters.AddWithValue("@edition", (object?)edition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@licence", licence);
        cmd.Parameters.AddWithValue("@copy", (object?)copyPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", (object?)publicUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>Write a document's section index as clause rows (read-from-source, the title as the requirement); existing rows keep their place. Returns rows written.</summary>
    public static int WriteSections(string connectionString, string sourceCode, IReadOnlyList<Section> sections, string readBy)
    {
        int written = 0;
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        foreach (var s in sections)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO knowledge.Clause (SourceId, ClauseRef, Topic, Requirement, Value, Units, Page, Confidence, ReadBy, CreatedBy)
SELECT src.Id, @ref, @topic, @title, NULL, NULL, @page, 'read-from-source', @by, @by
  FROM knowledge.Source src
 WHERE src.Code = @code AND src.RetiredAtUtc IS NULL
   AND NOT EXISTS (SELECT 1 FROM knowledge.Clause c WHERE c.SourceId = src.Id AND c.ClauseRef = @ref AND c.Topic = @topic AND c.RetiredAtUtc IS NULL)";
            cmd.Parameters.AddWithValue("@code", sourceCode);
            cmd.Parameters.AddWithValue("@ref", s.Ref);
            cmd.Parameters.AddWithValue("@topic", Slug(s.Title));
            cmd.Parameters.AddWithValue("@title", s.Title.Length > 1000 ? s.Title[..1000] : s.Title);
            cmd.Parameters.AddWithValue("@page", s.Page);
            cmd.Parameters.AddWithValue("@by", readBy);
            written += cmd.ExecuteNonQuery();
        }
        return written;
    }

    /// <summary>A heading as a topic slug: lower case, words joined by dashes, 120 characters at most.</summary>
    public static string Slug(string title)
    {
        string s = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return s.Length > 120 ? s[..120].TrimEnd('-') : s;
    }

    /// <summary>A clause row as the store holds it.</summary>
    public sealed record ClauseRow(Guid Id, string SourceCode, string ClauseRef, string Topic, string? Value, string? Units, int? Page, string Confidence);

    /// <summary>The clauses of one source, live rows only.</summary>
    public static IReadOnlyList<ClauseRow> LoadClauses(string connectionString, string sourceCode)
    {
        var rows = new List<ClauseRow>();
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT c.Id, s.Code, c.ClauseRef, c.Topic, c.Value, c.Units, c.Page, c.Confidence
                              FROM knowledge.Clause c JOIN knowledge.Source s ON s.Id = c.SourceId
                             WHERE s.Code = @code AND c.RetiredAtUtc IS NULL AND s.RetiredAtUtc IS NULL
                             ORDER BY c.ClauseRef, c.Topic";
        cmd.Parameters.AddWithValue("@code", sourceCode);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ClauseRow(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.GetString(7)));
        return rows;
    }

    /// <summary>
    /// Lift a clause row to read-from-source with the page found and, where the source states a value, that value;
    /// a remembered value the source contradicts is replaced and reported. Returns what changed, for the console.
    /// </summary>
    public static string Lift(string connectionString, ClauseRow row, Found found, string readBy)
    {
        bool valueChanged = found.Value is not null && row.Value is not null && found.Value != row.Value;
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE knowledge.Clause SET Page = @page, Confidence = 'read-from-source', ReadBy = @by,
                                   Value = COALESCE(@value, Value)
                             WHERE Id = @id AND RetiredAtUtc IS NULL";
        cmd.Parameters.AddWithValue("@page", found.Page);
        cmd.Parameters.AddWithValue("@by", readBy);
        cmd.Parameters.AddWithValue("@value", (object?)found.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", row.Id);
        int n = cmd.ExecuteNonQuery();
        return n == 0 ? "no row changed" :
            valueChanged ? $"lifted to read-from-source, page {found.Page}; VALUE CORRECTED {row.Value} -> {found.Value} {found.Units}"
                         : $"lifted to read-from-source, page {found.Page}{(found.Value is not null ? $", value {found.Value} {found.Units} confirmed" : "")}";
    }
}
