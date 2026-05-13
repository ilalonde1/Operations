#nullable enable

namespace Kor.Operations.Mcp.Smoke.Assertions;

internal static class Tolerance
{
    // 0.5% or $10, whichever LARGER. Catches AI rounding ($361.8K vs $361,803)
    // as pass; catches 2x firm-wide-vs-CAD as fail (Batch 90 regression).
    public static bool Matches(decimal expected, decimal actual)
        => Math.Abs(expected - actual) <= Math.Max(10m, Math.Abs(expected) * 0.005m);
}
