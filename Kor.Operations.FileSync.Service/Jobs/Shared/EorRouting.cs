#nullable enable
using System.Text;

namespace Kor.Operations.FileSync.Service.Jobs.Shared;

// The decisions behind the field-review round trip, kept pure so the tests
// exercise the exact code the two runners call.
//
//   1st @ 00:00  MoveReportsToEor   <project>/Reports/*  ->  _FIELD REVIEWS TO INITIAL/<EOR>/
//                                   + drops "Acknowledge and Move To Server <Month>.txt"
//                                   + records (period, EOR, control file) in FileSync.EorControlFiles
//   5th @ 08:00  MoveReportsToToSend <EOR>/*  ->  \\KOR-FS01\...\To Send   ONLY IF that record
//                                   exists for this period and the control file is now gone.
//
// The one rule both halves serve: a report leaves SharePoint only after a
// NAMED engineer has acknowledged it. Acknowledgement is evidence (a note we
// dropped, that they deleted), never the absence of a note.
//
// What broke on 2026-09-17 and why each piece here exists:
//   - EOR.csv carried a TAB inside the quotes ("\tWurmlinger") on 60 of 238
//     rows; Trim().Trim('"') left it in, so nothing matched and the reports
//     went to CatchAll.                       -> CleanCsvField strips inside the quotes too.
//   - "Alcazar Pastrana" could never match "Omar Alcazar Pastrana" because the
//     folder lookup was keyed by single words. -> ResolveEor tries the whole
//                                                 name, then a word.
//   - CatchAll never gets a control file, and the 5th read "no control file"
//     as "acknowledged", so ~70 un-initialled reports a month went to the
//     server.                                  -> DecideSweep: CatchAll is never
//                                                 swept; no record = no sweep.
internal static class EorRouting
{
    // --- EOR.csv --------------------------------------------------------------

    // ProjectNumber -> EOR surname, as written in _FIELD REVIEWS TO INITIAL/EOR.csv.
    // Header row is required (ProjectNumber, EOR); first mapping per project wins.
    public static Dictionary<string, string> ParseEorCsv(string csv)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header is null) return map;

        var cols = SplitCsvLine(header);
        int projIdx = -1, eorIdx = -1;
        for (int i = 0; i < cols.Length; i++)
        {
            var h = CleanCsvField(cols[i]);
            if (string.Equals(h, "ProjectNumber", StringComparison.OrdinalIgnoreCase)) projIdx = i;
            else if (string.Equals(h, "EOR", StringComparison.OrdinalIgnoreCase)) eorIdx = i;
        }

        if (projIdx < 0 || eorIdx < 0) return map;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = SplitCsvLine(line);
            if (parts.Length <= Math.Max(projIdx, eorIdx)) continue;
            var p = CleanCsvField(parts[projIdx]);
            var e = CleanCsvField(parts[eorIdx]);
            if (!string.IsNullOrWhiteSpace(p) && !map.ContainsKey(p))
                map[p] = e;
        }

        return map;
    }

    // Whitespace outside the quotes, the quotes, then whitespace INSIDE them,
    // then a stray BOM. "\tWurmlinger" -> Wurmlinger. Order matters: the old
    // Trim().Trim('"') stopped at the quotes and left the tab in.
    public static string CleanCsvField(string raw)
        => raw.Trim().Trim('"').Trim().Trim('\uFEFF').Trim();

    public static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
            }
            else if (c == ',' && !inQuote)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    // --- project -> EOR folder --------------------------------------------------

    public enum EorReason
    {
        Matched,          // routed to a named EOR folder
        NotInCsv,         // project number has no row in EOR.csv
        EmptyName,        // row exists but the EOR cell is blank
        NoFolderForName,  // row names someone with no folder under _FIELD REVIEWS TO INITIAL
    }

    public sealed record EorResolution(string Folder, EorReason Reason, string? CsvName)
    {
        public bool IsCatchAll => Reason != EorReason.Matched;
    }

    // Resolves the surname in EOR.csv to a folder name. Whole-name match first
    // ("Alcazar Pastrana" -> "Omar Alcazar Pastrana"), then any single word
    // ("Markulin" -> "John Markulin"), which is what the PS1 did. A name that
    // matches nothing lands in CatchAll WITH the reason, so the 1st-of-month
    // run can say so instead of piling it up silently.
    public static EorResolution ResolveEor(
        string projectNumber,
        IReadOnlyDictionary<string, string> eorMap,
        IReadOnlyCollection<string> eorFolders,
        string catchAllName)
    {
        if (!eorMap.TryGetValue(projectNumber, out var rawName))
            return new EorResolution(catchAllName, EorReason.NotInCsv, null);

        var name = NormalizeSpaces(rawName);
        if (name.Length == 0)
            return new EorResolution(catchAllName, EorReason.EmptyName, rawName);

        var candidates = eorFolders
            .Where(f => !string.Equals(f, catchAllName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1) the whole name is the folder, or ends the folder ("Alcazar Pastrana" in "Omar Alcazar Pastrana")
        foreach (var folder in candidates)
        {
            var norm = NormalizeSpaces(folder);
            if (string.Equals(norm, name, StringComparison.OrdinalIgnoreCase)
                || norm.EndsWith(" " + name, StringComparison.OrdinalIgnoreCase))
                return new EorResolution(folder, EorReason.Matched, rawName);
        }

        // 2) any word of the folder equals the whole name ("Markulin" in "John Markulin")
        foreach (var folder in candidates)
        {
            if (folder.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Any(w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase)))
                return new EorResolution(folder, EorReason.Matched, rawName);
        }

        return new EorResolution(catchAllName, EorReason.NoFolderForName, rawName);
    }

    private static string NormalizeSpaces(string s)
        => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // --- 5th of month: sweep or not ---------------------------------------------

    public enum SweepAction
    {
        Sweep,            // a note was dropped this period and it is gone -> the EOR acknowledged
        SkipCatchAll,     // nobody is asked to initial CatchAll; it is reported, never swept
        SkipNoBatch,      // no note was dropped in this folder this period -> nothing was acknowledged
        SkipNotAcked,     // the note is still there
    }

    public sealed record SweepDecision(SweepAction Action, string? ControlFileName);

    // pendingControlFiles: EOR folder -> control file name, as recorded by the
    // 1st-of-month run for THIS period (FileSync.EorControlFiles). The stored
    // name is the truth, not a recomputation from today's date, so a manual
    // fire in a later month still looks for the note that was actually dropped.
    public static SweepDecision DecideSweep(
        string eorFolder,
        string catchAllName,
        IReadOnlyDictionary<string, string> pendingControlFiles,
        IReadOnlyCollection<string> fileNamesInFolder)
    {
        if (string.Equals(eorFolder, catchAllName, StringComparison.OrdinalIgnoreCase))
            return new SweepDecision(SweepAction.SkipCatchAll, null);

        if (!pendingControlFiles.TryGetValue(eorFolder, out var controlFileName) || string.IsNullOrWhiteSpace(controlFileName))
            return new SweepDecision(SweepAction.SkipNoBatch, null);

        var stillThere = fileNamesInFolder.Any(n => string.Equals(n, controlFileName, StringComparison.OrdinalIgnoreCase));
        return stillThere
            ? new SweepDecision(SweepAction.SkipNotAcked, controlFileName)
            : new SweepDecision(SweepAction.Sweep, controlFileName);
    }

    // 'yyyy-MM' key for FileSync.EorControlFiles. Separate from the 'MM-yyyy'
    // tag the audit CSVs have always used, because this one has to sort.
    public static string PeriodKey(DateTimeOffset when)
        => when.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
