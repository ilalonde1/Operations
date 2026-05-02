#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

internal enum BucketKind
{
    Pdf,
    Image,
}

// Maps a logical bucket (Stickfile/SSI/RFI/Photos) to (a) the local subpath
// under <project>\ that triggers the bucket, (b) the SharePoint subfolder
// that file uploads land under, and (c) the file extensions in scope.
//
// Verbatim from watcher.ps1 §9-12 + the SINGLE_SYNC_*.ps1 destination paths.
// "Stickfile" lands files at the SharePoint project ROOT (no subfolder),
// matching PS1: $SPFolder = $ProjectName. The other three nest under the
// stated subfolder.
internal sealed record SyncBucket(
    string Name,
    string LocalSubpath,        // e.g. "05 Stickfile" or "04 Construction Admin\02 SSI (Structural Site Instructions)"
    string? SharePointSubfolder, // e.g. "Photos", "SSI", "RFI"; null for Stickfile (uploads to project root)
    BucketKind Kind,
    IReadOnlyCollection<string> Extensions)
{
    public static IReadOnlyList<SyncBucket> All { get; } = new[]
    {
        new SyncBucket(
            "Stickfile",
            @"05 Stickfile",
            null,
            BucketKind.Pdf,
            new[] { ".pdf" }),
        new SyncBucket(
            "SSI",
            @"04 Construction Admin\02 SSI (Structural Site Instructions)",
            "SSI",
            BucketKind.Pdf,
            new[] { ".pdf" }),
        new SyncBucket(
            "RFI",
            @"04 Construction Admin\03 RFI (Request for Info)\Sent to Inspectors",
            "RFI",
            BucketKind.Pdf,
            new[] { ".pdf" }),
        new SyncBucket(
            "Photos",
            @"04 Construction Admin\07 Photos",
            "Photos",
            BucketKind.Image,
            new[] { ".jpg", ".jpeg", ".png", ".heic", ".bmp", ".tif", ".tiff" }),
    };

    public static SyncBucket? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var b in All)
        {
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                return b;
        }

        return null;
    }
}
