#nullable enable
using Kor.Operations.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public sealed class RecipientResolver : IRecipientResolver
    {
        private readonly VantagepointRepository _vantagepointRepository;
        private readonly PreferencesRepository _preferencesRepository;

        public RecipientResolver(
            VantagepointRepository vantagepointRepository,
            PreferencesRepository preferencesRepository)
        {
            _vantagepointRepository = vantagepointRepository ?? throw new ArgumentNullException(nameof(vantagepointRepository));
            _preferencesRepository = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
        }

        public async Task<IReadOnlyList<RecipientSuggestion>> SearchSuggestionsAsync(
            string userUpn,
            string query,
            int maxResults,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<RecipientSuggestion>();
            }

            var peopleTask = _vantagepointRepository.SearchPeopleAsync(query, maxResults, ct);
            var memoryTask = SearchPreferencePeopleAsync(userUpn, query, maxResults);

            await Task.WhenAll(peopleTask, memoryTask).ConfigureAwait(false);
            var people = await peopleTask.ConfigureAwait(false);
            var memory = await memoryTask.ConfigureAwait(false);

            var results = memory
                .Concat(people.Select(p =>
                    new RecipientSuggestion(
                        p.Email,
                        $"{(string.IsNullOrWhiteSpace(p.Name) ? p.Email : p.Name)}  <{p.Email}>")))
                .GroupBy(p => p.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(maxResults)
                .ToList();

            return results;
        }

        private async Task<IReadOnlyList<RecipientSuggestion>> SearchPreferencePeopleAsync(
            string userUpn,
            string query,
            int maxResults)
        {
            try
            {
                return (await _preferencesRepository.SearchPeopleAsync(userUpn, query, maxResults).ConfigureAwait(false))
                    .Select(p => new RecipientSuggestion(
                        p.Email,
                        $"{(string.IsNullOrWhiteSpace(p.DisplayName) ? p.Email : p.DisplayName)}  <{p.Email}>"))
                    .ToList();
            }
            catch
            {
                return Array.Empty<RecipientSuggestion>();
            }
        }
    }
}
