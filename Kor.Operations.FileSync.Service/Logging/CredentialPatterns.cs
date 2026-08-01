#nullable enable
// TODO: Lift these three credential-redaction classes into Kor.Operations.Core
// once the Command Center window also needs them. Duplicated here verbatim from
// Kor.Operations.App/Logging/* to keep this scaffold step bounded.
namespace Kor.Operations.FileSync.Service.Logging;

using System.Text.RegularExpressions;

internal static class CredentialPatterns
{
    internal static readonly Regex CredentialPattern = new(
        @"(Password|PWD|User Id|UID)\s*=\s*[^;'\""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
