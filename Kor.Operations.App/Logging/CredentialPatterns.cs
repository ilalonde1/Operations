namespace Kor.Operations.Logging;

using System.Text.RegularExpressions;

internal static class CredentialPatterns
{
    internal static readonly Regex CredentialPattern = new(
        @"(Password|PWD|User Id|UID)\s*=\s*[^;'\""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
