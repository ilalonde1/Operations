#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IKorClientBdIntelligenceStore
{
    /// <summary>
    /// Load BD intelligence for a KOR client by Clendor.ClientID.
    /// Returns empty-ish object if the client isn't yet linked to a CanonicalOrg.
    /// </summary>
    Task<ClientBdIntelligence> LoadByClendorIdAsync(string clendorClientId, CancellationToken ct);
}
