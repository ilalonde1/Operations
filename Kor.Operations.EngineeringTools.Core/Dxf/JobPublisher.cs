namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Publishing a job: find it, decide what to build, build it, refuse it if it is wrong, and only
/// then put it where an engineer will open it.
/// </summary>
public static class JobPublisher
{
    public sealed record Request
    {
        public required string Project { get; init; }
        public string? ModelFolder { get; init; }
        public string? DxfFolder { get; init; }
        public string ProjectsRoot { get; init; } = PublishDiscovery.DefaultProjectsRoot;
        public string? RepoRoot { get; init; }
        public string? Reference { get; init; }
        public string? RuleSettingsConnection { get; init; }
        public string? StageFolder { get; init; }
        public IReadOnlyList<string> DropStoreys { get; init; } = Array.Empty<string>();
        public bool InferFloors { get; init; }
        public string? StickFilePdf { get; init; }
        public string? AnnotatedDxfFolder { get; init; }
        public string? TopStorey { get; init; }
        public string? Tower { get; init; }
        public string? Variant { get; init; }
        public bool PerBuilding { get; init; }
        public bool SkipDossier { get; init; }
        public bool Land { get; init; }
        public string? RendererScript { get; init; }
        public string? PdfInfoExe { get; init; }
    }

    public sealed record Built(
        string Label,
        string OutputPath,
        int Storeys,
        int Walls,
        int Columns,
        int Floors,
        IReadOnlyList<ModelViolation> Violations,
        string? ReportPath = null,
        string? QuestionsPath = null,
        string? SummaryPdfPath = null,
        // What the one-page rule cost this run. The summary shortens its findings list 8/6/4/3/2
        // until the page fits, and whatever it drops is dropped from the covering note an engineer
        // reads. The PDF says so itself, but the person running the publish should not have to open
        // it to find out -- the script printed this and the port stopped.
        int SummaryFindingsShown = 0,
        int SummaryFindingsTrimmed = 0,
        DxfToEtabsReport? Report = null)
    {
        public IReadOnlyList<ModelViolation> BlockingViolations
            => Violations.Where(v => v.BlocksPublishing).ToList();

        public IReadOnlyList<ModelViolation> AdvisoryViolations
            => Violations.Where(v => !v.BlocksPublishing).ToList();

        public bool Passed => BlockingViolations.Count == 0;
    }

