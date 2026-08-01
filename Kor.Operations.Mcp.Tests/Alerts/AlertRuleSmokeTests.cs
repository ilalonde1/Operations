#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Mcp.Alerts;
using Kor.Operations.Mcp.Alerts.Rules.Legal;
using Kor.Operations.Mcp.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Alerts;

/// <summary>
/// Per-rule smoke coverage for the Monday-Briefing alert system (Task #123).
///
/// These tests don't hit a real database — they exercise the structural and
/// resilience contracts every rule must satisfy: discoverability, unique
/// kebab-case RuleIds, valid Section, non-empty SQL, and the
/// catch-and-log-then-return-[] fallback that keeps a transient DB outage
/// from aborting the whole weekly alert run.
/// </summary>
public sealed class AlertRuleSmokeTests
{
    // Every IAlertRule implementation in the Mcp assembly. Reflection-discovered
    // so adding a new rule automatically gets covered (and a removed one fails
    // loudly if anything still expects it).
    public static readonly IReadOnlyList<Type> AllRuleTypes = typeof(IAlertRule).Assembly
        .GetTypes()
        .Where(t => !t.IsAbstract && t.IsClass && typeof(IAlertRule).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void Assembly_RegistersExpectedRuleCount()
    {
        // Five rules ship today: ArAging + four Legal rules. If the count
        // shifts, the test author should update the floor + verify the new
        // rule is wired into AlertGenerator's DI registrations too.
        Assert.True(AllRuleTypes.Count >= 5,
            $"Expected at least 5 IAlertRule implementations, found {AllRuleTypes.Count}: " +
            string.Join(", ", AllRuleTypes.Select(t => t.Name)));
    }

    [Theory]
    [MemberData(nameof(AllRuleTypeData))]
    public void Rule_HasNonEmptyKebabCaseRuleId(Type ruleType)
    {
        var rule = CreateRule(ruleType);
        Assert.False(string.IsNullOrWhiteSpace(rule.RuleId), $"{ruleType.Name}.RuleId is empty.");

        // Kebab-case: lowercase letters, digits, hyphens. No spaces, no
        // underscores, no PascalCase. RuleId ends up in URLs and audit logs
        // so it must be path-safe.
        foreach (var ch in rule.RuleId)
        {
            Assert.True(
                (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-',
                $"{ruleType.Name}.RuleId '{rule.RuleId}' contains non-kebab-case char '{ch}'.");
        }
    }

    [Fact]
    public void RuleIds_AreUnique_AcrossAssembly()
    {
        var ids = AllRuleTypes
            .Select(t => CreateRule(t))
            .Select(r => r.RuleId)
            .ToList();

        var dupes = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0,
            $"Duplicate RuleId(s) across IAlertRule implementations: {string.Join(", ", dupes)}.");
    }

    [Theory]
    [MemberData(nameof(AllRuleTypeData))]
    public void Rule_HasValidSection(Type ruleType)
    {
        var rule = CreateRule(ruleType);
        Assert.True(Enum.IsDefined(typeof(AlertSection), rule.Section),
            $"{ruleType.Name}.Section value '{rule.Section}' is not a defined AlertSection.");
    }

    [Theory]
    [MemberData(nameof(AllRuleTypeData))]
    public async Task RunAsync_WithUnreachableDb_ReturnsEmptyAndDoesNotThrow(Type ruleType)
    {
        // The whole point of the catch-and-log fallback in ArAgingRule +
        // LegalRuleBase is that a transient DB hiccup must not abort the
        // weekly alert run. If anyone removes the try/catch in a rule, this
        // test surfaces it as an UNHANDLED exception instead of an empty list.
        var rule = CreateRule(ruleType, sqlConnectionString: "Server=tcp:127.0.0.1,1;Database=missing;Connection Timeout=1;");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var alerts = await rule.RunAsync(cts.Token).ConfigureAwait(false);

        Assert.NotNull(alerts);
        Assert.Empty(alerts);
    }

    [Theory]
    [MemberData(nameof(LegalRuleTypeData))]
    public void LegalRule_HasNonEmptySql(Type ruleType)
    {
        // LegalRuleBase exposes Sql as protected — we read it via reflection.
        // A rule with empty SQL would silently emit zero alerts in production
        // (the reader returns no rows), so the gate catches the regression
        // here instead of in the Monday briefing.
        var rule = CreateRule(ruleType);
        var sql = (string?)typeof(LegalRuleBase)
            .GetProperty("Sql", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(rule);

        Assert.False(string.IsNullOrWhiteSpace(sql), $"{ruleType.Name}.Sql is empty.");
        Assert.Contains("Mcp.CollectionsCase", sql, StringComparison.Ordinal);
    }

    private static IAlertRule CreateRule(Type ruleType, string sqlConnectionString = "")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new McpOptions
        {
            Username = "test",
            Password = "test",
            SqlConnectionString = sqlConnectionString,
        });

        // Each rule's ctor takes IOptions<McpOptions> + ILogger<ConcreteRuleType>.
        // Resolve the generic ILogger<T> for the rule we're instantiating.
        var loggerType = typeof(NullLogger<>).MakeGenericType(ruleType);
        var logger = Activator.CreateInstance(loggerType)!;

        var ctor = ruleType.GetConstructors().Single();
        var rule = (IAlertRule)ctor.Invoke(new[] { (object)options, logger });
        return rule;
    }

    public static IEnumerable<object[]> AllRuleTypeData() =>
        AllRuleTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> LegalRuleTypeData() =>
        AllRuleTypes.Where(t => typeof(LegalRuleBase).IsAssignableFrom(t)).Select(t => new object[] { t });
}
