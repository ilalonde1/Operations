#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Email;

internal sealed class FavoriteProjectsService
{
    private readonly PreferencesRepository _preferencesRepository;

    public FavoriteProjectsService(PreferencesRepository preferencesRepository, ILogger<FavoriteProjectsService> logger)
    {
        _preferencesRepository = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
        _ = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ProjectEntry>> LoadFavoritesAsync(string userUpn, IEnumerable<ProjectEntry> allProjects)
    {
        var favoriteProjects = new List<ProjectEntry>();
        var rows = await _preferencesRepository.GetFavoritesAsync(userUpn);
        var allProjectsList = allProjects.ToList();

        foreach (var (ProjectNo, ProjectName) in rows)
        {
            if (string.IsNullOrWhiteSpace(ProjectNo))
                continue;

            var match = allProjectsList
                .FirstOrDefault(p => p.Code.Equals(ProjectNo, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                favoriteProjects.Add(match);
            }
        }

        return favoriteProjects;
    }
}
