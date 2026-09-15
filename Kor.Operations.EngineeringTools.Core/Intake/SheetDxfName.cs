using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A sheet from the stick file is named the way the office's export names a view (intake step
/// 15): "S2.01_1_LEVEL P3 PLAN - FOUNDATION PLAN - BLDG A &amp; B.dxf" — the sheet number, a view
/// index, and the sheet title the title block states. That name is what the DXF-to-ETABS reader
/// takes a sheet's storeys from (<c>PlanSheetNaming.Parse</c>), so a PDF page named by its page
/// number ("31168-p11") reached the model as a sheet of no storey at all.
/// </summary>
public static class SheetDxfName
{
    /// <summary>The view-style name when the title block gives a sheet number and a title; else the fallback stem.</summary>
    public static string For(SheetRecord record, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(record);
        return For(record.SheetNumber, record.TitleBlock, fallbackStem, record.BookmarkTitle, record.TitleText);
    }

    /// <summary>
    /// A SHEET'S TITLE IS WHICHEVER OF ITS STATEMENTS NAMES A LEVEL (intake step 30). A set states a
    /// sheet's title up to three ways — the title block's field, the PDF's own bookmark, the title
    /// text on the page — and the storey reader already falls through them in that order. The namer
    /// took the title block's field alone, and on the architect's set for 31170 (Vectorworks) that
    /// field reads "-" while the bookmark reads "A101-LEVEL P1 PLAN": every plan reached the model
    /// as "A101_1_-.dxf", a sheet of no storey, and the whole job built nothing. The bookmark's own
    /// leading sheet number is dropped so the name is not "A101_1_A101-LEVEL P1 PLAN".
    /// </summary>
    public static string For(string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem, string? bookmarkTitle, string? titleText = null)
    {
        ArgumentNullException.ThrowIfNull(titleBlock);
        string? number = string.IsNullOrWhiteSpace(sheetNumber) ? Field(titleBlock, "SHEET NUMBER", "SHEET NO") : sheetNumber.Trim();
        string? field = SheetTitle(titleBlock);
        string? bookmark = string.IsNullOrWhiteSpace(bookmarkTitle) ? null : bookmarkTitle.Trim();
        if (bookmark is not null && number is not null && bookmark.StartsWith(number, StringComparison.OrdinalIgnoreCase))
            bookmark = bookmark[number.Length..].TrimStart(' ', '-', '–', ':', '_').Trim();
        if (string.IsNullOrWhiteSpace(bookmark)) bookmark = null;
        bookmark = WithoutProjectTitle(titleBlock, bookmark);
        // THE THIRD STATEMENT: the title written on the page itself (intake step 46). The corpus analyzer's
        // second run left 72 sets with no storey ladder, and 298 of their 486 views were named by the PDF's
        // stem and page - sheets with a number, a level the storey reader had read, and no title-block
        // field or bookmark to name them by (30940: 65 plans, 18 with a level; 31009: 28, 22). A name that
        // says nothing places nothing.
        string? page = WithoutProjectTitle(titleBlock, string.IsNullOrWhiteSpace(titleText) ? null : titleText.Trim());
        // A SMALL JOB'S TITLE CARRIES ITS OWN NUMBER: "S-6 - MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER",
        // "S-5FOUNDATION PLAN". The page reads no sheet number for these (the hyphenated form is not the
        // issued S2.20.1 form), the view fell back to the stem and page, and the composer saw no name at all:
        // 68 of the corpus's 86 sets without a model, 383 plans, half of them small jobs named exactly so
        // (intake step 47, 2026-09-13). The leading token IS the number; the rest is the title.
        if (number is null && page is not null
            && System.Text.RegularExpressions.Regex.Match(page, @"^([A-Z]{1,3}-?\d{1,3}(?:\.\d+)*)(?=\s|-|–|:|[A-Z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } lead
            && page.Length > lead.Length)
        {
            number = lead.Groups[1].Value.ToUpperInvariant();
            page = page[lead.Length..].TrimStart(' ', '-', '–', ':', '_').Trim();
        }
        if (page is not null && number is not null && page.StartsWith(number, StringComparison.OrdinalIgnoreCase))
            page = page[number.Length..].TrimStart(' ', '-', '–', ':', '_').Trim();
        if (string.IsNullOrWhiteSpace(page)) page = null;
        page = WithoutProjectTitle(titleBlock, page);

        bool NamesALevel(string? t)
        {
            if (t is null || number is null) return false;
            var info = Dxf.PlanSheetNaming.Parse($"{number}_1_{t}.dxf");
            return info.Levels.Count > 0 || info.ParkadeLevels.Count > 0 || info.IsRoof || info.IsFoundation;
        }

        string? title = NamesALevel(field) ? field : NamesALevel(bookmark) ? bookmark : NamesALevel(page) ? page : field ?? bookmark ?? page;
        if (title is not null && title.Trim().Trim('-', '–').Length == 0) title = bookmark ?? page;
        // and a title with no number at all still names the view: the stem stands where the number would
        // (the composer reads the storey from the title, never from the number)
        if (number is null && title is not null) return Sanitise($"{fallbackStem}_1_{title}") + ".dxf";
        return (number is null || title is null ? fallbackStem : Sanitise($"{number}_1_{title}")) + ".dxf";
    }

    /// <summary>The same, from the sheet number the record read and the title block's fields.</summary>
    public static string For(string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(titleBlock);
        string? number = string.IsNullOrWhiteSpace(sheetNumber) ? Field(titleBlock, "SHEET NUMBER", "SHEET NO") : sheetNumber.Trim();
        string? title = SheetTitle(titleBlock);
        return (number is null || title is null ? fallbackStem : Sanitise($"{number}_1_{title}")) + ".dxf";
    }

    /// <summary>
    /// One VIEW of a sheet, named as the office's export names it (intake step 26): the sheet
    /// number, the view's index on the sheet, and the view's own title — "S2.20.1_2_LEVEL 4 PLAN
    /// (L4-L14) - CONCRETE OUTLINE - BLDG A.dxf". The DXF-to-ETABS reader takes the storeys from
    /// the title, so a sheet drawing two plans side by side reaches the model as two plans.
    /// </summary>
    public static string ForView(string? sheetNumber, int viewIndex, string viewTitle, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(viewTitle);
        string? number = string.IsNullOrWhiteSpace(sheetNumber) ? null : sheetNumber.Trim();
        return (number is null || string.IsNullOrWhiteSpace(viewTitle) ? $"{fallbackStem}-v{viewIndex}" : Sanitise($"{number}_{viewIndex}_{viewTitle.Trim()}")) + ".dxf";
    }

    // 01379-01 p77: a drawing title names the sheet; a job/project title never supplies the
    // missing sheet title, even when repeated in the heuristic text or a bookmark. Compare only
    // stated field values; these readers cannot infer that an otherwise unlabelled name is a project.
    private static string? SheetTitle(IReadOnlyDictionary<string, string> titleBlock) =>
        WithoutProjectTitle(titleBlock, Field(titleBlock, "SHEET TITLE"))
        ?? WithoutProjectTitle(titleBlock, Field(titleBlock, "DRAWING TITLE"));

    private static string? WithoutProjectTitle(IReadOnlyDictionary<string, string> titleBlock, string? title)
    {
        if (title is null) return null;
        string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();
        foreach (string key in new[] { "JOB TITLE", "PROJECT TITLE" })
            if (Field(titleBlock, key) is { } project
                && Normalise(title).Equals(Normalise(project), StringComparison.OrdinalIgnoreCase)) return null;
        return title;
    }

    private static string? Field(IReadOnlyDictionary<string, string> titleBlock, params string[] keys)
    {
        foreach (string key in keys)
            if (titleBlock.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }

    /// <summary>A file name: characters the file system refuses become spaces, runs of space collapse, 120 characters at most.</summary>
    public static string Sanitise(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? ' ' : c);
        string clean = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return clean.Length > 120 ? clean[..120].TrimEnd() : clean;
    }
}
