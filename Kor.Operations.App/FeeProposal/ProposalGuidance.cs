#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Kor.Opportunities.Data.Knowledge;

namespace Kor.Operations.App.FeeProposal;

public sealed record ProposalGuidanceRow(KnowledgeClaimRow Claim)
{
    public string Statement => Claim.Statement;
    public string Citation => $"(Shipley, {Claim.Ref ?? "passage " + Claim.PassageId})";
}

public sealed class ProposalGuidance(IKnowledgeClaimStore claims)
{
    public const string SourceTitle = "Shipley Associates Proposal Guide";
    public static string Keywords(ProposalBlockType block) => block switch
    {
        ProposalBlockType.Cover => "cover",
        ProposalBlockType.Introduction => "executive summary",
        ProposalBlockType.Company => "company|capability|qualification",
        ProposalBlockType.Personnel => "team|resume|staff",
        ProposalBlockType.References => "reference|past performance",
        ProposalBlockType.ProjectDescription => "requirement|understanding|customer",
        ProposalBlockType.Scope => "scope|approach|methodology",
        ProposalBlockType.FeeTable or ProposalBlockType.RfpRateSchedule or ProposalBlockType.RatesTable => "price|cost|fee",
        ProposalBlockType.ExcludedServices => "scope|exception|compliance",
        ProposalBlockType.ApprovalToProceed => "approval|agreement|compliance",
        ProposalBlockType.SignaturePage => "signature|approval|submission",
        ProposalBlockType.FreeText => "writing|clarity|proposal",
        ProposalBlockType.PageBreak => "page|layout|format",
        _ => throw new ArgumentOutOfRangeException(nameof(block))
    };
    public async Task<IReadOnlyList<ProposalGuidanceRow>> LoadAsync(ProposalBlockType block, CancellationToken ct)
    {
        var source = (await claims.ListSourcesAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(s => string.Equals(s.Title, SourceTitle, StringComparison.OrdinalIgnoreCase));
        if (source is null) return Array.Empty<ProposalGuidanceRow>();
        var keywords = Keywords(block);
        var rows = await claims.ListClaimsAsync(source.Id, "Rule:" + keywords, 8, ct).ConfigureAwait(false);
        return rows.Where(c => c.SourceId == source.Id && c.Kind == "Rule" && c.RetiredAtUtc is null
                && keywords.Split('|').Any(k => c.Statement.Contains(k, StringComparison.OrdinalIgnoreCase)
                    || c.Concepts.Any(concept => concept.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Take(8).Select(c => new ProposalGuidanceRow(c)).ToArray();
    }
    public Task<KnowledgePassageText?> PassageAsync(ProposalGuidanceRow row, CancellationToken ct)
        => claims.GetPassageAsync(row.Claim.PassageId, ct);
}
