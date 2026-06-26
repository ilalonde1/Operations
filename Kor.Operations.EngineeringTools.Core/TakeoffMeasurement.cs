#nullable enable

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public enum TakeoffElementType
    {
        Slab,
        Wall,
        Beam,
        Column,
        DropPanel,
        Foundation
    }

    public sealed record TakeoffMeasurement(
        TakeoffElementType ElementType,
        string Level,
        string GradeCode,
        double? AreaMm2 = null,
        double OpeningAreaMm2 = 0,
        double? LengthMm = null,
        double? WidthMm = null,
        double? DepthMm = null,
        double? ThicknessMm = null,
        double? StoreyHeightMm = null,
        double? RebarDensityOverrideKgPerM3 = null,
        bool ScaleConfirmed = false,
        bool OpeningsChecked = false);
}
