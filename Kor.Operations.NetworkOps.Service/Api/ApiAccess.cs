#nullable enable
using System.Security.Claims;

namespace Kor.Operations.NetworkOps.Service.Api;

// Who may use the Command Center API, as one pure decision so it can be tested without a token.
// The signature, issuer, audience and lifetime are checked by the JWT handler before this runs; this
// checks what the token SAYS: our tenant, the delegated scope the page asked for, and the app role
// Entra only grants to members of the cloud-only "NetworkOps Admins" group.
//
// MFA is not checked here: v2 access tokens carry no "amr" claim. It is enforced where the token is
// issued -- Conditional Access "NetworkOps API - require MFA" covers exactly this API -- so no token
// without MFA reaches this code. (docs/KOR-NetworkOps-Design-2026-09-28.md §7, Entra table.)
internal static class ApiAccess
{
    public const string Scope = "NetworkOps.Access";
    public const string Role = "NetworkOps.Admin";

    /// <summary>Null when the caller may proceed; otherwise why not (logged, never sent to the caller).</summary>
    public static string? Deny(ClaimsPrincipal user, string tenantId)
    {
        if (user.Identity?.IsAuthenticated != true) return "not signed in";
        if (!string.Equals(user.FindFirst("tid")?.Value, tenantId, StringComparison.OrdinalIgnoreCase)) return "token from another tenant";
        var scopes = (user.FindFirst("scp")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(Scope, StringComparer.Ordinal)) return $"token lacks the {Scope} scope";
        if (!user.FindAll("roles").Any(r => r.Value == Role)) return $"not in the {Role} role";
        return null;
    }

    /// <summary>The name written into every audit column: the signed-in UPN, never something the caller sends.</summary>
    public static string UserOf(ClaimsPrincipal user)
        => user.FindFirst("preferred_username")?.Value ?? user.FindFirst("upn")?.Value ?? user.FindFirst("oid")?.Value ?? "unknown";
}
