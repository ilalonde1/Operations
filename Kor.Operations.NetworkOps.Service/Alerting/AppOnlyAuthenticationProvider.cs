#nullable enable
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Kor.Operations.NetworkOps.Service.Alerting;

// App-only (client-credential) Graph auth -- the same shape as FileSync's, token per request,
// MSAL caching the token in between.
internal sealed class AppOnlyAuthenticationProvider(IConfidentialClientApplication cca) : IAuthenticationProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public async Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        var token = await cca.AcquireTokenForClient(Scopes).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.TryAdd("Authorization", "Bearer " + token.AccessToken);
    }
}
