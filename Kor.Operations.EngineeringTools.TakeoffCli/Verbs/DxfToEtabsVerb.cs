// The takeoff verb `dxf-to-etabs`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Usage: takeoff dxf-to-etabs <dxfFolder> <reference.e2k> <out.e2k> [--rules-db <connection>] [--bldg B] [--offset x,y] [--no-floors] [--report file.txt] [--questions questions.xlsx]
internal static class DxfToEtabsVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("dxf-to-etabs", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: takeoff dxf-to-etabs <dxfFolder> <reference.e2k|-> <out.e2k> [--levels levels.csv] [--rules-db <connection>] [--bldg B] [--offset x,y] [--no-floors] [--report file.txt]");
            Console.Error.WriteLine("       Pass '-' for the reference when giving --levels: a job that has never been modelled has no .e2k.");
            return 1;
        }
        if (!Directory.Exists(args[1])) { Console.Error.WriteLine($"DXF folder not found '{args[1]}'."); return 2; }

        // "-" means there is no reference model, which is the ordinary case for a job nobody has
        // modelled yet. The level list stands in for it; see E2kShellBuilder.
        bool noReference = args[2] == "-";
        if (!noReference && !File.Exists(args[2])) { Console.Error.WriteLine($"Reference .e2k not found '{args[2]}'."); return 2; }

        // The reference must be an engineer's model, never one of ours. A file round-tripped through
        // ETABS keeps its KOR-prefixed object names, and building from one produces a report saying
        // nothing was generated beside a file that plainly contains generated members -- both true,
        // together untrue. The publisher has always refused this; the CLI, which is what a
        // wrapper calls, did not.
        if (!noReference && File.ReadLines(args[2]).Take(40000).Any(l => Regex.IsMatch(l, @"""K[WCPFSO]\d+""")))
        {
            Console.Error.WriteLine(
                $"'{Path.GetFileName(args[2])}' carries KOR-generated object names, so it is this tool's own " +
                "output rather than an engineer's model. Building from it would report nothing generated " +
                "beside a file that already contains generated members. Point at the engineer's model.");
            return 2;
        }

        string? building = null;
        string? towerOnly = null;
        string? stickFile = null;
        string? annotatedDxf = null;
        bool membersRise = true;
        string? topStorey = null;
        var dropStoreys = new List<string>();
        string? levelsFile = null;
        string levelsUnit = "in";
        bool levelsUnitGiven = false;
        string? job = null;
        string? reportPath = null;
        string? questionsPath = null;
        string? rulesDb = null;
        (double X, double Y)? offset = null;
        bool includeFloors = true;
        bool inferFloors = false;
        // Whether the flag was GIVEN, not just its value: passing the default silently is how these
        // three came to look like they worked.
        bool bridgeGiven = false, joinGiven = false, extendGiven = false;
        double bridgeTolerance = new PlanClassificationOptions().BridgeTolerance;
        double joinTolerance = new PlanClassificationOptions().JoinTolerance;
        double extendLimit = new PlanClassificationOptions().ExtendLimit;

        // Layer names for THIS job. The database holds a default, and a default is the right shape for
        // a threshold -- a 48" wall is 48" on every job. It is the wrong shape for a layer name: what
        // drafting calls a column is a fact about one office, and often about one project within it.
        // 500 Foster draws columns on V-COL; KOR draws them on JBP_V_COL. Making the pattern a global
        // rule and setting it for one job breaks the other, so the rule alone cannot make this tool
        // agnostic -- it only makes the value visible. This is what makes a new job runnable today.
        List<string>? wallLayers = null, columnLayers = null, slabLayers = null;

        static List<string> Patterns(string raw) => raw
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        for (int i = 4; i < args.Length; i++)
        {
            string flag = args[i];
            if (flag.Equals("--bldg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) building = args[++i];
            else if (flag.Equals("--tower", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) towerOnly = args[++i];
            else if (flag.Equals("--top-storey", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) topStorey = args[++i];
            else if (flag.Equals("--levels", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) levelsFile = args[++i];
            else if (flag.Equals("--levels-unit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { levelsUnit = args[++i]; levelsUnitGiven = true; }
            else if (flag.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) job = args[++i];
            else if (flag.Equals("--drop-storeys", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                dropStoreys.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else if (flag.Equals("--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) reportPath = args[++i];
            else if (flag.Equals("--questions", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) questionsPath = args[++i];
            else if (flag.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rulesDb = args[++i];
            else if (flag.Equals("--no-floors", StringComparison.OrdinalIgnoreCase)) includeFloors = false;
            else if (flag.Equals("--no-storey-rise", StringComparison.OrdinalIgnoreCase)) membersRise = false;
            else if (flag.Equals("--infer-floors", StringComparison.OrdinalIgnoreCase)) inferFloors = true;
            else if (flag.Equals("--stick-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) stickFile = args[++i];
            else if (flag.Equals("--annotated-dxf", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) annotatedDxf = args[++i];
            else if (flag.Equals("--wall-layers", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) wallLayers = Patterns(args[++i]);
            else if (flag.Equals("--column-layers", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) columnLayers = Patterns(args[++i]);
            else if (flag.Equals("--slab-layers", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) slabLayers = Patterns(args[++i]);
            else if (flag.Equals("--bridge", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double bt))
            {
                bridgeTolerance = bt;
                bridgeGiven = true;
            }
            else if (flag.Equals("--join", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double jt))
            {
                joinTolerance = jt;
                joinGiven = true;
            }
            else if (flag.Equals("--extend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double ex))
            {
                extendLimit = ex;
                extendGiven = true;
            }
            else if (flag.Equals("--offset", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var parts = args[++i].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double ox) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double oy))
                {
                    offset = (ox, oy);
                }
                else { Console.Error.WriteLine("--offset expects <x>,<y> in inches."); return 1; }
            }
        }

        // A levels file says its own unit ("# unit: mm", as pdf-levels writes it) and is believed
        // unless --levels-unit says otherwise; a file that says nothing is in inches, as before.
        if (!levelsUnitGiven && levelsFile is not null && File.Exists(levelsFile))
        {
            foreach (string line in File.ReadLines(levelsFile).Take(5))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*unit\s*:\s*(in|ft|mm|m)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) { levelsUnit = m.Groups[1].Value.ToLowerInvariant(); break; }
            }
        }

        DxfToEtabsReport dxfReport;
        try
        {
            dxfReport = DxfToEtabsService.Run(new DxfToEtabsRequest
        {
            DxfFolder = args[1],
            Job = job,
            StickFilePdf = stickFile,
            AnnotatedDxfFolder = annotatedDxf,
            ReferenceE2k = noReference ? string.Empty : args[2],
            OutputE2k = args[3],
            BuildingTag = building,
            TowerOnly = towerOnly,
            TopStorey = topStorey,
            DropStoreys = dropStoreys,
            LevelsFile = levelsFile,
            LevelsUnit = levelsUnit,
            Offset = offset,
            Classification = new PlanClassificationOptions(),
            BridgeTolerance = bridgeGiven ? bridgeTolerance : null,
            JoinTolerance = joinGiven ? joinTolerance : null,
            ExtendLimit = extendGiven ? extendLimit : null,
            WallLayerPatterns = wallLayers,
            ColumnLayerPatterns = columnLayers,
            SlabLayerPatterns = slabLayers,
            Compose = new ComposeOptions { IncludeFloors = includeFloors, InferMissingFloors = inferFloors, MembersRiseToStoreyAbove = membersRise },
            RuleSettingsConnection = rulesDb,
            RequireRuleSettings = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            // A drawing set this tool cannot use is an ordinary outcome, not a defect in the tool. It
            // used to arrive as an unhandled .NET exception: no report, a stack trace, and the source
            // paths of this machine printed at an engineer who wanted to know what was wrong with her
            // drawings. The message was always the useful part; only the delivery was wrong.
            Console.Error.WriteLine($"Cannot build a model from these inputs: {ex.Message}");
            if (reportPath is not null)
            {
                File.WriteAllText(reportPath,
                    "No model was produced." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine);
                Console.Error.WriteLine($"Written to {reportPath}.");
            }

            return 2;
        }

        string text = DxfToEtabsService.FormatReport(dxfReport);
        Console.WriteLine(text);
        if (reportPath is not null) File.WriteAllText(reportPath, text);

        if (questionsPath is not null)
        {
            ModelQuestionnaire.Write(
                questionsPath, dxfReport,
                dxfReport.ClassificationUsed,
                dxfReport.ComposeUsed,
                Path.GetFileNameWithoutExtension(args[3]));
            Console.WriteLine($"questions for the engineer: {questionsPath}");
        }

        if (dxfReport.Summary.Walls + dxfReport.Summary.Columns > 0) return 0;

        // Nothing was generated, so nothing may be left behind that looks like a model. The file
        // written here is the reference with no drawing geometry added -- 870 KB, structurally valid,
        // and containing not one thing read from the drawings. Whoever copies it past a non-zero exit
        // code gets a building that is entirely somebody else's.
        if (File.Exists(args[3]))
        {
            try
            {
                File.Delete(args[3]);
                Console.Error.WriteLine(
                    $"No walls or columns were generated, so no model was written. " +
                    $"'{Path.GetFileName(args[3])}' would have been the reference model with nothing added to it.");
            }
            catch (IOException io)
            {
                Console.Error.WriteLine(
                    $"No walls or columns were generated. '{Path.GetFileName(args[3])}' holds the reference " +
                    $"model with nothing added and should not be used ({io.Message}).");
            }
        }

        return 3;
    }
}
