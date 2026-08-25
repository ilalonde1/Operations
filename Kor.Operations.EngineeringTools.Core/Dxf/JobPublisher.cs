using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Publishing a job: find it, decide what to build, build it, refuse it if it is wrong, and only
/// then put it where an engineer will open it.
///
/// This was a PowerShell script. Everything in it that decides something is here now, because a
/// decision the test suite cannot reach is a decision nobody is checking — and the way these were
/// checked was to publish against the live project share and look at what came out. That is how a
/// model reached an engineer carrying eight storeys of a building she had said was out of scope.
///
/// What is deliberately NOT here: rendering a PDF, which needs a browser, and copying files. Those
/// are plumbing and they can stay in a launcher. Choosing the reference, splitting the job into
/// buildings, and refusing a model that breaks an invariant are not plumbing.
/// </summary>
public static class JobPublisher
{
    public sealed record Request
    {
        public required string Project { get; init; }
        public required string ModelFolder { get; init; }
        public required string DxfFolder { get; init; }

        /// <summary>Named only when the folder holds more than one engineer-built model.</summary>
        public string? Reference { get; init; }

        public string? RuleSettingsConnection { get; init; }

        /// <summary>Where the model is written before it has passed. Nothing lands until it does.</summary>
        public required string StageFolder { get; init; }

        /// <summary>Give a storey with members but no drawn floor one borrowed from its neighbour.</summary>
        public bool InferFloors { get; init; }
    }

    public sealed record Built(
        string Label, string OutputPath, int Storeys, int Walls, int Columns, int Floors,
        IReadOnlyList<ModelViolation> Violations)
    {
        public IReadOnlyList<ModelViolation> BlockingViolations
            => Violations.Where(v => v.BlocksPublishing).ToList();

        public IReadOnlyList<ModelViolation> AdvisoryViolations
            => Violations.Where(v => !v.BlocksPublishing).ToList();

        public bool Passed => BlockingViolations.Count == 0;
    }

    public sealed record Outcome(string Reference, IReadOnlyList<Built> Models, string? Refused);

    /// <summary>
    /// Build every model this job needs — one per building — and verify each one.
    ///
    /// Nothing is copied anywhere. The caller lands what passed; a model that failed stays in the
    /// staging folder with its violations, which is the whole point of staging it.
    /// </summary>
    public static Outcome Run(Request request)
    {
        Directory.CreateDirectory(request.StageFolder);

        string? reference = request.Reference;
        if (reference is null)
        {
            var candidates = Directory.EnumerateFiles(request.ModelFolder)
                .Where(f => Path.GetExtension(f) is ".e2k" or ".$et")
                .Select(f => (Name: Path.GetFileName(f), Head: (Func<string>)(() => Head(f))))
                .ToList();

            reference = PublishPlan.ChooseReference(candidates, out string why);
            if (reference is null) return new Outcome(string.Empty, Array.Empty<Built>(), why);
        }

        string referencePath = Path.Combine(request.ModelFolder, reference);
        var document = E2kDocument.Load(referencePath);
        var storeys = document.ReadStories().Select(s => s.Name).ToList();

        var plans = PublishPlan.ForBuildings(storeys, ReachByStorey(request.DxfFolder, storeys));

        var built = new List<Built>();
        foreach (var plan in plans)
        {
            // One building gets the job's own name; several get the job's name and the building's,
            // because every output is named from the label and a second run would otherwise
            // overwrite the first one silently -- model, report, workbook and summary, all four.
            string label = plan.Building.Length == 0 ? request.Project : $"{request.Project}-{plan.Building}";
            string output = Path.Combine(request.StageFolder, $"{label}-FROM-DRAWINGS.e2k");

            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                RequireRuleSettings = request.RuleSettingsConnection is not null,
                RuleSettingsConnection = request.RuleSettingsConnection,
                DxfFolder = request.DxfFolder,
                ReferenceE2k = referencePath,
                OutputE2k = output,
                TowerOnly = plan.Tower.Length == 0 ? null : plan.Tower,
                DropStoreys = plan.DropStoreys,
                Compose = new ComposeOptions { InferMissingFloors = request.InferFloors },
            });

            File.WriteAllText(
                Path.Combine(request.StageFolder, $"{label}-FROM-DRAWINGS-report.txt"),
                DxfToEtabsService.FormatReport(report));

            ModelQuestionnaire.Write(
                Path.Combine(request.StageFolder, $"{label}-QUESTIONS.xlsx"),
                report, report.ClassificationUsed, report.ComposeUsed, label);

            // The reference goes in so the invariants judge what THIS TOOL built. On a gap-fill job
            // the engineer's own model is carried through into the output, and hers is not ours to
            // refuse: 31138 failed 514 checks, every one of them her work.
            var violations = ShippedModelInvariants.Check(
                File.ReadLines(output), 0.05, plan.DropStoreys, File.ReadLines(referencePath),
                report.FoundationStoreys);

            built.Add(new Built(label, output, report.Summary.Stories, report.Summary.Walls,
                report.Summary.Columns, report.Summary.Floors, violations));
        }

        return new Outcome(reference, built, null);
    }

    /// <summary>
    /// Where the structure read from each storey's sheets stands, in plan. This is what tells a
    /// tower floor with no prefix apart from the mid-rise's own — nothing in the NAME does.
    /// </summary>
    public static IReadOnlyList<PublishPlan.StoreyReach> ReachByStorey(
        string dxfFolder, IReadOnlyList<string> storeys)
    {
        var options = new PlanClassificationOptions();
        var reach = new Dictionary<string, PublishPlan.StoreyReach>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(dxfFolder, "*.dxf", SearchOption.TopDirectoryOnly))
        {
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

    /// <summary>Enough of a model to tell whose it is, without reading a 1.4 MB file to find out.</summary>
    private static string Head(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[64 * 1024];
        int read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, Math.Max(0, read));
    }
}
