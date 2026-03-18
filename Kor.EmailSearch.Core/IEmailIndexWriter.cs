#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.EmailSearch.Core;

public interface IEmailIndexWriter
{
    /// <summary>
    /// Upsert an email into dbo.Emails based on its file path.
    /// Returns EmailId (bigint).
    /// </summary>
    Task<long> UpsertEmailAsync(
        string projectNumber,
        string filePath,
        string source = "OUTLOOK",
        CancellationToken ct = default);
}
