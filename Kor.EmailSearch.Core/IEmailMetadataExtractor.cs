#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.EmailSearch.Core;

/// <summary>
/// Abstraction over MsgReader/MIME parsing so callers
/// do not depend on any specific email library.
/// </summary>
public interface IEmailMetadataExtractor
{
    /// <summary>
    /// Parse the given file and return metadata.
    /// Implementations should handle both .msg and .eml based on the file path.
    /// </summary>
    Task<EmailMetadata> ExtractAsync(
        string projectNumber,
        string filePath,
        CancellationToken ct = default);
}
