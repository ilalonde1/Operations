#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Brochures;
using Kor.Opportunities.Data.Crm;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations.App.Brochures;

public interface IBrochureRenderEvents
{
    event Func<ProposalRenderedEvent, Task>? ProposalRendered;
}

public sealed class BrochurePursuitLauncher(IServiceProvider services, IPursuitFileStore files, ICrmEngagementStore engagements)
{
    public void Launch(long opportunityId, long engagementId, string rfp, string project, string actor, Window owner)
    {
        var window = services.GetRequiredService<BrochureBuilderWindow>();
        window.Owner = owner;
        var builder = (BrochureBuilderViewModel)window.DataContext;
        builder.StartForPursuit($"{rfp} — {project}");
        var session = Connect(builder, opportunityId, engagementId, actor, message => builder.AttachmentMessage = message);
        window.Closed += (_, _) => session.Dispose();
        window.Show();
    }

    internal IDisposable Connect(IBrochureRenderEvents builder, long opportunityId, long engagementId,
        string actor, Action<string> report) => new Session(builder, files, engagements, opportunityId, engagementId, actor, report);

    private sealed class Session : IDisposable
    {
        private readonly IBrochureRenderEvents _builder;
        private readonly Func<ProposalRenderedEvent, Task> _handler;
        private readonly SemaphoreSlim _queue = new(1, 1);
        public Session(IBrochureRenderEvents builder, IPursuitFileStore files, ICrmEngagementStore engagements,
            long opportunityId, long engagementId, string actor, Action<string> report)
        {
            _builder = builder;
            _handler = async rendered =>
            {
                await _queue.WaitAsync().ConfigureAwait(true);
                try
                {
                    var pursuit = await engagements.GetByIdAsync(engagementId, CancellationToken.None).ConfigureAwait(true);
                    if (pursuit is null || pursuit.OpportunityId != opportunityId)
                        throw new InvalidOperationException("The bid changed or no longer exists.");
                    await using var stream = new FileStream(rendered.RenderedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    SqlPursuitFileStore.ValidateLength(stream.Length);
                    var file = await files.AddAsync(engagementId, opportunityId, Path.GetFileName(rendered.RenderedPath), stream,
                        stream.Length, rendered.ContentType, actor, CancellationToken.None).ConfigureAwait(true);
                    report($"Attached {file.FileName} to the bid.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Brochure rendered, but attaching it to the bid failed.");
                    report($"The PDF was generated at {rendered.RenderedPath}, but could not be attached to the bid. {ex.Message}");
                }
                finally { _queue.Release(); }
            };
            builder.ProposalRendered += _handler;
        }
        public void Dispose() => _builder.ProposalRendered -= _handler;
    }
}
