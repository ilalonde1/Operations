using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public interface IRecipientResolver
    {
        Task<IReadOnlyList<RecipientSuggestion>> SearchSuggestionsAsync(
            string userUpn,
            string query,
            int maxResults,
            CancellationToken ct);
    }

    public sealed record RecipientSuggestion(string Email, string Display);
}
