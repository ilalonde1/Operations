#nullable enable
using System.Globalization;
using System.Text;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class FileSystemProjectResearchPromptCatalog : IProjectResearchPromptCatalog
{
    private readonly BdProjectResearchExecutorOptions _options;
    private readonly ILogger<FileSystemProjectResearchPromptCatalog> _logger;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileSystemProjectResearchPromptCatalog(
        IOptions<BdProjectResearchExecutorOptions> options,
        ILogger<FileSystemProjectResearchPromptCatalog> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ResearchPromptPair? Resolve(
        string projectStage,
        string providerName,
        string projectName,
        string? proponentName,
        string? city,
        string? province)
    {
        try
        {
            var root = _options.PromptTemplatesDir;
            var systemPath = Path.Combine(root, "ProjectBrief", "system.md");
            var system = ReadCached(systemPath, required: true);
            if (system is null)
            {
                return null;
            }

            var userPath = ResolveUserPromptPath(root, providerName, projectStage);
            if (userPath is null)
            {
                WarnMissingOnce(Path.Combine(root, providerName, StageSlug(projectStage), "user.md"));
                WarnMissingOnce(Path.Combine(root, providerName, "user.md"));
                return null;
            }

            var user = ReadCached(userPath, required: true);
            if (user is null)
            {
                return null;
            }

            return new ResearchPromptPair(
                Substitute(system, projectStage, providerName, projectName, proponentName, city, province),
                Substitute(user, projectStage, providerName, projectName, proponentName, city, province));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD project research prompt resolution failed for project {ProjectName}, stage {Stage}, provider {ProviderName}.",
                projectName,
                projectStage,
                providerName);
            return null;
        }
    }

    private static string? ResolveUserPromptPath(string root, string providerName, string projectStage)
    {
        var candidates = new[]
        {
            Path.Combine(root, providerName, StageSlug(projectStage), "user.md"),
            Path.Combine(root, providerName, "user.md"),
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

        _logger.LogWarning("BD project research prompt template not found: {Path}", path);
    }

    private static string Substitute(
        string text,
        string projectStage,
        string providerName,
        string projectName,
        string? proponentName,
        string? city,
        string? province)
    {
        return text
            .Replace("{TODAY_UTC}", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PROJECT_NAME}", projectName, StringComparison.Ordinal)
            .Replace("{PROJECT_STAGE}", projectStage, StringComparison.Ordinal)
            .Replace("{PROPONENT_NAME}", proponentName ?? "", StringComparison.Ordinal)
            .Replace("{CITY}", city ?? "", StringComparison.Ordinal)
            .Replace("{PROVINCE}", province ?? "", StringComparison.Ordinal)
            .Replace("{PROVIDER_NAME}", providerName, StringComparison.Ordinal)
            .Replace("{CURRENT_INTEL_JSON}", "{}", StringComparison.Ordinal);
    }

    private static string StageSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sb = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }
}
