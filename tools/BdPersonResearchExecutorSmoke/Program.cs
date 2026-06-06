#nullable enable
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.People;
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

var personArg = ReadArg(args, "--person");
if (!long.TryParse(personArg, out var intelPersonId))
{
    return Fail("Usage: BdPersonResearchExecutorSmoke --person <intelPersonId>");
}

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

    services.Configure<BdPersonResearchExecutorOptions>(o =>
    {
        o.Enabled = true;
        o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        o.OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "r91c-smoke");
        o.PromptTemplatesDir = Path.Combine(AppContext.BaseDirectory, "ResearchPrompts");
        o.MaxOutputTokens = 8000;
    });

    // Shared Anthropic executor reads model + ApiKey from BdResearchExecutorOptions.
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
    services.AddSingleton<IPersonResearchPromptCatalog, FileSystemPersonResearchPromptCatalog>();

    services.AddSingleton<PersonBriefExtractor>();
    services.AddSingleton(_ => new IntelPersistenceService(opportunitiesDb));
    services.AddSingleton<IPersonRefreshChokepoint>(sp =>
        new SqlPersonRefreshChokepoint(
            opportunitiesDb,
            sp.GetRequiredService<PersonBriefExtractor>(),
            sp.GetRequiredService<IntelPersistenceService>(),
            sp.GetRequiredService<ILogger<SqlPersonRefreshChokepoint>>()));

    services.AddSingleton<BdPersonResearchExecutorService>();

    using var sp = services.BuildServiceProvider();
    var svc = sp.GetRequiredService<BdPersonResearchExecutorService>();
    var result = await svc.ExecuteOneAsync(intelPersonId, CancellationToken.None).ConfigureAwait(false);
    if (result is null)
    {
        return Fail("Person research executor returned no result (person missing, no current affiliation, or executor disabled).");
    }

    var preview = result.ResultJson.Length <= 600 ? result.ResultJson : result.ResultJson[..600];

    Console.WriteLine("IntelPersonId: " + intelPersonId);
    Console.WriteLine("Provider:      " + result.ProviderName);
    Console.WriteLine("InputTokens:   " + result.InputTokens);
    Console.WriteLine("OutputTokens:  " + result.OutputTokens);
    Console.WriteLine("ToolCallCount: " + result.ToolCallCount);
    Console.WriteLine("Elapsed:       " + result.Elapsed);
    Console.WriteLine("Preview:");
    Console.WriteLine(preview);

    return 0;
}
catch (Exception ex)
{
    return Fail(ex.GetType().Name + ": " + ex.Message);
}
