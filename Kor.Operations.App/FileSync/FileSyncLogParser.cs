#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Kor.Operations.App.FileSync;

// Parses Serilog's text format as configured in
// Kor.Operations.FileSync.Service/Logging/SerilogBootstrap.cs:
//
//   {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}
//   {SanitizedExceptionMessage}
//   {Exception}
//
// A "real" entry starts with a timestamp; anything else is a continuation
// line (exception message + stack trace) that belongs to the previous entry.
// Empty lines are skipped.
public static class FileSyncLogParser
{
    // Compiled once. Anchored at start so message bodies that happen to
    // contain a timestamp don't get mistaken for new entries.
    private static readonly Regex EntryHead = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>[A-Z]{3})\] (?<src>\S+) (?<msg>.*)$",
        RegexOptions.Compiled);

    // Reads from `reader` to EOF, parses entries, and appends to `into`.
    // `lastEntry` is mutated so callers can keep folding continuation lines
    // across multiple read passes (the file-tailer reads in chunks).
    public static void ParseInto(TextReader reader, List<FileSyncLogLine> into, ref FileSyncLogLine? lastEntry)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                // Blank line inside an exception block: preserve it as a
                // separator. Outside an exception block it's just noise.
                if (lastEntry is not null)
                    lastEntry.Details = lastEntry.Details.Length == 0 ? string.Empty : lastEntry.Details + "\n";
                continue;
            }

            var m = EntryHead.Match(line);
            if (m.Success)
            {
                var entry = new FileSyncLogLine
                {
                    Timestamp = DateTimeOffset.ParseExact(
                        m.Groups["ts"].Value,
                        "yyyy-MM-dd HH:mm:ss.fff zzz",
                        CultureInfo.InvariantCulture),
                    Level = m.Groups["lvl"].Value,
                    SourceContext = m.Groups["src"].Value,
                    Message = m.Groups["msg"].Value,
                };
                into.Add(entry);
                lastEntry = entry;
            }
            else if (lastEntry is not null)
            {
                lastEntry.Details = lastEntry.Details.Length == 0
                    ? line
                    : lastEntry.Details + "\n" + line;
            }
            // else: orphan continuation before any entry -- discard.
        }
    }
}
