#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services.Reporting;

/// <summary>
/// BD Director version 2: the prepared hour, emailed to Ian at 6:05 on weekdays (his ask, 15 October 2026: "in the app AND
/// as a morning email"). The hour is written in Claude's session and imported by `BdKnowledgeIngest hour import`; this job
/// only reads and sends it (CLAUDE.md rule 14: no model call). On Mondays it also sends the leads' action lists, as drafts
/// addressed to Ian. Nothing goes to the team: the one recipient is DirectorHourRecipient, Ian by default.
/// A day's hour is sent once: the row is stamped EmailedAtUtc and a second fire skips it.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DirectorHourEmailJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly GraphMailSender _mail;
    private readonly ILogger<DirectorHourEmailJob> _logger;
    private readonly Func<string, IBdRhythmStore> _storeFor;

    public DirectorHourEmailJob(IOptions<OpportunitiesWorkerOptions> options, GraphMailSender mail, ILogger<DirectorHourEmailJob> logger)
        : this(options, mail, logger, cs => new SqlBdRhythmStore(cs)) { }

    internal DirectorHourEmailJob(IOptions<OpportunitiesWorkerOptions> options, GraphMailSender mail, ILogger<DirectorHourEmailJob> logger,
        Func<string, IBdRhythmStore> storeFor)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storeFor = storeFor ?? throw new ArgumentNullException(nameof(storeFor));
    }

    /// <summary>One email to send, and the hour to stamp once it has gone.</summary>
    internal sealed record Outgoing(string Subject, string Html, long? StampHourId);

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.DirectorHourEmailEnabled)
        {
            _logger.LogDebug("{Job} skipped: disabled.", nameof(DirectorHourEmailJob));
            return;
        }

        if (string.IsNullOrWhiteSpace(opt.MorningReportTenantId) || string.IsNullOrWhiteSpace(opt.MorningReportClientId)
            || string.IsNullOrWhiteSpace(opt.MorningReportClientSecret))
        {
            _logger.LogWarning("{Job} skipped: Graph credentials missing (KOR_OPPORTUNITIES_MORNINGREPORT* env vars).", nameof(DirectorHourEmailJob));
            return;
        }

        try
        {
            var store = _storeFor(opt.OpportunitiesDb);
            var now = DateTimeOffset.Now;
            var today = DateOnly.FromDateTime(now.Date);
            var hour = await SelectHourAsync(store, today, context.CancellationToken).ConfigureAwait(false);
            var actions = now.DayOfWeek == DayOfWeek.Monday
                ? await store.ListActionsAsync(context.CancellationToken).ConfigureAwait(false)
                : Array.Empty<BdActionRow>();
            foreach (var mail in Compose(hour, actions, today, now))
            {
                await _mail.SendHtmlAsync(opt.MorningReportTenantId, opt.MorningReportClientId, opt.MorningReportClientSecret,
                    opt.MorningReportSenderUpn, opt.DirectorHourRecipient, mail.Subject, mail.Html, context.CancellationToken).ConfigureAwait(false);
                if (mail.StampHourId is { } id) await store.MarkHourEmailedAsync(id, context.CancellationToken).ConfigureAwait(false);
                _logger.LogInformation("{Job} sent '{Subject}' to {Recipient}.", nameof(DirectorHourEmailJob), mail.Subject, opt.DirectorHourRecipient);
            }
        }
        catch (Exception ex)
        {
            // Quartz must not retry-spin a broken mail config; log and wait for tomorrow's fire.
            _logger.LogError(ex, "{Job} failed.", nameof(DirectorHourEmailJob));
        }
    }

    /// <summary>
    /// What to send this morning. The hour when one was imported for today and not yet emailed; a one-line note when none
    /// was, so silence never has to be read as a broken job; the error list when the stored hour does not validate. On a
    /// Monday with open actions, the leads' lists as drafts.
    /// </summary>
    internal static async Task<DirectorHourRow?> SelectHourAsync(IBdRhythmStore store, DateOnly today, CancellationToken ct) =>
        await store.GetHourAsync("Morning", today, ct).ConfigureAwait(false)
        ?? await store.GetHourAsync("BD", today, ct).ConfigureAwait(false);

    internal static IReadOnlyList<Outgoing> Compose(DirectorHourRow? hour, IReadOnlyList<BdActionRow> mondayActions, DateOnly today, DateTimeOffset now)
    {
        var mails = new List<Outgoing>();
        var day = today.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        if (hour is null)
        {
            mails.Add(new Outgoing($"No BD hour was prepared for {day}",
                Note($"No hour was prepared for {day}. The action register, meetings and references are in KOR Operations, BD Director."), null));
        }
        else if (hour.EmailedAtUtc is null)
        {
            var (content, errors) = DirectorHourFile.Parse(hour.ContentJson);
            mails.Add(content is null
                ? new Outgoing($"{(hour.Kind == "Morning" ? "Your morning" : "The BD hour")} for {day} could not be read", Note("The imported hour did not validate: " + string.Join(" ", errors)), null)
                : new Outgoing(DirectorHourEmail.Subject(content), DirectorHourEmail.Render(content, hour.Id), hour.Id));
        }

        if (now.DayOfWeek == DayOfWeek.Monday && mondayActions.Count > 0 && BdLeadLists.Group(mondayActions).Count > 0)
            mails.Add(new Outgoing($"Monday lists, drafts for you · {day}", BdLeadLists.RenderDraftsEmail(mondayActions, now), null));
        return mails;
    }

    private static string Note(string text) =>
        "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:15px;color:#17212c\"><p>" + WebUtility.HtmlEncode(text) + "</p></div>";
}
