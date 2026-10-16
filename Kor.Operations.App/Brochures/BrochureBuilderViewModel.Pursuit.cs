#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Operations.App.FeeProposal;

namespace Kor.Operations.Brochures;

public sealed partial class BrochureBuilderViewModel : Kor.Operations.App.Brochures.IBrochureRenderEvents
{
    public event Func<ProposalRenderedEvent, Task>? ProposalRendered;
    public string PursuitLabel { get; private set; } = "";
    public bool IsPursuit => !string.IsNullOrEmpty(PursuitLabel);
    private string _attachmentMessage = "";
    public string AttachmentMessage { get => _attachmentMessage; set => SetField(ref _attachmentMessage, value); }

    public void StartForPursuit(string label)
    {
        PursuitLabel = $"PDFs generated in this window attach to {label}.";
        ProposalName = label;
        Cover.CoverTitle = label;
        OnPropertyChanged(nameof(PursuitLabel)); OnPropertyChanged(nameof(IsPursuit));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public async Task NotifyProposalRenderedAsync(string path)
    {
        var handlers = ProposalRendered;
        if (handlers is null) return;
        var rendered = new ProposalRenderedEvent(_proposalId ?? "", path, "application/pdf");
        foreach (Func<ProposalRenderedEvent, Task> handler in handlers.GetInvocationList())
            await handler(rendered).ConfigureAwait(true);
    }
}
