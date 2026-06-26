#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>Rebar call-outs (e.g. "15M@200") aggregated for one drawing sheet.</summary>
    public sealed record SheetCallouts(
        string Sheet,
        string Title,
        IReadOnlyDictionary<string, int> Callouts);

    public enum RebarChangeStatus
    {
        Unchanged,
        Changed,
        NewSheet,
        RemovedSheet
    }

    /// <summary>The reinforcing call-out change for one sheet between two issues.</summary>
    public sealed record RebarSheetChange(
        string Sheet,
        string Title,
        RebarChangeStatus Status,
        int BeforeCount,
        int AfterCount,
        int NetDelta,
        IReadOnlyList<string> Added,    // e.g. "+5x 20M@150"
        IReadOnlyList<string> Removed); // e.g. "-2x 20M@100"

    public sealed record RebarChangeResult(
        IReadOnlyList<RebarSheetChange> Sheets,
        int SheetsCompared,
        int SheetsChanged,
        int ContentChanged,
        int NewSheets,
        int RemovedSheets,
        int CalloutsAdded,
        int CalloutsRemoved,
        string BeforeLabel,
        string AfterLabel);
}
