using System.Reflection;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Hosting;

/// <summary>
/// The class this guards: a type that can appear as a minimal-API endpoint parameter carries a
/// public static <c>TryParse</c> or <c>BindAsync</c> whose shape ASP.NET's parameter binder does
/// not accept. The binder infers endpoint metadata lazily on the FIRST request, and one bad
/// endpoint poisons the whole route table, so every endpoint answers 500, including /health.
/// That took the MCP down on 2026-10-15: the generator's answer parser was a public static method
/// named TryParse with four parameters, and the run-now endpoint takes the generator.
///
/// Covers: every public type (including nested public types) in the MCP, Knowledge and Data
/// assemblies that could be an endpoint parameter, so static classes are skipped (a static class
/// cannot be a parameter type; KorReportLinks.TryParse in Data is one and is left alone); any
/// public static method named TryParse or BindAsync on the rest must match the binder's accepted
/// shapes exactly. Stricter than the binder, which only inspects types actually used as endpoint
/// parameters; the extra strictness is the point, because which types reach an endpoint changes.
/// Does not cover: endpoint lambdas themselves (the route table is not built here, so a parameter
/// type that is neither registered in DI nor bindable, which the binder would treat as a JSON body,
/// is not caught), types in other assemblies, and non-public types. Same-class fault it would NOT
/// catch: a type with a correctly shaped TryParse that the author expected DI to supply; the binder
/// would then parse it from the route or query string and the endpoint would behave wrongly
/// without throwing.
/// </summary>
public sealed class EndpointParameterBindingTests
{
    private static readonly Assembly[] Assemblies =
    {
        typeof(Kor.Operations.Mcp.Program).Assembly,
        typeof(Kor.Opportunities.Knowledge.Store.KnowledgeClaimHit).Assembly,
        typeof(Kor.Opportunities.Data.Crm.IBdDirectorStore).Assembly,
    };

    [Fact]
    public void NoPublicTypeCarriesABinderRejectedTryParseOrBindAsync()
    {
        var violations = Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => (t.IsPublic || t.IsNestedPublic) && !IsStaticClass(t))
            .SelectMany(Violations)
            .ToList();
        Assert.True(violations.Count == 0, "Binder-rejected static methods (rename them; the binder reserves these names):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void NegativeControl_TheCheckerFlagsTheShapeThatTookTheServiceDown_AndAcceptsTheBinderShapes()
    {
        var bad = Violations(typeof(WrongShape)).ToList();
        Assert.Single(bad);
        Assert.Contains("WrongShape.TryParse", bad[0], StringComparison.Ordinal);
        Assert.Empty(Violations(typeof(RightShape)));
    }

    /// <summary>Mirrors ASP.NET Core's ParameterBindingMethodCache: a public static TryParse must be
    /// bool TryParse(string, out T) or bool TryParse(string, IFormatProvider, out T); a public static
    /// BindAsync must be ValueTask&lt;T&gt; BindAsync(HttpContext) or (HttpContext, ParameterInfo).</summary>
    internal static IEnumerable<string> Violations(Type type)
    {
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m.Name == "TryParse" && !IsBinderTryParse(m, type))
                yield return $"{type.FullName}.TryParse({Describe(m)})";
            if (m.Name == "BindAsync" && !IsBinderBindAsync(m, type))
                yield return $"{type.FullName}.BindAsync({Describe(m)})";
        }
    }

    private static bool IsBinderTryParse(MethodInfo m, Type type)
    {
        if (m.ReturnType != typeof(bool)) return false;
        var p = m.GetParameters();
        if (p.Length is not (2 or 3)) return false;
        if (p[0].ParameterType != typeof(string)) return false;
        if (p.Length == 3 && p[1].ParameterType != typeof(IFormatProvider)) return false;
        var last = p[^1];
        return last.IsOut && last.ParameterType.IsByRef && last.ParameterType.GetElementType() == type;
    }

    private static bool IsBinderBindAsync(MethodInfo m, Type type)
    {
        var p = m.GetParameters();
        if (p.Length is not (1 or 2)) return false;
        if (p[0].ParameterType.FullName != "Microsoft.AspNetCore.Http.HttpContext") return false;
        if (p.Length == 2 && p[1].ParameterType != typeof(ParameterInfo)) return false;
        var r = m.ReturnType;
        if (!r.IsGenericType || r.GetGenericTypeDefinition() != typeof(ValueTask<>)) return false;
        var inner = r.GetGenericArguments()[0];
        return inner == type || Nullable.GetUnderlyingType(inner) == type;
    }

    private static bool IsStaticClass(Type t) => t.IsClass && t.IsAbstract && t.IsSealed;

    private static string Describe(MethodInfo m) =>
        string.Join(", ", m.GetParameters().Select(p => (p.IsOut ? "out " : "") + (p.ParameterType.IsByRef ? p.ParameterType.GetElementType()!.Name : p.ParameterType.Name)));

    public sealed record WrongShape(string Text)
    {
        // The 2026-10-15 shape: a domain parser that happens to be called TryParse.
        public static bool TryParse(string answer, int section, IReadOnlyList<string> offered, out WrongShape result)
        { result = new(answer); return section >= 0 && offered.Count >= 0; }
    }

    public sealed record RightShape(string Text)
    {
        public static bool TryParse(string s, IFormatProvider? provider, out RightShape result) { result = new(s); return true; }
    }
}
