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

    /// <summary>The reinforcing call-out change for one sheet between two issues. Weights cover the
    /// QUANTITY-BEARING call-outs only (count × length × CSA mass — the plan "36-15M4700@125" and
    /// bar-list "16-15M13.9" forms); intensity ("15M@200") and continuous ("C15M1200@350") changes
    /// carry no computable weight and are counted in <see cref="UnweighedChanges"/>, never guessed.</summary>
    public sealed record RebarSheetChange(
        string Sheet,
        string Title,
        RebarChangeStatus Status,
        int BeforeCount,
        int AfterCount,
        int NetDelta,
        IReadOnlyList<string> Added,    // e.g. "+5x 20M@150", "+1x 36-15M4700@125  (+585 lb)"
        IReadOnlyList<string> Removed,  // e.g. "-2x 20M@100"
        double AddedWeightLb = 0,
        double RemovedWeightLb = 0,
        int UnweighedChanges = 0)
    {
        public double NetWeightLb => AddedWeightLb - RemovedWeightLb;
    }

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
        string AfterLabel,
        // Total reinforcing call-outs the extractor actually READ across both issues (sum of every sheet's
        // before+after counts). The "can't-read" guard: a comparison of many sheets that read ~0 call-outs
        // means the set's annotation grammar wasn't recognised — NOT that nothing changed. Hosts must surface
        // that loudly rather than report a confident "0 changed".
        int TotalCalloutsRead = 0,
        // Weight of the weighable changes (quantity-bearing call-outs only; see RebarSheetChange).
        double AddedWeightLb = 0,
        double RemovedWeightLb = 0,
        int UnweighedChanges = 0)
    {
        public double NetWeightLb => AddedWeightLb - RemovedWeightLb;
    }
}
