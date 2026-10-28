using System.Reflection;
using Kor.Operations.NetworkOps.Service.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// 28 Oct 12:01: operations-e4 called networkops_run without approvedBy and got only "An error occurred invoking
// 'networkops_run'". The service log said why: "The arguments dictionary is missing a value for the required parameter
// 'approvedBy'". The SDK makes a nullable parameter with no default REQUIRED, and hides the reason from the caller. It was
// already a known trap ("pass approvedBy:null", 26 Oct) -- a rule in memory instead of in the code.
// COVERS: every parameter of every [McpServerTool] in the service assembly; and, through the SDK's own marshaller, that a
// call leaving every optional argument out reaches the method. DOES NOT COVER: a non-nullable parameter the description
// calls optional (it stays required, which is honest); a tool in another assembly; what the method does with the default.
public class McpToolOptionalParametersTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    private static IEnumerable<MethodInfo> Tools() => typeof(NetworkOpsMcpTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    private static bool MayBeNull(ParameterInfo p) =>
        Nullable.GetUnderlyingType(p.ParameterType) is not null
        || (!p.ParameterType.IsValueType && Nullability.Create(p).WriteState == NullabilityState.Nullable);

    [Fact]
    public void Every_parameter_that_may_be_null_has_a_default_so_leaving_it_out_works()
    {
        var tools = Tools().ToList();
        Assert.True(tools.Count >= 11, $"found {tools.Count} MCP tools; the scan is not seeing the service's tools");
        var bad = tools.SelectMany(m => m.GetParameters()
                .Where(p => p.ParameterType != typeof(IServiceProvider) && p.ParameterType != typeof(CancellationToken))
                .Where(p => MayBeNull(p) && !p.HasDefaultValue)
                .Select(p => $"{m.DeclaringType!.Name}.{m.Name}({p.Name})"))
            .ToList();
        Assert.True(bad.Count == 0, "nullable MCP parameters with no default (the SDK makes them REQUIRED and hides why): " + string.Join(", ", bad));
    }

    // The same marshaller the server uses: arguments with every optional one left out must bind, not throw.
    [Fact]
    public async Task The_sdks_marshaller_binds_a_call_that_leaves_the_optional_arguments_out()
    {
        var run = typeof(NetworkOpsMcpRun).GetMethod(nameof(NetworkOpsMcpRun.Run))!;
        var bound = new List<string>();
        // a stand-in with the same signature shape, so binding is tested without a database behind it
        var probe = AIFunctionFactory.Create(
            (IServiceProvider services, string host, string script, string purpose, int? timeoutSeconds = null, string? approvedBy = null, CancellationToken ct = default) =>
            { bound.Add($"{host}|{timeoutSeconds?.ToString() ?? "null"}|{approvedBy ?? "null"}"); return "ok"; });
        Assert.Equal(run.GetParameters().Select(p => p.Name), new[] { "services", "host", "script", "purpose", "timeoutSeconds", "approvedBy", "ct" });
        var args = new AIFunctionArguments { ["host"] = "KOR-APP01", ["script"] = "1", ["purpose"] = "a read-only probe" };
        args.Services = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        await probe.InvokeAsync(args);
        Assert.Equal(["KOR-APP01|null|null"], bound);
    }
}
