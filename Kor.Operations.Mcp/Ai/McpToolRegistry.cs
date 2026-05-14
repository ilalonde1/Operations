#nullable enable
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Ai;

/// <summary>
/// Auto-discovered registry of /ask-facing MCP tools. Walks every DI-registered
/// instance with [McpServerToolType] at construction, finds the single method
/// per class with [McpServerTool(Name=...)] returning Task&lt;string&gt;, and exposes:
///   - GetToolDefinitions(): the tool catalog Claude sees in each /ask request
///   - InvokeAsync(name, input, ct): dispatch the tool call from a tool_use block
///
/// Tools NOT served via /ask (e.g. ServerInfoTool's synchronous Ping that returns
/// PingResponse, used by native MCP transport only) are filtered out by the
/// "Task&lt;string&gt; return type" requirement.
/// </summary>
public sealed class McpToolRegistry
{
    private readonly Dictionary<string, ToolEntry> _byName;
    private readonly IReadOnlyList<object> _toolDefinitions;

    public McpToolRegistry(IEnumerable<object> toolInstances)
    {
        var entries = new Dictionary<string, ToolEntry>(StringComparer.Ordinal);
        foreach (var instance in toolInstances)
        {
            var type = instance.GetType();
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() == null)
                continue;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr == null) continue;
                if (method.ReturnType != typeof(Task<string>)) continue;
                var name = toolAttr.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        $"Tool {type.FullName}.{method.Name} has [McpServerTool] without a Name. Set [McpServerTool(Name=\"get_x\")].");
                }

                if (entries.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate tool name '{name}' on {type.FullName}.{method.Name}.");
                }

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? throw new InvalidOperationException(
                        $"Tool '{name}' ({type.FullName}.{method.Name}) is missing a method-level [Description].");
                entries[name] = new ToolEntry(name, description, instance, method);
            }
        }

        _byName = entries;
        _toolDefinitions = entries.Values
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => (object)new
            {
                name = e.Name,
                description = e.Description,
                input_schema = BuildInputSchema(e.Method),
            })
            .ToList();
    }

    public IReadOnlyList<object> GetToolDefinitions() => _toolDefinitions;

    /// <summary>
    /// Same shape as <see cref="GetToolDefinitions"/> but the last tool gets
    /// a <c>cache_control={type:"ephemeral"}</c> marker (Batch 92a). Anthropic's
    /// prompt cache covers everything up to and including the marker, so tools
    /// + system prompt land in the same cached prefix after the first call in
    /// a 5-min window.
    /// </summary>
    public IReadOnlyList<object> GetCacheableToolDefinitions()
    {
        if (_byName.Count == 0)
            return _toolDefinitions;
        var ordered = _byName.Values
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        var result = new List<object>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (i == ordered.Count - 1)
            {
                result.Add(new
                {
                    name = e.Name,
                    description = e.Description,
                    input_schema = BuildInputSchema(e.Method),
                    cache_control = new { type = "ephemeral" },
                });
            }
            else
            {
                result.Add(new
                {
                    name = e.Name,
                    description = e.Description,
                    input_schema = BuildInputSchema(e.Method),
                });
            }
        }
        return result;
    }

    public IReadOnlyCollection<string> ToolNames => _byName.Keys;

    public bool IsRegistered(string name) => _byName.ContainsKey(name);

    public async Task<string> InvokeAsync(string name, JsonElement input, CancellationToken ct)
    {
        if (!_byName.TryGetValue(name, out var entry))
            return Kor.Operations.Mcp.Tools.ToolErrorEnvelope.Validation(name, $"Unknown tool: {name}", durationMs: 0);

        var parameters = entry.Method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(CancellationToken))
            {
                args[i] = ct;
                continue;
            }

            args[i] = BindParameter(p, input);
        }

        var task = (Task<string>)entry.Method.Invoke(entry.Instance, args)!;
        return await task.ConfigureAwait(false);
    }

    private static object BuildInputSchema(MethodInfo method)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        var required = new List<string>();
        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;
            var name = p.Name ?? throw new InvalidOperationException($"Parameter on {method.DeclaringType?.Name}.{method.Name} has no name.");
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            properties[name] = new { type = JsonTypeFor(p.ParameterType), description = desc };
            if (!p.HasDefaultValue && !IsNullable(p))
                required.Add(name);
        }

        return new
        {
            type = "object",
            properties,
            required = required.ToArray(),
        };
    }

    private static string JsonTypeFor(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return "string";
        if (u == typeof(bool)) return "boolean";
        if (u == typeof(int) || u == typeof(long) || u == typeof(short)) return "integer";
        if (u == typeof(double) || u == typeof(float) || u == typeof(decimal)) return "number";
        return "string";
    }

    private static bool IsNullable(ParameterInfo p)
    {
        if (Nullable.GetUnderlyingType(p.ParameterType) != null) return true;
        // For reference-type params with `?` annotation, NullabilityInfoContext is the right check.
        // Simpler heuristic: any reference type with HasDefaultValue=false but a string? annotation
        // shows up in metadata as NullabilityInfoContext.Read(p).WriteState == Nullable.
        if (!p.ParameterType.IsValueType)
        {
            try
            {
                var nullCtx = new NullabilityInfoContext();
                var info = nullCtx.Create(p);
                return info.WriteState == NullabilityState.Nullable;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static object? BindParameter(ParameterInfo p, JsonElement input)
    {
        var name = p.Name!;
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var prop))
        {
            return p.HasDefaultValue ? p.DefaultValue : DefaultForType(p.ParameterType);
        }

        var target = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        if (prop.ValueKind == JsonValueKind.Null)
            return p.HasDefaultValue ? p.DefaultValue : DefaultForType(p.ParameterType);
        if (target == typeof(string)) return prop.GetString();
        if (target == typeof(int)) return prop.TryGetInt32(out var i) ? i : (int?)null;
        if (target == typeof(short)) return prop.TryGetInt16(out var s) ? s : (short?)null;
        if (target == typeof(long)) return prop.TryGetInt64(out var l) ? l : (long?)null;
        if (target == typeof(double)) return prop.TryGetDouble(out var d) ? d : (double?)null;
        if (target == typeof(bool)) return prop.ValueKind == JsonValueKind.True;
        return prop.GetRawText();
    }

    private static object? DefaultForType(Type t)
        => t.IsValueType ? Activator.CreateInstance(t) : null;

    private sealed record ToolEntry(string Name, string Description, object Instance, MethodInfo Method);
}
