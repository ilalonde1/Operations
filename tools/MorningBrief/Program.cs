using System.Globalization;
using System.Text;
using Kor.MorningBrief;
using Microsoft.Extensions.Configuration;

// One verb, database reads only. Configuration lives beside the published tool or in the environment.
if (args.Length != 5 || args[0] != "compose")
{
    Console.Error.WriteLine("MorningBrief compose --date <yyyy-MM-dd> --to <folder>");
    return 2;
}
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
    if (args[i] is not ("--date" or "--to") || !options.TryAdd(args[i], args[i + 1]))
    {
        Console.Error.WriteLine("Supply --date and --to once each.");
        return 2;
    }
if (!options.TryGetValue("--date", out var rawDate)
    || !DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
    || !options.TryGetValue("--to", out var folder) || string.IsNullOrWhiteSpace(folder))
{
    Console.Error.WriteLine("Supply --date yyyy-MM-dd and a destination folder.");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Production.json", optional: true)
        .AddEnvironmentVariables().Build();
    var facts = await MorningFacts.GatherAsync(configuration, date, DateTimeOffset.UtcNow, cancellation.Token);
    var path = Path.Combine(Path.GetFullPath(folder), $"morning-{date:yyyy-MM-dd}.md");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, MorningPromptComposer.Compose(facts, date), new UTF8Encoding(false), cancellation.Token);
    Console.WriteLine(path);
    return 0;
}
catch (Exception ex)
{
    // Exception messages from configuration/providers can contain credentials. Never print them.
    Console.Error.WriteLine($"Morning composition failed ({ex.GetType().Name}).");
    return 1;
}
