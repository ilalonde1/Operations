using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Knowledge;

// THE PROFESSION'S KNOWLEDGE, INGESTED (WP7, 2026-09-16). Reads KOR's copy of a code, a standard or a guideline and
// lifts the knowledge.Clause rows of that source (migration 096) from what was remembered to what the document says:
// the page each clause stands on and, where it states a number in the row's units, the value. Never the prose.
//   takeoff knowledge-ingest <pdf> --source <code> [--clause <ref> ...] [--rules-db <conn>] [--dry-run]
// Without --clause every live clause of the source is looked for. --dry-run finds and reports, writes nothing.
internal static class KnowledgeIngestVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("knowledge-ingest", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff knowledge-ingest <pdf> --source <code> [--clause <ref> ...] [--rules-db <conn>] [--dry-run]"); return 1; }
        string pdf = args[1];
        if (!File.Exists(pdf)) { Console.Error.WriteLine($"PDF not found '{pdf}'."); return 2; }
        string? source = null, rulesDb = null, title = null, publisher = null, licence = null, url = null; bool dry = false, index = false, items = false; var only = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) source = args[++i];
            else if (args[i].Equals("--clause", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) only.Add(args[++i]);
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rulesDb = args[++i];
            else if (args[i].Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) dry = true;
            // --register: write the source row (the edition read off the document) and --index its section headings
            else if (args[i].Equals("--register", StringComparison.OrdinalIgnoreCase) && i + 3 < args.Length) { title = args[++i]; publisher = args[++i]; licence = args[++i]; }
            else if (args[i].Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) url = args[++i];
            else if (args[i].Equals("--index", StringComparison.OrdinalIgnoreCase)) index = true;
            // --items: a short document numbered 1., 2., 3. (the Code of Ethics) - each item's text as the requirement; published-free or ours only
            else if (args[i].Equals("--items", StringComparison.OrdinalIgnoreCase)) items = true;
        }
        if (source is null) { Console.Error.WriteLine("--source <code> is required (a knowledge.Source.Code: NBC-2020, BCBC-2024, KOR-PPMP ...)."); return 1; }
        rulesDb ??= Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(rulesDb)) { Console.Error.WriteLine($"KorStandards is required: set {RuleSettings.ConnectionEnvironmentVariable} or pass --rules-db."); return 1; }

        if (title is not null)
        {
            var firstPages = ClauseIngest.ReadPages(pdf);
            string? edition = ClauseIngest.ReadEdition(firstPages);
            string copy = Path.GetFullPath(pdf);
            if (dry) Console.WriteLine($"{source}: would register \"{title}\" ({publisher}, {licence}, edition {edition ?? "-"}) copy {copy}{(url is null ? "" : $" url {url}")}");
            else
            {
                bool isNew = ClauseIngest.RegisterSource(rulesDb, source, title, publisher!, edition, licence!, copy, url, null);
                Console.WriteLine($"{source}: {(isNew ? "registered" : "refreshed")} \"{title}\" ({publisher}, {licence}, edition {edition ?? "-"})");
            }
        }
        if (items)
        {
            if (licence is not null && licence.Equals("licensed-cite-only", StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("--items stores the items' text: not for a licensed-cite-only source."); return 1; }
            var numbered = ClauseIngest.IndexNumberedItems(pdf);
            Console.WriteLine($"{source}: {numbered.Count} numbered item(s){(dry ? " (dry run)" : "")}");
            foreach (var s in numbered) Console.WriteLine($"  {s.Ref,-4} p.{s.Page,-3} {(s.Title.Length > 110 ? s.Title[..110] + "..." : s.Title)}");
            if (!dry) Console.WriteLine($"  {ClauseIngest.WriteSections(rulesDb, source, numbered, $"takeoff knowledge-ingest {DateTime.Now:yyyy-MM-dd} {Path.GetFileName(pdf)}")} clause row(s) written (items as requirements, read-from-source)");
        }
        if (index)
        {
            var sections = ClauseIngest.IndexSections(pdf);
            Console.WriteLine($"{source}: {sections.Count} numbered section heading(s) with their pages{(dry ? " (dry run)" : "")}");
            foreach (var s in sections.Take(dry ? sections.Count : 12)) Console.WriteLine($"  {s.Ref,-10} p.{s.Page,-4} {s.Title}");
            if (!dry && sections.Count > 12) Console.WriteLine($"  ... {sections.Count - 12} more");
            if (!dry)
            {
                int n = ClauseIngest.WriteSections(rulesDb, source, sections, $"takeoff knowledge-ingest {DateTime.Now:yyyy-MM-dd} {Path.GetFileName(pdf)}");
                Console.WriteLine($"  {n} clause row(s) written (headings as requirements, read-from-source)");
            }
            if (only.Count == 0) return 0;
        }
        if ((title is not null || items) && only.Count == 0 && !index) return 0;

        var clauses = ClauseIngest.LoadClauses(rulesDb, source);
        if (only.Count > 0) clauses = clauses.Where(c => only.Contains(c.ClauseRef, StringComparer.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"{source}: {clauses.Count} live clause(s) to look for in {Path.GetFileName(pdf)}");
        if (clauses.Count == 0) return 0;

        var pages = ClauseIngest.ReadPages(pdf);
        Console.WriteLine($"  {pages.Count} page(s) read as text");
        string readBy = $"takeoff knowledge-ingest {DateTime.Now:yyyy-MM-dd} {Path.GetFileName(pdf)}";
        int found = 0, lifted = 0;
        foreach (var c in clauses)
        {
            var f = ClauseIngest.Find(pages, c.ClauseRef, c.Units);
            if (f is null) { Console.WriteLine($"  {c.ClauseRef,-14} {c.Topic,-28} NOT FOUND at the head of a sentence on any page"); continue; }
            found++;
            string what = $"page {f.Page}" + (f.Value is not null ? $", states {f.Value} {f.Units}" : c.Units is not null ? ", no value in its units within reach" : "");
            string was = $"(row: {c.Confidence}, page {(c.Page?.ToString() ?? "-")}, value {c.Value ?? "-"})";
            if (dry) Console.WriteLine($"  {c.ClauseRef,-14} {c.Topic,-28} {what} {was}  \"{f.Head}\"");
            else { string r = ClauseIngest.Lift(rulesDb, c, f, readBy); lifted++; Console.WriteLine($"  {c.ClauseRef,-14} {c.Topic,-28} {what} {was} -> {r}"); }
        }
        Console.WriteLine($"  found {found} of {clauses.Count}; {(dry ? "dry run, nothing written" : $"{lifted} row(s) lifted")}");
        return 0;
    }
}
