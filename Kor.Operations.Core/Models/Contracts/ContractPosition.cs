#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Contracts;

/// <summary>
/// One clause from one contract document KOR has on file: a row of dbo.vw_ContractPositions in
/// KorTransmittals (db/KorTransmittals/011), filled by tools/ContractClauseLibrary. Client paper only,
/// and only documents that are signed or the latest version on file for their job.
/// </summary>
public sealed record ContractPosition(
    string SourceKey,
    string? Job,
    string? ClientName,
    string? ProjectName,
    string Paper,
    string Role,
    bool IsLatest,
    DateTime? FileDate,
    string FilePath,
    string Part,
    int ClauseId,
    string Section,
    string? Heading,
    string Topics,
    int Words,
    string ClauseText)
{
    /// <summary>"Signed" when the file is named signed/executed or carries a DocuSign marker; else "Latest on file".</summary>
    public bool IsSigned => string.Equals(Role, "executed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the Positions screen asks the store for. Null means "any".</summary>
public sealed record ContractPositionQuery(string? Topic, string? ClientName, string? Text, bool SignedOnly, int Take = 500);

/// <summary>The topics tools/ContractClauseLibrary/extract_clauses.py tags, in the order the screen offers them.</summary>
public static class ContractTopics
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("limitation_of_liability", "Limitation of liability"),
        ("consequential_damages", "Consequential damages"),
        ("indemnity", "Indemnity"),
        ("intellectual_property", "Intellectual property"),
        ("insurance", "Insurance"),
        ("termination", "Termination"),
        ("suspension", "Suspension / stand-down"),
        ("payment", "Payment / withholding"),
        ("standard_of_care", "Standard of care"),
        ("warranty_reperform", "Warranty / re-perform"),
        ("permits_approvals", "Permits / applicable laws"),
        ("dispute", "Dispute / governing law"),
        ("confidentiality", "Confidentiality"),
        ("ai_tools", "AI tools"),
        ("limitation_period", "Limitation period"),
        ("assignment_subconsultants", "Assignment / subconsultants"),
        ("lien", "Liens"),
        ("field_review", "Field review"),
        ("schedule_delay", "Schedule / delay"),
    ];

    public static string LabelOf(string key)
    {
        foreach (var (k, label) in All)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return key.Replace('_', ' ');
    }
}