    public sealed record Outcome(string Reference, IReadOnlyList<Built> Models, string? Refused)
    {
        public string? ModelFolder { get; init; }
        public string? DxfFolder { get; init; }
        public string? StageFolder { get; init; }
        public IReadOnlyList<string> Landed { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Withdrawn { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public static Outcome Run(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.RuleSettingsConnection))
            return new Outcome(string.Empty, Array.Empty<Built>(),
                "KOR_ENGINEERINGTOOLS_STANDARDSDB is not set; refusing to publish a model from built-in rules.");

        string repoRoot = FindRepoRoot(request.RepoRoot);
        PublishDiscoveryResult discovery;
        try
        {
            discovery = PublishDiscovery.Discover(new PublishDiscoveryRequest(
                request.Project, request.ModelFolder, request.DxfFolder, request.Reference, request.ProjectsRoot));
        }
        catch (Exception ex)
        {
            return new Outcome(string.Empty, Array.Empty<Built>(), ex.Message);
        }

        string labelForStage = string.IsNullOrWhiteSpace(request.Variant)
            ? request.Project
            : $"{request.Project}-{request.Variant.Trim().ToUpperInvariant()}";
        string stage = request.StageFolder ?? Path.Combine(Path.GetTempPath(), $"kor-publish-{labelForStage}");
        PrepareStage(stage, request.StageFolder is null);

        string referencePath = Path.Combine(discovery.ModelFolder, discovery.Reference);
        var document = E2kDocument.Load(referencePath);
        var storeys = document.ReadStories().Select(s => s.Name).ToList();
        var plans = PlansFor(request, discovery, storeys);

        var built = new List<Built>();
        foreach (var plan in plans)
        {
            string label = LabelFor(request.Project, request.Variant, request.PerBuilding, plan);
            string output = Path.Combine(stage, $"{label}-FROM-DRAWINGS.e2k");
            string reportPath = Path.Combine(stage, $"{label}-FROM-DRAWINGS-report.txt");
            string questionsPath = Path.Combine(stage, $"{label}-QUESTIONS.xlsx");

            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                RequireRuleSettings = true,
                RuleSettingsConnection = request.RuleSettingsConnection,
                DxfFolder = discovery.DxfFolder,
                StickFilePdf = ResolveOptionalFile(request.StickFilePdf),
                AnnotatedDxfFolder = ResolveOptionalFolder(request.AnnotatedDxfFolder),
                ReferenceE2k = referencePath,
                OutputE2k = output,
                TowerOnly = string.IsNullOrWhiteSpace(plan.Tower) ? null : plan.Tower,
                TopStorey = request.TopStorey,
                DropStoreys = plan.DropStoreys
                    .Concat(request.DropStoreys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Compose = new ComposeOptions { InferMissingFloors = request.InferFloors },
            });

            File.WriteAllText(reportPath, DxfToEtabsService.FormatReport(report));
            ModelQuestionnaire.Write(questionsPath, report, report.ClassificationUsed, report.ComposeUsed, label);

            var dropped = plan.DropStoreys
                .Concat(request.DropStoreys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var violations = ShippedModelInvariants.Check(
                File.ReadLines(output), 0.05, dropped, File.ReadLines(referencePath),
                report.FoundationStoreys, File.ReadLines(reportPath), ModelQuestionnaire.TextLines(questionsPath));

            built.Add(new Built(
                label,
                output,
                report.SavedModel.Storeys.Count,
                report.SavedModel.Walls,
                report.SavedModel.Columns,
                report.SavedModel.Floors,
                violations,
                reportPath,
                questionsPath,
                Report: report));
        }

        if (built.Any(b => !b.Passed))
            return Result(discovery, stage, built, "one or more generated models failed publish-blocking invariants.");

        PublishToolPaths tools;
        try
        {
            tools = PublishExternalTools.Locate(repoRoot, request.RendererScript, request.PdfInfoExe);
        }
        catch (Exception ex)
        {
            return Result(discovery, stage, built, ex.Message);
        }

        for (int i = 0; i < built.Count; i++)
        {
            var one = built[i];
            if (one.Report is null || one.ReportPath is null || one.QuestionsPath is null) continue;

            try
            {
                var summary = PublishSummary.Write(new PublishSummaryRequest(
                    request.Project, one.Label, discovery.DxfFolder, discovery.Reference,
                    one.Report, one.ReportPath, one.QuestionsPath, stage, Path.GetTempPath(), tools));
                built[i] = one with
                {
                    SummaryPdfPath = summary.PdfPath,
                    SummaryFindingsShown = summary.FindingsShown,
                    SummaryFindingsTrimmed = summary.TrimmedAway,
                };
            }
            catch (Exception ex)
            {
                return Result(discovery, stage, built, ex.Message);
            }
        }

        var warnings = new List<string>();
        var explainers = PublishExplainers.Evaluate(new PublishExplainersRequest(
            request.Project,
            discovery.ModelFolder,
            repoRoot,
            request.ProjectsRoot,
            built[0].OutputPath,
            built[0].ReportPath ?? string.Empty,
            built[0].Report?.SavedModel ?? E2kModelContents.Empty,
            request.SkipDossier,
            request.PerBuilding || !string.IsNullOrWhiteSpace(request.Variant)));
        warnings.AddRange(explainers.Warnings);

        if (explainers.Refused is not null)
        {
            var withdrawnOnRefusal = request.Land
                ? Withdraw(explainers.ToWithdraw)
                : Array.Empty<string>();
            return Result(discovery, stage, built, explainers.Refused)
                with { Warnings = warnings, Withdrawn = withdrawnOnRefusal };
        }

        if (!request.Land)
            return Result(discovery, stage, built, null) with { Warnings = warnings };

        var landed = new List<string>();
        var withdrawn = new List<string>();
        foreach (string file in StagedFilesFor(built))
        {
            string target = Path.Combine(discovery.ModelFolder, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            landed.Add(Path.GetFileName(file));
        }

        foreach (var explainer in explainers.ToCopy)
        {
            File.Copy(explainer.Source, explainer.Target, overwrite: true);
            landed.Add(Path.GetFileName(explainer.Target));
        }

        withdrawn.AddRange(Withdraw(explainers.ToWithdraw));

        foreach (var one in built)
        {
            string old = Path.Combine(discovery.ModelFolder, $"{one.Label}-QUESTIONS-for-Andrea.xlsx");
            string current = Path.Combine(discovery.ModelFolder, $"{one.Label}-QUESTIONS.xlsx");
            if (File.Exists(old) && File.Exists(current))
            {
                File.Delete(old);
                withdrawn.Add(Path.GetFileName(old));
            }
        }

        var stale = StaleOwnedFiles(repoRoot, discovery.ModelFolder, landed).ToList();
        if (stale.Count > 0)
            return Result(discovery, stage, built, "STALE - these predate the source that built them: " + string.Join(", ", stale))
                with { Landed = landed, Withdrawn = withdrawn, Warnings = warnings };

        return Result(discovery, stage, built, null)
            with { Landed = landed, Withdrawn = withdrawn, Warnings = warnings };
    }

    private static IReadOnlyList<PublishPlan.Model> PlansFor(
        Request request,
        PublishDiscoveryResult discovery,
        IReadOnlyList<string> storeys)
    {
        if (request.PerBuilding && string.IsNullOrWhiteSpace(request.Variant))
        {
            var reachRules = PlanRulesFor(request.RuleSettingsConnection);
            return PublishPlan.ForBuildings(storeys, ReachByStorey(discovery.DxfFolder, storeys, reachRules));
        }

        string tower = request.Tower?.Trim().ToUpperInvariant() ?? string.Empty;
        string building = request.Variant?.Trim().ToUpperInvariant() ?? tower;
        return new[] { new PublishPlan.Model(building, tower, Array.Empty<string>()) };
    }

    private static string LabelFor(string project, string? variant, bool perBuilding, PublishPlan.Model plan)
    {
        if (!string.IsNullOrWhiteSpace(variant))
            return $"{project}-{variant.Trim().ToUpperInvariant()}";
        if (perBuilding && plan.Building.Length > 0)
            return $"{project}-{plan.Building}";
        return project;
    }

    private static Outcome Result(
        PublishDiscoveryResult discovery,
        string stage,
        IReadOnlyList<Built> built,
        string? refused)
        => new(discovery.Reference, built, refused)
        {
            ModelFolder = discovery.ModelFolder,
            DxfFolder = discovery.DxfFolder,
            StageFolder = stage,
        };

    private static void PrepareStage(string stage, bool defaultStage)
    {
        if (defaultStage && Directory.Exists(stage))
            Directory.Delete(stage, recursive: true);
        Directory.CreateDirectory(stage);
    }

    private static string? ResolveOptionalFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found '{path}'.", path);
        return Path.GetFullPath(path);
    }

