using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MsgReader.Outlook;
using MsgReader.Mime;
using MsgReaderMessage = MsgReader.Outlook.Storage.Message;
using MsgReaderMimeMessage = MsgReader.Mime.Message;

// IMPORTANT:
//  - Reference your existing project that contains EmailIndexDatabase and EmailMetadata,
//    OR add those two source files to this console app project.
using EmailFilerv2; // Namespace where EmailIndexDatabase and EmailMetadata live

namespace EmailIndexer.Maintenance
{
    internal static class Program
    {
        // Defaults
        private static readonly string DefaultRoot = @"\\kor-fs01\Projects\Projects";
        private static readonly string[] DefaultCategories = new[]
        {
            "01 Small Jobs","03 Residential","04 Commercial","05 Office",
            "06 Hotel","07 Industrial-Garage","08 Inst-Rec-Church","09 Reno-Seismic-Resto"
        };

        private static int MaxDegree => Math.Max(1, Environment.ProcessorCount - 1);

        private class Options
        {
            public string Root = DefaultRoot;
            public List<string> Categories = DefaultCategories.ToList();
            public string ProjectFilter = "";     // project name/code contains this
            public bool IncludeBodies = false;    // build/update FTS (body) for changed files
            public bool Force = false;            // ignore delta, rebuild headers (and bodies if IncludeBodies)
            public bool DryRun = false;           // just report what would happen
            public int Parallelism = MaxDegree;   // per-project file parse concurrency
            public string LogFile = "";           // optional log file path
        }

        private static int Main(string[] args)
        {
            var opt = ParseArgs(args);
            var started = DateTime.Now;

            using var log = string.IsNullOrWhiteSpace(opt.LogFile) ? null : new StreamWriter(opt.LogFile, append: true);
            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                Console.WriteLine(line);
                log?.WriteLine(line);
                log?.Flush();
            }

            try
            {
                Log("=== EmailIndexer.Maintenance start ===");
                Log($"Root: {opt.Root}");
                Log($"Categories: {string.Join("; ", opt.Categories)}");
                Log($"Filter: {(string.IsNullOrWhiteSpace(opt.ProjectFilter) ? "(none)" : opt.ProjectFilter)}");
                Log($"IncludeBodies: {opt.IncludeBodies}  Force: {opt.Force}  DryRun: {opt.DryRun}  Parallelism: {opt.Parallelism}");

                var projectDirs = EnumerateProjects(opt.Root, opt.Categories, opt.ProjectFilter).ToList();
                Log($"Found {projectDirs.Count:N0} project(s) with an Emails folder.");

                int completed = 0, skipped = 0, failed = 0;

                foreach (var projectPath in projectDirs)
                {
                    var name = Path.GetFileName(projectPath);
                    var emailsPath = Path.Combine(projectPath, "Emails");
                    var dbPath = Path.Combine(emailsPath, ".email_index.db");

                    Log($"--- [{name}] ---");
                    Log($"DB: {dbPath}");

                    if (opt.DryRun)
                    {
                        Log("DryRun=true → skipping actual indexing.");
                        skipped++;
                        continue;
                    }

                    try
                    {
                        int changed, updated, total;
                        var sw = Stopwatch.StartNew();
                        IndexProject(projectPath, opt.IncludeBodies, opt.Force, opt.Parallelism, out total, out changed, out updated);
                        sw.Stop();

                        Log($"Indexed in {sw.Elapsed:mm\\:ss}. Files scanned: {total:N0}, changed: {changed:N0}, updated: {updated:N0}");
                        completed++;
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR indexing '{name}': {ex.Message}");
                        failed++;
                    }
                }

                var dur = DateTime.Now - started;
                Log($"=== Done. Completed: {completed}, Skipped: {skipped}, Failed: {failed}. Elapsed {dur:g} ===");
                return failed == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal: " + ex);
                return 1;
            }
        }

