using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Readers;

/// <summary>A file reader produces text and references without knowing about database rows.</summary>
public interface IReader
{
    Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default);
}

public sealed record Chunk(string? Ref, string Text)
{
    public int Tokens => TokenEstimate.Of(Text);
}

public sealed class NoTextLayerException(string path, int pages, int withText)
    : Exception($"{Path.GetFileName(path)}: {withText} of {pages} pages carry usable text; no text layer.")
{
    public int Pages { get; } = pages;
    public int PagesWithText { get; } = withText;
}

/// <summary>"marP2.html" before "marP10.html": digits compare as numbers.</summary>
public sealed partial class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();
    public int Compare(string? x, string? y)
    {
        var ax = Digits().Split(x ?? ""); var ay = Digits().Split(y ?? "");
        for (var i = 0; i < Math.Min(ax.Length, ay.Length); i++)
        {
            int c;
            if (int.TryParse(ax[i], out var nx) && int.TryParse(ay[i], out var ny)) c = nx.CompareTo(ny);
            else c = string.Compare(ax[i], ay[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return ax.Length.CompareTo(ay.Length);
    }
    [GeneratedRegex(@"(\d+)")]
    private static partial Regex Digits();
}
