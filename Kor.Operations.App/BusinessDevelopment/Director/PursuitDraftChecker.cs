#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Rendering.Proposal;
using Kor.Opportunities.Data.Crm;
using UglyToad.PdfPig;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed record DraftDocument(string FileName, string? Text, int? Pages, ProposalDocxMetadata? Metadata)
{
    public static async Task<DraftDocument> ReadAsync(string fileName, Stream source, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return new(fileName, null, null, null);
        // SQL streams are forward-only. Spool locally, with automatic cleanup, for the document readers.
        var path = Path.Combine(Path.GetTempPath(), "kor-draft-" + Guid.NewGuid().ToString("N"));
        await using var seekable = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await source.CopyToAsync(seekable, ct).ConfigureAwait(false);
        seekable.Position = 0;
        ct.ThrowIfCancellationRequested();
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            var docx = ProposalDocxReader.Read(seekable);
            return new(fileName, docx.Text, null, docx.Metadata);
        }
        using var pdf = PdfDocument.Open(seekable);
        var text = string.Join("\n", pdf.GetPages().Select(p => { ct.ThrowIfCancellationRequested(); return p.Text; }));
        return new(fileName, text, pdf.NumberOfPages, null);
    }
}

public sealed record DraftCheckResult(string Status, string Note);

public static class PursuitDraftChecker
{
    // Null means no write at all. Manual statuses, notes and attribution belong to a person.
    public static DraftCheckResult? Check(PursuitRequirement item, DraftDocument draft, string rfp)
    {
        if (item.CheckKind == "Manual") return null;
        var parameter = item.CheckParameter ?? "";
        DraftCheckResult Result(string status, string found)
        {
            var note = $"{draft.FileName}: {found}";
            return new(status, note.Length <= 500 ? note : note[..497] + "…");
        }
        switch (item.CheckKind)
        {
            case "PhrasePresent":
                if (draft.Text is null) return Result("Open", "Text could not be read; choose a PDF or Word document.");
                if (string.IsNullOrWhiteSpace(parameter)) return Result("Open", "Enter a phrase to check.");
                var present = draft.Text.Contains(parameter, StringComparison.OrdinalIgnoreCase);
                return Result(present ? "Met" : "NotMet", $"Phrase {(present ? "found" : "not found")}: {parameter}");
            case "PageLimit":
                if (Path.GetExtension(draft.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                    return Result("Open", "Render to PDF to count pages");
                if (!int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
                    return Result("Open", "Enter a positive whole page limit.");
                if (draft.Pages is not { } pages) return Result("Open", "Choose a PDF to count pages.");
                return Result(pages <= limit ? "Met" : "NotMet", $"{pages} PDF pages; limit {limit}. Counts the entire PDF; verify any exclusions manually.");
            case "FileName":
                if (string.IsNullOrWhiteSpace(parameter)) return Result("Open", "Enter a file name pattern.");
                if (parameter.Contains("{rfp}", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(rfp))
                    return Result("Open", "The bid has no RFP number.");
                // Escape the RFP separately: only stars in the person's pattern are wildcards.
                var pattern = string.Join(".*", parameter.Split('*')
                    .Select(p => string.Join(Regex.Escape(rfp), p.Split("{rfp}", StringSplitOptions.None).Select(Regex.Escape))));
                var matches = Regex.IsMatch(draft.FileName, "\\A" + pattern + "\\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
                return Result(matches ? "Met" : "NotMet", $"File name {(matches ? "matches" : "does not match")} {parameter.Replace("{rfp}", rfp, StringComparison.Ordinal)}");
            default:
                return Result("Open", "Unknown check kind; review the item.");
        }
    }
}
