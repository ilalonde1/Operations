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
        return For(record.SheetNumber, record.TitleBlock, fallbackStem);
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
