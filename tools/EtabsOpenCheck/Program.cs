// EtabsOpenCheck — OUR .e2k opened in real ETABS, the way the engineer opens it, and what ETABS says about it read back
// (2026-10-28, Ian's goal via the Virtual Drafter: "Jim opens our .e2k in real ETABS, and nothing in our pipeline ever
// has"). Runs on KOR-210 in the logged-on console session (ETABS 23's licence there is Ian's Sign-in seat, held by that
// user, and its windows need a desktop to be pictured), started as a one-off interactive scheduled task.
//
//     EtabsOpenCheck <model.e2k> <outDir> [--etabs "C:\Program Files\Computers and Structures\ETABS 23\ETABS.exe"]
//
// It copies the .e2k into <outDir> and opens THE COPY (the deliverable is never written), then writes <outDir>\report.txt:
//   1. the import: File.ImportFile(TextFile) - the API returns a code and no log (ETABS 22.6 and 23.3 alike: the same
//      ImportFile(FileName, FileType, Type) in both, read off each ETABSv1.dll on KOR-210, 2026-10-28),
//      so every file ETABS writes into <outDir> during the import is listed, and any text one copied into the report
//      verbatim; and a picture of the screen is taken if the import has not returned in 60 s (a dialog waiting for a click)
//   2. ETABS's own counts against the .e2k's: storeys and their elevations (ft), frames by section, areas by property,
//      openings - every difference listed
//   3. Analyze.CreateAnalysisModel's return code - the API's nearest thing to Check Model, which it does not expose
//   4. ETABS's window as ETABS draws it after the import, as PNG (window-*.png) - the look at the deliverable as the
//      engineer sees it
//   5. the model saved as <outDir>\etabs-check.EDB (+ ETABS's own text twin .$et) - evidence beside, never the package
//   6. with --gravity [--expect-kip N]: self-weight only, run twice on the copy - AS DELIVERED, then PINNED (UX UY UZ) at
//      every Base point, a test assumption - each saved as gravity-<tag>.EDB; for each: RunAnalysis's code, ETABS's
//      analysis .LOG verbatim (instability, lost digits), the base reaction FZ against N (KOR.Drafter e2k_selfweight.py),
//      and each storey's largest vertical (and horizontal) movement. COVERS: does it stand under its own weight, does the
//      weight add up. DOES NOT: loads beyond self-weight, lateral, P-delta, cracked stiffness, design, springs.
// ETABS is closed at the end without saving anything else. Only the ETABS this tool started is ever closed or ended.
//
// COVERS: does it import (return code, what ETABS wrote, any dialog on screen); does ETABS hold the same storeys,
// elevations, frames per section, areas per property and openings as the file; does the analysis model build.
// DOES NOT: Analyze > Check Model's own list (not in the API); per-storey plan views (the API cannot change the view, so
// the picture is whatever ETABS shows on opening); loads, materials' values, section dimensions, design.
// SAME-CLASS FAULT IT MISSES: an object ETABS imports at a different PLACE but with the same section and storey - counts
// agree. ETABS's own text twin (.$et) is kept so that comparison can be made against the .e2k.
//
// ⚠ A MANUAL PRE-DELIVERY CHECK, NOT A BUILD GATE: it needs Ian's signed-in console session on KOR-210 (his ETABS Sign-in
// seat; SYSTEM has neither the licence nor a desktop), so it runs when Ian says so, before a model goes to an engineer.
// HISTORY: our output was opened in ETABS once before, by hand - 24 Aug, ETABS 22.6.0 on KOR-210, Analyze > Check Model run
// twice (8 warnings, then 3 benign slab-edge mesh slivers; docs/KOR-DxfToEtabs-Handoff-2026-08-24.md). Check Model's list
// is still read that way, by hand. WHICH ETABS: the engineers' models are in both - of their .EDBs exported in Sept, 92
// opened in ETABS 22.6 and 9 more needed ETABS 23 (the newer jobs; docs/pdf-intake/part-2-s31-s60.md) - so a model is
// checked in both when it matters; a newer importer accepting a file does not prove the older one will (--etabs). The
// same binary runs on both: the 14 API methods it calls have the same signatures in 22.6 (API 2.8) and 23.3 (API 2.16).
// Halifax 31180 v3, 28 Oct, both versions: import 0, 0 count differences, analysis model 0, and each one's re-saved text
// model holds 433 of 433 column-storeys at the e2k's storey, place and section.
//
// REUSED (Operations tools/EtabsExportE2k/Program.cs, 2026-09-11 on KOR-210): start a fresh ETABS from the named exe
// through cHelper.CreateObject, never attach to one a person has open; resolve ETABSv1.dll from that ETABS's folder,
// never ship it (ETABS refuses a client carrying its own copy); open a copy; ApplicationExit(false). From
// Kor.Operations.App EtabsApiExporter.cs: the ETABS install folder convention. CsiComRegistration is not needed -
// cHelper starts ETABS from its exe without the COM ProgID.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ETABSv1;

