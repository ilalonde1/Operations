#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Kor.EmailSearch.Core
{
    public sealed class EmailSearchService : IEmailSearchService
    {
        private readonly string _connString;
        public EmailSearchService(string connString) => _connString = connString;

        // Map each row coming back from the proc, including TotalRows (which SQL returns as BIGINT/long)
        private sealed class RowWithTotal
        {
            public long TotalRows { get; set; }                 // <- long on purpose
            public int EmailId { get; set; }
            public string ProjectNumber { get; set; } = "";
            public string Subject { get; set; } = "";
            public string FromEmail { get; set; } = "";
            public DateTime? SentOnUtc { get; set; }
            public string FilePath { get; set; } = "";
            public int AttachmentCount { get; set; }
            public bool HasAttachments { get; set; }
        }

        public async Task<SearchResult> SearchAsync(
            string? query = null, string? project = null,
            DateTime? fromUtc = null, DateTime? toUtc = null,
            bool? hasAttach = null, int page = 1, int pageSize = 50)
        {
            var fullTextQuery = BuildFullTextCondition(query);

            using (var conn = new SqlConnection(_connString))
            {
                await conn.OpenAsync();

                using (var multi = await conn.QueryMultipleAsync(
                    "dbo.SearchEmailsPaged",
                    new { query = fullTextQuery, project, fromUtc, toUtc, hasAttach, page, pageSize },
                    commandTimeout: 120,
                    commandType: CommandType.StoredProcedure))
                {
                    var typed = await multi.ReadAsync<RowWithTotal>();

                    var result = new SearchResult { Rows = new List<EmailRow>() };
                    foreach (var r in typed)
                    {
                        if (result.TotalRows == 0)
                        {
                            // Safe conversion from long -> int
                            result.TotalRows = r.TotalRows > int.MaxValue
                                ? int.MaxValue
                                : (int)r.TotalRows;
                        }

                        result.Rows.Add(new EmailRow
                        {
                            EmailId = r.EmailId,
                            ProjectNumber = r.ProjectNumber ?? "",
                            Subject = r.Subject ?? "",
                            FromEmail = r.FromEmail ?? "",
                            SentOnUtc = r.SentOnUtc,
                            FilePath = r.FilePath ?? "",
                            AttachmentCount = r.AttachmentCount,
                            HasAttachments = r.HasAttachments
                        });
                    }

                    return result;
                }
            }
        }
        private static string? BuildFullTextCondition(string? input)
        {
            if (input is null)
                return null;

            if (string.IsNullOrWhiteSpace(input))
                return input;

            var normalizedInput = input.Trim();

            var tokens = normalizedInput
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Select(t =>
                {
                    // Escape embedded quotes for SQL full-text syntax safety
                    var safe = t.Replace("\"", "\"\"");
                    return $"\"{safe}*\"";
                })
                .ToArray();

            if (tokens.Length == 0)
                return input;

            return string.Join(" AND ", tokens);
        }
    }
}
