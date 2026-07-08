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
    public string StoredPath => Model.LocalPath;
    public string TypeDisplay => Model.ContentType ?? "File";
    public string UploadedByDisplay => Model.UploadedBy;
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
