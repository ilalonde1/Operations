#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Deltek;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Keeps opportunities.BdStaff (the BD owner-identity directory, plan D2)
/// current from the authoritative Deltek EMMain employee list. Two passes:
///   1. Register every active employee's email as a routable identity (so a
///      grabbed pursuit's UPN owner carries a DisplayName and self-routes).
///   2. Bridge a legacy first-name owner ("Conor", "Jim") to a mailbox ONLY on
///      an unambiguous single first-name match — never a guess; ambiguous or
///      unmatched names are left unrouted and logged.
/// Read-only against Deltek; writes only the small BdStaff table. Safe no-op
/// when Deltek is unavailable (the directory keeps its last-synced rows).
/// </summary>
[DisallowConcurrentExecution]
public sealed class BdStaffDirectorySyncJob : IJob
{
    private readonly IKorStaffDirectoryAccessor _accessor;
    private readonly IBdStaffDirectory _directory;
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BdStaffDirectorySyncJob> _logger;

    public BdStaffDirectorySyncJob(
        IKorStaffDirectoryAccessor accessor,
        IBdStaffDirectory directory,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BdStaffDirectorySyncJob> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BdStaffDirectorySyncEnabled)
        {
            _logger.LogDebug("{Job} skipped: disabled.", nameof(BdStaffDirectorySyncJob));
            return;
        }

        if (_accessor is NullKorStaffDirectoryAccessor)
        {
            _logger.LogDebug("{Job} skipped: Deltek access unavailable.", nameof(BdStaffDirectorySyncJob));
            return;
        }

        var ct = context.CancellationToken;
        var sw = Stopwatch.StartNew();

        IReadOnlyList<DeltekStaffRow> staff;
        try
        {
            staff = await _accessor.GetActiveStaffAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Job}: EMMain read failed; directory left as-is.", nameof(BdStaffDirectorySyncJob));
            return;
        }

        // Pass 1: every employee email is a routable identity.
        var registered = 0;
        foreach (var s in staff)
        {
            ct.ThrowIfCancellationRequested();
            var email = s.Email.Trim();
            var display = $"{s.FirstName} {s.LastName}".Trim();
            try
            {
                await _directory.UpsertAsync(email, email, string.IsNullOrWhiteSpace(display) ? null : display,
                    isActive: true, source: "deltek:emmain", ct).ConfigureAwait(false);
                registered++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Job}: upsert failed for {Email}.", nameof(BdStaffDirectorySyncJob), email);
            }
        }

        // Pass 2: bridge legacy first-name owners on an UNAMBIGUOUS match only.
        // Group active staff by first name; a name owned by exactly one employee
        // fills that name's unrouted directory row. >1 employee → ambiguous, skip.
        var byFirstName = staff
            .Where(s => !string.IsNullOrWhiteSpace(s.FirstName))
            .GroupBy(s => s.FirstName!.Trim(), StringComparer.OrdinalIgnoreCase);

        var bridged = 0;
        var ambiguous = 0;
        foreach (var group in byFirstName)
        {
            ct.ThrowIfCancellationRequested();
            var emails = group.Select(g => g.Email.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (emails.Count != 1)
            {
                ambiguous++;
                _logger.LogInformation(
                    "{Job}: first name '{Name}' maps to {Count} employees — left unrouted (no guess).",
                    nameof(BdStaffDirectorySyncJob), group.Key, emails.Count);
                continue;
            }

            var winner = group.First();
            var display = $"{winner.FirstName} {winner.LastName}".Trim();
            try
            {
                if (await _directory.FillMailboxIfEmptyAsync(group.Key, emails[0],
                        string.IsNullOrWhiteSpace(display) ? null : display,
                        "deltek:firstname-match", ct).ConfigureAwait(false))
                {
                    bridged++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Job}: first-name fill failed for {Name}.", nameof(BdStaffDirectorySyncJob), group.Key);
            }
        }

        // Pass 3: surface owners STILL unrouted after the sync so they can be
        // given a manual mailbox (config override or a BdStaff edit) — e.g. a
        // nickname ("Jim" is John Markulin in Deltek) or an ambiguous first name
        // ("John" maps to three employees). Never guessed here.
        try
        {
            var pending = (await _directory.GetAllAsync(ct).ConfigureAwait(false))
                .Where(s => string.IsNullOrWhiteSpace(s.Mailbox))
                .Select(s => s.StaffKey)
                .ToList();
            if (pending.Count > 0)
            {
                _logger.LogInformation(
                    "{Job}: {Count} owner(s) still unrouted after sync (need a manual mailbox): {Keys}.",
                    nameof(BdStaffDirectorySyncJob), pending.Count, string.Join(", ", pending));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Job}: unrouted-summary pass failed (non-fatal).", nameof(BdStaffDirectorySyncJob));
        }

        _logger.LogInformation(
            "{Job}: staff={Staff} registered={Registered} firstNameBridged={Bridged} ambiguousNames={Ambiguous} elapsedMs={Ms}.",
            nameof(BdStaffDirectorySyncJob), staff.Count, registered, bridged, ambiguous, sw.ElapsedMilliseconds);
    }
}
