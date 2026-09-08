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
}

/// <summary>Input path index and its decision; ObjectIndex is zero-based in the corresponding geometry list.</summary>
public sealed record PathFate(int PathIndex, Disposition Disposition, PathReason Reason, int? ObjectIndex)
{
    public static Disposition DispositionOf(PathReason reason) => reason switch
    {
        PathReason.BecameSlab or PathReason.BecameColumnByDeclaredSize or PathReason.BecameColumnByShape
            => Disposition.Read,
        PathReason.EmittedAsLine => Disposition.Unaccounted,
        PathReason.MarkupOnlyMode or PathReason.FurnitureRegion or PathReason.GridAxis or PathReason.Underline
            or PathReason.PaperFill or PathReason.SheetFrame or PathReason.FrameEdgeLine
            or PathReason.GridLineExcluded or PathReason.ColumnTooSmall or PathReason.UnfilledSmallShape
            or PathReason.ColumnAspect or PathReason.TooShort or PathReason.TooFewPoints
            or PathReason.CollapsedByThinning => Disposition.Discarded,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown path reason."),
    };
}
