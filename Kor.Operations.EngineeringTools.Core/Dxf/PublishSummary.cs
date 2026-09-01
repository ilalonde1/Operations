using System.Net;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record PublishSummaryRequest(
    string Project,
    string Label,
    string DxfFolder,
    string Reference,
    DxfToEtabsReport Report,
    string ReportPath,
    string QuestionsPath,
    string StageFolder,
    string TempFolder,
    PublishToolPaths Tools);

public sealed record PublishSummaryResult(
    string HtmlPath,
    string PdfPath,
    int Pages,
    int FindingsShown,
    int TrimmedAway);

public static class PublishSummary
{
    public static PublishSummaryResult Write(PublishSummaryRequest request)
    {
        var allFindings = FindingsFrom(request.Report.Summary.Flags);
        int findingsShown = 8;
        int trimmedAway = 0;
        string htmlPath = Path.Combine(request.TempFolder, $"kor-summary-{request.Label}.html");
        string pdfPath = Path.Combine(request.StageFolder, $"KOR-{request.Label}-SUMMARY.pdf");
        int pages = 0;

        foreach (int tryCount in new[] { 8, 6, 4, 3, 2 })
        {
            findingsShown = tryCount;
            var shown = FirstSentences(allFindings, tryCount).ToList();
            trimmedAway = allFindings.Count - shown.Count;
            File.WriteAllText(htmlPath, BuildHtml(request, shown, trimmedAway));

            PublishExternalTools.RenderPdf(request.Tools, htmlPath, pdfPath);
            pages = PublishExternalTools.PageCount(request.Tools, pdfPath);
            if (pages <= 1) break;
        }

        if (pages > 1)
            throw new InvalidOperationException(
                $"The one-page summary is {pages} pages even with only {findingsShown} finding(s) listed.");

        return new PublishSummaryResult(htmlPath, pdfPath, pages, findingsShown, trimmedAway);
    }

    private static string BuildHtml(PublishSummaryRequest request, IReadOnlyList<string> findings, int trimmedAway)
    {
        var counts = new List<(string Label, int Count)>
        {
            ("Storeys populated", request.Report.SavedModel.Storeys.Count),
            ("Wall panels", request.Report.SavedModel.Walls),
            ("Columns", request.Report.SavedModel.Columns),
            ("Floor plates", request.Report.SavedModel.Floors),
        };

        int flooredStoreys = request.Report.SavedModel.PlatesByStorey.Count;
        if (flooredStoreys > request.Report.SavedModel.Floors)
            counts.Add(("Storeys with a floor", flooredStoreys));

        counts.Add(("Headers", request.Report.SavedModel.Headers));
        counts.Add(("Openings cut", request.Report.SavedModel.Openings));

        int openQuestions = ModelQuestionnaire
            .StandingQuestions(request.Report.ClassificationUsed, request.Report.ComposeUsed, request.Report)
            .Count(q => !q.Decided);

        string waiting = openQuestions switch
        {
            0 => "Nothing there is waiting on you.",
            1 => "One row is marked NEEDS YOU; nothing in the drawings could settle it.",
            _ => $"{openQuestions} rows are marked NEEDS YOU; nothing in the drawings could settle them.",
        };

        string E(string value) => WebUtility.HtmlEncode(value);
        var html = new List<string>
        {
            "<title>" + E($"{request.Project} - model from drawings") + "</title>",
            "<style>body{font:12.5px/1.42 \"Segoe UI\",system-ui,sans-serif;max-width:46rem;margin:0 auto;padding:20px 26px;color:#1a1a1a}h1{font-size:19px;margin:0 0 2px;font-weight:650}.sub{color:#5b5b5b;font-size:11.5px;margin:0 0 12px}h2{font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:#7a2230;margin:14px 0 5px}table{border-collapse:collapse;width:100%;font-size:12.5px}td{padding:2px 8px 2px 0;border-bottom:1px solid #eeeae5}td.n{text-align:right;font-variant-numeric:tabular-nums;font-weight:600}li{margin:0 0 3px}ul{margin:4px 0;padding-left:18px}p{margin:5px 0}code{background:#f4f2ef;padding:1px 4px;border-radius:3px;font-size:11.5px}</style>",
            "<h1>" + E(request.Label) + " &mdash; model from drawings</h1>",
            "<p class=\"sub\">Generated " + DateTime.Today.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) +
            " from " + E(Path.GetFileName(request.DxfFolder)) + ", on top of " + E(request.Reference) +
            ". It removes the typing; it does none of the engineering.</p>",
            "<h2>What was built</h2><table>",
        };

