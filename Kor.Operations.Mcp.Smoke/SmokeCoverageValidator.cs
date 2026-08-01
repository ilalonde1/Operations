#nullable enable
namespace Kor.Operations.Mcp.Smoke;

public static class SmokeCoverageValidator
{
    private static readonly string[] DefaultExemptToolNames =
    [
        // Raw ad-hoc SQL fallback; intentionally not calibrated by canonical Business-service smoke cases.
        "query_kor_data",
    ];

    public static void Validate(
        IReadOnlyCollection<string> registeredToolNames,
        IReadOnlyCollection<string> expectedToolNames,
        IReadOnlyCollection<string>? exemptToolNames = null)
    {
        var registered = new HashSet<string>(registeredToolNames, StringComparer.Ordinal);
        var expected = new HashSet<string>(expectedToolNames, StringComparer.Ordinal);
        var exempt = new HashSet<string>(exemptToolNames ?? DefaultExemptToolNames, StringComparer.Ordinal);

        var missingCoverage = registered
            .Where(name => !expected.Contains(name) && !exempt.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (missingCoverage.Length > 0)
        {
            throw new InvalidOperationException(
                "Smoke coverage gap: tools "
                + string.Join(", ", missingCoverage)
                + " are registered but have no calibrator in TestCases.cs. Add a calibrator or add to the exempt list.");
        }

        var staleExpected = expected
            .Where(name => !registered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (staleExpected.Length > 0)
        {
            throw new InvalidOperationException(
                "Smoke coverage drift: tools "
                + string.Join(", ", staleExpected)
                + " are expected by TestCases.cs but are not registered by the MCP service.");
        }
    }
}
