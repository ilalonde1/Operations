#nullable enable
using System.Net.Security;
using System.Security.Cryptography;

namespace Kor.Operations.NetworkOps.Transport;

/// <summary>
/// Whether KOR-MESH01's TLS certificate is trusted -- the ONE rule, for the service's MeshCentral reads and (mirrored in
/// Service/Mesh/install-mesh.ps1) for a PC downloading the remote-control agent. Since 2026-10-02 MESH01 serves a Let's
/// Encrypt certificate that RENEWS every ~60 days, so a pin on it would break at every renewal: a certificate that passes
/// Windows' own validation (a trusted chain, the right name, in date) is accepted. The SHA-256 pin still accepts the
/// self-signed MeshCentral certificate, so a rollback to it (kor-mesh-cert-install.sh) keeps NetworkOps working.
/// The pin alone was the rule until the swap, and the swap broke the Mesh sweep for 20 minutes (rack: "Unable to connect").
/// </summary>
public static class MeshTrust
{
    public static bool Accepts(byte[]? rawCert, SslPolicyErrors errors, string pinnedSha256)
    {
        if (rawCert is null) return false;
        if (errors == SslPolicyErrors.None) return true;
        var pin = pinnedSha256.Replace(":", "").Replace(" ", "").Trim();
        return pin.Length == 64 && Convert.ToHexString(SHA256.HashData(rawCert)).Equals(pin, StringComparison.OrdinalIgnoreCase);
    }
}
