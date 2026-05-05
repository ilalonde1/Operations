#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Kor.Operations.Financials;

internal static class DeltekCatalogValidator
{
    private static readonly Regex CatalogNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ValidateCatalog(string catalog)
    {
        var value = (catalog ?? string.Empty).Trim();
        if (!CatalogNamePattern.IsMatch(value))
            throw new InvalidOperationException("Deltek catalog name contains unsupported characters.");
        return value;
    }
}
