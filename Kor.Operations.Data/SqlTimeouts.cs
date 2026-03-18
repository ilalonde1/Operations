#nullable enable
namespace Kor.Operations.Data;

internal static class SqlTimeouts
{
    /// <summary>UI-facing queries  fail fast to keep the interface responsive.</summary>
    internal const int UiFacing = 30;

    /// <summary>Batch and reporting queries  allow time for large data sets.</summary>
    internal const int Batch = 300;
}
