#nullable enable
using System.Globalization;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class FileSystemAwardProgramResearchPromptCatalog : IAwardProgramResearchPromptCatalog
{
    private readonly BdResearchExecutorOptions _options;
    private readonly ILogger<FileSystemAwardProgramResearchPromptCatalog> _logger;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileSystemAwardProgramResearchPromptCatalog(
        IOptions<BdResearchExecutorOptions> options,
        ILogger<FileSystemAwardProgramResearchPromptCatalog> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ResearchPromptPair? Resolve()
    {
        try
        {
            var root = _options.PromptTemplatesDir;
            var system = ReadCached(Path.Combine(root, "AwardPrograms", "system.md"), required: true);
            var user = ReadCached(Path.Combine(root, "AwardPrograms", "user.md"), required: true);
            if (system is null || user is null)
            {
                return null;
            }

            return new ResearchPromptPair(Substitute(system), Substitute(user));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Award program prompt resolution failed.");
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

        _logger.LogWarning("Award program research prompt template not found: {Path}", path);
    }

    private static string Substitute(string text) =>
        text.Replace("{TODAY_UTC}", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
