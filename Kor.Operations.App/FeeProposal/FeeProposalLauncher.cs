#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations.App.FeeProposal;

public sealed record ProposalRenderedEvent(string ProposalId, string RenderedPath, string ContentType);

/// <summary>The builder's persistence events, also usable by the launcher regression fixture.</summary>
public interface IFeeProposalEvents
{
    event EventHandler<string>? ProposalSaved;
    event Func<ProposalRenderedEvent, Task>? ProposalRendered;
}

/// <summary>Shared pursuit handoff. The builder's own proposal persistence stays in its existing store.</summary>
public sealed class FeeProposalLauncher(IServiceProvider services, IOpportunityFeeProposalLinkStore links,
    ICrmEngagementStore engagements, ICrmActivityStore activities, IPursuitFileStore files)
{
    private readonly IOpportunityFeeProposalLinkStore _links = links;
    private readonly ICrmEngagementStore _engagements = engagements;
    private readonly ICrmActivityStore _activities = activities;
    private readonly IPursuitFileStore _files = files;
    public void Launch(long? opportunityId, long engagementId, string opportunityKey, string projectName, string actor,
        Window? owner, Func<Task> refresh, Action<string> report)
    {
        var window = services.GetRequiredService<FeeProposalBuilderWindow>();
        window.Owner = owner;
        IDisposable? session = null;
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            window.Loaded -= loaded;
            try
            {
                if (window.DataContext is FeeProposalBuilderViewModel builder)
                {
                    builder.StartFromOpportunity(string.IsNullOrWhiteSpace(projectName) ? opportunityKey : $"{opportunityKey} — {projectName}");
                    session = Connect(builder, opportunityId, engagementId, actor, refresh, report);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Fee-proposal prefill from pursuit failed.");
                report("Fee proposal opened, but automatic prefill failed.");
            }
        };
        window.Loaded += loaded;
        window.Closed += (_, _) => { window.Loaded -= loaded; session?.Dispose(); };
        window.Show();
    }

    internal IDisposable Connect(IFeeProposalEvents builder, long? opportunityId, long engagementId, string actor,
        Func<Task> refresh, Action<string> report)
        => new PursuitSession(this, builder, opportunityId, engagementId, actor, refresh, report);

    private sealed class PursuitSession : IDisposable
    {
        private readonly FeeProposalLauncher _launcher;
        private readonly IFeeProposalEvents _builder;
        private readonly long? _opportunityId;
        private readonly long _engagementId;
        private readonly string _actor;
        private readonly Func<Task> _refresh;
        private readonly Action<string> _report;
        private Guid? _linkedOnSave;
        private Task _pending = Task.CompletedTask;
        private bool _disposed;

        public PursuitSession(FeeProposalLauncher launcher, IFeeProposalEvents builder, long? opportunityId,
            long engagementId, string actor, Func<Task> refresh, Action<string> report)
        {
            (_launcher, _builder, _opportunityId, _engagementId, _actor, _refresh, _report)
                = (launcher, builder, opportunityId, engagementId, actor, refresh, report);
            builder.ProposalSaved += OnSaved;
            builder.ProposalRendered += OnRenderedAsync;
        }

        private void OnSaved(object? sender, string proposalId)
        {
            _builder.ProposalSaved -= OnSaved; // first save only, even if this builder is later repurposed
            _pending = LinkProposalToPursuitAsync(proposalId);
        }

        private async Task LinkProposalToPursuitAsync(string proposalId)
        {
            if (_opportunityId is not { } opportunityId || !Guid.TryParse(proposalId, out var id)) return;
            try
            {
                await _launcher._links.LinkAsync(opportunityId, id, _actor, CancellationToken.None).ConfigureAwait(true);
                _linkedOnSave = id;
                try
                {
                    await _launcher._activities.AppendAsync(new CrmActivity
                    {
                        EngagementId = _engagementId, ActivityType = CrmActivityType.Deliverable,
                        Subject = "Fee proposal saved",
                        Body = $"Fee proposal {id:N} saved from this pursuit and linked to its opportunity.",
                    }, _actor, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex) { Log.Warning(ex, "Auto-logging the fee-proposal activity failed; the link committed."); }
                var fresh = await _launcher._engagements.GetByIdAsync(_engagementId, CancellationToken.None).ConfigureAwait(true);
                if (fresh is not null && fresh.OpportunityId == opportunityId && fresh.Stage == CrmEngagementStage.Drafting)
                    await _launcher._engagements.UpdateAsync(fresh with { Stage = CrmEngagementStage.Submitted }, _actor, CancellationToken.None).ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Linking fee proposal to pursuit failed.");
                Report("The proposal was saved, but linking it to the pursuit or advancing its stage failed. Refresh and try again.");
            }
        }

        private Task OnRenderedAsync(ProposalRenderedEvent rendered)
        {
            // Capture the previous task before replacing it: a render waits for first-save
            // linkage, and a second render cannot race the first submitted-file update.
            var next = AttachAfterAsync(_pending, rendered);
            _pending = next;
            return next;
        }

        private async Task AttachAfterAsync(Task previous, ProposalRenderedEvent rendered)
        {
            await previous.ConfigureAwait(true);
            if (_opportunityId is not { } opportunityId || !Guid.TryParse(rendered.ProposalId, out var id)) return;
            PursuitFile? attachment = null;
            try
            {
                if (_linkedOnSave != id && !await _launcher._links.IsLinkedAsync(opportunityId, id, CancellationToken.None).ConfigureAwait(true)) return;
                var record = await _launcher._engagements.GetByIdAsync(_engagementId, CancellationToken.None).ConfigureAwait(true);
                if (record is null || record.OpportunityId != opportunityId) return;
                await using (var content = new FileStream(rendered.RenderedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    SqlPursuitFileStore.ValidateLength(content.Length);
                    attachment = await _launcher._files.AddAsync(_engagementId, opportunityId, Path.GetFileName(rendered.RenderedPath),
                        content, content.Length, rendered.ContentType, _actor, CancellationToken.None).ConfigureAwait(true);
                }
                // Preserve current stage and any edits made during the upload.
                record = await _launcher._engagements.GetByIdAsync(_engagementId, CancellationToken.None).ConfigureAwait(true);
                if (record is null || record.OpportunityId != opportunityId) throw new InvalidOperationException("The pursuit changed during the attachment upload.");
                await _launcher._engagements.UpdateAsync(record with
                {
                    SubmittedFileId = attachment.Id, SubmittedFileName = attachment.FileName,
                }, _actor, CancellationToken.None).ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Attaching rendered fee proposal to pursuit failed.");
                Report(attachment is null
                    ? $"The document was rendered, but could not be attached to the pursuit. {ex.Message}"
                    : $"Attached {attachment.FileName}, but could not set it as the submitted file. The attachment remains on the pursuit. Refresh and try again.");
            }
        }

        private async Task RefreshAsync()
        {
            if (_disposed) return;
            try { await _refresh().ConfigureAwait(true); }
            catch (Exception ex) { Log.Warning(ex, "Proposal saved to pursuit, but refreshing the screen failed."); Report("The pursuit was saved. Refresh to see the latest attachment."); }
        }

        private void Report(string message)
        {
            if (!_disposed) _report(message);
        }

        public void Dispose()
        {
            _disposed = true;
            _builder.ProposalSaved -= OnSaved;
            _builder.ProposalRendered -= OnRenderedAsync;
        }
    }
}
