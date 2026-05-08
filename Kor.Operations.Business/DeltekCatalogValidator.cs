#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Kor.Operations.Financials;

public static class DeltekCatalogValidator
{
    private const string DefaultCatalog = "C0000052267P_1_KOR00000000";
    private static readonly Regex CatalogNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ValidateCatalog(string catalog)
    {
        var value = (catalog ?? string.Empty).Trim();
        if (!CatalogNamePattern.IsMatch(value))
            throw new InvalidOperationException("Deltek catalog name contains unsupported characters.");
        return value;
    }

    // Standard pattern: fall back to the configured KOR catalog when the option is blank,
    // then validate. Every site that interpolates the catalog into SQL should route through here
    // so a misconfigured Vp.Catalog can never produce a SQL-injection vector.
    public static string ResolveCatalog(string? catalog)
        => ValidateCatalog(string.IsNullOrWhiteSpace(catalog) ? DefaultCatalog : catalog);
}
