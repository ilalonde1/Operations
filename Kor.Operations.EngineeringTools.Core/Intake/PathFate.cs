namespace Kor.Operations.EngineeringTools.Intake;

public enum PathReason
{
    BecameSlab, BecameColumnByDeclaredSize, BecameColumnByShape,
    EmittedAsLine,
    MarkupOnlyMode, FurnitureRegion, GridAxis, Underline, PaperFill, SheetFrame, FrameEdgeLine,
    /// <summary>
    /// A paper-coloured fill painted over a wall after it, across its thickness and at least a door
    /// wide: the drafter's knockout for an opening. It is read — the wall it opened is emitted as
    /// the piers on either side of it (intake step 14). The object index is into Geometry.Doorways.
    /// </summary>
    Doorway,
    /// <summary>
    /// A clip path drawn immediately before a wall's fill, in pieces that lie on the wall: the fill
    /// shows only through them, so the wall is emitted as those pieces (intake step 14; Revit's
    /// export clips a core face to its piers this way — 31168 p22). Read; the object index is the
    /// first pier. A clip that shapes nothing read stays NoInk.
    /// </summary>
    ClipOfWall,
    /// <summary>
    /// One of two parallel face lines a wall's thickness apart, overlapping a wall's length, with
    /// no third line at the same spacing beyond either (a hatch has many): a wall drawn as its
    /// two faces (intake step 20). Read; the object index is the wall.
    /// </summary>
    BecameWallFace,
    /// <summary>
    /// A filled band thicker than the thickest wall and many times longer than it is wide
    /// (intake step 21): 31168's parkade plans carry a 63"–66" grey band 127 ft long at the
    /// property line with the real 12"–15" wall drawn as two lines inside it. It is not a slab
    /// and not a wall; it stays unaccounted, inked and named, for the ledger.
    /// </summary>
    Band,
    /// <summary>
    /// A line on the sheet's match line — the line labelled MATCH LINE that a plan too wide for one
    /// sheet was split on (intake step 22). Read: the line goes to the DXF on a MATCH layer, and
    /// the DXF side joins the sheets that share it into one plan. No sheet closes a floor at a
    /// match line on its own.
    /// </summary>
    MatchLine,
    /// <summary>
    /// One line of the closed loop that is a floor's edge (intake step 24). A plan whose perimeter
    /// is a slab edge rather than a wall — every tower plan, every ground floor — draws that edge
    /// as ordinary lines, and the loop they make is the storey's plate. Read; the object index is
    /// the slab.
    /// </summary>
    BecameSlabEdge,
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
            or PathReason.BecameFooting or PathReason.GridAxis or PathReason.Doorway or PathReason.ClipOfWall
            or PathReason.BecameWallFace or PathReason.MatchLine or PathReason.BecameSlabEdge
            => Disposition.Read,
        PathReason.EmittedAsLine or PathReason.FootingBoxNoLabel or PathReason.Band => Disposition.Unaccounted,
        PathReason.MarkupOnlyMode or PathReason.FurnitureRegion or PathReason.Underline
            or PathReason.PaperFill or PathReason.SheetFrame or PathReason.FrameEdgeLine
            or PathReason.GridLineExcluded or PathReason.ColumnTooSmall or PathReason.UnfilledSmallShape
            or PathReason.ColumnAspect or PathReason.TooShort or PathReason.TooFewPoints
            or PathReason.CollapsedByThinning or PathReason.NoInk => Disposition.Discarded,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown path reason."),
    };
}
