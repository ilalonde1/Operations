#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// A ruling written for people and the drafter ("an X is an opening", units 'rule') is not a setting, and a build that
/// requires its rules does not stop on one - while a setting that will not parse still stops it (2026-10-27).
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. KOR.Drafter migrations 131, 132 and 134 (and one row of 119) put the engineer's and the drafter's
/// rulings into analysis.Ruling with units 'rule' and prose for a value. vw_RuleSetting serves every row, and the loader
/// refused each as an unreadable number - so EVERY rules-required DXF-to-ETABS build stopped with "KorStandards rule
/// settings could not be read", keys it never reads included. Found by the full suite at d92e50b1: four tests that build
/// with rules failed in under a second each, none of them about rules.
///
/// WHAT THIS COVERS: the loader's per-row decision (RuleSettings.KindOf) - a 'rule' row is left out, a malformed number in
/// a numeric setting still stops the run, an empty list still stops it, a number and a list are settings; and the LIVE
/// store - LoadRequired over the real view succeeds and counts exactly the 'rule' rows the view serves.
/// WHAT IT DOES NOT: any other non-setting a later migration might add under a units word of its own - it would stop the
/// run again (correctly loud), and operations-ee's read-back after each ruling migration (count the rows this loader
/// refuses; expect 0) is what catches it before a build does.
/// A same-class fault this would NOT catch: a ruling for people given a NUMERIC value with numeric units - it would load as
/// a setting nobody reads; harmless to the build, invisible here.
/// </remarks>
public sealed class ARulingWrittenForPeopleIsNotASettingTests
{
    [Theory]
    [InlineData("X = opening", "rule", "ForPeople")]
    [InlineData("top-to-bottom; columns right-to-left", "RULE", "ForPeople")]
    [InlineData("48", "in", "Setting")]
    [InlineData("forty-eight", "in", "Unreadable")]          // a malformed dxf.* number still stops the run
    [InlineData("maybe", "bool", "Unreadable")]               // no yes/no and no number in it (a number anywhere reads as one)
    [InlineData("S-WALL;WALL", "layers", "Setting")]
    [InlineData("   ", "names", "EmptyList")]
    public void EachRowIsASettingARulingForPeopleOrAStop(string value, string units, string expected)
        => Assert.Equal(expected, RuleSettings.KindOf(value, units, out _).ToString());

    [Fact]
    public void TheLiveStoreLoadsAndCountsItsRulingsForPeople()
    {
        string connection = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable) ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; this reads KorStandards and never skips.");

        int served;
        using (var db = new SqlConnection(connection))
        {
            db.Open();
            using var count = db.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM analysis.vw_RuleSetting WHERE SettingUnits = @u";
            count.Parameters.AddWithValue("@u", RuleSettings.RulingForPeopleUnits);
            served = (int)count.ExecuteScalar()!;
        }

        var settings = RuleSettings.LoadRequired(connection, DxfToEtabsService.RequiredRuleKeys, out int leftOut);

        Assert.Equal(served, leftOut);
        Assert.DoesNotContain(settings.Values, s => s.Units.Equals(RuleSettings.RulingForPeopleUnits, StringComparison.OrdinalIgnoreCase));
    }
}
