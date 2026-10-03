#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Service.Api;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The API is its own small web host (ApiHost) with its OWN service container: a singleton registered on the service's main
// host is invisible to it unless ApiHost takes it in its constructor and registers it again. On 2026-10-02 the new
// GET /api/network asked for NetworkMapService, which only the main host had; ASP.NET inferred it as a request body, the
// web host refused to build, and the whole service stopped (exit 1067) for ~10 minutes -- every test green.
// THE CLASS: an endpoint parameter that is a service of this assembly but not registered in ApiHost's container.
//
// WHAT IT COVERS: every parameter of every Map{Get,Post,Put,Delete} lambda in ApiHost.cs AND in the other three files that
// map endpoints into the SAME container -- SessionApi.cs, Updates/UpdatesApi.cs, Agents/AgentApi.cs -- whose type is a
// non-record class/interface of the service assembly must be registered in ApiHost's container (builder.Services.AddSingleton,
// either the constructor instance by name or a <Interface> registration).
// WHAT IT DOES NOT: starting the host (the deploy's /api/ping proof does that); endpoints written as method groups rather
// than lambdas; services resolved by hand inside a handler (h.RequestServices.GetService); a sub-API file added later and not
// added to Files below (the count floor is the only guard against that).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: an endpoint whose Map* call and lambda are split across lines the single-line regex
// does not join; a service resolved (not registered) under a DIFFERENT interface than the one it was AddSingleton'd as.
// 2026-10-02 this gate read ONLY ApiHost.cs while three sibling files fed the same container -- the exact "broad name, narrow
// check" trap (CLAUDE.md rule 11); it now reads all four.
public sealed class ApiHostServicesTests
{
    // The files whose Map* lambdas resolve from ApiHost's container. ApiHost.cs is first: it alone holds the registrations.
    private static readonly string[] Files =
        ["Api/ApiHost.cs", "Api/SessionApi.cs", "Updates/UpdatesApi.cs", "Agents/AgentApi.cs"];

    private static string Read(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, "Kor.Operations.NetworkOps.Service", rel.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Every_service_an_endpoint_asks_for_is_registered_in_the_api_host()
    {
        var registrations = Read(Files[0]);   // ApiHost.cs: where AddSingleton and the constructor live
        var assembly = typeof(ApiHost).Assembly;
        // Classes AND interfaces of the service assembly (an interface can be a registered endpoint parameter), minus records.
        var services = assembly.GetTypes().Where(t => (t.IsClass && !t.IsAbstract || t.IsInterface)
                                                      && t.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is null
                                                      && t.Namespace?.StartsWith("Kor.Operations.NetworkOps.Service", StringComparison.Ordinal) == true).ToList();
        var ctor = typeof(ApiHost).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single().GetParameters();

        var asked = new List<(string File, string Route, Type Type)>();
        foreach (var file in Files)
        {
            var src = Read(file);
            foreach (Match m in Regex.Matches(src, @"Map(Get|Post|Put|Delete)\(""([^""]*)"",\s*(?:async\s*)?\(([^)]*)\)\s*=>"))
                foreach (var p in m.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var token = Regex.Replace(p, @"\[[^\]]*\]\s*", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('?');
                    var t = services.FirstOrDefault(x => x.FullName!.EndsWith("." + token, StringComparison.Ordinal));
                    if (t is not null) asked.Add((file, m.Groups[2].Value, t));
                }
        }
        Assert.True(asked.Count >= 12, $"only {asked.Count} service parameters found across {Files.Length} files -- the pattern no longer reads the API source");
        // Every one of the four files must have been looked at (a sibling renamed/moved must fail loudly, not silently drop).
        foreach (var file in Files) Assert.Contains("Map", Read(file));

        bool Registered(Type t)
        {
            var param = ctor.FirstOrDefault(c => c.ParameterType == t);
            if (param is not null && Regex.IsMatch(registrations, @"builder\.Services\.AddSingleton(<[^>]+>)?\(" + param.Name + @"\)")) return true;
            // A <Interface>(instance) registration, e.g. AddSingleton<Agents.IAgentDirectory>(store).
            return Regex.IsMatch(registrations, @"AddSingleton<[^>]*\b" + Regex.Escape(t.Name) + @">\(");
        }

        var missing = asked.Where(a => !Registered(a.Type))
            .Select(a => $"{a.File} {a.Route} asks for {a.Type.Name}").Distinct().ToList();
        Assert.True(missing.Count == 0, "not registered in ApiHost's own container (the host will not start): " + string.Join("; ", missing));
    }
}
