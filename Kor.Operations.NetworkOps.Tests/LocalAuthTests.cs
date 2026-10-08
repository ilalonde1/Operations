#nullable enable
using Kor.Operations.NetworkOps.Transport;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// LocalAuth decides whether a host is reached with a LOCAL credential (a workgroup box) or the service account (a domain
// machine). The real SMB session -- WNetAddConnection2 against \\host\IPC$ -- is live-verified on BK01; this pins the pure
// decision around it: which host strings carry a credential, that aliases and casing resolve to it, and that an
// unregistered (domain) host carries none and Ensure does nothing for it (never touching the network).
//
// WHAT THIS DOES NOT COVER: that WNetAddConnection2 actually authenticates, that the c$ write and SCM call then ride the
// session, or that a dropped session re-opens -- all of which need a real workgroup target (BK01). A same-class fault it
// would NOT catch: a credential registered under the wrong host string (the IP when the push addresses the name, or vice
// versa), since both are just strings here -- that only shows when the live push reaches, or fails to reach, the box.
public sealed class LocalAuthTests
{
    [Fact]
    public void A_registered_host_and_its_aliases_resolve_case_insensitively()
    {
        LocalAuth.Register("10.255.0.99", @"TESTBOX\administrator", "unit-test-not-a-real-password", ["TESTBOX-NAME"]);

        Assert.True(LocalAuth.Has("10.255.0.99"));        // the IP the push targets
        Assert.True(LocalAuth.Has("TESTBOX-NAME"));       // its name alias
        Assert.True(LocalAuth.Has("testbox-name"));       // a probe may address it in any casing
        Assert.False(LocalAuth.Has("10.255.0.98"));       // a neighbour with no credential is still domain-only
    }

    [Fact]
    public void An_unregistered_host_has_no_credential_and_Ensure_does_nothing()
    {
        Assert.False(LocalAuth.Has("unit-test-domain-pc"));
        LocalAuth.Ensure("unit-test-domain-pc");   // the domain path: returns at once, never touches the network
    }
}
