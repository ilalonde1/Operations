#nullable enable
using System.Globalization;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class FileSystemPersonResearchPromptCatalog : IPersonResearchPromptCatalog
{
    private readonly BdPersonResearchExecutorOptions _options;
    private readonly ILogger<FileSystemPersonResearchPromptCatalog> _logger;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileSystemPersonResearchPromptCatalog(
        IOptions<BdPersonResearchExecutorOptions> options,
        ILogger<FileSystemPersonResearchPromptCatalog> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ResearchPromptPair? Resolve(
        string providerName,
        string personDisplayName,
        string? currentTitle,
        string? currentEmployerName,
        long intelPersonId)
    {
        try
        {
            var root = _options.PromptTemplatesDir;
            var systemPath = Path.Combine(root, "PersonBrief", "system.md");
            var system = ReadCached(systemPath, required: true);
            if (system is null)
            {
                return null;
            }

            var userPath = Path.Combine(root, providerName, "user.md");
            var user = ReadCached(userPath, required: true);
            if (user is null)
            {
                return null;
            }

            return new ResearchPromptPair(
                Substitute(system, providerName, personDisplayName, currentTitle, currentEmployerName, intelPersonId),
                Substitute(user, providerName, personDisplayName, currentTitle, currentEmployerName, intelPersonId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD person research prompt resolution failed for person {DisplayName} (id {Id}), provider {ProviderName}.",
                personDisplayName,
                intelPersonId,
                providerName);
            return null;
        }
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

        _logger.LogWarning("BD person research prompt template not found: {Path}", path);
    }

    private static string Substitute(
        string text,
        string providerName,
        string personDisplayName,
        string? currentTitle,
        string? currentEmployerName,
        long intelPersonId)
    {
        return text
            .Replace("{TODAY_UTC}", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PERSON_DISPLAY_NAME}", personDisplayName, StringComparison.Ordinal)
            .Replace("{CURRENT_TITLE}", currentTitle ?? "(unknown)", StringComparison.Ordinal)
            .Replace("{CURRENT_EMPLOYER_NAME}", currentEmployerName ?? "(unknown)", StringComparison.Ordinal)
            .Replace("{INTEL_PERSON_ID}", intelPersonId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PROVIDER_NAME}", providerName, StringComparison.Ordinal);
    }
}
