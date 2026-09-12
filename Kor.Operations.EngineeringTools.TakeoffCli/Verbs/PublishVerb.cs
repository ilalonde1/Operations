// The takeoff verb `publish`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// VERIFY a generated model against a trusted one: how closely does the geometry built from
// drawings reproduce what the imported model already says is there, storey by storey.
// Usage: takeoff e2k-compare <reference.e2k> <candidate.e2k> <story> [<story> ...]
// WHICH BUILDING EACH STOREY BELONGS TO, read off the drawings rather than passed in by hand.
//
// A site model carries several buildings in one storey list, and until now the operator had to
// know the shape of the job and say so: -Tower C -TopStorey C-ROOF -DropStoreys LEVEL 3..LEVEL 10.
// That is the tool asking the engineer to know what the tool is looking at.
//
// A storey NAMED for a building belongs to it. A storey named for nobody -- "LEVEL 12", "LEVEL P1"
// -- belongs to whichever building's footprint its structure stands inside, and to all of them
// when it stands under all of them, which is what a shared podium or parkade is.
//
// Usage: takeoff dxf-buildings <dxfFolder> <reference.e2k>
// PUBLISH a job: discover it, generate to staging, verify, build the summary, gate explainers,
// and only then land the files an engineer will open.
//
// Usage: takeoff publish <job> [--model-folder folder] [--dxf-folder folder] [--reference f.e2k]
//                        [--rules-db c] [--infer-floors] [--stage folder] [--land]
//                        [--tower tag] [--top-storey name] [--drop-storeys a,b,c]
//                        [--variant name] [--per-building] [--skip-dossier]
internal static class PublishVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("publish", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        // A job number never starts with "--", so an option in the job position means the job was
        // left out -- including the "--help" a person reasonably tries first. Without this the
        // publisher goes looking for a job folder named "--help" and reports it cannot find one.
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: takeoff publish <job> [--model-folder <folder>] [--dxf-folder <folder>] [--reference <f.e2k>] [--rules-db <c>] [--infer-floors] [--stick-file <f.pdf>] [--annotated-dxf <folder>] [--stage <folder>] [--drop-storeys <a,b,c>] [--tower <tag>] [--top-storey <name>] [--variant <name>] [--per-building] [--skip-dossier] [--land]");
            return 1;
        }

        int firstOption = Array.FindIndex(args, 1, a => a.StartsWith("--", StringComparison.Ordinal));
        if (firstOption < 0) firstOption = args.Length;

        string? pubModelFolder = null, pubDxfFolder = null;
        string pubProject;
        int optionStart;
        if (firstOption >= 4)
        {
            // Backwards-compatible form kept for local scripts:
            // takeoff publish <modelFolder> <dxfFolder> <job>
            pubModelFolder = args[1];
            pubDxfFolder = args[2];
            pubProject = args[3];
            optionStart = 4;
        }
        else
        {
            pubProject = args[1];
            optionStart = 2;
        }

        string? pubReference = null, pubRules = null, pubStick = null, pubAnnotated = null;
        string? pubStage = null, pubTopStorey = null, pubTower = null, pubVariant = null;
        string? pubProjectsRoot = null, pubRepoRoot = null, pubRenderer = null, pubPdfInfo = null;
        bool pubInfer = false, pubLand = false, pubPerBuilding = false, pubSkipDossier = false;
        var pubDrop = new List<string>();

        for (int i = optionStart; i < args.Length; i++)
        {
            if (args[i].Equals("--reference", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubReference = args[++i];
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubRules = args[++i];
            else if (args[i].Equals("--stage", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubStage = args[++i];
            else if (args[i].Equals("--model-folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubModelFolder = args[++i];
            else if (args[i].Equals("--dxf-folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubDxfFolder = args[++i];
            else if (args[i].Equals("--projects-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubProjectsRoot = args[++i];
            else if (args[i].Equals("--repo-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubRepoRoot = args[++i];
            else if (args[i].Equals("--renderer", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubRenderer = args[++i];
            else if (args[i].Equals("--pdfinfo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubPdfInfo = args[++i];
            else if (args[i].Equals("--infer-floors", StringComparison.OrdinalIgnoreCase)) pubInfer = true;
            else if (args[i].Equals("--stick-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubStick = args[++i];
            else if (args[i].Equals("--annotated-dxf", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubAnnotated = args[++i];
            else if (args[i].Equals("--top-storey", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubTopStorey = args[++i];
            else if (args[i].Equals("--tower", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubTower = args[++i];
            else if (args[i].Equals("--variant", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pubVariant = args[++i];
            else if (args[i].Equals("--drop-storeys", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                pubDrop = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            else if (args[i].Equals("--per-building", StringComparison.OrdinalIgnoreCase)) pubPerBuilding = true;
            else if (args[i].Equals("--skip-dossier", StringComparison.OrdinalIgnoreCase)) pubSkipDossier = true;
            else if (args[i].Equals("--land", StringComparison.OrdinalIgnoreCase)) pubLand = true;
            else { Console.Error.WriteLine($"Unknown argument '{args[i]}'."); return 1; }
        }

        pubRules ??= Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");

        var outcome = JobPublisher.Run(new JobPublisher.Request
        {
            Project = pubProject,
            ModelFolder = pubModelFolder,
            DxfFolder = pubDxfFolder,
            ProjectsRoot = pubProjectsRoot ?? PublishDiscovery.DefaultProjectsRoot,
            RepoRoot = pubRepoRoot,
            Reference = pubReference,
            RuleSettingsConnection = pubRules,
            StageFolder = pubStage,
            InferFloors = pubInfer,
            StickFilePdf = pubStick,
            AnnotatedDxfFolder = pubAnnotated,
            TopStorey = pubTopStorey,
            Tower = pubTower,
            Variant = pubVariant,
            PerBuilding = pubPerBuilding,
            SkipDossier = pubSkipDossier,
            Land = pubLand,
            RendererScript = pubRenderer,
            PdfInfoExe = pubPdfInfo,
            DropStoreys = pubDrop,
        });

        if (outcome.Refused is not null && outcome.Models.Count == 0)
        {
            Console.Error.WriteLine($"REFUSED - {outcome.Refused}");
            return 3;
        }

        Console.WriteLine($"model folder : {outcome.ModelFolder}");
        Console.WriteLine($"drawings     : {outcome.DxfFolder}");
        Console.WriteLine($"reference    : {outcome.Reference}");
        Console.WriteLine($"stage        : {outcome.StageFolder}");
        Console.WriteLine($"buildings : {outcome.Models.Count}");
        Console.WriteLine();

        foreach (var one in outcome.Models)
        {
            Console.WriteLine($"{one.Label}");
            Console.WriteLine($"  storeys {one.Storeys}   walls {one.Walls}   columns {one.Columns}   floors {one.Floors}");
            if (one.SummaryPdfPath is not null)
            {
                // Say what the one-page rule cost, because what it drops is dropped from the note an
                // engineer reads. Silent on a clean run; nobody needs "trimmed 0".
                string trimmed = one.SummaryFindingsTrimmed > 0
                    ? $" ({one.SummaryFindingsShown} finding(s) shown, {one.SummaryFindingsTrimmed} trimmed to hold one page)"
                    : string.Empty;
                Console.WriteLine($"  summary: {Path.GetFileName(one.SummaryPdfPath)}{trimmed}");
            }

            if (one.Passed)
            {
                if (one.AdvisoryViolations.Count == 0)
                {
                    Console.WriteLine("  verify-e2k: passes every publish-blocking invariant.");
                    continue;
                }

                Console.WriteLine($"  verify-e2k: passes every publish-blocking invariant; reports {one.AdvisoryViolations.Count} advisory check(s).");
                foreach (var group in one.AdvisoryViolations.GroupBy(v => v.Rule))
                {
                    Console.WriteLine($"    [advisory:{group.Key}] {group.Count()}");
                    foreach (var breach in group.Take(3)) Console.WriteLine($"        {breach.What}");
                }
                continue;
            }

            Console.WriteLine($"  verify-e2k: FAILS {one.BlockingViolations.Count} publish-blocking check(s) - NOT landed.");
            foreach (var group in one.BlockingViolations.GroupBy(v => v.Rule))
            {
                Console.WriteLine($"    [{group.Key}] {group.Count()}");
                foreach (var breach in group.Take(3)) Console.WriteLine($"        {breach.What}");
            }
            if (one.AdvisoryViolations.Count > 0)
            {
                Console.WriteLine($"    advisory check(s) also reported: {one.AdvisoryViolations.Count}");
                foreach (var group in one.AdvisoryViolations.GroupBy(v => v.Rule))
                {
                    Console.WriteLine($"    [advisory:{group.Key}] {group.Count()}");
                    foreach (var breach in group.Take(3)) Console.WriteLine($"        {breach.What}");
                }
            }
        }

        foreach (string warning in outcome.Warnings)
        {
            Console.WriteLine();
            Console.WriteLine(warning);
        }

        if (outcome.Refused is not null)
        {
            Console.WriteLine();
            Console.Error.WriteLine($"REFUSED - {outcome.Refused}");
        }

        if (!pubLand) { Console.WriteLine(); Console.WriteLine("staged only; pass --land to copy what passed into the job folder."); }
        else
        {
            Console.WriteLine();
            foreach (string file in outcome.Landed) Console.WriteLine($"  landed {file}");
            foreach (string file in outcome.Withdrawn) Console.WriteLine($"  withdrew {file}");
        }

        return outcome.Refused is null && outcome.Models.All(m => m.Passed) ? 0 : 3;
    }
}