namespace Kor.EtabsOpenCheck;

internal static class Program
{
    private const string DefaultEtabs = @"C:\Program Files\Computers and Structures\ETABS 23\ETABS.exe";
    private static readonly List<string> Report = new();
    private static string _out = ".";

    private static void Say(string line) { Console.WriteLine(line); Report.Add(line); }

    private static int Main(string[] args)
    {
        string etabsExe = DefaultEtabs;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals("--etabs", StringComparison.OrdinalIgnoreCase)) etabsExe = args[i + 1];
        if (!File.Exists(etabsExe)) { Console.Error.WriteLine($"ETABS not found at {etabsExe} (pass --etabs <ETABS.exe>); this runs on KOR-210."); return 3; }
        string etabsDir = Path.GetDirectoryName(Path.GetFullPath(etabsExe)) ?? ".";
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
            string.Equals(name.Name, "ETABSv1", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(etabsDir, "ETABSv1.dll"))
                ? context.LoadFromAssemblyPath(Path.Combine(etabsDir, "ETABSv1.dll")) : null;
        int code;
        try { code = Run(args, etabsExe); }
        catch (Exception ex) { Say($"STOPPED: {ex.GetType().Name}: {ex.Message}"); code = 4; }
        try { File.WriteAllLines(Path.Combine(_out, "report.txt"), Report, new UTF8Encoding(true)); } catch (IOException) { }
        return code;
    }

    private static int Run(string[] args, string etabsExe)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: EtabsOpenCheck <model.e2k> <outDir> [--etabs <ETABS.exe>]"); return 1; }
        string source = Path.GetFullPath(args[0]);
        _out = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(_out);
        string copy = Path.Combine(_out, "etabs-check.e2k");
        File.Copy(source, copy, overwrite: true);
        Say($"source  : {source} ({new FileInfo(source).Length:N0} bytes, SHA-256 {Sha(source)})");
        Say($"opened  : {copy} (a copy; the source is never written)");
        Say($"ETABS   : {etabsExe} [{FileVersionInfo.GetVersionInfo(etabsExe).ProductVersion}]");
        Say($"as      : {Environment.UserDomainName}\\{Environment.UserName}, session {Process.GetCurrentProcess().SessionId}");
        var want = E2k.Read(copy);

        var before = Process.GetProcessesByName("ETABS").Select(p => p.Id).ToHashSet();
        cHelper helper = new Helper();
        cOAPI etabs = helper.CreateObject(etabsExe) ?? throw new InvalidOperationException("cHelper.CreateObject gave nothing back");
        int ret = etabs.ApplicationStart();
        Say($"ApplicationStart: {ret}{(ret != 0 ? " (licence? nothing else was tried)" : "")}");
        if (ret != 0) return 3;
        Process? mine = null;
        for (int i = 0; i < 60 && mine is null; i++)
        {
            mine = Process.GetProcessesByName("ETABS").Where(p => !before.Contains(p.Id)).OrderBy(p => p.StartTime).FirstOrDefault();
            if (mine is null) Thread.Sleep(500);
        }
        Say($"our ETABS: pid {mine?.Id.ToString(CultureInfo.InvariantCulture) ?? "not found"}; already running and left alone: {string.Join(", ", before)}");
        int exit = 0;
        try
        {
            cSapModel model = etabs.SapModel;

            // --read-results <EDB> <case>: an EDB already ANALYSED (by hand in the GUI - the API's RunAnalysis would not start
            // the solver, 2026-10-28), opened and its results read; nothing is run, ETABS exits without saving
            int rr = Array.FindIndex(args, x => x.Equals("--read-results", StringComparison.OrdinalIgnoreCase));
            if (rr >= 0 && rr + 2 < args.Length)
            {
                string analysed = Path.GetFullPath(args[rr + 1]), rcase = args[rr + 2];
                double expect = args.SkipWhile(x => !x.Equals("--expect-kip", StringComparison.OrdinalIgnoreCase)).Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).FirstOrDefault();
                Say($"R. OpenFile({analysed}): {model.File.OpenFile(analysed)}");
                model.SetPresentUnits(eUnits.lb_ft_F);
                int nc = 0; string[] cn = Array.Empty<string>(); int[] cs = Array.Empty<int>();
                model.Analyze.GetCaseStatus(ref nc, ref cn, ref cs);
                Say($"   case status (4 finished): {string.Join(", ", Enumerable.Range(0, nc).Select(i => $"{cn[i]}={cs[i]}"))}");
                ReadResults(model, rcase, expect);
                return 0;
            }

            // 1. THE IMPORT - watched: a dialog waiting for a click never returns, and the picture shows which one
            var startFiles = Directory.GetFiles(_out).ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));
            var importDone = new ManualResetEventSlim();
            var watcher = new Thread(() => { if (!importDone.Wait(TimeSpan.FromSeconds(60))) Capture(mine, "import-stuck-60s"); }) { IsBackground = true };
            watcher.Start();
            var sw = Stopwatch.StartNew();
            ret = model.File.ImportFile(copy, eFileTypeIO.TextFile, 1);
            importDone.Set();
            Say($"1. ImportFile(TextFile): {ret} in {sw.Elapsed.TotalSeconds:F1} s");
            if (ret != 0)
            {
                int open = model.File.OpenFile(copy);
                Say($"   OpenFile instead: {open}");
                if (open != 0) { Say("   NOT IMPORTED - nothing further is read"); Capture(mine, "import-failed"); return 2; }
            }
            foreach (var f in Directory.GetFiles(_out).Where(f => !startFiles.TryGetValue(f, out var t) || File.GetLastWriteTimeUtc(f) != t).OrderBy(f => f))
            {
                var fi = new FileInfo(f);
                Say($"   ETABS wrote: {fi.Name} ({fi.Length:N0} bytes)");
                if (fi.Length < 512 * 1024 && Regex.IsMatch(fi.Extension, @"^\.(log|txt|\$.*|mon|msg|out|err|wrn|lst)$", RegexOptions.IgnoreCase))
                    foreach (var l in File.ReadAllLines(f)) Say($"   | {l}");
            }

            // 2. ETABS'S OWN COUNTS against the file's
            model.SetPresentUnits(eUnits.lb_ft_F);
            int diffs = 0;
            double baseElev = 0; int nStory = 0;
            string[] sn = Array.Empty<string>(), similar = Array.Empty<string>();
            double[] se = Array.Empty<double>(), sh = Array.Empty<double>(), splice = Array.Empty<double>();
            bool[] master = Array.Empty<bool>(), spliceAbove = Array.Empty<bool>(); int[] color = Array.Empty<int>();
            model.Story.GetStories_2(ref baseElev, ref nStory, ref sn, ref se, ref sh, ref master, ref similar, ref spliceAbove, ref splice, ref color);
            var got = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nStory; i++) got[sn[i]] = se[i];
            Say($"2. storeys: ETABS {nStory} above a base at {baseElev:F2} ft; the file {want.Storeys.Count}");
            foreach (var (name, elev) in want.Storeys)
            {
                if (!got.TryGetValue(name, out var e)) { Say($"   MISSING in ETABS: storey {name} ({elev:F2} ft)"); diffs++; }
                else if (Math.Abs(e - elev) > 0.005) { Say($"   ELEVATION {name}: file {elev:F3} ft, ETABS {e:F3} ft"); diffs++; }
            }
            foreach (var name in got.Keys.Where(k => !want.Storeys.Any(s => s.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))) { Say($"   EXTRA in ETABS: storey {name}"); diffs++; }

            int nf = 0; string[] fn = Array.Empty<string>(), fp = Array.Empty<string>(), fs = Array.Empty<string>(), p1 = Array.Empty<string>(), p2 = Array.Empty<string>();
            double[] x1 = Array.Empty<double>(), y1 = Array.Empty<double>(), z1 = Array.Empty<double>(), x2 = Array.Empty<double>(), y2 = Array.Empty<double>(), z2 = Array.Empty<double>(),
                ang = Array.Empty<double>(), o1x = Array.Empty<double>(), o2x = Array.Empty<double>(), o1y = Array.Empty<double>(), o2y = Array.Empty<double>(), o1z = Array.Empty<double>(), o2z = Array.Empty<double>();
            int[] card = Array.Empty<int>();
            model.FrameObj.GetAllFrames(ref nf, ref fn, ref fp, ref fs, ref p1, ref p2, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2, ref ang, ref o1x, ref o2x, ref o1y, ref o2y, ref o1z, ref o2z, ref card);
            diffs += Compare("frames by section", want.FramesBySection, Enumerable.Range(0, nf).GroupBy(i => fp[i]).ToDictionary(g => g.Key, g => g.Count()));

            int na = 0; string[] an = Array.Empty<string>();
            model.AreaObj.GetNameList(ref na, ref an);
            var byProp = new Dictionary<string, int>(); int openings = 0;
            foreach (var a in an)
            {
                bool isOpening = false; model.AreaObj.GetOpening(a, ref isOpening);
                if (isOpening) { openings++; continue; }
                string prop = ""; model.AreaObj.GetProperty(a, ref prop);
                byProp[prop] = byProp.GetValueOrDefault(prop) + 1;
            }
            diffs += Compare("areas by property", want.AreasByProperty, byProp);
            Say($"   openings: file {want.Openings}, ETABS {openings}{(want.Openings != openings ? "  DIFFERENT" : "")}");
            if (want.Openings != openings) diffs++;
            Say($"   counts: {diffs} difference(s)");

            // 4. THE LOOK - ETABS's window as it draws the imported model
            model.View.RefreshView(0, false);
            Thread.Sleep(3000);
            Capture(mine, "window-after-import");

            // 3. THE ANALYSIS MODEL - the API's nearest thing to Check Model
            sw.Restart();
            int am = model.Analyze.CreateAnalysisModel();
            Say($"3. CreateAnalysisModel: {am} in {sw.Elapsed.TotalSeconds:F1} s (0 = built)");
            Capture(mine, "window-after-analysis-model");

            // 5. evidence beside, never the package
            int saved = model.File.Save(Path.Combine(_out, "etabs-check.EDB"));
            Say($"5. saved as {Path.Combine(_out, "etabs-check.EDB")}: {saved}");
            exit = ret == 0 && diffs == 0 && am == 0 ? 0 : 2;

            // 6. --gravity: self-weight only, on THIS copy, twice - as delivered, then pinned at Base (a test assumption)
            if (args.Any(x => x.Equals("--gravity", StringComparison.OrdinalIgnoreCase)))
            {
                double expect = args.SkipWhile(x => !x.Equals("--expect-kip", StringComparison.OrdinalIgnoreCase)).Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).FirstOrDefault();
                Gravity(model, "as-delivered", pin: false, expect);
                Gravity(model, "pinned-at-base", pin: true, expect);
            }
        }
        finally
        {
            try { etabs.ApplicationExit(false); } catch (COMException) { }
            if (mine is not null && !mine.WaitForExit(30_000)) { Say($"our ETABS (pid {mine.Id}) did not close in 30 s - ended"); mine.Kill(); }
            Say($"ETABS closed; still running and untouched: {string.Join(", ", Process.GetProcessesByName("ETABS").Select(p => p.Id))}");
        }
        Say(exit == 0 ? "VERDICT: imports, every count agrees, the analysis model builds" : "VERDICT: see the lines above");
        return exit;
    }

    // A linear static case of the file's own "Dead" pattern (SELFWEIGHT 1 - the e2k declares no case, and ETABS adds none
    // on import), run alone; the analysis log verbatim, the total base reaction against the hand-worked self-weight, and the
    // largest vertical movement on each storey. pin: UX UY UZ at every point on the Base storey - the firm's commonest
    // support (5,480 of 6,173 restrained points in the 59 engineers' models we hold) - a TEST assumption on this copy only.
    private static void Gravity(cSapModel model, string tag, bool pin, double expectKip)
    {
        Say($"6. GRAVITY ({tag}{(pin ? ": UX UY UZ pinned at every Base point - a test assumption, this copy only" : ": supports exactly as the e2k has them")})");
        model.SetModelIsLocked(false);
        if (pin)
        {
            int n = 0; string[] names = Array.Empty<string>();
            model.PointObj.GetNameListOnStory("Base", ref n, ref names);
            var fix = new[] { true, true, true, false, false, false };
            int set = names.Count(p => { var v = (bool[])fix.Clone(); return model.PointObj.SetRestraint(p, ref v) == 0; });
            Say($"   pinned {set} of {n} point(s) on Base");
        }
        // EVERY STEP SAYS ITS CODE (2026-10-28, first run: RunAnalysis 0 in 3-5 s, no log, no results - and nothing said
        // which step had not taken). The patterns and cases as ETABS holds them, before and after.
        int npat = 0; string[] pats = Array.Empty<string>();
        model.LoadPatterns.GetNameList(ref npat, ref pats);
        Say($"   load patterns: {string.Join(", ", pats.Select(p => { double m = 0; model.LoadPatterns.GetSelfWTMultiplier(p, ref m); return $"{p} (self-weight x{m:g})"; }))}");
        Say($"   cases before: {Cases(model)}");
        Say($"   StaticLinear.SetCase(KOR-Dead): {model.LoadCases.StaticLinear.SetCase("KOR-Dead")}");
        string[] lt = { "Load" }, ln = { pats.FirstOrDefault(p => p.Equals("Dead", StringComparison.OrdinalIgnoreCase)) ?? "Dead" }; double[] sf = { 1.0 };
        Say($"   StaticLinear.SetLoads(KOR-Dead, {ln[0]} x1): {model.LoadCases.StaticLinear.SetLoads("KOR-Dead", 1, ref lt, ref ln, ref sf)}");
        string edb = Path.Combine(_out, $"gravity-{tag}.EDB");
        Say($"   saved {Path.GetFileName(edb)}: {model.File.Save(edb)}");
        Say($"   SetRunCaseFlag(all, off): {model.Analyze.SetRunCaseFlag("", false, true)}; SetRunCaseFlag(KOR-Dead, on): {model.Analyze.SetRunCaseFlag("KOR-Dead", true, false)}");
        Say($"   cases to run: {Cases(model)}");
        // THE SOLVER, said and run in-process (2026-10-28 rerun: case set and flagged, RunAnalysis 0 in 3-5 s, status 1 "not
        // run", no file written - the solver never started, and nothing said why). SolverProcessType 1 = inside ETABS's own
        // process ("GUI process"); every other solver setting is kept as ETABS has it. The screen is pictured every 2 s
        // during the run and once after, so a message box ETABS raises is caught.
        int stype = 0, ptype = 0, par = 0, fsize = 0, threads = 0; string stiff = "";
        model.Analyze.GetSolverOption_3(ref stype, ref ptype, ref par, ref fsize, ref threads, ref stiff);
        Say($"   solver as ETABS has it: type {stype}, process type {ptype} (0 auto, 1 GUI process, 2 separate), parallel runs {par}, response file max {fsize} MB, threads {threads}, stiffness case '{stiff}'");
        Say($"   SetSolverOption_3(process type 1): {model.Analyze.SetSolverOption_3(stype, 1, par, fsize, threads, stiff)}");
        var before = DateTime.Now.AddSeconds(-2);
        var running = new CancellationTokenSource(); int shot = 0;
        var shots = new Thread(() => { while (!running.IsCancellationRequested && shot < 30) { Thread.Sleep(2000); if (!running.IsCancellationRequested) CaptureScreen($"run-{tag}-{++shot:D2}"); } }) { IsBackground = true };
        shots.Start();
        var sw = Stopwatch.StartNew();
        int run = model.Analyze.RunAnalysis();
        running.Cancel();
        Say($"   RunAnalysis: {run} in {sw.Elapsed.TotalSeconds:F1} s; {shot} picture(s) taken during it");
        CaptureScreen($"run-{tag}-after");
        int nc = 0; string[] cn = Array.Empty<string>(); int[] cs = Array.Empty<int>();
        model.Analyze.GetCaseStatus(ref nc, ref cn, ref cs);
        Say($"   case status (1 not run, 2 could not start, 3 not finished, 4 finished): {string.Join(", ", Enumerable.Range(0, nc).Select(i => $"{cn[i]}={cs[i]}"))}");
        var written = Directory.GetFiles(_out, "*", SearchOption.AllDirectories).Where(f => File.GetLastWriteTime(f) >= before).OrderBy(f => f).ToList();
        Say($"   ETABS wrote during the run: {string.Join(", ", written.Select(f => Path.GetRelativePath(_out, f)))}");
        foreach (var log in written.Where(f => f.EndsWith(".LOG", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(log);
            Say($"   analysis log {Path.GetRelativePath(_out, log)}, {lines.Length} line(s), verbatim:");
            foreach (var l in lines) Say($"   | {l}");
        }
        if (run != 0 || !Enumerable.Range(0, nc).Any(i => cn[i] == "KOR-Dead" && cs[i] == 4)) { Say("   KOR-Dead did not finish - no results read"); return; }
        ReadResults(model, "KOR-Dead", expectKip);
    }

    // The results of one finished case: the total base reaction against the hand-worked self-weight, and each storey's
    // largest vertical and horizontal movement (inches). Reads only.
    private static void ReadResults(cSapModel model, string caseName, double expectKip)
    {
        model.Results.Setup.DeselectAllCasesAndCombosForOutput();
        Say($"   SetCaseSelectedForOutput({caseName}): {model.Results.Setup.SetCaseSelectedForOutput(caseName)}");
        int nr = 0; string[] lc = Array.Empty<string>(), st = Array.Empty<string>(); double[] sn = Array.Empty<double>(), fx = Array.Empty<double>(), fy = Array.Empty<double>(), fz = Array.Empty<double>(), mx = Array.Empty<double>(), my = Array.Empty<double>(), mz = Array.Empty<double>();
        double gx = 0, gy = 0, gz = 0;
        model.Results.BaseReact(ref nr, ref lc, ref st, ref sn, ref fx, ref fy, ref fz, ref mx, ref my, ref mz, ref gx, ref gy, ref gz);
        if (nr > 0)
        {
            double kip = fz[0] / 1000;
            Say($"   base reaction FZ {kip:N1} kip (FX {fx[0] / 1000:N1}, FY {fy[0] / 1000:N1}); hand-worked self-weight {(expectKip > 0 ? $"{expectKip:N1} kip -> {(kip - expectKip) / expectKip:P2}" : "not given (--expect-kip)")}");
        }
        else Say("   no base reaction returned");

        int nd = 0; string[] obj = Array.Empty<string>(), elm = Array.Empty<string>(), dlc = Array.Empty<string>(), dst = Array.Empty<string>();
        double[] dsn = Array.Empty<double>(), u1 = Array.Empty<double>(), u2 = Array.Empty<double>(), u3 = Array.Empty<double>(), r1 = Array.Empty<double>(), r2 = Array.Empty<double>(), r3 = Array.Empty<double>();
        model.Results.JointDispl("All", eItemTypeElm.GroupElm, ref nd, ref obj, ref elm, ref dlc, ref dst, ref dsn, ref u1, ref u2, ref u3, ref r1, ref r2, ref r3);
        var storeyOf = new Dictionary<string, string>();
        int ns = 0; string[] stories = Array.Empty<string>();
        model.Story.GetNameList(ref ns, ref stories);
        foreach (var s in stories.Append("Base"))
        {
            int np = 0; string[] pts = Array.Empty<string>();
            model.PointObj.GetNameListOnStory(s, ref np, ref pts);
            foreach (var p in pts) storeyOf[p] = s;
        }
        Say($"   joint displacements: {nd} result(s); largest |UZ| (in) and |UX|,|UY| (in) per storey:");
        foreach (var g in Enumerable.Range(0, nd).GroupBy(i => storeyOf.GetValueOrDefault(obj[i], "?")).OrderByDescending(g => g.Key == "Base" ? -1 : Array.IndexOf(stories, g.Key)))
        {
            int w = g.OrderByDescending(i => Math.Abs(u3[i])).First();
            Say($"     {g.Key,-18} UZ {u3[w] * 12,9:F3} at {obj[w],-8} max |UX| {g.Max(i => Math.Abs(u1[i])) * 12:F3} |UY| {g.Max(i => Math.Abs(u2[i])) * 12:F3}");
        }
    }

    private static string Cases(cSapModel model)
    {
        int n = 0; string[] names = Array.Empty<string>(); bool[] run = Array.Empty<bool>();
        model.Analyze.GetRunCaseFlag(ref n, ref names, ref run);
        return n == 0 ? "(none)" : string.Join(", ", Enumerable.Range(0, n).Select(i =>
        {
            eLoadCaseType type = default; int sub = 0; model.LoadCases.GetTypeOAPI(names[i], ref type, ref sub);
            return $"{names[i]} [{type}] {(run[i] ? "RUN" : "off")}";
        }));
    }

    private static int Compare(string what, Dictionary<string, int> file, Dictionary<string, int> etabs)
    {
        int d = 0;
        Say($"   {what}: file {file.Values.Sum()} in {file.Count} kind(s), ETABS {etabs.Values.Sum()} in {etabs.Count}");
        foreach (var k in file.Keys.Union(etabs.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            int a = file.GetValueOrDefault(k), b = etabs.GetValueOrDefault(k);
            if (a != b) { Say($"     {k}: file {a}, ETABS {b}"); d++; }
        }
        return d;
    }

    // The window, drawn by Windows as it is on the desktop (PrintWindow, full content); the whole screen if there is no
    // window yet - a dialog ETABS raised is then in the picture
    private static void Capture(Process? p, string name)
    {
        try
        {
            string path = Path.Combine(_out, name + ".png");
            p?.Refresh();
            IntPtr h = p?.MainWindowHandle ?? IntPtr.Zero;
            if (h != IntPtr.Zero && GetWindowRect(h, out var r) && r.Right > r.Left && r.Bottom > r.Top)
            {
                using var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
                bmp.Save(path, ImageFormat.Png);
            }
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using (var all = new Bitmap(screen.Width, screen.Height))
            {
                using (var g = Graphics.FromImage(all)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                all.Save(Path.Combine(_out, name + "-screen.png"), ImageFormat.Png);
            }
            Say($"   picture: {name}.png{(h == IntPtr.Zero ? " (no window handle - screen only)" : "")} + {name}-screen.png");
        }
        catch (Exception ex) { Say($"   picture {name} not taken: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void CaptureScreen(string name)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var all = new Bitmap(screen.Width, screen.Height);
            using (var g = Graphics.FromImage(all)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
            all.Save(Path.Combine(_out, name + ".png"), ImageFormat.Png);
        }
        catch (Exception ex) { Say($"   picture {name} not taken: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string Sha(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s))[..16];
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
}

/// <summary>What the .e2k says: storeys with elevations (ft, from the base up), frames by section, areas by property,
/// openings - each assignment one object, as ETABS makes them.</summary>
internal sealed record E2k(List<(string Name, double Elevation)> Storeys, Dictionary<string, int> FramesBySection,
    Dictionary<string, int> AreasByProperty, int Openings)
{
    public static E2k Read(string path)
    {
        string t = File.ReadAllText(path);
        var unit = Regex.Match(t, @"^\s*UNITS\s+""[^""]*""\s+""([^""]+)""", RegexOptions.Multiline).Groups[1].Value.ToUpperInvariant();
        double toFt = unit switch { "FT" or "FEET" => 1, "IN" or "INCHES" => 1 / 12.0, "M" => 1 / 0.3048, "MM" => 1 / 304.8, _ => throw new InvalidOperationException($"length unit '{unit}' not known") };
        // STORY lines run from the top down; the last has ELEV (the base), the others HEIGHT
        var lines = Regex.Matches(t, @"^\s*STORY\s+""([^""]+)""\s+(HEIGHT|ELEV)\s+(-?[\d.]+)", RegexOptions.Multiline).Cast<Match>().ToList();
        double elev = lines.LastOrDefault(m => m.Groups[2].Value == "ELEV") is { } b ? double.Parse(b.Groups[3].Value, CultureInfo.InvariantCulture) * toFt : 0;
        var storeys = new List<(string, double)>();
        foreach (var m in Enumerable.Reverse(lines).Where(m => m.Groups[2].Value == "HEIGHT"))
        {
            elev += double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * toFt;
            storeys.Add((m.Groups[1].Value, elev));
        }
        var frames = Regex.Matches(t, @"^\s*LINEASSIGN\s+""[^""]+""\s+""[^""]+""\s+SECTION\s+""([^""]+)""", RegexOptions.Multiline)
            .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.Count());
        var areas = new Dictionary<string, int>(); int openings = 0;
        foreach (Match m in Regex.Matches(t, @"^\s*AREAASSIGN\s+""[^""]+""\s+""[^""]+""\s+(SECTION\s+""([^""]+)""|OPENING\s+""Yes"")", RegexOptions.Multiline))
        {
            if (m.Groups[2].Success) areas[m.Groups[2].Value] = areas.GetValueOrDefault(m.Groups[2].Value) + 1;
            else openings++;
        }
        return new E2k(storeys, frames, areas, openings);
    }
}
