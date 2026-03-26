#nullable enable
using System;
using System.Threading.Tasks;

namespace Kor.EmailSearch.Core;

public interface IEmailSearchService
{
    Task<SearchResult> SearchAsync(
        string? query = null, string? project = null,
        DateTime? fromUtc = null, DateTime? toUtc = null,
        bool? hasAttach = null, int page = 1, int pageSize = 50);
}