        private static Options ParseArgs(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--root":
                        o.Root = args[++i];
                        break;
                    case "--categories":
                        o.Categories = args[++i].Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                        break;
                    case "--project":
                    case "--filter":
                        o.ProjectFilter = args[++i];
                        break;
                    case "--include-bodies":
                    case "--fts":
                        o.IncludeBodies = true;
                        break;
                    case "--force":
                        o.Force = true;
                        break;
                    case "--dry-run":
                        o.DryRun = true;
                        break;
                    case "--parallel":
                        o.Parallelism = Math.Max(1, int.Parse(args[++i]));
                        break;
                    case "--log":
                        o.LogFile = args[++i];
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelp();
                        Environment.Exit(0);
                        break;
                    default:
                        Console.WriteLine($"Unknown arg: {a}");
                        PrintHelp();
                        Environment.Exit(1);
                        break;
                }
            }
            return o;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
EmailIndexer.Maintenance

Usage:
  EmailIndexer.Maintenance.exe [--root \\kor-fs01\Projects\Projects]
                               [--categories ""01 Small Jobs;03 Residential;...""]
                               [--project <contains-text>]
                               [--include-bodies] [--force]
                               [--parallel 6]
                               [--dry-run]
                               [--log C:\Logs\email-indexer.log]

Examples:
  EmailIndexer.Maintenance.exe --include-bodies --parallel 6
  EmailIndexer.Maintenance.exe --project 31100-01 --force
  EmailIndexer.Maintenance.exe --categories ""04 Commercial;05 Office"" --fts
