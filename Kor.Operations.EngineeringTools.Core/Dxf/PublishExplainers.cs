using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record PublishExplainerFile(string Source, string Target);

public sealed record PublishExplainersRequest(
    string Project,
    string ModelFolder,
    string RepoRoot,
    string ProjectsRoot,
    string CurrentModelPath,
    string CurrentReportPath,
    E2kModelContents CurrentContents,
    bool SkipDossier,
    bool IsVariant);

public sealed record PublishExplainersResult(
    IReadOnlyList<PublishExplainerFile> ToCopy,
    IReadOnlyList<string> ToWithdraw,
    IReadOnlyList<string> Warnings,
    string? Refused)
{
    public bool Skip => ToCopy.Count == 0;
}

public static class PublishExplainers
{
    private static readonly Regex JobNumber = new(@"\b(3\d{4})\b", RegexOptions.Compiled);

    public static PublishExplainersResult Evaluate(PublishExplainersRequest request)
    {
        string dossierSourceHtml = Path.Combine(request.RepoRoot, "docs", "KOR-DxfToEtabs-dossier.html");
        string dossierSourcePdf = Path.Combine(request.RepoRoot, "docs", "KOR-DxfToEtabs-web.pdf");
        string onePagerSourcePdf = Path.Combine(request.RepoRoot, "docs", "KOR-DxfToEtabs-onepager-web.pdf");
        string dossierTarget = Path.Combine(request.ModelFolder, "KOR-Model-From-Drawings-DOSSIER.pdf");
        string onePagerTarget = Path.Combine(request.ModelFolder, "KOR-Model-From-Drawings-READ-THIS-FIRST.pdf");
        string[] targets = { dossierTarget, onePagerTarget };

        if (request.IsVariant)
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), Array.Empty<string>(), Array.Empty<string>(), null);

        if (request.SkipDossier)
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), targets, Array.Empty<string>(), null);

        if (!File.Exists(dossierSourceHtml))
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), Array.Empty<string>(),
                new[] { "Dossier source HTML was not found; no general explainer copied." }, null);

        var sourceHtml = File.ReadAllText(dossierSourceHtml);
        var describedJobs = JobNumber.Matches(sourceHtml).Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        if (!describedJobs.Contains(request.Project, StringComparer.Ordinal))
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), Array.Empty<string>(),
                new[] { $"The dossier and one-pager describe {string.Join(", ", describedJobs)} - not {request.Project}. They were not copied." },
                null);

        // Staleness is asked of the ARTIFACTS THAT SHIP, which is these two PDFs -- the HTML is a
        // build input and is never copied to a job folder. Asking it of the HTML made the gate
        // unclearable: a PDF can be re-rendered, which genuinely re-derives it from current source,
        // but an HTML source can only be brought forward by editing its prose. With nothing to fix
        // in the prose the only way past was to touch the file, and a gate whose remedy is
        // laundering a timestamp teaches exactly the wrong habit.
        var stale = StaleSources(request.RepoRoot, new[] { dossierSourcePdf, onePagerSourcePdf }).ToList();
        if (stale.Count > 0)
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), targets, Array.Empty<string>(),
                "STALE - these explainers predate the source that builds models: " + string.Join(", ", stale));

        // And the other direction, which nothing checked: the prose was edited and the PDF was
        // never re-rendered, so the file an engineer opens is not the document that was written.
        // This is the same class as the Edge-cached "File not found" PDF that shipped -- the source
        // was right and the artifact was not.
        string onePagerSourceHtml = Path.Combine(request.RepoRoot, "docs", "KOR-DxfToEtabs-onepager.html");
        var unrendered = new[]
            {
                (Html: dossierSourceHtml, Pdf: dossierSourcePdf),
                (Html: onePagerSourceHtml, Pdf: onePagerSourcePdf),
            }
            .Where(p => File.Exists(p.Html) && File.Exists(p.Pdf)
                        && File.GetLastWriteTime(p.Pdf) < File.GetLastWriteTime(p.Html))
            .Select(p => $"{Path.GetFileName(p.Pdf)} is older than {Path.GetFileName(p.Html)}")
            .ToList();
        if (unrendered.Count > 0)
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), targets, Array.Empty<string>(),
                "NOT RE-RENDERED - the prose moved on and the PDF beside it did not: " + string.Join("; ", unrendered));

        var wrong = new List<string>();
        var counts = CountsForNamedJobs(request, describedJobs);
        var allowed = AllowedCounts(counts.Reuse);

        if (File.Exists(dossierSourcePdf))
        {
            string? pdfText = PdfText(dossierSourcePdf);
            if (pdfText is null)
                wrong.Add($"{Path.GetFileName(dossierSourcePdf)} will not open as a PDF");
            else if (LooksLikeBrowserError(pdfText))
                wrong.Add("dossier PDF renders as a browser error page");
            else
                foreach (var (what, count) in CurrentCounts(request.CurrentContents))
                {
                    if (count == 0) continue;
                    if (!ContainsNumber(pdfText, count))
                        wrong.Add($"{what} = {count}");
                }
        }

        if (File.Exists(onePagerSourcePdf))
        {
            string? onePagerText = PdfText(onePagerSourcePdf);
            if (onePagerText is null)
                wrong.Add($"{Path.GetFileName(onePagerSourcePdf)} will not open as a PDF");
            else if (LooksLikeBrowserError(onePagerText))
                wrong.Add("one-pager PDF renders as a browser error page");
            else
            {
                var claims = CountClaims(onePagerText).ToList();
                if (claims.Count == 0)
                    wrong.Add("one-pager PDF contains no checked model count claims");
                CheckClaims(claims, counts.ModelCounts, allowed, "one-pager", wrong);
            }
        }

        string prose = HtmlToText(sourceHtml);

        // THE PROSE SCANNER READS PROSE. TABLES HAVE THEIR OWN READER, WHICH KNOWS ABOUT CELLS.
        //
        // The dossier compares two jobs side by side, so its summary table is
        // "Wall panels 335 205 / Columns 713 304 / Floor plates 15 15" -- 31168 then 31138. Every
        // tag becomes a space here, so flattened it reads "...335 205 Columns 713 304 Floor
        // plates", and a scanner looking for "<number> <noun>" pairs each label with the PREVIOUS
        // row's second column. It invented "205 Columns", "304 Floor plates" and "15 Headers" and
        // refused a publish whose model matched the dossier on every real number.
        //
        // CheckDossierTable below reads the same table by cell and gets it right, so the tables are
        // dropped here rather than the scanner taught to parse them.
        string proseOutsideTables = HtmlToText(Regex.Replace(sourceHtml, @"(?s)<table.*?</table>", " "));
        CheckClaims(CountClaims(proseOutsideTables), counts.ModelCounts, allowed, "dossier", wrong);
        CheckDossierTable(sourceHtml, prose, counts.ModelCounts, wrong);
        CheckPlatelessStoreys(request, prose, wrong);
        CheckOneSuiteCount(prose, wrong);

        if (wrong.Count > 0)
            return new PublishExplainersResult(Array.Empty<PublishExplainerFile>(), targets, Array.Empty<string>(),
                "DOSSIER OUT OF DATE - these counts are not in it: " + string.Join("; ", wrong));

        var copy = new List<PublishExplainerFile>();
        if (File.Exists(dossierSourcePdf)) copy.Add(new PublishExplainerFile(dossierSourcePdf, dossierTarget));
        if (File.Exists(onePagerSourcePdf)) copy.Add(new PublishExplainerFile(onePagerSourcePdf, onePagerTarget));
        return new PublishExplainersResult(copy, Array.Empty<string>(), Array.Empty<string>(), null);
    }

    // The delivery pipeline, as opposed to the code that decides what a model SAYS. These files
    // find the job folder, build the summary page, gate these very explainers and copy files; none
    // of them can change a count, an outline or a storey. They are excluded so that changing them
    // does not declare the explainers stale.
    //
    // This distinction only became necessary on 31 August, when publishing moved out of
    // tools\Publish-EtabsModel.ps1 and into this folder. Before that the publisher sat outside the
    // watched directory and the question never arose. Excluding them keeps the gate meaning what it
    // was written to mean; leaving them in would have fired it on every publish change, and a gate
    // that cries wolf is one people learn to re-render past without reading.
    //
    // ⚠ If model-building logic is ever added to one of these, take it back out of this list. The
    // claims gate below still checks every stated number against the model either way.
    internal static readonly string[] DeliveryPipelineFiles =
    {
        "JobPublisher.cs",
        "PublishPlan.cs",
        "PublishDiscovery.cs",
        "PublishSummary.cs",
        "PublishExplainers.cs",
        "PublishExternalTools.cs",
    };

    private static IEnumerable<string> StaleSources(string repoRoot, IEnumerable<string> sources)
    {
        string dxfSource = Path.Combine(repoRoot, "Kor.Operations.EngineeringTools.Core", "Dxf");
        if (!Directory.Exists(dxfSource)) yield break;

        var newest = Directory.EnumerateFiles(dxfSource, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !DeliveryPipelineFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .Select(File.GetLastWriteTime)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        foreach (string source in sources)
            if (File.Exists(source) && File.GetLastWriteTime(source) < newest)
                yield return Path.GetFileName(source);
    }

    private static (Dictionary<string, int> ModelCounts, IReadOnlyList<int> Reuse) CountsForNamedJobs(
        PublishExplainersRequest request,
        IReadOnlyList<string> describedJobs)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var reuse = new List<int>();

        foreach (string job in describedJobs)
        {
            E2kModelContents? contents = null;
            string? report = null;
            if (job == request.Project)
            {
                contents = request.CurrentContents;
                report = request.CurrentReportPath;
            }
            else
            {
                var model = TryFindGeneratedModel(request.ProjectsRoot, job);
                if (model is not null)
                {
                    contents = E2kDocument.Load(model).ReadContents();
                    report = Path.Combine(Path.GetDirectoryName(model) ?? string.Empty, $"{job}-FROM-DRAWINGS-report.txt");
                }
            }

            if (contents is null) continue;
            counts[$"{job}.wall"] = contents.Walls;
            counts[$"{job}.column"] = contents.Columns;
            counts[$"{job}.plate"] = contents.Floors;
            counts[$"{job}.header"] = contents.Headers;

            if (report is not null && File.Exists(report))
            {
                var match = Regex.Match(File.ReadAllText(report),
                    @"(?<w>\d+) wall\(s\) and (?<c>\d+) column\(s\) were already modelled");
                if (match.Success)
                {
                    reuse.Add(int.Parse(match.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture));
                    reuse.Add(int.Parse(match.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return (counts, reuse);
    }

    private static string? TryFindGeneratedModel(string projectsRoot, string job)
    {
        if (!Directory.Exists(projectsRoot)) return null;

        var jobFolder = Directory.EnumerateDirectories(projectsRoot)
            .SelectMany(d => Directory.EnumerateDirectories(d, job + "*"))
            .FirstOrDefault();
        if (jobFolder is null) return null;

        return PublishDiscovery.EnumerateDirectories(jobFolder, maxDepth: 4)
            .Prepend(jobFolder)
            .SelectMany(d => Directory.EnumerateFiles(d, $"{job}-FROM-DRAWINGS.e2k", SearchOption.TopDirectoryOnly))
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, HashSet<int>> AllowedCounts(IReadOnlyList<int> reuse)
    {
        var allowed = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["wall"] = new(new[] { 78, 29, 45, 60, 22 }),
            ["column"] = new(new[] { 87, 67, 43 }),
            ["plate"] = new(new[] { 14, 7, 2 }),
            ["header"] = new(new[] { 5 }),
        };
        foreach (int value in reuse)
        {
            allowed["wall"].Add(value);
            allowed["column"].Add(value);
        }
        return allowed;
    }

    private static IEnumerable<(string What, int Count)> CurrentCounts(E2kModelContents contents)
    {
        yield return ("walls", contents.Walls);
        yield return ("columns", contents.Columns);
        yield return ("plates", contents.Floors);
        yield return ("headers", contents.Headers);
        yield return ("openings", contents.Openings);
    }

    /// <summary>
    /// The words in an explainer PDF, or null if the file will not open as one.
    /// </summary>
    /// <remarks>
    /// A file that is not a readable PDF is a defect of exactly the kind this gate exists to catch
    /// -- the 59 KB Edge "File not found" page shipped to job folders for days -- so it must arrive
    /// as a refusal naming the file, not as a PdfDocumentFormatException thrown through the
    /// publisher. Truncated, zero-length and half-written renders all land here.
    /// </remarks>
    private static string? PdfText(string pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool LooksLikeBrowserError(string text)
        => text.Contains("ERR_FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
           || text.Contains("File not found", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNumber(string text, int number)
    {
        string plain = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string grouped = number.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return Regex.IsMatch(text, @"\b" + Regex.Escape(plain) + @"\b")
               || Regex.IsMatch(text, @"\b" + Regex.Escape(grouped) + @"\b");
    }

    private static string HtmlToText(string html)
    {
        string prose = Regex.Replace(html, @"(?s)<style.*?</style>", " ");
        prose = Regex.Replace(prose, @"(?s)<script.*?</script>", " ");
        prose = Regex.Replace(prose, @"<[^>]+>", " ");
        prose = Regex.Replace(prose, @"&[a-z]+;", " ");
        return Regex.Replace(prose, @"\s+", " ");
    }

    private static IEnumerable<(int Number, string What, string Text)> CountClaims(string prose)
    {
        const string pattern = @"(?:(?<n>\d[\d,]*)\s+(?:of\s+(?:your|her|its|the)\s+)?(?<what>wall panels|walls|columns|floor plates|plates|headers))|(?:(?<what2>wall panels|walls|columns|floor plates|plates|headers)\s*[:(]\s*(?<n2>\d[\d,]*))";
        foreach (Match match in Regex.Matches(prose, pattern, RegexOptions.IgnoreCase))
        {
            string nText = match.Groups["n"].Success ? match.Groups["n"].Value : match.Groups["n2"].Value;
            string whatText = match.Groups["what"].Success ? match.Groups["what"].Value : match.Groups["what2"].Value;
            string what = whatText.Contains("wall", StringComparison.OrdinalIgnoreCase) ? "wall"
                : whatText.Contains("column", StringComparison.OrdinalIgnoreCase) ? "column"
                : whatText.Contains("header", StringComparison.OrdinalIgnoreCase) ? "header"
                : "plate";
            yield return (int.Parse(nText.Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture),
                what, match.Value);
        }
    }

    private static void CheckClaims(
        IEnumerable<(int Number, string What, string Text)> claims,
        IReadOnlyDictionary<string, int> modelCounts,
        IReadOnlyDictionary<string, HashSet<int>> allowed,
        string source,
        List<string> wrong)
    {
        foreach (var claim in claims)
        {
            bool ok = modelCounts.Any(x => x.Key.EndsWith("." + claim.What, StringComparison.OrdinalIgnoreCase)
                                           && x.Value == claim.Number);
            if (!ok && (!allowed.TryGetValue(claim.What, out var values) || !values.Contains(claim.Number)))
                wrong.Add($"{source} says '{claim.Text}' - no model has that many {claim.What}s");
        }
    }

    private static void CheckDossierTable(
        string html,
        string prose,
        IReadOnlyDictionary<string, int> modelCounts,
        List<string> wrong)
    {
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wall panels"] = "wall",
            ["Columns, sized"] = "column",
            ["Floor plates"] = "plate",
            ["Headers over openings"] = "header",
        };

        var header = Regex.Match(html, @"(?s)<tr>\s*<th>What was generated</th>(?<cells>.*?)</tr>");
        var tableJobs = header.Success
            ? Regex.Matches(header.Groups["cells"].Value, @"<th>[^<]*?\b(3\d{4})\b").Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList()
            : new List<string>();
        if (tableJobs.Count == 0)
        {
            wrong.Add("dossier summary table does not name the jobs its columns describe");
            return;
        }

        foreach (var row in rows)
        {
            string label = row.Key;
            string what = row.Value;
            string pattern = Regex.Escape(label) + "[^0-9]*" +
                             string.Join(@"\s+", Enumerable.Range(0, tableJobs.Count).Select(_ => @"([\d,]+)"));
            var match = Regex.Match(prose, pattern);
            if (!match.Success)
            {
                wrong.Add($"dossier table has no '{label}' row");
                continue;
            }

            for (int i = 0; i < tableJobs.Count; i++)
            {
                string job = tableJobs[i];
                int stated = int.Parse(match.Groups[i + 1].Value.Replace(",", string.Empty),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (modelCounts.TryGetValue($"{job}.{what}", out int actual) && stated != actual)
                    wrong.Add($"dossier table: {label} for {job} says {stated}, model has {actual}");
            }
        }
    }

    private static void CheckPlatelessStoreys(PublishExplainersRequest request, string prose, List<string> wrong)
    {
        var listedFor = Regex.Match(prose,
            @"Storeys still carrying members with no plate[^0-9]*\b(?<job>3\d{4})\b");
        if (!listedFor.Success || listedFor.Groups["job"].Value != request.Project || !File.Exists(request.CurrentReportPath))
            return;

        var report = Regex.Match(File.ReadAllText(request.CurrentReportPath),
            @"carry walls or columns but no floor plate[^:]*:\s*(?<list>[^.]+)\.");
        if (!report.Success) return;

        foreach (string raw in report.Groups["list"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!Regex.IsMatch(prose, Regex.Escape(raw)))
                wrong.Add($"dossier does not name '{raw}' among the storeys left without a plate");
    }

    private static void CheckOneSuiteCount(string prose, List<string> wrong)
    {
        var suite = Regex.Matches(prose, @"(?<n>\d[\d,]*)\s+tests").Cast<Match>()
            .Select(m => int.Parse(m.Groups["n"].Value.Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        if (suite.Count > 1)
            wrong.Add("dossier states more than one test count: " + string.Join(", ", suite));
    }
}
