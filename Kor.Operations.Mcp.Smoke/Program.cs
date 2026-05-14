#nullable enable
using System.Globalization;
using Kor.Operations.Mcp.Smoke;
using Kor.Operations.Mcp.Smoke.Assertions;
using Kor.Operations.Mcp.Smoke.Audit;
using Kor.Operations.Mcp.Smoke.Calibrators;
using Kor.Operations.Mcp.Smoke.Config;
using Kor.Operations.Mcp.Smoke.Http;

Console.WriteLine("--- Kor.Operations.Mcp.Smoke ---");

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

SmokeConfig config;
try
{
    config = SmokeConfig.Load();
}
catch (Exception ex)
{
    Console.WriteLine("Config load failed: " + ex.Message);
    return 2;
}

Console.WriteLine("Config: " + config.SourcePath);
Console.WriteLine("Endpoint: " + config.Mcp.Endpoint);
var smokeUserUpn = $"smoke-{Guid.NewGuid():N}@kor";
Console.WriteLine("Audit UserUpn: " + smokeUserUpn);
Console.WriteLine();

var services = new SmokeServices(config);
var ask = new AskClient(config, smokeUserUpn);
var audit = new AuditQuery(config.Mcp.SqlConnectionString);

try
{
    var registeredTools = await ask.ListToolNamesAsync(cts.Token).ConfigureAwait(false);
    var expectedToolNames = TestCases.All
        .SelectMany(t => t.ExpectedToolNames)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    SmokeCoverageValidator.Validate(registeredTools, expectedToolNames);
    Console.WriteLine("Smoke coverage: " + expectedToolNames.Length + " calibrated tool(s), " + registeredTools.Count + " registered tool(s).");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine("Smoke coverage validation failed: " + ex.Message);
    return 1;
}

var passed = 0;
var failed = 0;

for (var i = 0; i < TestCases.All.Count; i++)
{
    var test = TestCases.All[i];
    var prefix = $"[{i + 1,2}/{TestCases.All.Count}] {test.Id}";
    try
    {
        var calibrator = test.CalibratorFactory(services);
        var expectation = await calibrator.CalibrateAsync(cts.Token).ConfigureAwait(false);
        var coverageDrift = ValidateDeclaredToolCoverage(test, expectation);
        if (coverageDrift != null)
        {
            failed++;
            Console.WriteLine($"{prefix.PadRight(66, '.')} FAIL  {coverageDrift}");
            continue;
        }

        var call = await ask.AskAsync(test.Question, test.CurrentlyViewing, cts.Token).ConfigureAwait(false);
        var rows = await WaitForAuditRowsAsync(audit, smokeUserUpn, call.StartedAtUtc, expectation.ExpectedToolCalls, cts.Token).ConfigureAwait(false);
        var failures = Evaluate(expectation, call, rows, test.MaxDurationMs);
        if (failures.Count == 0)
        {
            passed++;
            Console.WriteLine($"{prefix.PadRight(66, '.')} PASS  ({DescribeSuccess(expectation, call)})");
        }
        else
        {
            failed++;
            Console.WriteLine($"{prefix.PadRight(66, '.')} FAIL  {string.Join("; ", failures)}");
            Console.WriteLine("       Answer: " + Truncate(call.Answer, 400));
            Console.WriteLine("       Audit:  " + string.Join(" | ", rows.Select(r => $"{r.ToolName}:{r.InputJson}")));
        }
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"{prefix.PadRight(66, '.')} FAIL  [INFRA] {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: {passed} / {TestCases.All.Count} passed.");
return failed == 0 ? 0 : 1;

static async Task<IReadOnlyList<AuditRow>> WaitForAuditRowsAsync(
    AuditQuery audit,
    string userUpn,
    DateTime startUtc,
    IReadOnlyList<ExpectedToolCall> expectedToolCalls,
    CancellationToken ct)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    IReadOnlyList<AuditRow> rows = Array.Empty<AuditRow>();
    while (true)
    {
        rows = await audit.RowsForUserBetweenAsync(userUpn, startUtc, DateTime.UtcNow.AddSeconds(2), ct).ConfigureAwait(false);
        if (expectedToolCalls.Count == 0 || HasExpectedAuditRows(rows, expectedToolCalls))
            return rows;
        if (DateTime.UtcNow >= deadline)
            return rows;
        await Task.Delay(250, ct).ConfigureAwait(false);
    }
}

