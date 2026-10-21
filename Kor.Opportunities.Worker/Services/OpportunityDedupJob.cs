#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Nightly merge of one tender held under several feeds (APC, Bonfire, Bids&amp;Tenders, BC Bid and its engineering feed,
/// CivicInfo), so the leads' lists show each tender once. Runs OpportunityDedup, the same code as tools/BdOpportunityDedup,
/// with same-source re-posts included; every safety gate holds (pursuit, owner, notes, status disagreement, one portal
/// with different numbers, similarity floor). Held groups are left for a person and counted in the job's result.
/// First run by hand 2026-10-20: 139 merged, open tenders 581 to 511.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class OpportunityDedupJob : IJob
{
    private const double MinSimilarity = 0.90;
    private const string Actor = "OpportunityDedupJob";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<OpportunityDedupJob> _logger;

    public OpportunityDedupJob(IOptions<OpportunitiesWorkerOptions> options, ILogger<OpportunityDedupJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.OpportunityDedupEnabled)
        {
            context.Result = "disabled";
            return;
        }

        var ct = context.CancellationToken;
        await using var con = new SqlConnection(opt.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        var rows = await OpportunityDedup.LoadAsync(con, ct).ConfigureAwait(false);
        var outcome = OpportunityDedup.BuildPlan(rows, includeSameSource: true, MinSimilarity);

        int merged = 0, failed = 0;
        foreach (var p in outcome.Plans)
        {
            var error = await OpportunityDedup.MergeAsync(con, p, Actor, ct).ConfigureAwait(false);
            if (error is null) merged++;
            else
            {
                failed++;
                _logger.LogWarning("{Job}: merging {Loser} into {Survivor} failed: {Error}", nameof(OpportunityDedupJob),
                    p.Loser.OpportunityKey, p.Survivor.OpportunityKey, error);
            }
        }

        context.Result = $"{merged} merged, {failed} failed, {outcome.Held.Count} groups held for a person ({rows.Count} active rows read)";
        _logger.LogInformation("{Job}: {Result}", nameof(OpportunityDedupJob), context.Result);
    }
}
