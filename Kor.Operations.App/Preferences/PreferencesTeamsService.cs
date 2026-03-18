#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations;

internal sealed class PreferencesTeamsService
{
    private readonly PreferencesRepository _repository;
    private readonly ILogger<PreferencesTeamsService> _logger;

    public PreferencesTeamsService(PreferencesRepository repository, ILogger<PreferencesTeamsService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Team>> LoadTeamsAsync(string userUpn)
    {
        var teams = new List<Team>();
        var rows = await _repository.GetTeamsAsync(userUpn);

        foreach (var (teamId, name) in rows)
        {
            if (teams.Any(t => t.Id == teamId))
                continue;

            var team = new Team
            {
                Id = teamId,
                Name = name,
                IsCommon = false
            };

            var members = await _repository.GetMembersAsync(teamId);

            foreach (var (email, displayName) in members)
            {
                var resolved =
                    !string.IsNullOrWhiteSpace(displayName)
                        ? displayName
                        : FallbackNameFromEmail(email);

                team.Members.Add(new TeamMember
                {
                    Email = email,
                    DisplayName = resolved
                });
            }

            teams.Add(team);
        }

        return teams;
    }

    public async Task<Team> AddTeamAsync(string userUpn, string name)
    {
        var id = await _repository.AddTeamAsync(userUpn, name);
        return new Team
        {
            Id = id,
            Name = name,
            IsCommon = false
        };
    }

    public Task DeleteTeamAsync(Guid teamId)
        => _repository.DeleteTeamAsync(teamId);

    public async Task<TeamMember> AddMemberAsync(Guid teamId, string email, string? displayName)
    {
        await _repository.AddMemberAsync(teamId, email, displayName);

        return new TeamMember
        {
            Email = email,
            DisplayName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : FallbackNameFromEmail(email)
        };
    }

    public Task RemoveMemberAsync(Guid teamId, string email)
        => _repository.RemoveMemberAsync(teamId, email);

    private static string FallbackNameFromEmail(string email)
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
}
