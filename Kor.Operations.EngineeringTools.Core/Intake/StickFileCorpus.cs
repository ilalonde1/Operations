#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// The census of the drawing corpus: for every job on the projects share, what it holds that the
/// intake can learn from — the structural stick-file issues (dated), the architects' sets under
/// them, and the engineer's own ETABS models — so the rules are chosen by how many sets share a
/// failure and validated against every set at once, not one drawing at a time (Ian, 2026-09-11:
/// "build an analyzer to get all the info you need at once").
/// </summary>
/// <remarks>
/// The share's layout, read on 2026-09-11 from 31168:
/// <code>
///   \\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\
///       05 Stickfile\31168-01 - 2026-04-21- YMCA Langara - Stickfile.pdf     ← a structural issue
///       05 Stickfile\01 Architectural\...pdf                                   ← the architect's set(s)
///       02 Engineering\02 Lateral Design\01 ETABS Models\*.e2k, *.EDB        ← the engineer's models
/// </code>
/// EVERY LISTING IS BOUNDED (CLAUDE.md rule 4): one directory listing per category, per job, per
/// named folder — never a recursive walk of a job. Over SMB the walk is what costs, and a job's
/// "02 Engineering" runs thousands of files deep. The ETABS folder is found by name at a fixed
/// depth under "02 Engineering" (its child, or its grandchild), which is where PublishDiscovery
/// finds it too; a model folder filed elsewhere is counted as absent and this says so in the
/// summary. WHAT THIS DOES NOT COUNT: models outside an "ETABS Models" folder (the 2026-08 server-side
/// walk found 1,126 .e2k on the volume; this finds the ones filed where the convention says); a
/// stick file whose name does not say STICKFILE (counted as an "other" PDF, listed by name, not read).
/// </remarks>
public static class StickFileCorpus
{
    public const string StickFileFolder = "05 Stickfile";
    public const string ArchitecturalFolder = "01 Architectural";
    public const string EngineeringFolder = "02 Engineering";
    public const string ModelFolderWord = "ETABS Models";

