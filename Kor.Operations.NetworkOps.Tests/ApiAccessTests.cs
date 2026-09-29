#nullable enable
using System.Security.Claims;
using Kor.Operations.NetworkOps.Service.Api;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Who may use the Command Center API, given a token's claims.
//
// WHAT IT COVERS: our tenant only; the delegated scope must be present (among others); the
// NetworkOps.Admin role must be present; an anonymous caller is refused; the audit name is the
// signed-in UPN. WHAT IT DOES NOT: signature, issuer, audience or lifetime (the JWT handler checks
// those before this runs), or MFA, which Conditional Access enforces when the token is issued --
// proven live by signing in, not here. A SAME-CLASS FAULT IT WOULD NOT CATCH: a mistyped audience in
// appsettings would reject every real token while every case here passes; only a live call shows it.
public sealed class ApiAccessTests
{
    private const string Tenant = "d9be1f7f-aacf-461a-8d1b-5528b86d540f";

    private static ClaimsPrincipal User(string? tid = Tenant, string? scp = "NetworkOps.Access", string[]? roles = null, string upn = "ilalonde@korstructural.com")
    {
        var claims = new List<Claim> { new("preferred_username", upn) };
        if (tid is not null) claims.Add(new("tid", tid));
        if (scp is not null) claims.Add(new("scp", scp));
        foreach (var r in roles ?? ["NetworkOps.Admin"]) claims.Add(new("roles", r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    [Fact]
    public void A_member_of_our_tenant_with_the_scope_and_role_is_let_in()
        => Assert.Null(ApiAccess.Deny(User(), Tenant));

    [Fact]
    public void The_scope_may_arrive_among_others()
        => Assert.Null(ApiAccess.Deny(User(scp: "User.Read NetworkOps.Access"), Tenant));

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "NetworkOps.Access", "NetworkOps.Admin", "another tenant")]
    [InlineData(Tenant, "User.Read", "NetworkOps.Admin", "scope")]
    [InlineData(Tenant, null, "NetworkOps.Admin", "scope")]
    [InlineData(Tenant, "NetworkOps.Access", "SomethingElse", "role")]
    [InlineData(Tenant, "NetworkOps.AccessX", "NetworkOps.Admin", "scope")]   // no prefix matching
    public void Anything_short_of_all_three_is_refused(string tid, string? scp, string role, string reason)
        => Assert.Contains(reason, ApiAccess.Deny(User(tid, scp, [role]), Tenant));

    [Fact]
    public void An_anonymous_caller_is_refused()
        => Assert.Equal("not signed in", ApiAccess.Deny(new ClaimsPrincipal(new ClaimsIdentity()), Tenant));

    [Fact]
    public void The_audit_name_is_the_signed_in_upn()
        => Assert.Equal("ilalonde@korstructural.com", ApiAccess.UserOf(User()));
}
