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
