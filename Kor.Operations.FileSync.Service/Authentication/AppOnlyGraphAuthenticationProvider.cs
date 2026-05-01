#nullable enable
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Kor.Operations.FileSync.Service.Authentication;

internal sealed class AppOnlyGraphAuthenticationProvider : IAuthenticationProvider
{
    private static readonly string[] DefaultScopes = { "https://graph.microsoft.com/.default" };

    private readonly IConfidentialClientApplication _cca;

    public AppOnlyGraphAuthenticationProvider(IConfidentialClientApplication cca)
    {
        _cca = cca ?? throw new ArgumentNullException(nameof(cca));
    }

    public async Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _cca.AcquireTokenForClient(DefaultScopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        request.Headers.TryAdd("Authorization", "Bearer " + result.AccessToken);
    }
}
