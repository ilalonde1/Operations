using System.Collections.Generic;

namespace Kor.EmailSearch.Core
{
    public sealed class SearchResult
    {
        public int TotalRows { get; set; }
        public List<EmailRow> Rows { get; set; } = new List<EmailRow>();
    }
}
