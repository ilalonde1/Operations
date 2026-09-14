// EtabsExportE2k — the engineer's ETABS models (.EDB, binary) exported to .e2k (text) so the PDF
// intake can measure itself against them: 57 of the 66 jobs that hold both a stick file and a model
// hold the model as .EDB only (corpus census 2026-09-11). ETABS itself does the export, through its
// API, on a machine that has it (KOR-210, Ian's test machine); this drives it and never touches the
// engineer's file — each .EDB is copied to a work folder first and opened from there.
//
//     EtabsExportE2k <census.csv> <workDir> [--jobs 31168-01,...] [--etabs "C:\Program Files\Computers and Structures\ETABS 22\ETABS.exe"]
//
// census.csv is `takeoff corpus-census --out` (one row per job, model_folder column). For every job
// with a model folder and no .e2k in it, the newest .EDB in that folder (top level, then one level
// down) is copied to <workDir>\<job>\, opened in ETABS, and saved there; the text model ETABS writes
// (<job>.$et — the same syntax as an .e2k) is kept as <workDir>\<job>\<job>.e2k. The
// result is one line per job and a CSV (<workDir>\export.csv): job, edb, e2k, bytes, or the reason
// there is none. Exit 0 when every job it tried exported; 2 otherwise.
//
// WHAT THIS DOES NOT DO: choose between several models in a folder by anything but their date (the
// newest is taken and NAMED, so a person can override it); write anywhere on the projects share;
// run on a machine without ETABS (it says so and exits 3).
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ETABSv1;

namespace Kor.EtabsExportE2k;

internal static class Program
{
    private const string DefaultEtabs = @"C:\Program Files\Computers and Structures\ETABS 22\ETABS.exe";

