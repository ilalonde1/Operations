#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations;

public sealed class PeopleLookupService
{
    private readonly VantagepointRepository _vantagepointRepository;
    private readonly PreferencesRepository _preferencesRepository;
    private readonly ILogger<PeopleLookupService> _logger;

    public PeopleLookupService(
        VantagepointRepository vantagepointRepository,
        PreferencesRepository preferencesRepository,
        ILogger<PeopleLookupService> logger)
    {
        _vantagepointRepository = vantagepointRepository ?? throw new ArgumentNullException(nameof(vantagepointRepository));
        _preferencesRepository = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<List<(string Email, string? DisplayName)>> SearchPeopleAsync(string userUpn, string term, int limit = 10)
        => _preferencesRepository.SearchPeopleAsync(userUpn, term, limit);

    public async Task<string?> ResolveDisplayNameForEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        try
        {
            var vpRepo = BuildRepo();
            var rows = await vpRepo.SearchContactsAsync(email, 1);

            if (rows.Count > 0)
            {
                var r = rows[0];
                var name = (r.Name ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // swallow; we will fall back to email-derived name
        }

        return FallbackNameFromEmail(email);
    }

    public string FallbackNameFromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;

        var local = email.Split('@')[0]
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Trim();

        var parts = local.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            parts[i] = p.Length == 1
                ? p.ToUpper()
                : char.ToUpper(p[0]) + p.Substring(1).ToLower();
        }

        return string.Join(" ", parts);
    }

    private VantagepointRepository BuildRepo() => _vantagepointRepository;
}
