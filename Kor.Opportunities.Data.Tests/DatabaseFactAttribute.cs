using System;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// A test that needs the live opportunities database. Without KOR_OPPORTUNITIES_OPPORTUNITIESDB it is reported as SKIPPED,
/// with the reason, instead of returning early and counting as a pass (audit m2, 2026-10-21: five lifecycle tests passed
/// without asserting anything wherever the database was not configured).
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public const string Variable = "KOR_OPPORTUNITIES_OPPORTUNITIESDB";

    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Needs the live opportunities database: set {Variable}.";
    }
}