static bool HasExpectedAuditRows(IReadOnlyList<AuditRow> rows, IReadOnlyList<ExpectedToolCall> expectedToolCalls)
{
    foreach (var expectedCall in expectedToolCalls)
    {
        var found = rows.Any(row =>
            string.Equals(row.ToolName, expectedCall.ToolName, StringComparison.Ordinal)
            && expectedCall.InputJsonContains.All(s => row.InputJson.Contains(s, StringComparison.Ordinal))
            && (expectedCall.Predicate == null || expectedCall.Predicate(row)));
        if (!found)
            return false;
    }
    return true;
}

static List<string> Evaluate(CalibratedExpectation expectation, AskCallResult call, IReadOnlyList<AuditRow> rows, int maxDurationMs)
{
    var failures = new List<string>();
    foreach (var expectedCall in expectation.ExpectedToolCalls)
    {
        var toolRows = rows.Where(row => string.Equals(row.ToolName, expectedCall.ToolName, StringComparison.Ordinal)).ToList();
        if (toolRows.Count == 0)
        {
            failures.Add($"[TOOL-CHOICE] expected {expectedCall.ToolName}");
            continue;
        }

        var scoped = toolRows.FirstOrDefault(row =>
            expectedCall.InputJsonContains.All(s => row.InputJson.Contains(s, StringComparison.Ordinal))
            && (expectedCall.Predicate == null || expectedCall.Predicate(row)));
        if (scoped == null)
        {
            failures.Add($"[SCOPE-MISMATCH] expected {expectedCall.ToolName} with {string.Join(", ", expectedCall.InputJsonContains)}");
        }
    }

    foreach (var value in expectation.ExpectedValues)
    {
        if (!AnswerNumberMatcher.ContainsMatch(call.Answer, value.ExpectedAmount, out _))
            failures.Add($"[NUMBER-MISMATCH] expected {Format(value.ExpectedAmount)} for {value.Label}");
    }

    if (call.DurationMs > maxDurationMs)
        failures.Add($"[TOO-SLOW] {call.DurationMs} ms > {maxDurationMs} ms");

    return failures;
}

static string DescribeSuccess(CalibratedExpectation expectation, AskCallResult call)
{
    var amount = expectation.ExpectedValues.Count == 0
        ? "no numeric assertion"
        : "expected " + string.Join(", ", expectation.ExpectedValues.Select(v => $"{v.Label}={Format(v.ExpectedAmount)}"));
    var tool = expectation.ExpectedToolCalls.Count == 0
        ? "tool=n/a"
        : "tool=" + string.Join("+", expectation.ExpectedToolCalls.Select(t => t.ToolName).Distinct(StringComparer.Ordinal));
    return $"{amount}; {tool}; {call.DurationMs} ms";
}

static string Format(decimal value)
    => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", CultureInfo.CurrentCulture);

static string? ValidateDeclaredToolCoverage(TestCase test, CalibratedExpectation expectation)
{
    var declared = test.ExpectedToolNames
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    var actual = expectation.ExpectedToolCalls
        .Select(call => call.ToolName)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    var missing = actual
        .Where(name => !declared.Contains(name, StringComparer.Ordinal))
        .ToArray();

    return missing.Length == 0
        ? null
        : "[COVERAGE-DRIFT] calibrator expects "
          + string.Join(", ", actual)
          + " but TestCase declares "
          + string.Join(", ", declared);
}

static string Truncate(string value, int max)
    => value.Length <= max ? value : value[..max] + "...";