        foreach (var (label, count) in counts)
            html.Add("<tr><td>" + E(label) + "</td><td class=\"n\">" + count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "</td></tr>");
        html.Add("</table>");

        if (findings.Count > 0)
        {
            html.Add("<h2>What was not, and why</h2><ul>");
            foreach (string finding in findings)
                html.Add("<li>" + E(finding) + "</li>");
            html.Add("</ul>");
            if (trimmedAway > 0)
                html.Add("<p class=\"sub\">Shortened to the first sentence of each, and " + trimmedAway +
                         " further finding(s) are not listed here. All of them appear in full in <code>" +
                         E(Path.GetFileName(request.ReportPath)) + "</code>.</p>");
        }

        html.Add("<h2>What it did not touch</h2><p>No loads, diaphragms, stiffness modifiers, section properties, meshing or design &mdash; those are yours. Geometry already in your model was recognised and left alone rather than duplicated.</p>");
        html.Add("<h2>What it decided for you</h2><p>Every judgement it had to make is listed in <code>" +
                 E(Path.GetFileName(request.QuestionsPath)) + "</code>, each with the measurement behind it beside it. " +
                 E(waiting) + " Rows tied to a rule can be changed from the answer cell, and that becomes the rule for every job afterwards; you are asked once. Rows without a rule key are visible scope decisions, not yet learnable settings. A second sheet lists every rule this model was built on, read-only, including the geometry tolerances no decision asks about.</p>");
        html.Add("<p class=\"sub\" style=\"margin-top:22px\">Location by location, the full account is in <code>" +
                 E(Path.GetFileName(request.ReportPath)) + "</code>.</p>");
        return string.Join(Environment.NewLine, html);
    }

    private static IReadOnlyList<string> FindingsFrom(IReadOnlyList<string> flags)
    {
        var relevant = flags
            .Select(f => f.Trim().TrimStart('-').Trim())
            .Where(f => Regex.IsMatch(f, @"not |no |could not|were |outside|drawn more than once|beneath", RegexOptions.IgnoreCase))
            .ToList();

        var modelWide = relevant.Where(f => !f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase)).ToList();
        var grouped = relevant
            .Where(f => f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                string text = Regex.Replace(f, @"^.*?\.dxf:\s*", string.Empty);
                var number = Regex.Match(text, @"^(\d[\d,]*)");
                int total = number.Success
                    ? int.Parse(number.Groups[1].Value.Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture)
                    : 0;
                return new
                {
                    Shape = Regex.Replace(text, @"\d[\d,]*", "#"),
                    Text = text,
                    Total = total,
                };
            })
            .GroupBy(x => x.Shape)
            .Select(g =>
            {
                var first = g.First();
                if (g.Count() == 1) return first.Text;
                string withoutLeadingNumber = Regex.Replace(first.Text, @"^\d[\d,]*\s*", string.Empty);
                return $"{g.Sum(x => x.Total)} across {g.Count()} drawings: {withoutLeadingNumber}";
            });

        return modelWide.Concat(grouped).ToList();
    }

    private static IEnumerable<string> FirstSentences(IReadOnlyList<string> findings, int count)
    {
        foreach (string finding in findings.Take(count))
        {
            var match = Regex.Match(finding, @"^(.+?[.!])(\s|$)");
            yield return match.Success && match.Groups[1].Value.Length < finding.Length
                ? match.Groups[1].Value + " ..."
                : finding;
        }
    }
}
