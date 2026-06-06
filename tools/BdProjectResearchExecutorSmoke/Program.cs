#nullable enable
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.Projects;
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

var mpiArg = ReadArg(args, "--mpi");
if (!long.TryParse(mpiArg, out var mpiId))
{
    return Fail("Usage: BdProjectResearchExecutorSmoke --mpi <majorProjectsInventoryId> [--provider ProjectBrief]");
}

var providerName = ReadArg(args, "--provider") ?? "ProjectBrief";

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

    services.Configure<BdProjectResearchExecutorOptions>(o =>
    {
        o.Enabled = true;
        o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        o.OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "r91b-smoke");
        o.PromptTemplatesDir = Path.Combine(AppContext.BaseDirectory, "ResearchPrompts");
        o.MaxOutputTokens = 8000;
    });

    // The org-side BdResearchExecutorOptions is consumed by the shared
    // AnthropicResearchExecutorService for the model id. Configure it
    // with the same key.
    services.Configure<BdResearchExecutorOptions>(o =>
    {
        o.Enabled = true;
        o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        o.MaxOutputTokens = 8000;
    });

    var opportunitiesDb = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
        ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing");
    services.AddSingleton(Options.Create(new OpportunitiesWorkerOptions
    {
        OpportunitiesDb = opportunitiesDb,
    }));

    services.AddSingleton<IResearchExecutorService, AnthropicResearchExecutorService>();
    services.AddSingleton<IProjectResearchPromptCatalog, FileSystemProjectResearchPromptCatalog>();

    services.AddSingleton<IProjectIntelExtractor, ProjectBriefExtractor>();
    services.AddSingleton<DefaultProjectIntelExtractor>();
    services.AddSingleton<ProjectIntelExtractorRegistry>();
    services.AddSingleton(sp => new ProjectIntelPersistenceService(
        opportunitiesDb,
        sp.GetRequiredService<ILogger<ProjectIntelPersistenceService>>()));
    services.AddSingleton<IMajorProjectEnrichmentTrackingStore>(sp =>
        new SqlMajorProjectEnrichmentTrackingStore(
            opportunitiesDb,
            sp.GetRequiredService<ProjectIntelExtractorRegistry>(),
            sp.GetRequiredService<ProjectIntelPersistenceService>(),
            sp.GetRequiredService<ILogger<SqlMajorProjectEnrichmentTrackingStore>>()));

    services.AddSingleton<BdProjectResearchExecutorService>();

    using var sp = services.BuildServiceProvider();
    var svc = sp.GetRequiredService<BdProjectResearchExecutorService>();
    var result = await svc.ExecuteOneAsync(mpiId, providerName, CancellationToken.None).ConfigureAwait(false);
    if (result is null)
    {
        return Fail("Project research executor returned no result.");
    }

    var preview = result.ResultJson.Length <= 600 ? result.ResultJson : result.ResultJson[..600];

    Console.WriteLine("MpiId: " + mpiId);
    Console.WriteLine("Provider: " + result.ProviderName);
    Console.WriteLine("InputTokens: " + result.InputTokens);
    Console.WriteLine("OutputTokens: " + result.OutputTokens);
    Console.WriteLine("ToolCallCount: " + result.ToolCallCount);
    Console.WriteLine("Elapsed: " + result.Elapsed);
    Console.WriteLine("Preview:");
    Console.WriteLine(preview);

    return 0;
}
catch (Exception ex)
{
    return Fail(ex.GetType().Name + ": " + ex.Message);
}