    /// <summary>
    /// A structural stick file is a PDF in 05 Stickfile whose name says STICKFILE; its issue date is
    /// the date its name carries, wherever it carries it — "31168-01 - 2026-04-21- … - Stickfile.pdf",
    /// "01783-01 2026-02-23 … Stickfile.pdf", "01195-01 - … - Struct Stickfile - 2021-12-21.pdf",
    /// "20210913 … IFCr0.pdf" — else the file's own last-write date, flagged. The first census
    /// (2026-09-11) wanted "job - date-" at the start and found 31 jobs; the names themselves showed
    /// 778 more stick files under the other spellings.
    /// </summary>
    public static readonly Regex StickWord = new(@"stick", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex IsoDate = new(@"(?<!\d)(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?!\d)", RegexOptions.Compiled);
    public static readonly Regex CompactDate = new(@"(?<!\d)(?<y>20\d{2})(?<m>\d{2})(?<d>\d{2})(?!\d)|(?<!\d)(?<yy>\d{2})(?<m2>\d{2})(?<d2>\d{2})(?!\d)", RegexOptions.Compiled);
    public static readonly Regex JobNumber = new(@"^(?<job>\d{5}-\d{2})", RegexOptions.Compiled);

    /// <summary>"31###-01 YYYY-MM-DD Project Name Stickfile.pdf" is the office's naming template, filed beside the issues; not one of them.</summary>
    public static bool IsTemplate(string name) => name.Contains("###", StringComparison.Ordinal) || name.Contains("YYYY", StringComparison.OrdinalIgnoreCase);

    /// <summary>The issue date a file name states, as yyyy-MM-dd, or null when it states none.</summary>
    public static string? DateInName(string name)
    {
        var iso = IsoDate.Match(name);
        if (iso.Success) return $"{iso.Groups["y"].Value}-{iso.Groups["m"].Value}-{iso.Groups["d"].Value}";
        var compact = CompactDate.Match(name);
        if (!compact.Success) return null;
        if (compact.Groups["y"].Success) return $"{compact.Groups["y"].Value}-{compact.Groups["m"].Value}-{compact.Groups["d"].Value}";
        int m = int.Parse(compact.Groups["m2"].Value, CultureInfo.InvariantCulture), d = int.Parse(compact.Groups["d2"].Value, CultureInfo.InvariantCulture);
        return m is >= 1 and <= 12 && d is >= 1 and <= 31 ? $"20{compact.Groups["yy"].Value}-{compact.Groups["m2"].Value}-{compact.Groups["d2"].Value}" : null;
    }

    /// <param name="DateFromName">False when the name states no date and the file's last-write date stands in.</param>
    public sealed record Issue(string Path, string Date, bool DateFromName, long Bytes);

    public sealed record JobCensus(
        string Category,
        string Folder,
        string Job,
        IReadOnlyList<Issue> StructuralIssues,
        IReadOnlyList<string> OtherStickPdfs,
        IReadOnlyList<string> ArchitecturalPdfs,
        string? ModelFolder,
        int E2kModels,
        int EdbModels)
    {
        public bool HasStickFile => StructuralIssues.Count > 0;
        public bool HasArchitecturalSet => ArchitecturalPdfs.Count > 0;
        public bool HasModel => E2kModels + EdbModels > 0;
        /// <summary>The current set: the newest issue the office DATED in its name; a file dated only by the filesystem stands in only when no name says.</summary>
        public Issue? Newest => (StructuralIssues.Any(i => i.DateFromName) ? StructuralIssues.Where(i => i.DateFromName) : StructuralIssues).MaxBy(i => i.Date, StringComparer.Ordinal);
        public string? NewestIssue => Newest?.Path;
    }

    /// <summary>Every job folder under every category of the root, counted; errors on a folder are one line each, not a stop.</summary>
    public static IReadOnlyList<JobCensus> Census(string root, IList<string> problems, Action<string>? progress = null, int parallel = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Projects root not found '{root}'.");

        var jobs = new List<(string Category, string Folder)>();
        foreach (string category in Safe(() => Directory.EnumerateDirectories(root), problems, root))
        {
            string name = Path.GetFileName(category);
            foreach (string folder in Safe(() => Directory.EnumerateDirectories(category), problems, category))
                if (JobNumber.IsMatch(Path.GetFileName(folder)))
                    jobs.Add((name, folder));
            progress?.Invoke($"{name}: {jobs.Count(j => j.Category == name)} job folder(s)");
        }

        var results = new JobCensus?[jobs.Count];
        var problemLock = new object();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            var local = new List<string>();
            results[i] = One(jobs[i].Category, jobs[i].Folder, local);
            if (local.Count > 0) lock (problemLock) foreach (var p in local) problems.Add(p);
        });
        return results.Where(r => r is not null).Select(r => r!).OrderBy(r => r.Category, StringComparer.Ordinal).ThenBy(r => r.Job, StringComparer.Ordinal).ToList();
    }

    /// <summary>One job, three bounded listings.</summary>
    public static JobCensus One(string category, string folder, IList<string> problems)
    {
        string job = JobNumber.Match(Path.GetFileName(folder)).Groups["job"].Value;
        var issues = new List<Issue>();
        var others = new List<string>();
        var arch = new List<string>();

        string stick = Path.Combine(folder, StickFileFolder);
        if (Directory.Exists(stick))
        {
            // DirectoryInfo's listing carries each file's size with its name - one round trip for both
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var fi in new DirectoryInfo(stick).EnumerateFiles("*.pdf", SearchOption.TopDirectoryOnly)) sizes[fi.FullName] = fi.Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"{stick}: {ex.Message}"); }
            foreach (string pdf in sizes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(pdf);
                long bytes = sizes[pdf];
                if (!StickWord.IsMatch(name) || IsTemplate(name)) { others.Add(pdf); continue; }
                string? dated = DateInName(name);
                if (dated is not null) issues.Add(new Issue(pdf, dated, DateFromName: true, bytes));
                else
                {
                    string fallback;
                    try { fallback = File.GetLastWriteTimeUtc(pdf).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"{pdf}: {ex.Message}"); fallback = "0000-00-00"; }
                    issues.Add(new Issue(pdf, fallback, DateFromName: false, bytes));
                }
            }
            string architectural = Path.Combine(stick, ArchitecturalFolder);
            if (Directory.Exists(architectural))
            {
                arch.AddRange(Safe(() => Directory.EnumerateFiles(architectural, "*.pdf", SearchOption.TopDirectoryOnly), problems, architectural));
                foreach (string sub in Safe(() => Directory.EnumerateDirectories(architectural), problems, architectural))
                    arch.AddRange(Safe(() => Directory.EnumerateFiles(sub, "*.pdf", SearchOption.TopDirectoryOnly), problems, sub));
            }
        }

        string? modelFolder = null;
        int e2k = 0, edb = 0;
        string engineering = Path.Combine(folder, EngineeringFolder);
        if (Directory.Exists(engineering))
        {
            foreach (string child in Safe(() => Directory.EnumerateDirectories(engineering), problems, engineering))
            {
                if (Path.GetFileName(child).Contains(ModelFolderWord, StringComparison.OrdinalIgnoreCase)) { modelFolder = child; break; }
                foreach (string grandchild in Safe(() => Directory.EnumerateDirectories(child), problems, child))
                    if (Path.GetFileName(grandchild).Contains(ModelFolderWord, StringComparison.OrdinalIgnoreCase)) { modelFolder = grandchild; break; }
                if (modelFolder is not null) break;
            }
            if (modelFolder is not null)
            {
                var files = Safe(() => Directory.EnumerateFiles(modelFolder, "*.*", SearchOption.TopDirectoryOnly), problems, modelFolder).ToList();
                foreach (string sub in Safe(() => Directory.EnumerateDirectories(modelFolder), problems, modelFolder))
                    files.AddRange(Safe(() => Directory.EnumerateFiles(sub, "*.*", SearchOption.TopDirectoryOnly), problems, sub));
                e2k = files.Count(f => f.EndsWith(".e2k", StringComparison.OrdinalIgnoreCase));
                edb = files.Count(f => f.EndsWith(".edb", StringComparison.OrdinalIgnoreCase));
            }
        }

        return new JobCensus(category, folder, job, issues, others, arch, modelFolder, e2k, edb);
    }

    /// <summary>The totals a person reads first: X of Y, never a sample.</summary>
    public static string Summary(IReadOnlyList<JobCensus> census)
    {
        var sb = new StringBuilder();
        int n = census.Count;
        int stick = census.Count(j => j.HasStickFile);
        int archSets = census.Count(j => j.HasArchitecturalSet);
        int model = census.Count(j => j.HasModel);
        int both = census.Count(j => j.HasStickFile && j.HasModel);
        int all3 = census.Count(j => j.HasStickFile && j.HasModel && j.HasArchitecturalSet);
        int issues = census.Sum(j => j.StructuralIssues.Count);
        double newestMb = census.Sum(j => j.Newest?.Bytes ?? 0) / 1048576.0, allMb = census.Sum(j => j.StructuralIssues.Sum(i => i.Bytes)) / 1048576.0;
        int archPdfs = census.Sum(j => j.ArchitecturalPdfs.Count);
        int otherPdfs = census.Sum(j => j.OtherStickPdfs.Count);
        int e2k = census.Sum(j => j.E2kModels), edb = census.Sum(j => j.EdbModels);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{n} job folders under {census.Select(j => j.Category).Distinct().Count()} categories");
        int undated = census.Sum(j => j.StructuralIssues.Count(i => !i.DateFromName));
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {stick} of {n} have a structural stick file ({issues} issues in all, {undated} dated only by the file; newest per job is the current set: {newestMb:N0} MB to mirror, {allMb:N0} MB for every issue)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {archSets} of {n} have an architect's set under 05 Stickfile\\01 Architectural ({archPdfs} PDFs)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {otherPdfs} other PDFs sit in 05 Stickfile without STICKFILE in the name (listed in the files CSV, not read as issues)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {model} of {n} have an ETABS Models folder with a model in it ({e2k} .e2k, {edb} .EDB)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {both} of {n} have BOTH a stick file and a model - a yardstick each; {all3} have all three");
        // one stick file filed under many jobs is a copy the job-folder template carried, not many sets:
        // "01783-01 … Lytton Stickfile.pdf" sat in 16 job folders on the first census (2026-09-11)
        var copied = census.SelectMany(j => j.StructuralIssues.Select(i => (j.Job, Name: System.IO.Path.GetFileName(i.Path))))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(t => t.Job).Distinct().Count() > 1).ToList();
        if (copied.Count > 0)
        {
            string worst = string.Join("; ", copied.OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} x{g.Count()}"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {copied.Count} stick file name(s) sit under more than one job - copies, one set each, not {copied.Sum(g => g.Count())}: {worst}");
        }
        foreach (var g in census.GroupBy(j => j.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {g.Key,-24} jobs {g.Count(),4}  stick {g.Count(j => j.HasStickFile),4}  arch {g.Count(j => j.HasArchitecturalSet),4}  model {g.Count(j => j.HasModel),4}  both {g.Count(j => j.HasStickFile && j.HasModel),4}");
        return sb.ToString();
    }

    /// <summary>One row per job, for the ledger and for a person in Excel.</summary>
    public static void WriteCsv(IReadOnlyList<JobCensus> census, string path)
    {
        static string Q(string? s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var lines = new List<string> { "category,job,folder,structural_issues,newest_issue_date,newest_date_from_name,newest_issue,other_stick_pdfs,architectural_pdfs,model_folder,e2k,edb" };
        foreach (var j in census)
        {
            var newest = j.Newest;
            lines.Add(string.Join(",", Q(j.Category), Q(j.Job), Q(j.Folder), j.StructuralIssues.Count.ToString(CultureInfo.InvariantCulture),
                Q(newest?.Date), Q(newest is null ? null : newest.DateFromName ? "yes" : "no"), Q(newest?.Path), j.OtherStickPdfs.Count.ToString(CultureInfo.InvariantCulture),
                j.ArchitecturalPdfs.Count.ToString(CultureInfo.InvariantCulture), Q(j.ModelFolder),
                j.E2kModels.ToString(CultureInfo.InvariantCulture), j.EdbModels.ToString(CultureInfo.InvariantCulture)));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // and every PDF the job rows only count - the undated stick-file PDFs and the architects' sets -
        // one row per file beside it, because "1,101 other PDFs" is a number and their NAMES are what
        // say whether the dated-issue rule is missing the older structural sets (first run, 2026-09-11)
        string files = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-files.csv");
        var rows = new List<string> { "category,job,kind,file" };
        foreach (var j in census)
        {
            foreach (var f in j.OtherStickPdfs) rows.Add(string.Join(",", Q(j.Category), Q(j.Job), Q("other-stick"), Q(f)));
            foreach (var f in j.ArchitecturalPdfs) rows.Add(string.Join(",", Q(j.Category), Q(j.Job), Q("architectural"), Q(f)));
            foreach (var i in j.StructuralIssues) rows.Add(string.Join(",", Q(j.Category), Q(j.Job), Q("structural-issue"), Q(i.Path)));
        }
        File.WriteAllLines(files, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static IEnumerable<string> Safe(Func<IEnumerable<string>> list, IList<string> problems, string where)
    {
        List<string> items;
        try { items = list().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{where}: {ex.Message}");
            return [];
        }
        return items;
    }
}
