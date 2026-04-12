#nullable enable

namespace Kor.Operations.Services;

internal interface IAiContextProvider
{
    string ProviderName { get; }
    bool HasData { get; }
    string BuildContext();
    string BuildLocalContext();
}
