#nullable enable
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string? ReadArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

var orgArg = ReadArg(args, "--org");
if (!long.TryParse(orgArg, out var canonicalOrgId))
{
    return Fail("Usage: BdResearchExecutorSmoke --org <canonicalOrgId> [--provider FirmNarrative]");
}

var providerName = ReadArg(args, "--provider") ?? "FirmNarrative";

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
    services.Configure<BdResearchExecutorOptions>(o =>
    {
        o.Enabled = true;
        o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        o.OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "r83-smoke");
        o.PromptTemplatesDir = Path.Combine(AppContext.BaseDirectory, "ResearchPrompts");
        o.MaxOutputTokens = 8000;
    });
    var opportunitiesDb = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
        ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing");
    services.AddSingleton(Options.Create(new OpportunitiesWorkerOptions
    {
        OpportunitiesDb = opportunitiesDb,
    }));
    services.AddSingleton<IResearchExecutorService, AnthropicResearchExecutorService>();
    services.AddSingleton<IResearchPromptCatalog, FileSystemResearchPromptCatalog>();
    services.AddSingleton(_ => new IntelReadService(opportunitiesDb));
    services.AddSingleton<BdResearchExecutorService>();

    using var sp = services.BuildServiceProvider();
    var svc = sp.GetRequiredService<BdResearchExecutorService>();
    var result = await svc.ExecuteOneAsync(canonicalOrgId, providerName, CancellationToken.None).ConfigureAwait(false);
    if (result is null)
    {
        return Fail("Research executor returned no result.");
    }

    var outputDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "r83-smoke",
        DateTime.UtcNow.ToString("yyyy-MM-dd"));
    var outputPath = Path.Combine(outputDir, $"refresh-{result.CanonicalOrgId}-{result.ProviderName}.json");
    var exists = File.Exists(outputPath);
    var preview = result.ResultJson.Length <= 400 ? result.ResultJson : result.ResultJson[..400];

    Console.WriteLine("Output: " + outputPath);
    Console.WriteLine("Exists: " + exists);
    Console.WriteLine("InputTokens: " + result.InputTokens);
    Console.WriteLine("OutputTokens: " + result.OutputTokens);
    Console.WriteLine("ToolCallCount: " + result.ToolCallCount);
    Console.WriteLine("Elapsed: " + result.Elapsed);
    Console.WriteLine("Preview:");
    Console.WriteLine(preview);

    return exists ? 0 : 1;
}
catch (Exception ex)
{
    return Fail(ex.GetType().Name + ": " + ex.Message);
}
