namespace Kor.Operations.EngineeringTools.Intake;

public enum PathReason
{
    BecameSlab, BecameColumnByDeclaredSize, BecameColumnByShape,
    EmittedAsLine,
    MarkupOnlyMode, FurnitureRegion, GridAxis, Underline, PaperFill, SheetFrame, FrameEdgeLine,
    GridLineExcluded, ColumnTooSmall, UnfilledSmallShape, ColumnAspect, TooShort, TooFewPoints,
    /// <summary>
    /// Never reached the classifier: the read it classifies thins points closer than
    /// <c>MinVertexDistanceMm</c> and drops a subpath left with fewer than two, so hatching and
    /// dash fragments vanish before any rule sees them. 85–91% of the paths on 31130's hatched
    /// parkade plans go this way; the ledger counts them here, against the unthinned read.
    /// </summary>
    CollapsedByThinning,
    NoInk,
    BecameWall,
    /// <summary>One dash of a footing outline the schedule sizes (Intake.FootingOutlines); the object index is the footing's.</summary>
    BecameFooting,
    /// <summary>
    /// One dash of a dashed box of a scheduled footing size that no label on the plan names. The box
    /// is still emitted with the mark its size matches, flagged, for the consumer to judge; the
    /// ledger does not call its pieces read (audit F2, 2026-09-08).
    /// </summary>
    FootingBoxNoLabel,
}

/// <summary>Input path index and its decision; ObjectIndex is zero-based in the corresponding geometry list.</summary>
public sealed record PathFate(int PathIndex, Disposition Disposition, PathReason Reason, int? ObjectIndex)
{
    public static Disposition DispositionOf(PathReason reason) => reason switch
    {
        // GridAxis is read since step 8: the line through a bubble is a named axis in Geometry.GridAxes
        // and on the DXF's GRID layer; the path itself has no object index because many pieces make one axis.
        PathReason.BecameSlab or PathReason.BecameColumnByDeclaredSize or PathReason.BecameColumnByShape or PathReason.BecameWall
            or PathReason.BecameFooting or PathReason.GridAxis
            => Disposition.Read,
        PathReason.EmittedAsLine or PathReason.FootingBoxNoLabel => Disposition.Unaccounted,
        PathReason.MarkupOnlyMode or PathReason.FurnitureRegion or PathReason.Underline
            or PathReason.PaperFill or PathReason.SheetFrame or PathReason.FrameEdgeLine
            or PathReason.GridLineExcluded or PathReason.ColumnTooSmall or PathReason.UnfilledSmallShape
            or PathReason.ColumnAspect or PathReason.TooShort or PathReason.TooFewPoints
            or PathReason.CollapsedByThinning or PathReason.NoInk => Disposition.Discarded,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown path reason."),
    };
}
