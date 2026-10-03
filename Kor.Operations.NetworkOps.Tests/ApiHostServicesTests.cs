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
// WHAT IT COVERS: every parameter of every Map{Get,Post,Put,Delete} lambda in ApiHost.cs whose type is a non-record class of
// the service assembly must be an ApiHost constructor parameter AND be passed to builder.Services.AddSingleton.
// WHAT IT DOES NOT: starting the host (the deploy's /api/ping proof does that); endpoints written as method groups rather
// than lambdas; services resolved by hand inside a handler (h.RequestServices.GetService).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a service registered under an INTERFACE in ApiHost while the endpoint asks for the
// concrete class (registered, so this passes; resolved, it is not the same registration).
public sealed class ApiHostServicesTests
{
    private static string Source()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, "Kor.Operations.NetworkOps.Service", "Api", "ApiHost.cs"));
    }

    [Fact]
    public void Every_service_an_endpoint_asks_for_is_registered_in_the_api_host()
    {
        var src = Source();
        var assembly = typeof(ApiHost).Assembly;
        var services = assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is null
                                                      && t.Namespace?.StartsWith("Kor.Operations.NetworkOps.Service", StringComparison.Ordinal) == true).ToList();
        var ctor = typeof(ApiHost).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single().GetParameters();

        var asked = new List<(string Route, Type Type)>();
        foreach (Match m in Regex.Matches(src, @"Map(Get|Post|Put|Delete)\(""([^""]*)"",\s*(?:async\s*)?\(([^)]*)\)\s*=>"))
            foreach (var p in m.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var token = Regex.Replace(p, @"\[[^\]]*\]\s*", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('?');
                var t = services.FirstOrDefault(x => x.FullName!.EndsWith("." + token, StringComparison.Ordinal));
                if (t is not null) asked.Add((m.Groups[2].Value, t));
            }
        Assert.True(asked.Count >= 10, $"only {asked.Count} service parameters found -- the pattern no longer reads ApiHost.cs");

        var missing = asked.Where(a =>
        {
            var param = ctor.FirstOrDefault(c => c.ParameterType == a.Type);
            return param is null || !Regex.IsMatch(src, @"builder\.Services\.AddSingleton(<[^>]+>)?\(" + param.Name + @"\)");
        }).Select(a => $"{a.Route} asks for {a.Type.Name}").Distinct().ToList();
        Assert.True(missing.Count == 0, "not registered in ApiHost's own container (the host will not start): " + string.Join("; ", missing));
    }
}
