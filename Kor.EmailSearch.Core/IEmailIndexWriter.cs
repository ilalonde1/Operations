#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.EmailSearch.Core;

public interface IEmailIndexWriter
{
    Task<long> UpsertEmailAsync(
        string projectNumber,
        string filePath,
        string source = "OUTLOOK",
        CancellationToken ct = default);
}
