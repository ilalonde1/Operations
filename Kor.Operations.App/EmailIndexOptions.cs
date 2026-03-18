#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Kor.Operations;

internal sealed class EmailIndexOptions
{
    public long MaxAttachmentBytes { get; init; } = 52_428_800;
    public IReadOnlySet<string> BlockedExtensions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".ps1", ".msi", ".vbs", ".dll" };

    public static EmailIndexOptions FromAppConfig()
    {
        var maxBytes = long.TryParse(
            ConfigurationManager.AppSettings["EmailIndex:MaxAttachmentBytes"],
            out var mb) ? mb : 52_428_800;

        var blocked = ConfigurationManager.AppSettings["EmailIndex:BlockedExtensions"]
            ?.Split(',')
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
               { ".exe", ".bat", ".cmd", ".ps1", ".msi", ".vbs", ".dll" };

        return new EmailIndexOptions
        {
            MaxAttachmentBytes = maxBytes,
            BlockedExtensions = blocked
        };
    }
}
