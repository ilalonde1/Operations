#nullable enable

namespace Kor.Operations.Core;

public static class MathHelpers
{
    public static double SafeDiv(double num, double den)
        => den == 0.0 ? 0.0 : (num / den);

    public static decimal SafeDiv(decimal num, decimal den)
        => den == 0m ? 0m : (num / den);
}
