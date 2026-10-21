#nullable enable
using System;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.Crm;

/// <summary>Display projection of one pursuit attachment.</summary>
public sealed class PursuitFileRowView
{
    public PursuitFileRowView(PursuitFile model)
    {
        Model = model;
    }

    public PursuitFile Model { get; }
    public long Id => Model.Id;
    public string FileName => Model.FileName;
    public string? StoredPath => Model.LocalPath;
    /// <summary>What kind of file, in words a person uses (PDF, Excel, Word), not a media type.</summary>
    public string TypeDisplay => System.IO.Path.GetExtension(Model.FileName).ToLowerInvariant() switch
    {
        ".pdf" => "PDF",
        ".xlsx" or ".xlsm" or ".xls" or ".csv" => "Excel",
        ".docx" or ".doc" => "Word",
        ".zip" => "Zip",
        ".dwg" or ".dxf" => "Drawing",
        ".msg" or ".eml" => "Email",
        ".png" or ".jpg" or ".jpeg" => "Image",
        var ext when ext.Length > 1 => ext[1..].ToUpperInvariant(),
        _ => "File",
    };

    /// <summary>Who added it; a portal download says so plainly instead of the job's key.</summary>
    public string UploadedByDisplay => Model.UploadedBy switch
    {
        "apc" => "APC download",
        "bids-and-tenders" => "Bids&Tenders download",
        var by => by,
    };
    public string UploadedDisplay => Model.UploadedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string SizeDisplay
    {
        get
        {
            if (Model.SizeBytes is not { } b || b < 0) return "";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = b;
            var u = 0;
            while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
            return u == 0 ? $"{b} B" : $"{size:0.#} {units[u]}";
        }
    }
}
