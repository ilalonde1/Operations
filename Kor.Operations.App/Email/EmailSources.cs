#nullable enable
namespace Kor.Operations.App.Email;

/// <summary>
/// Canonical writer labels for the <c>Source</c> column in <c>KorEmailIndex.dbo.Emails</c>.
/// Every code path that calls <c>dbo.UpsertEmail</c> must pass one of these values
/// — the proc rejects calls that omit <c>@Source</c>.
/// </summary>
/// <remarks>
/// The schema default <c>'FILEDROP'</c> is intentionally NOT exposed here. Hitting
/// that default means a writer forgot to specify a Source — that's a bug, not a
/// category. Historical rows tagged FILEDROP were produced by older clients that
/// didn't pass <c>@Source</c> at all.
///
/// Cross-codebase note: <c>EmailFilerv2</c> (the .NET 4.8 VSTO addin) is in a
/// separate solution that cannot reference this assembly. It defines its own
/// local <c>VSTO</c> constant — keep the value in sync if either label changes.
///
/// The PowerShell backfill script (<c>Backfill-EmailIndex.ps1</c>) hardcodes
/// <c>"BACKFILL"</c> as a literal string for the same reason.
/// </remarks>
internal static class EmailSources
{
    /// <summary>
    /// Operations.App WPF "File Emails to Project" picker. Manual filing of
    /// emails the user dragged in from Outlook or selected via the picker UI.
    /// </summary>
    public const string WpfPicker = "WPF-PICKER";

    /// <summary>
    /// EmailFilerv2 VSTO Outlook addin. Triggered when items move into one of
    /// the per-project subfolders under "Emails To File" in Outlook.
    /// Mirror of the addin's local constant — keep in sync.
    /// </summary>
    public const string Vsto = "VSTO";

    /// <summary>
    /// One-off catch-up writes from <c>Backfill-EmailIndex.ps1</c> or similar
    /// maintenance jobs. The PowerShell script uses the literal value directly;
    /// this constant exists so any C# code that filters by source has a single
    /// place to look.
    /// </summary>
    public const string Backfill = "BACKFILL";
}
