#nullable enable
using System.Globalization;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class FileSystemResearchPromptCatalog : IResearchPromptCatalog
{
    private readonly BdResearchExecutorOptions _options;
    private readonly ILogger<FileSystemResearchPromptCatalog> _logger;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileSystemResearchPromptCatalog(
        IOptions<BdResearchExecutorOptions> options,
        ILogger<FileSystemResearchPromptCatalog> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ResearchPromptPair? Resolve(string orgKind, string providerName, string orgDisplayName)
    {
        try
        {
            var root = _options.PromptTemplatesDir;
            var systemPath = Path.Combine(root, "shared", "system.md");
            var system = ReadCached(systemPath, required: true);
            if (system is null)
            {
                return null;
            }

            var userPath = ResolveUserPromptPath(root, providerName, orgKind);
            if (userPath is null)
            {
                WarnMissingOnce(Path.Combine(root, providerName, orgKind, "user.md"));
                WarnMissingOnce(Path.Combine(root, providerName, "user.md"));
                WarnMissingOnce(Path.Combine(root, "shared", "user-default.md"));
                return null;
            }

            var user = ReadCached(userPath, required: true);
            if (user is null)
            {
                return null;
            }

            return new ResearchPromptPair(
                Substitute(system, orgKind, providerName, orgDisplayName),
                Substitute(user, orgKind, providerName, orgDisplayName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD research prompt resolution failed for org kind {OrgKind}, provider {ProviderName}.",
                orgKind,
                providerName);
            return null;
        }
    }

    private string? ResolveUserPromptPath(string root, string providerName, string orgKind)
    {
        var candidates = new[]
        {
            Path.Combine(root, providerName, orgKind, "user.md"),
            Path.Combine(root, providerName, "user.md"),
            Path.Combine(root, "shared", "user-default.md"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? ReadCached(string path, bool required)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(path, out var value))
            {
                return value;
            }
        }

        if (!File.Exists(path))
        {
            if (required)
            {
                WarnMissingOnce(path);
            }

            return null;
        }

        var text = File.ReadAllText(path);
        lock (_gate)
        {
            _cache[path] = text;
        }

        return text;
    }

    private void WarnMissingOnce(string path)
    {
        lock (_gate)
        {
            if (!_missingWarnings.Add(path))
            {
                return;
            }
        }

        _logger.LogWarning("BD research prompt template not found: {Path}", path);
    }

    private static string Substitute(
        string text,
        string orgKind,
        string providerName,
        string orgDisplayName)
    {
        return text
            .Replace("{ORG_DISPLAY_NAME}", orgDisplayName, StringComparison.Ordinal)
            .Replace("{ORG_KIND}", orgKind, StringComparison.Ordinal)
            .Replace("{PROVIDER_NAME}", providerName, StringComparison.Ordinal)
            .Replace("{TODAY_UTC}", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