");
        }

        private static IEnumerable<string> EnumerateProjects(string root, IEnumerable<string> categories, string containsFilter)
        {
            if (!Directory.Exists(root)) yield break;

            foreach (var cat in categories)
            {
                var catPath = Path.Combine(root, cat);
                if (!Directory.Exists(catPath)) continue;

                foreach (var proj in Directory.EnumerateDirectories(catPath))
                {
                    var emails = Path.Combine(proj, "Emails");
                    if (!Directory.Exists(emails)) continue;

                    if (!string.IsNullOrWhiteSpace(containsFilter))
                    {
                        var name = Path.GetFileName(proj);
                        if (name.IndexOf(containsFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            proj.IndexOf(containsFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    yield return proj;
                }
            }
        }

        private static void IndexProject(
            string projectPath,
            bool includeBodies,
            bool force,
            int parallelism,
            out int totalScanned,
            out int changed,
            out int updated)
        {
            string emailsRoot = Path.Combine(projectPath, "Emails");
            if (!Directory.Exists(emailsRoot))
            {
                totalScanned = changed = updated = 0;
                return;
            }

            var db = new EmailIndexDatabase(Path.Combine(emailsRoot, ".email_index.db"));

            // Collect target files
            var currentFiles = Directory.GetDirectories(emailsRoot)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.msg"))
                .Concat(Directory.GetDirectories(emailsRoot)
                    .SelectMany(dir => Directory.EnumerateFiles(dir, "*.eml")))
                .ToList();

            totalScanned = currentFiles.Count;

            if (force)
            {
                // Full rebuild (headers); bodies optionally after
                var parsed = ParseHeaders(currentFiles, parallelism);
                WriteHeaders(db, parsed);
                updated = parsed.Count;

                if (includeBodies)
                {
                    var bodies = ParseBodies(parsed.Select(p => p.FilePath).ToList(), parallelism);
                    WriteBodies(db, bodies);
                }

                changed = updated; // on force, all considered changed
                return;
            }

            // Delta
            var known = db.GetKnownFilesMap(); // Dictionary<string, (long length, long mtimeTicks)>
            var toParse = new ConcurrentBag<string>();

            Parallel.ForEach(currentFiles,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                path =>
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        long mt = fi.LastWriteTimeUtc.Ticks;
                        long len = fi.Length;
                        if (!known.TryGetValue(path, out var prior) || prior.length != len || prior.mtimeTicks != mt)
                            toParse.Add(path);
                    }
                    catch { }
                });

            changed = toParse.Count;
            var headers = ParseHeaders(toParse.ToList(), parallelism);
            WriteHeaders(db, headers);
            updated = headers.Count;

            if (includeBodies)
            {
                var bodies = ParseBodies(headers.Select(h => h.FilePath).ToList(), parallelism);
                WriteBodies(db, bodies);
            }

            // Cleanup deletions
            var currentSet = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);
            db.DeleteMissingExcept(currentSet);
        }

        private static List<EmailMetadata> ParseHeaders(List<string> files, int parallelism)
        {
            var parsed = new ConcurrentBag<EmailMetadata>();
            Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                path =>
                {
                    try { parsed.Add(FastHeaderOnly(path)); }
                    catch { }
                });
            return parsed.ToList();
        }

        private static List<(string path, string body)> ParseBodies(List<string> files, int parallelism)
        {
            var bodies = new ConcurrentBag<(string path, string body)>();
            Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                path =>
                {
                    try { bodies.Add((path, TryExtractBody(path))); }
                    catch { bodies.Add((path, "")); }
                });
            return bodies.ToList();
        }

        private static void WriteHeaders(EmailIndexDatabase db, List<EmailMetadata> items)
        {
            db.BeginTransaction();
            try
            {
                using var cmd = db.CreateCommand(); // batched upserts
                foreach (var m in items)
                {
                    var fi = new FileInfo(m.FilePath);
                    db.UpsertHeader(m, fi.Length, fi.LastWriteTimeUtc.Ticks, cmd);
                }
                db.CommitTransaction();
            }
            catch
            {
                db.RollbackTransaction();
                throw;
            }
        }

        private static void WriteBodies(EmailIndexDatabase db, List<(string path, string body)> items)
        {
            db.BeginTransaction();
            try
            {
                using var cmd = db.CreateCommand(); // batched upserts
                foreach (var b in items)
                    db.UpsertBodyFts(b.path, b.body, cmd);
                db.CommitTransaction();
            }
            catch
            {
                db.RollbackTransaction();
                throw;
            }
        }

        // ---- Parsing helpers (fast header-only + optional body) ----
        private static EmailMetadata FastHeaderOnly(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string sender = "";
            string dateStr = "";
            string subj = fileName;

            try
            {
                if (filePath.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var msg = new MsgReaderMessage(filePath))
                    {
                        sender = msg.Sender != null ? (msg.Sender.Email ?? msg.Sender.DisplayName ?? "") : "";
                        dateStr = msg.SentOn != null ? msg.SentOn.Value.ToString("yyyy-MM-dd HH:mm") : "";
                        subj = string.IsNullOrWhiteSpace(msg.Subject) ? fileName : msg.Subject;
                    }
                }
                else // .eml
                {
                    using (var fs = File.OpenRead(filePath))
                    {
                        var eml = new MsgReaderMimeMessage(fs);
                        sender = eml.Headers != null && eml.Headers.From != null
                            ? (eml.Headers.From.Address ?? eml.Headers.From.DisplayName ?? "")
                            : "";
                        dateStr = eml.Headers != null ? eml.Headers.DateSent.ToString("yyyy-MM-dd HH:mm") : "";
                        subj = eml.Headers != null && !string.IsNullOrWhiteSpace(eml.Headers.Subject) ? eml.Headers.Subject : fileName;
                    }
                }
            }
            catch { }

            return new EmailMetadata
            {
                FilePath = filePath,
                FileName = subj,
                Sender = sender,
                SendDate = dateStr,
                BodyPreview = null
            };
        }

        private static string TryExtractBody(string filePath)
        {
            try
            {
                if (filePath.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var msg = new MsgReaderMessage(filePath))
                    {
                        if (!string.IsNullOrEmpty(msg.BodyText)) return msg.BodyText;
                        return msg.BodyHtml ?? "";
                    }
                }
                else
                {
                    using (var fs = File.OpenRead(filePath))
                    {
                        var eml = new MsgReaderMimeMessage(fs);
                        var text = eml.TextBody != null ? eml.TextBody.GetBodyAsText() : "";
                        var html = eml.HtmlBody != null ? eml.HtmlBody.GetBodyAsText() : "";
                        return string.IsNullOrEmpty(text) ? html : text + "\n" + html;
                    }
                }
            }
            catch { return ""; }
        }
    }
}
