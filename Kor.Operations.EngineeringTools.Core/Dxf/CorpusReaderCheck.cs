using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>How one engineer's model fared when this tool tried to read it.</summary>
public sealed record CorpusReadResult(
    string Path,
    string Outcome,
    string Detail,
    int Storeys,
    int Walls,
    int Columns);

/// <summary>
/// This tool's reader, run against every model KOR engineers have actually built.
///
/// A job arrives as a folder of drawings AND a reference model, and the reference is read before
/// anything else happens: its storeys become the framework, its units set every coordinate, its
/// materials and sections are what generated members are built from. Every rule in this tool has
/// been measured against the portfolio at some point. The READER never has. It has been exercised
/// against exactly two reference models, both from one office, both exported the same way.
///
/// So the first thing an unfamiliar job does is hand this tool a file unlike anything it has read,
/// and the failure modes there are not subtle — a model whose units it cannot resolve does not
/// degrade, it throws at the door. This measures that: how many of 1,100-odd real models can be
/// read at all, and what specifically stops the ones that cannot.
///
/// Read-only. It opens files and parses them; it writes nothing anywhere.
/// </summary>
public static class CorpusReaderCheck
{
    /// <summary>Objects this tool wrote. A model round-tripped through ETABS keeps these names.</summary>
    private static readonly Regex OurOwn = new(@"""K[A-Z]\d+""", RegexOptions.Compiled);

    public const string OutcomeOk = "ok";
    public const string OutcomeOurs = "skipped-our-own-output";
    public const string OutcomeUnreadable = "unreadable-file";
    public const string OutcomeNoStoreys = "no-storeys";
    public const string OutcomeNoUnits = "no-length-unit";
    public const string OutcomeNoGeometry = "parsed-but-no-members";
    public const string OutcomeThrew = "reader-threw";

    /// <summary>
    /// Every model under <paramref name="root"/>, in a stable order so a failing file can be found
    /// again. <paramref name="limit"/> of zero means all of them.
    /// </summary>
    public static IEnumerable<string> Models(string root, int limit = 0)
    {
        // One traversal, streamed. Two EnumerateFiles calls with an OrderBy over both meant the
        // whole share had to be walked and sorted before the first file could be read -- so
        // --limit bought nothing, and a ten-minute run over SMB produced no output at all.
        //
        // With a limit the order is whatever the filesystem gives, which is fine for a spot check.
        // A full run sorts afterwards, where the sort costs nothing because everything has been
        // walked anyway.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        // The pattern goes to the FILESYSTEM, not to a LINQ Where. "*.*" with a .NET-side filter
        // makes this enumerate every drawing, PDF and archive on a projects volume to find a
        // hundred-odd models -- which on the file server ran for minutes at 1 second of CPU,
        // because it was walking millions of names it had no use for.
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.e2k", options)
                .Concat(Directory.EnumerateFiles(root, "*.$et", options));

            // Sorting is only worth its cost when everything is being read anyway; with a limit it
            // would force the whole walk before the first file, which is what --limit exists to avoid.
            if (limit == 0) files = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        int n = 0;
        foreach (string f in files)
        {
            yield return f;
            if (limit > 0 && ++n >= limit) yield break;
        }
    }

    /// <summary>Read one model exactly as a production run would, and say what happened.</summary>
    public static CorpusReadResult Check(string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CorpusReadResult(path, OutcomeUnreadable, ex.GetType().Name, 0, 0, 0);
        }

        // A binary .$et autosave is not a text model and is not evidence about anything.
        if (lines.Length == 0 || lines.Take(200).Any(l => l.Contains('\0')))
            return new CorpusReadResult(path, OutcomeUnreadable, "binary, not a text model", 0, 0, 0);

        if (lines.Take(40000).Any(l => OurOwn.IsMatch(l)))
            return new CorpusReadResult(path, OutcomeOurs, "carries KOR-generated object names", 0, 0, 0);

        try
        {
            var doc = E2kDocument.Parse(lines);

            var storeys = doc.ReadStories();
            if (storeys.Count == 0)
                return new CorpusReadResult(path, OutcomeNoStoreys, "no STORY lines this tool recognises", 0, 0, 0);

            // The hard one. Production throws here rather than guessing, because a model in
            // millimetres read as inches produces a building of the wrong size and says nothing.
            double? unit = doc.LengthUnitInInches();
            if (unit is null)
                return new CorpusReadResult(path, OutcomeNoUnits,
                    "no CONTROLS UNITS line naming IN, FT, MM, CM or M", storeys.Count, 0, 0);

            var geometry = E2kGeometryReader.Read(doc);
            int walls = geometry.Walls.Count, columns = geometry.Columns.Count;

            if (walls + columns == 0)
                return new CorpusReadResult(path, OutcomeNoGeometry,
                    "storeys and units read, but no walls or columns found", storeys.Count, 0, 0);

            return new CorpusReadResult(path, OutcomeOk, $"{unit:0.###} in/unit", storeys.Count, walls, columns);
        }
        catch (Exception ex)
        {
            return new CorpusReadResult(path, OutcomeThrew, $"{ex.GetType().Name}: {ex.Message}", 0, 0, 0);
        }
    }

    /// <summary>The results grouped into what an engineer would want to know.</summary>
    public static string Summarise(IReadOnlyList<CorpusReadResult> results)
    {
        var sb = new System.Text.StringBuilder();
        int considered = results.Count(r => r.Outcome != OutcomeOurs);
        int ok = results.Count(r => r.Outcome == OutcomeOk);

        sb.AppendLine($"{results.Count:N0} file(s) walked, {considered:N0} engineer-authored, {ok:N0} read cleanly.");
        if (considered > 0)
            sb.AppendLine($"Readable: {100.0 * ok / considered:0.0}% of engineer models.");
        sb.AppendLine();

        foreach (var group in results.GroupBy(r => r.Outcome).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"{group.Count(),6:N0}  {group.Key}");
            foreach (var example in group.Where(r => r.Outcome != OutcomeOk && r.Outcome != OutcomeOurs)
                         .GroupBy(r => r.Detail).OrderByDescending(g => g.Count()).Take(4))
            {
                sb.AppendLine($"          {example.Count(),5:N0} x {example.Key}");
                sb.AppendLine($"                  e.g. {example.First().Path}");
            }
        }

        var read = results.Where(r => r.Outcome == OutcomeOk).ToList();
        if (read.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Of the {read.Count:N0} read: storeys {read.Min(r => r.Storeys)}-{read.Max(r => r.Storeys)}, " +
                          $"walls {read.Min(r => r.Walls):N0}-{read.Max(r => r.Walls):N0}, " +
                          $"columns {read.Min(r => r.Columns):N0}-{read.Max(r => r.Columns):N0}.");
        }

        return sb.ToString();
    }
}
