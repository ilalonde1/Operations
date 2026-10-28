#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.Core.Models.Contracts;
using Kor.Operations.Data;
using Xunit;

namespace Kor.Operations.App.Tests.Contracts;

/// <summary>
/// SqlContractClauseStore against the REAL clause library in KorTransmittals, run only when
/// KOR_CONTRACTS_LIVE=1 (it reads the built app's config for the connection string and only ever reads).
/// The pattern is NetworkOpsWindowsRenderTests' live switch.
///
/// WHAT IT COVERS: the SQL parses and runs on the real view; the topic, client, text and signed-only
/// filters each narrow the result; the mapping fills every column the screen shows. The counts printed
/// are read back from the database, not assumed.
/// WHAT IT DOES NOT COVER: the library's correctness (that is tools/ContractClauseLibrary's look), the
/// screen (ContractsScreensRenderTests), or a database with the view missing (that is the status line's job).
/// </summary>
[Trait("Speed", "Slow")]
public sealed class ContractClauseStoreLiveTests
{
    [Fact]
    public async Task Live_library_answers_the_screen_filters()
    {
        if (Environment.GetEnvironmentVariable("KOR_CONTRACTS_LIVE") != "1") return;
        var cs = BuiltAppConnectionString("KorTransmittalsDb");
        var store = new SqlContractClauseStore(cs);

        var clients = await store.ListClientsAsync(CancellationToken.None);
        Assert.True(clients.Count > 10, $"EXPECTED more than 10 clients in the live library; ACTUAL {clients.Count}");
        Assert.Contains("Wesgroup Properties Ltd.", clients);

        var caps = await store.SearchAsync(new ContractPositionQuery("limitation_of_liability", null, null, false), CancellationToken.None);
        Assert.True(caps.Count > 0, "EXPECTED limitation-of-liability clauses on file; ACTUAL 0");
        Assert.All(caps, c => Assert.Contains("limitation_of_liability", c.Topics.Split('|')));
        Assert.All(caps, c => Assert.False(string.IsNullOrWhiteSpace(c.ClauseText)));
        Assert.All(caps, c => Assert.False(string.IsNullOrWhiteSpace(c.FilePath)));
        Assert.True(caps.TakeWhile(c => c.IsSigned).Count() == caps.Count(c => c.IsSigned), "Signed clauses must come first.");

        var wesgroupSigned = await store.SearchAsync(new ContractPositionQuery("limitation_of_liability", "Wesgroup Properties Ltd.", null, true), CancellationToken.None);
        Assert.True(wesgroupSigned.Count > 0, "EXPECTED Wesgroup's signed cap clauses; ACTUAL 0");
        Assert.All(wesgroupSigned, c => Assert.Equal("Wesgroup Properties Ltd.", c.ClientName));
        Assert.All(wesgroupSigned, c => Assert.True(c.IsSigned));
        Assert.Contains(wesgroupSigned, c => c.ClauseText.Contains("insurance proceeds", StringComparison.OrdinalIgnoreCase));

        var text = await store.SearchAsync(new ContractPositionQuery(null, null, "subrogation", false), CancellationToken.None);
        Assert.True(text.Count > 0, "EXPECTED clauses mentioning subrogation; ACTUAL 0");
        Assert.All(text, c => Assert.True(c.ClauseText.Contains("subrogation", StringComparison.OrdinalIgnoreCase) || (c.Heading ?? "").Contains("subrogation", StringComparison.OrdinalIgnoreCase)));

        var none = await store.SearchAsync(new ContractPositionQuery("ai_tools", "Wesgroup Properties Ltd.", "zzzz-no-such-words-zzzz", true), CancellationToken.None);
        Assert.Empty(none);

        Console.WriteLine($"live: clients={clients.Count} caps={caps.Count} (signed {caps.Count(c => c.IsSigned)}) wesgroupSigned={wesgroupSigned.Count} subrogation={text.Count}");
    }

    /// <summary>The connection string the app itself uses, from its built config beside the exe.</summary>
    private static string BuiltAppConnectionString(string name)
    {
        var root = XamlStaticResourceOrderTests.GetRepoRoot();
        var config = Directory.GetFiles(Path.Combine(root, "Kor.Operations.App", "bin"), "Kor.Operations.App.dll.config", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new InvalidOperationException("No built Kor.Operations.App.dll.config under Kor.Operations.App/bin; build the app first.");
        var doc = XDocument.Load(config);
        var cs = doc.Descendants("connectionStrings").Elements("add")
            .FirstOrDefault(a => string.Equals((string?)a.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("connectionString")?.Value
            ?? throw new InvalidOperationException($"connection string '{name}' not in {config}");
        // The app supplies the SQL login from KOR_DB_USER / KOR_DB_PASSWORD at startup (EnvironmentSecretOverrides);
        // the test does the same, and never prints either.
        var user = Env("KOR_DB_USER");
        var password = Env("KOR_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password)) return cs;
        var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs);
        if (!string.IsNullOrWhiteSpace(user)) b.UserID = user;
        if (!string.IsNullOrWhiteSpace(password)) b.Password = password;
        b.IntegratedSecurity = false;
        return b.ConnectionString;
    }

    private static string? Env(string name)
        => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(name);
}
