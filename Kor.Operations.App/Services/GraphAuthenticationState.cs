#nullable enable
using Microsoft.Kiota.Abstractions.Authentication;

namespace Kor.Operations.Services
{
    internal sealed class GraphAuthenticationState
    {
        internal IAuthenticationProvider? AuthenticationProvider { get; set; }
    }
}
