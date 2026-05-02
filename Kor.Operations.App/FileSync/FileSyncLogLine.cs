#nullable enable
using System;

namespace Kor.Operations.App.FileSync;

// One Serilog log entry as surfaced to the Log Viewer DataGrid. Continuation
// lines (sanitized exception message + stack trace) are folded into Details
// during parse so each row stays a single grid item.
public sealed class FileSyncLogLine
{
    public DateTimeOffset Timestamp { get; init; }

    // Three-letter Serilog level: VRB/DBG/INF/WRN/ERR/FTL.
    public string Level { get; init; } = "INF";

    // Source type (Microsoft.Extensions.Logging SourceContext). Last segment
    // -- e.g. "RenameReportsUploadsRunner" -- is what the user actually scans;
    // we expose both the full path and the short form for filtering vs display.
    public string SourceContext { get; init; } = string.Empty;

    public string ShortSource => string.IsNullOrEmpty(SourceContext)
        ? string.Empty
        : SourceContext.Substring(SourceContext.LastIndexOf('.') + 1);

    public string Message { get; init; } = string.Empty;

    // Exception message + stack trace, joined with newlines. Empty for entries
    // without an exception. Surfaced in the right-hand detail panel.
    public string Details { get; set; } = string.Empty;

    // Numeric severity for "Warn+ / Error+ / Fatal+" filter shortcuts.
    public int Severity => Level switch
    {
        "VRB" => 0,
        "DBG" => 1,
        "INF" => 2,
        "WRN" => 3,
        "ERR" => 4,
        "FTL" => 5,
        _ => 2,
    };

    // Single string for "copy line" right-click. Reconstructs the on-disk
    // line plus any exception block.
    public string ToCopyText()
    {
        var head = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {SourceContext} {Message}";
        return string.IsNullOrEmpty(Details) ? head : head + Environment.NewLine + Details;
    }
}
