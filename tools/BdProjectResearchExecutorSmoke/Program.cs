#nullable enable
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.Projects;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Extensions.Configuration;
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

var batchMode = args.Any(a => string.Equals(a, "--batch", StringComparison.OrdinalIgnoreCase));
var mpiArg = ReadArg(args, "--mpi");
long mpiId = 0;
if (!batchMode && !long.TryParse(mpiArg, out mpiId))
{
    return Fail("Usage: BdProjectResearchExecutorSmoke --mpi <id> [--provider ProjectBrief] | --batch");
}

var providerName = ReadArg(args, "--provider") ?? "ProjectBrief";

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

    // In --batch mode we mirror the Worker's binding so this smoke
    // exercises the SAME env-var path the cron uses. Catches missing
    // .Bind() calls in Program.cs (R94 person-executor bug).
    //
    // Loads appsettings.json (same one the Worker ships with) FIRST so
    // EligibleStages / EligibleKinds come from the canonical source, then
    // env vars override. Without the JSON the in-code defaults (now empty)
    // would yield zero candidates — verification would silently degrade.
    var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var configBuilder = new ConfigurationBuilder();
    if (File.Exists(appsettingsPath))
    {
        configBuilder.AddJsonFile(appsettingsPath, optional: false);
    }
    configBuilder.AddEnvironmentVariables("KOR_OPPORTUNITIES_");
    var config = configBuilder.Build();
    services.AddSingleton<IConfiguration>(config);

    if (batchMode)
    {
        services.AddOptions<BdProjectResearchExecutorOptions>()
            .Bind(config.GetSection("BdProjectResearchExecutor"))
            .PostConfigure(o => o.EligibleStages = o.EligibleStages.Distinct().ToArray());
        services.AddOptions<BdResearchExecutorOptions>()
            .Bind(config.GetSection("BdResearchExecutor"))
            .PostConfigure(o => o.EligibleKinds = o.EligibleKinds.Distinct().ToArray());
    }
    else
    {
        services.Configure<BdProjectResearchExecutorOptions>(o =>
        {
            o.Enabled = true;
            o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            o.OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "r91b-smoke");
            o.PromptTemplatesDir = Path.Combine(AppContext.BaseDirectory, "ResearchPrompts");
            o.MaxOutputTokens = 8000;
        });
        services.Configure<BdResearchExecutorOptions>(o =>
        {
            o.Enabled = true;
            o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            o.MaxOutputTokens = 8000;
        });
    }

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

    if (batchMode)
    {
        var batch = await svc.RunBatchAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("Batch result:");
        Console.WriteLine("  Considered: " + batch.ProjectsConsidered);
        Console.WriteLine("  Executed:   " + batch.ProjectsExecuted);
        Console.WriteLine("  Successes:  " + batch.Successes);
        Console.WriteLine("  Failures:   " + batch.Failures);
        Console.WriteLine("  InTokens:   " + batch.TotalInputTokens);
        Console.WriteLine("  OutTokens:  " + batch.TotalOutputTokens);
        return 0;
    }

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
