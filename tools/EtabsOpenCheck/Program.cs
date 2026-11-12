// EtabsOpenCheck — OUR .e2k opened in real ETABS, the way the engineer opens it, and what ETABS says about it read back
// (2026-10-28, Ian's goal via the Virtual Drafter: "Jim opens our .e2k in real ETABS, and nothing in our pipeline ever
// has"). Runs on KOR-210 in the logged-on console session (ETABS 23's licence there is Ian's Sign-in seat, held by that
// user, and its windows need a desktop to be pictured), started as a one-off interactive scheduled task.
//
//     EtabsOpenCheck <model.e2k> <outDir> [--etabs "C:\Program Files\Computers and Structures\ETABS 23\ETABS.exe"]
//                    [--start-timeout-s 120]
//
// START WATCH (KOR.Drafter row 2104, 12 Nov): ApplicationStart not returned within --start-timeout-s (default 120) ->
//   'APPLICATION DID NOT START', the windows of every process started since named, start-stuck-<n>s-screen.png, an ETABS
//   that is this process's CHILD ended (none = nothing ended, said), exit 5. StartWatch.cs is a copy of KOR.Drafter's
//   tested Core/Csi/CsiStartWatch.cs. UNPROVEN LIVE: no licence box was planted; the first staged start proves it.
// PRE-CHECK START GATE (KOR.Drafter row 2103 step B, Ian 12 Nov #133436 M1): --precheck-takeoff <takeoff.exe> [--pin-storey P]
//   runs `takeoff precheck model` on the model ETABS opens (--precheck-e2k may be left out; given, it must be the same
//   bytes) before ETABS starts; anything but its exit 0, or neither that nor --no-precheck "<why>" given, = PRECHECK
//   REFUSED, nothing started, exit 6. PrecheckStartGate.cs is a copy of KOR.Drafter's tested Core/Csi/PrecheckStartGate.cs.
//
// It copies the .e2k into <outDir> and opens THE COPY (the deliverable is never written), then writes <outDir>\report.txt:
//   1. the import: File.ImportFile(TextFile) - the API returns a code and no log (ETABS 22.6 and 23.3 alike: the same
//      ImportFile(FileName, FileType, Type) in both, read off each ETABSv1.dll on KOR-210, 2026-10-28),
//      so every file ETABS writes into <outDir> during the import is listed, and any text one copied into the report
//      verbatim; and a picture of the screen is taken if the import has not returned in 60 s (a dialog waiting for a click)
//   2. ETABS's own counts against the .e2k's: storeys and their elevations (ft), frames by section, areas by property,
//      openings, and restrained points by storey and DOF set (POINTASSIGN ... RESTRAINT; since 28 Oct) - every
//      difference listed
//   3. Analyze.CreateAnalysisModel's return code - the API's nearest thing to Check Model, which it does not expose
//   4. ETABS's window as ETABS draws it after the import, as PNG (window-*.png) - the look at the deliverable as the
//      engineer sees it
//   5. the model saved as <outDir>\etabs-check.EDB (+ ETABS's own text twin .$et) - evidence beside, never the package
//   6. with --gravity [--expect-kip N]: self-weight only, run twice on the copy - AS DELIVERED, then PINNED (UX UY UZ) at
//      every Base point, a test assumption - each saved as gravity-<tag>.EDB; for each: RunAnalysis's code, ETABS's
//      analysis .LOG verbatim (instability, lost digits), the base reaction FZ against N (KOR.Drafter e2k_selfweight.py),
//      and each storey's largest vertical (and horizontal) movement. COVERS: does it stand under its own weight, does the
//      weight add up. DOES NOT: loads beyond self-weight, lateral, P-delta, cracked stiffness, design, springs.
//   7. with --reactions <uses.csv> <transfers.csv> (10 Nov 2026, KOR.Drafter row 1964): DEAD, SDL and LIVE on the copy,
//      pinned on a storey (--support-storey, default Base), and the forces at the bottom of every member a transfer slab carries - see Reactions() for its
//      gates, what it covers and what it does not. REFUSED before ETABS starts (exit 2): --reactions without two files after
//      it, --support-storey without a name or without --reactions, --gravity with --reactions. The step refuses a uses.csv
//      kPa that is not a finite number of 0 or more. reactions.csv columns: storey, storey_above, kind, member, etabs_name,
//      pier, panels_in_pier, transfer_panels_in_pier (added 10 Nov after review 1254: every transfer panel of that pier on
//      that storey - a pier's force is the whole pier's, sum it once), case, P_kN, V2_kN, V3_kN, T_kNm, M2_kNm, M3_kNm, at.
// ETABS is closed at the end without saving anything else. Only the ETABS this tool started is ever closed or ended.
//
// COVERS: does it import (return code, what ETABS wrote, any dialog on screen); does ETABS hold the same storeys,
// elevations, frames per section, areas per property, openings and restrained points (per storey and DOF set) as the
// file; does the analysis model build.
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
    private static bool _standardSolver;
    private static (string? Takeoff, string? E2k, string? Pin, string? NoPrecheck, string? Bad, List<string> Others) _gate;

    private static void Say(string line) { Console.WriteLine(line); Report.Add(line); }

    // The stability gate's reading of an ETABS analysis log: every "NUMBER OF NEGATIVE EIGENVALUES = N" it states.
    internal static List<int> NegativeEigenvalues(string logText) =>
        Regex.Matches(logText, @"NUMBER OF NEGATIVE EIGENVALUES\s*=\s*(\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();

    private static string StabilityGate(List<int> negEig, out bool stable)
    {
        stable = negEig.Count > 0 && negEig.All(n => n == 0);
        return $"GATE stability: NUMBER OF NEGATIVE EIGENVALUES = {(negEig.Count > 0 ? string.Join(", ", negEig) : "not in the analysis log")}: {(stable ? "PASS" : "FAIL - UNSTABLE, reactions are NOT a basis for SAFE")}";
    }

    private static int Main(string[] args)
    {
        // Offline: the stability gate alone on a saved log (an ETABS .LOG or a gate log quoting it). No ETABS needed.
        if (args.Length == 2 && args[0].Equals("--stability-of", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(StabilityGate(NegativeEigenvalues(File.ReadAllText(args[1])), out bool ok));
            return ok ? 0 : 1;
        }
        // PRE-CHECK START GATE (KOR.Drafter row 2103 step B, Ian 12 Nov #133436 M1): its flags are taken out first and checked
        // in Run on the very e2k ETABS opens, before ETABS starts
        _gate = PrecheckStartGate.Parse(args);
        args = _gate.Others.ToArray();
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
        _standardSolver = args.Any(x => x.Equals("--standard-solver", StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(_out);
        string copy = Path.Combine(_out, "etabs-check.e2k");
        File.Copy(source, copy, overwrite: true);
        Say($"source  : {source} ({new FileInfo(source).Length:N0} bytes, SHA-256 {Sha(source)})");
        Say($"opened  : {copy} (a copy; the source is never written)");
        Say($"ETABS   : {etabsExe} [{FileVersionInfo.GetVersionInfo(etabsExe).ProductVersion}]");
        Say($"as      : {Environment.UserDomainName}\\{Environment.UserName}, session {Process.GetCurrentProcess().SessionId}");
        var want = E2k.Read(copy);
        // a malformed --reactions/--support-storey, or --gravity with --reactions, is refused HERE, before ETABS starts:
        // the flags are only read, so a bad command line costs nothing (reviews 1254, 1255)
        if (ReactionArgsRefusal(args) is { } refusal) { Say($"REFUSED: {refusal} - ETABS was not started"); return 2; }
        // the pre-check gate on the EXACT model ETABS opens: --precheck-e2k may be left out (then it is this source); given,
        // it must be these bytes (sha256), else refused - the gate proves the file that runs, never a neighbour
        string? gateE2k = _gate.E2k ?? (_gate.Takeoff != null ? source : null);
        string? gateBad = _gate.Bad;
        if (gateBad == null && _gate.E2k != null && (!File.Exists(_gate.E2k) || Sha(_gate.E2k) != Sha(source)))
            gateBad = $"--precheck-e2k {_gate.E2k} is not the model ETABS opens ({Path.GetFileName(source)}, SHA-256 {Sha(source)[..16]})";
        var verdict = PrecheckStartGate.Check(_gate.Takeoff, gateE2k, _gate.Pin, _gate.NoPrecheck, gateBad);
        foreach (var l in verdict.Output) Say("   precheck| " + l);
        Say(verdict.Line);
        if (!verdict.Start) return PrecheckStartGate.RefusedExit;

        var before = Process.GetProcessesByName("ETABS").Select(p => p.Id).ToHashSet();
        var allBefore = CsiStartWatch.Snapshot();
        cHelper helper = new Helper();
        cOAPI etabs = helper.CreateObject(etabsExe) ?? throw new InvalidOperationException("cHelper.CreateObject gave nothing back");
        // KOR.Drafter row 2104: ApplicationStart blocks behind a licence or start box (SAFE L2, 12 Nov: 37.6 min, then -1)
        int ret = CsiStartWatch.Run(() => etabs.ApplicationStart(), TimeSpan.FromSeconds(StartTimeoutS(args)), () => StartDidNotReturn(StartTimeoutS(args), allBefore));
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
            diffs += Restraints(model, want, sn.Take(nStory).Append("Base"));
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

            // 7. --reactions <uses.csv> <transfers.csv> [--support-storey S]: DEAD, SDL and LIVE on THIS copy, pinned on S, and the force at
            // the bottom of every member a transfer slab carries (row 1964, record #131651)
            // the flags were checked before ETABS started (ReactionArgsRefusal)
            int rx = Array.FindIndex(args, x => x.Equals("--reactions", StringComparison.OrdinalIgnoreCase));
            int ss = Array.FindIndex(args, x => x.Equals("--support-storey", StringComparison.OrdinalIgnoreCase));
            if (rx >= 0)
            {
                // --support-storey <name>: the storey whose every point is pinned (default Base). 01819 (10 Nov): Base held 0
                // points - the tower stands on its P3 foundation slab at elevation 0 with no member below it.
                string supportStorey = ss >= 0 ? args[ss + 1] : "Base";
                int bad = Reactions(model, mine, Path.GetFullPath(args[rx + 1]), Path.GetFullPath(args[rx + 2]), supportStorey);
                Say($"GATE import, counts and analysis model: {(exit == 0 ? "PASS" : "FAIL")}");
                if (bad != 0 || exit != 0) exit = 2;
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

    // 7. REACTIONS FOR THE TRANSFER SLABS (KOR.Drafter row 1964, record #131651: a transfer slab's SAFE model takes the loads
    // from above as reactions of our ETABS model, per pattern, never a combination). On THIS copy only:
    //   - UX UY UZ pinned at every point of the support storey (--support-storey, default Base) - a TEST assumption (the
    //     composed .e2k carries no support), stated;
    //   - patterns: the file's own "Dead" (self-weight 1) as case KOR-Dead; SDL (SuperDead) and LIVE (Live), each with its own
    //     linear static case, loaded uniformly (gravity) on every FLOOR area of a storey that uses.csv names, at that row's kPa
    //     (uses.csv: storey,use,sdl_kpa,ll_kpa - the storey's use as the SAFE route's one rule chose it, written by the caller);
    //   - the model saved, OPENED AGAIN from the saved .EDB (on 28 Oct the API's RunAnalysis did not start the solver on a
    //     model it had just set up, while F5 on the saved .EDB, opened fresh, ran at once - Operations 635a4976), and run;
    //     if the cases still have not finished, F5 is posted to OUR ETABS's window and the analysis .LOG is waited on;
    //   - written: reactions.csv - for each member of transfers.csv (storey,storey_above,kind,member,...: COLUMN = a frame,
    //     PANEL = a wall; the label on storey_above), each case: a column's forces at its bottom station (FrameForce, the
    //     smallest station), a wall's pier forces at the pier's Bottom on that storey (PierForce); kN and kN-m, ETABS's sign
    //     (P < 0 is compression). Plus base-reactions.csv.
    // GATES (each a line "GATE ... PASS|FAIL"): supports set; floor areas loaded X of Y; 3 of 3 cases finished; SDL and LIVE
    // base reaction within 2 % of the load applied (the floor polygons' plan area less the openings' on the same storey,
    // times kPa); reactions written X of N members x 3 cases.
    // COVERS: the vertical load each discontinued member delivers, per pattern, from a linear elastic model of the whole
    // tower as composed. DOES NOT: cracked stiffness, construction sequence, P-delta, lateral loads, live load reduction,
    // partition or snow drift loads, the engineer's own supports (pins at Base are ours); a member whose label is not on
    // storey_above in ETABS is listed, not guessed.
    // SAME-CLASS FAULT IT MISSES: a floor area ETABS holds but no use row names gets no SDL/LIVE - it is counted in "loaded
    // X of Y", so the gate fails, but a floor that is MISSING from the composed model takes no load and no gate sees it.
    private static int Reactions(cSapModel model, Process? mine, string usesCsv, string transfersCsv, string supportStorey)
    {
        int bad = 0;
        var Ic = CultureInfo.InvariantCulture;
        Say($"7. REACTIONS (uses {usesCsv}, SHA-256 {Sha(usesCsv)}; transfers {transfersCsv}, SHA-256 {Sha(transfersCsv)})");
        // both files are read as CSV with quoted fields, the way reactions.csv is written (review row 1254)
        var uses = File.ReadAllLines(usesCsv).Skip(1).Where(l => l.Trim().Length > 0).Select(CsvFields)
            .ToDictionary(p => p[0].Trim(), p => (Use: p[1].Trim(), Sdl: double.Parse(p[2], Ic), Live: double.Parse(p[3], Ic)), StringComparer.OrdinalIgnoreCase);
        var negative = uses.Where(kv => !double.IsFinite(kv.Value.Sdl) || kv.Value.Sdl < 0 || !double.IsFinite(kv.Value.Live) || kv.Value.Live < 0).Select(kv => kv.Key).ToList();
        if (negative.Count > 0) { Say($"REFUSED: uses.csv gives a kPa that is not a finite number of 0 or more for storey(s) {string.Join(", ", negative)} - nothing loaded or run"); return 1; }
        var members = File.ReadAllLines(transfersCsv).Skip(1).Where(l => l.Trim().Length > 0).Select(CsvFields)
            .Select(p => (Storey: p[0].Trim(), Above: p[1].Trim(), Kind: p[2].Trim(), Label: p[3].Trim())).ToList();
        Say($"   uses: {uses.Count} storey row(s); transfer members: {members.Count} on {members.Select(m => m.Storey).Distinct().Count()} storey(s)");

        model.SetModelIsLocked(false);
        model.SetPresentUnits(eUnits.kN_m_C);

        // supports
        int nb = 0; string[] basePts = Array.Empty<string>();
        model.PointObj.GetNameListOnStory(supportStorey, ref nb, ref basePts);
        int pinned = basePts.Count(p => { var v = new[] { true, true, true, false, false, false }; return model.PointObj.SetRestraint(p, ref v) == 0; });
        Say($"GATE supports: UX UY UZ pinned at {pinned} of {nb} point(s) on storey {supportStorey} - a TEST assumption, this copy only: {(nb > 0 && pinned == nb ? "PASS" : "FAIL")}");
        if (!(nb > 0 && pinned == nb)) bad++;

        // patterns and cases
        Say($"   LoadPatterns.Add(SDL, SuperDead): {model.LoadPatterns.Add("SDL", eLoadPatternType.SuperDead, 0, true)}; Add(LIVE, Live): {model.LoadPatterns.Add("LIVE", eLoadPatternType.Live, 0, true)}");
        Say($"   StaticLinear.SetCase(KOR-Dead): {model.LoadCases.StaticLinear.SetCase("KOR-Dead")}");
        { string[] lt = { "Load" }, ln = { "Dead" }; double[] sf = { 1.0 }; Say($"   StaticLinear.SetLoads(KOR-Dead, Dead x1): {model.LoadCases.StaticLinear.SetLoads("KOR-Dead", 1, ref lt, ref ln, ref sf)}"); }
        string[] cases = { "KOR-Dead", "SDL", "LIVE" };

        // floor loads, and the load applied (for the equilibrium gate)
        int na = 0; string[] an = Array.Empty<string>();
        model.AreaObj.GetNameList(ref na, ref an);
        double PlanArea(string a)
        {
            int np = 0; string[] pts = Array.Empty<string>();
            model.AreaObj.GetPoints(a, ref np, ref pts);
            var xy = pts.Select(p => { double x = 0, y = 0, z = 0; model.PointObj.GetCoordCartesian(p, ref x, ref y, ref z, "Global"); return (x, y); }).ToList();
            double s = 0; for (int i = 0; i < xy.Count; i++) { var (x1, y1) = xy[i]; var (x2, y2) = xy[(i + 1) % xy.Count]; s += x1 * y2 - x2 * y1; }
            return Math.Abs(s) / 2;
        }
        int floors = 0, loaded = 0; double appliedSdl = 0, appliedLive = 0, openSdl = 0, openLive = 0;
        var unnamed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var statedNone = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var a in an)
        {
            string label = "", story = ""; model.AreaObj.GetLabelFromName(a, ref label, ref story);
            bool isOpening = false; model.AreaObj.GetOpening(a, ref isOpening);
            if (isOpening)
            {
                if (uses.TryGetValue(story, out var uo)) { double ar = PlanArea(a); openSdl += uo.Sdl * ar; openLive += uo.Live * ar; }
                continue;
            }
            eAreaDesignOrientation o = default; model.AreaObj.GetDesignOrientation(a, ref o);
            if (o != eAreaDesignOrientation.Floor) continue;
            floors++;
            if (!uses.TryGetValue(story, out var u)) { unnamed[story] = unnamed.GetValueOrDefault(story) + 1; continue; }
            if (u.Sdl == 0 && u.Live == 0) { statedNone[story] = statedNone.GetValueOrDefault(story) + 1; continue; }   // a row that says: no use, no load
            // Dir 10 = the gravity direction (a positive value acts down). NOT 6: 6 is global +Z, and run 2 (10 Nov 14:24) pushed
            // SDL and LIVE UP - base FZ -43,212.6 kN against the self-weight's +404,048.6 kN; the equilibrium gate caught it.
            int r1 = u.Sdl > 0 ? model.AreaObj.SetLoadUniform(a, "SDL", u.Sdl, 10, true, "Global") : 0;
            int r2 = u.Live > 0 ? model.AreaObj.SetLoadUniform(a, "LIVE", u.Live, 10, true, "Global") : 0;
            if (r1 == 0 && r2 == 0) { loaded++; double ar = PlanArea(a); appliedSdl += u.Sdl * ar; appliedLive += u.Live * ar; }
            else Say($"   SetLoadUniform {a} ({label} on {story}): SDL {r1}, LIVE {r2}");
        }
        foreach (var kv in unnamed) Say($"   no use row for storey {kv.Key}: {kv.Value} floor area(s) carry no SDL or LIVE");
        foreach (var kv in statedNone) Say($"   storey {kv.Key}: {kv.Value} floor area(s) carry no SDL or LIVE - its row in uses.csv says no use ({uses[kv.Key].Use})");
        int none = statedNone.Values.Sum();
        bool loadsOk = floors > 0 && loaded + none == floors;
        Say($"GATE floor areas loaded: {loaded} of {floors} loaded, {none} unloaded as uses.csv states, {floors - loaded - none} neither: {(loadsOk ? "PASS" : "FAIL")}");
        if (!loadsOk) bad++;
        Say($"   applied (floor plan area x kPa, less the openings' on a named storey): SDL {appliedSdl - openSdl:N1} kN ({appliedSdl:N1} - {openSdl:N1}), LIVE {appliedLive - openLive:N1} kN ({appliedLive:N1} - {openLive:N1})");

        // save, open again, run
        string edb = Path.Combine(_out, "reactions.EDB");
        Say($"   saved {Path.GetFileName(edb)}: {model.File.Save(edb)}");
        Say($"   OpenFile({Path.GetFileName(edb)}) again: {model.File.OpenFile(edb)}");
        model.SetPresentUnits(eUnits.kN_m_C);
        Say($"   SetRunCaseFlag(all, off): {model.Analyze.SetRunCaseFlag("", false, true)}; on: {string.Join(", ", cases.Select(c => $"{c} {model.Analyze.SetRunCaseFlag(c, true, false)}"))}");
        Say($"   cases to run: {Cases(model)}");
        int stype = 0, ptype = 0, par = 0, fsize = 0, threads = 0; string stiff = "";
        model.Analyze.GetSolverOption_3(ref stype, ref ptype, ref par, ref fsize, ref threads, ref stiff);
        if (_standardSolver)
            // --standard-solver: ETABS's advanced solver gives "LIMITED INSTABILITY INFORMATION"; the standard one names the
            // joints and DOFs that lose stiffness in the analysis log, which is how an unstable model's mechanisms are found.
            Say($"   solver was type {stype}; SetSolverOption_3(type 0 standard, as asked by --standard-solver): {model.Analyze.SetSolverOption_3(0, ptype, par, fsize, threads, stiff)}");
        else
            Say($"   solver as ETABS has it (kept): type {stype}, process type {ptype} (0 auto, 1 GUI process, 2 separate), threads {threads}");
        var t0 = DateTime.Now.AddSeconds(-2);
        var sw = Stopwatch.StartNew();
        int run = model.Analyze.RunAnalysis();
        Say($"   RunAnalysis: {run} in {sw.Elapsed.TotalSeconds:F1} s; case status: {Status(model, cases)}");
        CaptureScreen("reactions-run-after");
        if (!Finished(model, cases))
        {
            // F5 to OUR ETABS's window, as Ian ran it by hand on 28 Oct; the .LOG is watched, the API is not called meanwhile
            mine?.Refresh();
            IntPtr h = mine?.MainWindowHandle ?? IntPtr.Zero;
            Say($"   not finished by the API - F5 posted to our ETABS's window {h}: {(h != IntPtr.Zero && PostMessage(h, 0x0100, (IntPtr)0x74, IntPtr.Zero) && PostMessage(h, 0x0101, (IntPtr)0x74, IntPtr.Zero))}");
            string log = Path.ChangeExtension(edb, ".LOG");
            var deadline = DateTime.Now.AddMinutes(25); bool done = false; int shot = 0;
            while (DateTime.Now < deadline && !done)
            {
                Thread.Sleep(10_000);
                if (++shot % 6 == 1) CaptureScreen($"reactions-f5-{shot:D3}");
                try { done = File.Exists(log) && File.GetLastWriteTime(log) >= t0 && Regex.IsMatch(ReadShared(log), @"ANALYSIS\s+COMPLETE", RegexOptions.IgnoreCase); } catch (IOException) { }
            }
            Say($"   .LOG {(done ? "says ANALYSIS COMPLETE" : "did not say ANALYSIS COMPLETE in 25 min")} after {sw.Elapsed.TotalMinutes:F1} min");
            Thread.Sleep(15_000);
            CaptureScreen("reactions-f5-after");
            Say($"   case status: {Status(model, cases)}");
        }
        var negEig = new List<int>();
        foreach (var f in Directory.GetFiles(_out, "reactions.*").Where(f => File.GetLastWriteTime(f) >= t0 && f.EndsWith(".LOG", StringComparison.OrdinalIgnoreCase)))
        {
            Say($"   analysis log {Path.GetFileName(f)}, verbatim:");
            string text = ReadShared(f);
            foreach (var l in text.Split('\n')) Say($"   | {l.TrimEnd()}");
            negEig.AddRange(NegativeEigenvalues(text));
        }
        bool fin = Finished(model, cases);
        Say($"GATE cases finished: {cases.Count(c => CaseStatus(model, c) == 4)} of {cases.Length}: {(fin ? "PASS" : "FAIL")}");
        // ETABS's own stability check: a model with negative stiffness eigenvalues is unstable (a mechanism or a free part), and
        // reactions read off it are no basis for the transfer slabs. FAIL when the count is above 0 or the log does not state it.
        Say(StabilityGate(negEig, out bool stable));
        if (!stable) bad++;
        if (!fin) { bad++; Say("   no results read"); return bad; }

        // results
        model.SetPresentUnits(eUnits.kN_m_C);
        model.Results.Setup.DeselectAllCasesAndCombosForOutput();
        foreach (var c in cases) model.Results.Setup.SetCaseSelectedForOutput(c);
        var baseLines = new List<string> { "case,FX_kN,FY_kN,FZ_kN,MX_kNm,MY_kNm,MZ_kNm" };
        var fz = new Dictionary<string, double>();
        {
            int nr = 0; string[] lc = Array.Empty<string>(), st = Array.Empty<string>(); double[] sn = Array.Empty<double>(), fx = Array.Empty<double>(), fy = Array.Empty<double>(), z = Array.Empty<double>(), mx = Array.Empty<double>(), my = Array.Empty<double>(), mz = Array.Empty<double>();
            double gx = 0, gy = 0, gz = 0;
            model.Results.BaseReact(ref nr, ref lc, ref st, ref sn, ref fx, ref fy, ref z, ref mx, ref my, ref mz, ref gx, ref gy, ref gz);
            for (int i = 0; i < nr; i++) { fz[lc[i]] = z[i]; baseLines.Add(string.Join(",", lc[i], fx[i].ToString("F2", Ic), fy[i].ToString("F2", Ic), z[i].ToString("F2", Ic), mx[i].ToString("F1", Ic), my[i].ToString("F1", Ic), mz[i].ToString("F1", Ic))); }
        }
        File.WriteAllLines(Path.Combine(_out, "base-reactions.csv"), baseLines);
        foreach (var (c, want) in new[] { ("SDL", appliedSdl - openSdl), ("LIVE", appliedLive - openLive) })
        {
            double got = fz.GetValueOrDefault(c);
            bool ok = want > 0 && Math.Abs(got - want) / want <= 0.02;
            Say($"GATE equilibrium {c}: base FZ {got:N1} kN vs applied {want:N1} kN ({(want > 0 ? (got - want) / want : double.NaN):P2}): {(ok ? "PASS" : "FAIL")}");
            if (!ok) bad++;
        }
        Say($"   KOR-Dead base FZ {fz.GetValueOrDefault("KOR-Dead"):N1} kN (self-weight; not gated here)");

        // the members
        int np2 = 0; string[] st2 = Array.Empty<string>(), pn = Array.Empty<string>(), plc = Array.Empty<string>(), loc = Array.Empty<string>();
        double[] pp = Array.Empty<double>(), pv2 = Array.Empty<double>(), pv3 = Array.Empty<double>(), pt = Array.Empty<double>(), pm2 = Array.Empty<double>(), pm3 = Array.Empty<double>();
        int pr = model.Results.PierForce(ref np2, ref st2, ref pn, ref plc, ref loc, ref pp, ref pv2, ref pv3, ref pt, ref pm2, ref pm3);
        Say($"   PierForce: {pr}, {np2} row(s)");
        var pierAt = new Dictionary<(string Story, string Pier, string Case), int>();
        for (int i = 0; i < np2; i++) if (loc[i].Equals("Bottom", StringComparison.OrdinalIgnoreCase)) pierAt[(st2[i], pn[i], plc[i])] = i;
        var rows = new List<string> { "storey,storey_above,kind,member,etabs_name,pier,panels_in_pier,transfer_panels_in_pier,case,P_kN,V2_kN,V3_kN,T_kNm,M2_kNm,M3_kNm,at" };
        int written = 0; var missing = new List<string>();
        var panelsInPier = new Dictionary<(string, string), int>();
        foreach (var a in an)
        {
            string l = "", s = "", p = ""; model.AreaObj.GetLabelFromName(a, ref l, ref s);
            if (model.AreaObj.GetPier(a, ref p) == 0 && p.Length > 0 && p != "None") panelsInPier[(s, p)] = panelsInPier.GetValueOrDefault((s, p)) + 1;
        }
        // a pier's force is the WHOLE pier's: every transfer PANEL in one pier on one storey repeats it, so each such row
        // names all of them (transfer_panels_in_pier) - a reader sums a pier once, never once per panel (review row 1254)
        var transferPanelsInPier = new Dictionary<(string, string), List<string>>();
        foreach (var m in members.Where(m => !m.Kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)))
        {
            string n = "", p = "";
            if (model.AreaObj.GetNameFromLabel(m.Label, m.Above, ref n) != 0 || n.Length == 0 || model.AreaObj.GetPier(n, ref p) != 0 || p.Length == 0 || p == "None") continue;
            if (!transferPanelsInPier.TryGetValue((m.Above, p), out var list)) transferPanelsInPier[(m.Above, p)] = list = new List<string>();
            list.Add(m.Label);
        }
        Say($"   piers holding more than one transfer panel: {transferPanelsInPier.Count(kv => kv.Value.Count > 1)} of {transferPanelsInPier.Count}");
        foreach (var m in members)
        {
            string name = "";
            bool col = m.Kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase);
            int got = col ? model.FrameObj.GetNameFromLabel(m.Label, m.Above, ref name) : model.AreaObj.GetNameFromLabel(m.Label, m.Above, ref name);
            if (got != 0 || name.Length == 0) { missing.Add($"{m.Kind} {m.Label} on {m.Above}: no such label in ETABS"); continue; }
            int casesWritten = 0;
            if (col)
            {
                int n = 0; string[] obj = Array.Empty<string>(), elm = Array.Empty<string>(), lc = Array.Empty<string>(), stt = Array.Empty<string>();
                double[] os = Array.Empty<double>(), es = Array.Empty<double>(), sn = Array.Empty<double>(), P = Array.Empty<double>(), V2 = Array.Empty<double>(), V3 = Array.Empty<double>(), T = Array.Empty<double>(), M2 = Array.Empty<double>(), M3 = Array.Empty<double>();
                model.Results.FrameForce(name, eItemTypeElm.ObjectElm, ref n, ref obj, ref os, ref elm, ref es, ref lc, ref stt, ref sn, ref P, ref V2, ref V3, ref T, ref M2, ref M3);
                foreach (var c in cases)
                {
                    var idx = Enumerable.Range(0, n).Where(i => lc[i] == c).OrderBy(i => os[i]).ToList();
                    if (idx.Count == 0) continue;
                    int i0 = idx[0];
                    rows.Add(string.Join(",", Q(m.Storey), Q(m.Above), Q(m.Kind), Q(m.Label), Q(name), "", "", "", c, P[i0].ToString("F2", Ic), V2[i0].ToString("F2", Ic), V3[i0].ToString("F2", Ic), T[i0].ToString("F2", Ic), M2[i0].ToString("F2", Ic), M3[i0].ToString("F2", Ic), $"station {os[i0].ToString("F3", Ic)} m (bottom)"));
                    casesWritten++;
                }
            }
            else
            {
                string pier = ""; model.AreaObj.GetPier(name, ref pier);
                foreach (var c in cases)
                {
                    if (pier.Length == 0 || pier == "None" || !pierAt.TryGetValue((m.Above, pier, c), out int i)) continue;
                    rows.Add(string.Join(",", Q(m.Storey), Q(m.Above), Q(m.Kind), Q(m.Label), Q(name), Q(pier), panelsInPier.GetValueOrDefault((m.Above, pier)).ToString(Ic), Q(string.Join(";", transferPanelsInPier.GetValueOrDefault((m.Above, pier)) ?? new List<string>())), c, pp[i].ToString("F2", Ic), pv2[i].ToString("F2", Ic), pv3[i].ToString("F2", Ic), pt[i].ToString("F2", Ic), pm2[i].ToString("F2", Ic), pm3[i].ToString("F2", Ic), "pier bottom"));
                    casesWritten++;
                }
                if (casesWritten < cases.Length) missing.Add($"PANEL {m.Label} on {m.Above}: pier '{pier}', {casesWritten} of {cases.Length} case(s) at the pier's bottom");
            }
            if (casesWritten == cases.Length) written++;
            else if (col) missing.Add($"COLUMN {m.Label} on {m.Above}: {casesWritten} of {cases.Length} case(s)");
        }
        File.WriteAllLines(Path.Combine(_out, "reactions.csv"), rows);
        foreach (var x in missing) Say($"   NOT WRITTEN: {x}");
        Say($"GATE reactions written: {written} of {members.Count} member(s) x {cases.Length} cases ({rows.Count - 1} row(s) in reactions.csv): {(written == members.Count && members.Count > 0 ? "PASS" : "FAIL")}");
        if (!(written == members.Count && members.Count > 0)) bad++;
        return bad;
    }

    private static string? ReactionArgsRefusal(string[] args)
    {
        int rx = Array.FindIndex(args, x => x.Equals("--reactions", StringComparison.OrdinalIgnoreCase));
        int ss = Array.FindIndex(args, x => x.Equals("--support-storey", StringComparison.OrdinalIgnoreCase));
        if (rx >= 0 && (rx + 2 >= args.Length || args[rx + 1].StartsWith("--") || args[rx + 2].StartsWith("--")))
            return "--reactions needs <uses.csv> <transfers.csv> after it";
        if (ss >= 0 && (ss + 1 >= args.Length || args[ss + 1].StartsWith("--")))
            return "--support-storey needs a storey name after it";
        if (ss >= 0 && rx < 0) return "--support-storey is read only with --reactions";
        if (rx >= 0 && args.Any(x => x.Equals("--gravity", StringComparison.OrdinalIgnoreCase)))
            return "--gravity and --reactions together - --gravity pins every Base point on the same model, supports the reactions' gate would not state";
        int st = Array.FindIndex(args, x => x.Equals("--start-timeout-s", StringComparison.OrdinalIgnoreCase));
        if (st >= 0 && (st + 1 >= args.Length || !int.TryParse(args[st + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) || s < 10))
            return "--start-timeout-s wants whole seconds, at least 10";
        return null;
    }

    private static string Q(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    // one CSV line into its fields: a field in double quotes may hold commas, and "" inside it is one quote
    private static string[] CsvFields(string line)
    {
        var fields = new List<string>(); var cur = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else if (c == '"') quoted = false; else cur.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        fields.Add(cur.ToString());
        return fields.ToArray();
    }

    private static int CaseStatus(cSapModel model, string c)
    {
        int nc = 0; string[] cn = Array.Empty<string>(); int[] cs = Array.Empty<int>();
        model.Analyze.GetCaseStatus(ref nc, ref cn, ref cs);
        int i = Array.IndexOf(cn, c);
        return i >= 0 ? cs[i] : 0;
    }

    private static bool Finished(cSapModel model, string[] cases) => cases.All(c => CaseStatus(model, c) == 4);

    private static string Status(cSapModel model, string[] cases) => string.Join(", ", cases.Select(c => $"{c}={CaseStatus(model, c)}")) + " (1 not run, 2 could not start, 3 not finished, 4 finished)";

    private static string ReadShared(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    // THE SUPPORTS (2026-10-28, the first file to carry them: KOR.Drafter e2k_base_supports.py's 46 POINTASSIGN ...
    // RESTRAINT "UX UY UZ" on Base). Restrained points by storey and DOF set, the file's POINTASSIGN lines against what
    // ETABS holds after the import - a support the importer drops reads as a difference, not as silence. The walls'
    // AREAASSIGN ... ADDRESTRAINT is counted and named, not compared: on 28 Oct it made no point restraint on import.
    private static int Restraints(cSapModel model, E2k want, IEnumerable<string> storeys)
    {
        try
        {
            string[] dof = { "UX", "UY", "UZ", "RX", "RY", "RZ" };
            var got = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in storeys)
            {
                int n = 0; string[] pts = Array.Empty<string>();
                model.PointObj.GetNameListOnStory(s, ref n, ref pts);
                foreach (var p in pts)
                {
                    bool[] v = new bool[6]; model.PointObj.GetRestraint(p, ref v);
                    if (v.Any(x => x)) { string k = $"{s} | {string.Join(" ", dof.Where((_, i) => v[i]))}"; got[k] = got.GetValueOrDefault(k) + 1; }
                }
            }
            Say($"   the file also carries {want.AreaAddRestraints} wall AREAASSIGN ADDRESTRAINT line(s) - not compared (no point restraint came of them on import, 28 Oct)");
            return Compare("restrained points by storey | DOFs", want.PointRestraints, got);
        }
        catch (Exception ex) { Say($"   restraints NOT READ: {ex.GetType().Name}: {ex.Message}"); return 1; }
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

    /// <summary>--start-timeout-s N (KOR.Drafter row 2104; default 120, at least 10 - checked with the other flags before
    /// ETABS starts, see ReactionArgsRefusal).</summary>
    private static int StartTimeoutS(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--start-timeout-s", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s >= 10 ? s : 120;
    }

    /// <summary>From the start watch's thread when ApplicationStart has not returned within the cap: name every window of a
    /// process started since (the licence or start box), picture the screen, end only an ETABS that is THIS process's
    /// child (none = nothing ended, said; a sibling run's ETABS is never touched), write the report and exit 5 - the COM
    /// call never returns. Exit 5: 0-4 are taken. UNPROVEN LIVE: no licence box was planted.</summary>
    private static void StartDidNotReturn(int capS, HashSet<int> allBefore)
    {
        lock (Report)
        {
            Say($"APPLICATION DID NOT START within {capS} s: ApplicationStart has not returned (a licence or start dialog holds it?)");
            var titles = CsiStartWatch.WindowTitlesOfProcessesNotIn(allBefore);
            Say(titles.Count == 0 ? "   windows of processes started since: none visible" : "   windows of processes started since: " + string.Join("; ", titles));
            CaptureScreen($"start-stuck-{capS}s-screen");
            var mine = CsiStartWatch.ChildrenNamed("ETABS", Environment.ProcessId);
            var others = Process.GetProcessesByName("ETABS").Select(p => { using (p) return p.Id; }).Where(id => !mine.Any(m => m.Id == id)).ToList();
            if (mine.Count == 0) Say($"   no ETABS is a child of this process (pid {Environment.ProcessId}): NOTHING ENDED - end the stuck one by hand");
            foreach (var p in mine)
                using (p)
                {
                    try { p.Kill(); Say($"   our ETABS (pid {p.Id}, child of this process) ended"); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Say($"   our ETABS (pid {p.Id}) not ended: {ex.Message}"); }
                }
            Say($"   other ETABS left alone: {(others.Count == 0 ? "none" : string.Join(", ", others))}");
            try { File.WriteAllLines(Path.Combine(_out, "report.txt"), Report, new UTF8Encoding(true)); } catch (IOException) { }
        }
        Environment.Exit(5);
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
    Dictionary<string, int> AreasByProperty, int Openings, Dictionary<string, int> PointRestraints, int AreaAddRestraints)
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
        // POINTASSIGN "pt" "storey" RESTRAINT "UX UY UZ" - keyed "storey | DOFs", the DOFs in ETABS's order
        string[] order = { "UX", "UY", "UZ", "RX", "RY", "RZ" };
        var restraints = Regex.Matches(t, @"^\s*POINTASSIGN\s+""[^""]+""\s+""([^""]+)""\s+RESTRAINT\s+""([^""]*)""", RegexOptions.Multiline)
            .GroupBy(m => $"{m.Groups[1].Value} | {string.Join(" ", order.Where(d => m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(d, StringComparer.OrdinalIgnoreCase)))}")
            .ToDictionary(g => g.Key, g => g.Count());
        int areaAdd = Regex.Matches(t, @"^\s*AREAASSIGN\b.*\bADDRESTRAINT\b", RegexOptions.Multiline).Count;
        return new E2k(storeys, frames, areas, openings, restraints, areaAdd);
    }
}
