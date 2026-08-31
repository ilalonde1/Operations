using Frame = Kor.Operations.EngineeringTools.Dxf.AnnotationOverlay.Frame;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Moves a classified plan onto the model's grid -- after it has been read, never before.
///
/// WHY THE ORDER MATTERS, measured on 31168. Turning the raw linework a quarter turn and then
/// classifying it loses the 2,754 sq ft mezzanine slab and invents ten walls, even with the
/// rotation done exactly (0, 1, -1, 0 rather than through Math.Cos, which is 6.1e-17 off). An exact
/// quarter turn preserves every distance, every angle and every axis alignment, so a reader that
/// gave a different answer afterwards was reading something other than the geometry: somewhere in
/// classification, x and y are not treated alike.
///
/// That is worth finding and is not this. The drafter drew in the drawing's own frame and the
/// reader was built against it, so the reading happens there and only the ANSWER is moved. Nothing
/// downstream can then tell the drawing was ever anywhere else.
/// </summary>
public static class PlanGeometryTransform
{
    public static PlanGeometrySet Apply(PlanGeometrySet set, Frame frame)
    {
        var moved = new PlanGeometrySet();

        foreach (var w in set.Walls)
            moved.Walls.Add(w with { Start = frame.Apply(w.Start), End = frame.Apply(w.End) });

        foreach (var c in set.Columns)
            moved.Columns.Add(c with
            {
                Center = frame.Apply(c.Center),
                // The bearing of the long face turns with the plan, or a rectangular column comes
                // out square to the wrong axis.
                AxisAngleDegrees = Normalise(c.AxisAngleDegrees + frame.RotationDegrees),
            });

        foreach (var o in set.WallOpenings)
            moved.WallOpenings.Add(o with { Start = frame.Apply(o.Start), End = frame.Apply(o.End) });

        foreach (var t in set.Tags)
            moved.Tags.Add(t with { Point = frame.Apply(t.Point) });

        moved.Slabs.AddRange(set.Slabs.Select(l => Move(l, frame)));
        moved.Openings.AddRange(set.Openings.Select(l => Move(l, frame)));
        moved.EnclosedByWalls.AddRange(set.EnclosedByWalls.Select(l => Move(l, frame)));
        moved.RefusedForSize.AddRange(set.RefusedForSize.Select(l => Move(l, frame)));
        moved.Flags.AddRange(set.Flags);

        return moved;
    }

    private static PlanLoop Move(PlanLoop loop, Frame frame) =>
        new(loop.Layer, loop.Points.Select(frame.Apply).ToList(), loop.ClosedExactly)
        {
            ThicknessInchesFromTag = loop.ThicknessInchesFromTag,
        };

    private static double Normalise(double degrees)
    {
        double d = degrees % 180.0;
        return d < 0 ? d + 180.0 : d;
    }
}
