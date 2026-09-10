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
        return For(record.SheetNumber, record.TitleBlock, fallbackStem, record.BookmarkTitle);
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
    public static string For(string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem, string? bookmarkTitle)
    {
        ArgumentNullException.ThrowIfNull(titleBlock);
        string? number = string.IsNullOrWhiteSpace(sheetNumber) ? Field(titleBlock, "SHEET NUMBER", "SHEET NO") : sheetNumber.Trim();
        string? field = Field(titleBlock, "SHEET TITLE", "DRAWING TITLE");
        string? bookmark = string.IsNullOrWhiteSpace(bookmarkTitle) ? null : bookmarkTitle.Trim();
        if (bookmark is not null && number is not null && bookmark.StartsWith(number, StringComparison.OrdinalIgnoreCase))
            bookmark = bookmark[number.Length..].TrimStart(' ', '-', '–', ':', '_').Trim();
        if (string.IsNullOrWhiteSpace(bookmark)) bookmark = null;

        bool NamesALevel(string? t)
        {
            if (t is null || number is null) return false;
            var info = Dxf.PlanSheetNaming.Parse($"{number}_1_{t}.dxf");
            return info.Levels.Count > 0 || info.ParkadeLevels.Count > 0 || info.IsRoof || info.IsFoundation;
        }

        string? title = NamesALevel(field) ? field : NamesALevel(bookmark) ? bookmark : field ?? bookmark;
        if (title is not null && title.Trim().Trim('-', '–').Length == 0) title = bookmark;
        return (number is null || title is null ? fallbackStem : Sanitise($"{number}_1_{title}")) + ".dxf";
    }

    /// <summary>The same, from the sheet number the record read and the title block's fields.</summary>
    public static string For(string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(titleBlock);
        string? number = string.IsNullOrWhiteSpace(sheetNumber) ? Field(titleBlock, "SHEET NUMBER", "SHEET NO") : sheetNumber.Trim();
        string? title = Field(titleBlock, "SHEET TITLE", "DRAWING TITLE");
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
