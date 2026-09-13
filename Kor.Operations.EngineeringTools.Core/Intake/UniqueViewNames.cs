namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A VIEW'S NAME IS UNIQUE WITHIN THE SET (2026-09-12). Two pages can carry the same sheet number
/// and title — 31168's 2026-09-10 issue names "S2.01 - LEVEL P3 PLAN FOUNDATIONS PLAN BLDG C" twice —
/// and the view's name is the storey reader's input, so it must stay a storey name. The disk handoff
/// overwrote the first view with the second, silently; the in-memory handoff threw on the duplicate
/// key and the whole set built nothing (corpus run 5). The second carries its page, in a form no
/// storey word reads: "(page 12)" — a number with no level word before it.
/// </summary>
public sealed class UniqueViewNames
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _renamed = new();

    /// <summary>The views renamed because their name was already taken, in the order they came.</summary>
    public IReadOnlyList<string> Renamed => _renamed;

    /// <summary>The name to write the view under: its own where it is the first, "(page N)" appended where it is not.</summary>
    public string Claim(string fileName, int page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (_used.Add(fileName)) return fileName;
        string again = $"{Path.GetFileNameWithoutExtension(fileName)} (page {page}){Path.GetExtension(fileName)}";
        for (int n = 2; !_used.Add(again); n++)
            again = $"{Path.GetFileNameWithoutExtension(fileName)} (page {page}, {n}){Path.GetExtension(fileName)}";
        _renamed.Add(again);
        return again;
    }
}
