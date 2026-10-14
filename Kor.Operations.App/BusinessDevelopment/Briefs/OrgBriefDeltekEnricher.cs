#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>Shared org-export enrichment using the existing Deltek capability.</summary>
public sealed class OrgBriefDeltekEnricher(IDeltekClientContextService deltek)
{
    public async Task<OrgBriefData> EnrichAsync(OrgBriefData data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.DeltekClientId))
            return data with { Deltek = null, DeltekNote = "Deltek relationship history unknown: organization not linked to a Deltek client." };
        try
        {
            var intel = await deltek.LoadAsync(data.DeltekClientId.Trim(), ct).ConfigureAwait(false);
            if (intel is null)
                return data with { Deltek = null, DeltekNote = "Deltek relationship history unavailable: linked client could not be resolved." };

            return data with
            {
                DeltekNote = null,
                Deltek = new OrgBriefDeltekSection(
                    intel.ClientId, intel.ClientName, intel.ProjectCount, intel.LifetimeFee,
                    intel.LatestProjectStart, intel.LatestProjectName,
                    intel.Projects.Take(5).Select(p => new OrgBriefDeltekProject(
                        p.Wbs1, p.Name, p.OpenDate, p.Status, p.Fee, p.FeeBilled)).ToList(),
                    intel.ContactsUnavailable ? null : intel.Contacts.Count, intel.Ar?.TotalOutstanding,
                    intel.Ar?.Outstanding90Plus, intel.HasDegradedSections)
                { BilledPostedThrough = intel.SummaryPostedThrough }
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Org brief Deltek lookup failed for canonical org {OrgId}.", data.Id);
            return data with { Deltek = null, DeltekNote = "Deltek relationship history unavailable: could not retrieve records." };
        }
    }
}
