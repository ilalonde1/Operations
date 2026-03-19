#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations;

public sealed class PreferencesFavoritesService
{
    private readonly PreferencesRepository _repository;
    private readonly ILogger<PreferencesFavoritesService> _logger;

    public PreferencesFavoritesService(PreferencesRepository repository, ILogger<PreferencesFavoritesService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<FavoriteProject>> LoadFavoritesAsync(string userUpn)
    {
        var favorites = new List<FavoriteProject>();

        foreach (var (ProjectNo, ProjectName) in await _repository.GetFavoritesAsync(userUpn))
        {
            favorites.Add(new FavoriteProject
            {
                ProjectNo = ProjectNo,
                ProjectName = ProjectName ?? string.Empty
            });
        }

        return favorites;
    }

    public Task AddFavoriteAsync(string userUpn, string projectNo, string projectName)
        => _repository.AddFavoriteAsync(userUpn, projectNo, projectName);

    public Task RemoveFavoriteAsync(string userUpn, string projectNo)
        => _repository.RemoveFavoriteAsync(userUpn, projectNo);
}
