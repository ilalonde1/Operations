#nullable enable
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kor.Operations.NetworkOps.Transport;

/// <summary>
/// Whether KOR-MESH01's TLS certificate is trusted -- the ONE rule, for the service's MeshCentral reads and (mirrored in
/// Service/Mesh/install-mesh.ps1) for a PC downloading the remote-control agent. Since 2026-10-02 MESH01 serves a Let's
/// Encrypt certificate that RENEWS every ~60 days, so a pin on the leaf would break at every renewal. Two things are
/// accepted, and nothing else:
///   - the exact pinned SHA-256 (the self-signed MeshCentral cert, so a rollback -- kor-mesh-cert-install.sh -- keeps
///     NetworkOps working), whatever Windows thinks of the chain; or
///   - a cert Windows itself validates (trusted chain, right name, in date) AND issued by Let's Encrypt.
/// The issuer requirement is the tightening over "any publicly-valid cert" (2026-10-03 audit): a cert some OTHER public CA
/// mis-issues for this name is refused, while LE renewals (same issuer organisation, new leaf/intermediate) still pass.
/// The pin alone was the rule until the LE swap, and the swap broke the Mesh sweep for 20 minutes (rack: "Unable to connect").
/// </summary>
public static class MeshTrust
{
    /// <summary>The issuer organisation the CA path requires. Stable across LE intermediate rotation (R10/R11/E5/E6/...).</summary>
    public const string ExpectedIssuer = "Let's Encrypt";

    public static bool Accepts(byte[]? rawCert, SslPolicyErrors errors, string pinnedSha256)
    {
        if (rawCert is null) return false;
        var pin = pinnedSha256.Replace(":", "").Replace(" ", "").Trim();
        // The pinned self-signed cert: accepted by exact hash regardless of the chain (the rollback path).
        if (pin.Length == 64 && Convert.ToHexString(SHA256.HashData(rawCert)).Equals(pin, StringComparison.OrdinalIgnoreCase))
            return true;
        // Otherwise require BOTH a chain Windows validated AND our expected issuer -- public-CA-valid alone is not enough.
        if (errors != SslPolicyErrors.None) return false;
        try { using var leaf = new X509Certificate2(rawCert); return leaf.Issuer.Contains(ExpectedIssuer, StringComparison.OrdinalIgnoreCase); }
        catch (CryptographicException) { return false; }
    }
}
