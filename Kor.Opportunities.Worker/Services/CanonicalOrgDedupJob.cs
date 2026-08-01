#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// RETIRED 2026-06-15 (apparatus audit). This job previously ran an unattended
/// weekly canonical-org auto-merge that was a hand-copied, perpetually-stale fork
/// of <c>tools/BdCanonicalDedup</c>. The audit found its FK list had drifted ~10
/// inbound CanonicalOrg FKs behind the schema, so merges either silently rolled
/// back (duplicates accumulated) or CASCADE-deleted child rows — and it had no
/// retired/frozen-anchor guards or collision handling.
///
/// Deduplication is now a SINGLE, supervised implementation: the hardened
/// <c>tools/BdCanonicalDedup</c> CLI (schema-driven FK repoint, retired-survivor
/// exclusion, frozen-anchor protection, collision handling), run with the
/// post-merge audit the firm requires for every dedup pass (wrong survivors have
/// shipped twice — auto-merge without curation is not acceptable).
///
/// This job is kept as a registered no-op (and gated off by
/// <see cref="OpportunitiesWorkerOptions.CanonicalOrgDedupEnabled"/>, default
/// false) so the schedule/DI wiring stays intact and any lingering trigger logs a
/// clear pointer instead of running divergent merge logic. Do not re-introduce a
/// second merge engine here — if automated dedup is ever wanted, extract the CLI's
/// engine into a shared Kor.Opportunities.Data service and call THAT from both.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CanonicalOrgDedupJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<CanonicalOrgDedupJob> _logger;

    public CanonicalOrgDedupJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<CanonicalOrgDedupJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task Execute(IJobExecutionContext context)
    {
        // Retired: even if a stale config flips the flag on, this never mutates the
        // org graph. Dedup is supervised via the tools/BdCanonicalDedup CLI only.
        if (_options.Value.CanonicalOrgDedupEnabled)
        {
            _logger.LogWarning(
                "CanonicalOrgDedupJob is RETIRED and will not run. CanonicalOrgDedupEnabled is set true but " +
                "automated unattended dedup has been removed (split-brain + wrong-survivor history). " +
                "Run the supervised tools/BdCanonicalDedup CLI with a post-merge audit instead. " +
                "Set CanonicalOrgDedupEnabled=false to silence this warning.");
        }
        else
        {
            _logger.LogDebug("CanonicalOrgDedupJob skipped: retired (supervised CLI dedup only).");
        }

        context.Result = "CanonicalOrgDedupJob retired — no-op (use tools/BdCanonicalDedup CLI).";
        return Task.CompletedTask;
    }
}