    private static int Main(string[] args)
    {
        // THE API ASSEMBLY IS ETABS'S OWN. ETABSv1.dll is loaded from the folder of the ETABS this run
        // starts, never from beside this exe: an API client carrying its own copy is refused by ETABS
        // ("The developer of the API client ... needs to update it ... ETABS will close", KOR-210,
        // 2026-09-11), and CSI's instruction is Copy Local = False for exactly that. Registered before
        // Run is compiled, because Run's body names the API's types.
        string etabsExe = DefaultEtabs;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals("--etabs", StringComparison.OrdinalIgnoreCase)) etabsExe = args[i + 1];
        string etabsDir = Path.GetDirectoryName(Path.GetFullPath(etabsExe)) ?? ".";
        // said here, before Run is compiled: Run names the API's types, and on a machine with no ETABS the
        // loader would fail first and say so less plainly
        if (!File.Exists(etabsExe)) { Console.Error.WriteLine($"ETABS not found at {etabsExe} (pass --etabs <path to ETABS.exe>); this runs on KOR-210."); return 3; }
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (!string.Equals(name.Name, "ETABSv1", StringComparison.OrdinalIgnoreCase)) return null;
            string dll = Path.Combine(etabsDir, "ETABSv1.dll");
            if (!File.Exists(dll)) { Console.Error.WriteLine($"ETABSv1.dll not found beside {etabsExe}"); return null; }
            Console.WriteLine($"API assembly: {dll}");
            return context.LoadFromAssemblyPath(dll);
        };

        // nothing this tool does may end in a stack trace on the person running it: every failure is one
        // line saying which step, and the exit code says whether anything exported
        try { return Run(args); }
        catch (FileNotFoundException ex) when (string.IsNullOrEmpty(ex.Message) || (ex.FileName?.StartsWith("ETABSv1", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            Console.Error.WriteLine($"stopped: the API assembly could not be loaded from {etabsDir} (ETABSv1.dll must sit beside ETABS.exe)");
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stopped: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: EtabsExportE2k <census.csv> <workDir> [--jobs a,b] [--etabs <ETABS.exe>]");
            return 1;
        }
        string census = args[0], work = args[1], etabsExe = DefaultEtabs;
        HashSet<string>? only = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                only = new HashSet<string>(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
            else if (args[i].Equals("--etabs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                etabsExe = args[++i];
        }
        Directory.CreateDirectory(work);
        if (!File.Exists(census)) { Console.Error.WriteLine($"census not found: {census}"); return 1; }
        if (!File.Exists(etabsExe)) { Console.Error.WriteLine($"ETABS not found at {etabsExe} (pass --etabs <path to ETABS.exe>)"); return 3; }

        var jobs = ReadCensus(census).Where(j => j.ModelFolder.Length > 0 && (only is null || only.Contains(j.Job))).ToList();
        Console.WriteLine($"{jobs.Count} job(s) with a model folder in {Path.GetFileName(census)}");
        if (jobs.Count == 0) return 0;

        cOAPI? etabs = Start(etabsExe);
        if (etabs is null) return 3;
        cSapModel? model = etabs.SapModel;
        if (model is null) { Console.Error.WriteLine("ETABS started but gave no model object (SapModel is null)."); try { etabs.ApplicationExit(false); } catch (COMException) { } return 3; }

        // edb_written: the day the engineer last saved the model, beside the drawing's issue date in the corpus ledger
        // (CorpusAnalyzer.YardstickProvenance) - her model seldom follows the drawings, and a verdict is read with its age
        var rows = new List<string> { "job,edb,e2k,bytes,outcome,edb_written" };
        int ok = 0, tried = 0;
        try
        {
            foreach (var job in jobs)
            {
                string outcome;
                string edb = "", e2k = "";
                long bytes = 0;
                try
                {
                    // every job with an .EDB is exported, an .e2k beside it or not: the .e2k there may be this
                    // tool's own published output (31168's "31168-FROM-DRAWINGS.e2k"), and the yardstick must be
                    // the engineer's - the first run skipped 15 jobs for "has an .e2k already"
                    var candidates = Models(job.ModelFolder).Where(f => f.EndsWith(".edb", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (candidates.Count == 0)
                    {
                        outcome = "no .EDB in the model folder";
                    }
                    else
                    {
                        tried++;
                        // THE NEWEST THAT OPENS. A folder holds several models, and the newest is not always one
                        // ETABS 22 can read: 31168's newest was a "secondary elements" file that would not open
                        // while its full building model beside it would (2026-09-11). Newest first, then the next,
                        // each named in the outcome so a person sees what was taken and what was passed over.
                        // AT MOST TWO ATTEMPTS: a job whose newest model is a newer ETABS's is usually a job whose older
                        // ones are too, and every refusal is a dialog for the person at the keyboard (twelve jobs, a
                        // dozen refusals each, 2026-09-11). The newest and the one before it; the rest are named as passed over.
                        var ordered = candidates.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                        var notTried = ordered.Skip(2).Select(f => f.Name).ToList();
                        ordered = ordered.Take(2).ToList();
                        string jobDir = Path.Combine(work, job.Job);
                        Directory.CreateDirectory(jobDir);
                        e2k = Path.Combine(jobDir, job.Job + ".e2k");
                        if (File.Exists(e2k)) File.Delete(e2k);
                        var passedOver = new List<string>();
                        outcome = "no .EDB in the model folder";
                        Console.Write($"  {job.Job,-10} ");
                        foreach (var candidate in ordered)
                        {
                            edb = candidate.FullName;
                            string copy = Path.Combine(jobDir, candidate.Name);
                            File.Copy(candidate.FullName, copy, overwrite: true);            // ETABS opens the COPY; the engineer's file is never touched
                            Console.Write($"opening {candidate.Name} ({candidate.Length / 1048576.0:F0} MB) ... ");
                            int ret = model.File.OpenFile(copy);
                            if (ret != 0)
                            {
                                passedOver.Add(candidate.Name);
                                outcome = $"OpenFile returned {ret} on every .EDB tried ({passedOver.Count}): {string.Join("; ", passedOver)}" + (notTried.Count > 0 ? $"; not tried ({notTried.Count}): {string.Join("; ", notTried)}" : "");
                                Console.Write("would not open; ");
                                continue;
                            }
                            Console.Write("saving text model ... ");
                            // ETABS 22.6 given "<job>.e2k" wrote "<job>.EDB" and its text twin "<job>.$et" and left no .e2k
                            // (31016-01 on KOR-210, 2026-09-11: "Save returned 0 but wrote nothing"). The .$et IS the text
                            // model — $ File, $ PROGRAM INFORMATION, STORIES, AREAASSIGN — so the text twin is what is kept,
                            // under the .e2k name every reader here expects.
                            string stem = Path.Combine(jobDir, job.Job);
                            ret = model.File.Save(stem + ".EDB");
                            string et = stem + ".$et";
                            if (ret != 0) { outcome = $"Save returned {ret}"; break; }
                            if (!File.Exists(et) && !File.Exists(e2k)) { outcome = "Save returned 0 but wrote no text model (.$et)"; break; }
                            if (File.Exists(et)) File.Copy(et, e2k, overwrite: true);
                            bytes = new FileInfo(e2k).Length;
                            string head = ReadHead(e2k, 2000);
                            outcome = head.Contains("$ PROGRAM INFORMATION", StringComparison.OrdinalIgnoreCase) || head.Contains("$ File", StringComparison.OrdinalIgnoreCase)
                                ? "OK" : "wrote a file that does not read as an .e2k";
                            if (outcome == "OK")
                            {
                                ok++;
                                if (passedOver.Count > 0) outcome += $" (passed over, would not open: {string.Join("; ", passedOver)})";
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    outcome = $"{ex.GetType().Name}: {ex.Message}";
                }
                Console.WriteLine($"{(edb.Length > 0 ? "" : $"  {job.Job,-10} ")}{outcome}{(bytes > 0 ? $"  ({bytes / 1024:N0} KB)  <- {Path.GetFileName(edb)}" : "")}");
                rows.Add(string.Join(",", Q(job.Job), Q(edb), Q(e2k), bytes.ToString(CultureInfo.InvariantCulture), Q(outcome),
                    edb.Length > 0 && File.Exists(edb) ? File.GetLastWriteTime(edb).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""));
            }
        }
        finally
        {
            // the ETABS this tool started is closed without saving anything
            try { etabs.ApplicationExit(false); } catch (COMException) { }
        }
        File.WriteAllLines(Path.Combine(work, "export.csv"), rows, new UTF8Encoding(true));
        Console.WriteLine($"{ok} of {tried} exported; {Path.Combine(work, "export.csv")}");
        return ok == tried ? 0 : 2;
    }

    /// <summary>
    /// A fresh ETABS, started by this tool from the EXE named — never an attached one. KOR-210 has ETABS
    /// 20, 21 and 22 installed, so the ProgID could start any of them, and attaching to an ETABS a person
    /// had open handed back a model object with nothing behind it (NullReferenceException inside
    /// cFile.OpenFile, 2026-09-11). The instance is closed at the end, saving nothing.
    /// </summary>
    private static cOAPI? Start(string etabsExe)
    {
        try
        {
            cHelper helper = new Helper();
            Console.Write($"starting {etabsExe} through its API ... ");
            cOAPI? etabs = helper.CreateObject(etabsExe);
            if (etabs is null) { Console.Error.WriteLine("CreateObject gave nothing back."); return null; }
            int ret = etabs.ApplicationStart();
            if (ret != 0) { Console.Error.WriteLine($"ApplicationStart returned {ret} (licence?)."); return null; }
            Console.WriteLine("started");
            return etabs;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException or FileNotFoundException)
        {
            Console.Error.WriteLine($"ETABS could not be started on this machine: {ex.GetType().Name}: {ex.Message}. Run this on KOR-210, where ETABS 22 is installed.");
            return null;
        }
    }

    /// <summary>The model files in a folder and one level down: .e2k and .EDB, by the listing.</summary>
    private static List<string> Models(string folder)
    {
        var files = new List<string>();
        if (!Directory.Exists(folder)) return files;
        files.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly));
        foreach (var sub in Directory.EnumerateDirectories(folder))
            files.AddRange(Directory.EnumerateFiles(sub, "*.*", SearchOption.TopDirectoryOnly));
        return files.Where(f => f.EndsWith(".edb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".e2k", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string ReadHead(string path, int chars)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[chars];
        int n = reader.Read(buffer, 0, chars);
        return new string(buffer, 0, n);
    }

    private sealed record JobRow(string Job, string ModelFolder);

    private static IEnumerable<JobRow> ReadCensus(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = Parse(lines[0]);
        int job = header.IndexOf("job"), folder = header.IndexOf("model_folder");
        if (job < 0 || folder < 0) throw new InvalidOperationException("census.csv needs job and model_folder columns");
        foreach (var line in lines.Skip(1))
        {
            var f = Parse(line);
            if (f.Count > Math.Max(job, folder)) yield return new JobRow(f[job], f[folder]);
        }
    }

    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static List<string> Parse(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