    private static string? ResolveOptionalFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Folder not found '{path}'.");
        return Path.GetFullPath(path);
    }

    private static IEnumerable<string> StaleOwnedFiles(string repoRoot, string folder, IReadOnlyList<string> owned)
    {
        string dxfSource = Path.Combine(repoRoot, "Kor.Operations.EngineeringTools.Core", "Dxf");
        if (!Directory.Exists(dxfSource)) yield break;

        var newest = Directory.EnumerateFiles(dxfSource, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.GetLastWriteTime)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        foreach (string file in owned)
        {
            string path = Path.Combine(folder, file);
            if (File.Exists(path) && File.GetLastWriteTime(path) < newest)
                yield return file;
        }
    }

    private static IReadOnlyList<string> StagedFilesFor(IReadOnlyList<Built> built)
        => built.SelectMany(b => new[] { b.OutputPath, b.ReportPath, b.QuestionsPath, b.SummaryPdfPath })
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> Withdraw(IReadOnlyList<string> targets)
    {
        var withdrawn = new List<string>();
        foreach (string target in targets)
        {
            if (!File.Exists(target)) continue;
            File.Delete(target);
            withdrawn.Add(Path.GetFileName(target));
        }

        return withdrawn;
    }

    private static string FindRepoRoot(string? given)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return Path.GetFullPath(given);

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Kor.Operations.EngineeringTools.Core", "Kor.Operations.EngineeringTools.Core.csproj"))
                && Directory.Exists(Path.Combine(current.FullName, "tools")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static PlanClassificationOptions PlanRulesFor(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) return new PlanClassificationOptions();

        try
        {
            var settings = RuleSettings.LoadRequired(connection, DxfToEtabsService.RequiredRuleKeys);
            return DxfToEtabsService.ApplyRules(new PlanClassificationOptions(), settings);
        }
        catch
        {
            return new PlanClassificationOptions();
        }
    }

    public static IReadOnlyList<PublishPlan.StoreyReach> ReachByStorey(
        string dxfFolder, IReadOnlyList<string> storeys, PlanClassificationOptions? rules = null)
    {
        var options = rules ?? new PlanClassificationOptions();
        var reach = new Dictionary<string, PublishPlan.StoreyReach>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(dxfFolder, "*.dxf", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (options.NonStructuralSheetPatterns.Any(
                    pattern => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;

            var sheet = PlanSheetNaming.Parse(file);
            var on = PlanSheetNaming.MatchStories(sheet, storeys);
            if (on.Count == 0) continue;

            var found = StructuralPlanClassifier.Classify(
                DxfPlanReader.ReadSegments(file),
                options,
                sheet,
                DxfPlanReader.ReadPositionedTags(file));
            var points = found.Walls.SelectMany(w => new[] { w.Start, w.End })
                .Concat(found.Columns.Select(c => c.Center))
                .ToList();
            if (points.Count == 0) continue;

            double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X), maxY = points.Max(p => p.Y);

            foreach (string storey in on)
                reach[storey] = reach.TryGetValue(storey, out var had)
                    ? new PublishPlan.StoreyReach(storey,
                        Math.Min(had.MinX, minX), Math.Min(had.MinY, minY),
                        Math.Max(had.MaxX, maxX), Math.Max(had.MaxY, maxY))
                    : new PublishPlan.StoreyReach(storey, minX, minY, maxX, maxY);
        }

        return reach.Values.ToList();
    }
}
