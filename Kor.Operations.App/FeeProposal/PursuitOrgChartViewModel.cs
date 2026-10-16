#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Kor.Operations.Rendering.Proposal;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.FeeProposal;

public sealed class PursuitOrgChartViewModel(IPursuitFileStore files, IOpportunityFeeProposalLinkStore links,
    IFeeProposalStore proposals, IProposalStaffStore staff, long engagementId, long opportunityId,
    string rfp, string project, string actor) : ObservableObject
{
    public OrgChartSpec Spec { get; private set; } = new() { Subtitle = $"{project} - {rfp}" };
    public string ContextLines { get; set; } = "";
    private OrgChartGroup? _selectedGroup;
    public OrgChartGroup? SelectedGroup { get => _selectedGroup; set => SetField(ref _selectedGroup, value); }
    private string _message = "";
    public string Message { get => _message; set => SetField(ref _message, value); }
    private bool _busy;
    public bool IsReady => !_busy;

    public async Task PerformAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; OnPropertyChanged(nameof(IsReady));
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Pursuit org chart action failed."); Message = ex.Message; }
        finally { _busy = false; OnPropertyChanged(nameof(IsReady)); }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        var linked = await links.LatestAsync(opportunityId, ct).ConfigureAwait(true);
        if (linked is null) { Message = "No linked fee proposal yet. Enter the project team below."; return; }
        var proposal = await proposals.LoadByIdAsync(linked.Value.ToString("N"), ct).ConfigureAwait(true);
        if (proposal is null) { Message = "The linked fee proposal could not be found. Enter the project team below."; return; }
        var personnel = proposal.Blocks.Where(b => b.BlockType == ProposalBlockType.Personnel).Select(b => b.Personnel).FirstOrDefault(p => p is not null);
        if (personnel is null) { Message = "The linked proposal has no Personnel block. Enter the project team below."; return; }
        Spec = FromPersonnel(personnel, await staff.LoadAllAsync(ct).ConfigureAwait(true), project, rfp);
        ContextLines = string.Join(Environment.NewLine, Spec.Context.Lines);
        SelectedGroup = Spec.Groups.FirstOrDefault();
        OnPropertyChanged(nameof(Spec)); OnPropertyChanged(nameof(ContextLines));
        Message = $"Team pre-filled from {proposal.Name}. Review the leadership chain and assign the RFP roles.";
    }

    public static OrgChartSpec FromPersonnel(PersonnelBlockContent personnel,
        System.Collections.Generic.IReadOnlyList<ProposalStaffMember> staff, string project, string rfp)
    {
        var spec = new OrgChartSpec { Subtitle = $"{project} - {rfp}", Note = personnel.CollaborationNote };
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        OrgChartPerson? Person(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) return null;
            var person = staff.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"A person in the proposal's Personnel block is missing from the staff library ({id}). Update the proposal team first.");
            return new() { Name = person.FullName, Cred = person.Credentials, Title = person.Title };
        }
        foreach (var id in new[] { personnel.LeadStaffId, personnel.SupportingStaffId })
            if (Person(id) is { } person) spec.Chain.Add(person);
        var group = new OrgChartGroup();
        foreach (var id in personnel.AdditionalStaffIds)
            if (Person(id) is { } person) group.People.Add(person);
        spec.Groups.Add(group);
        return spec;
    }

    public string Json()
    {
        Spec.Context.Lines = ContextLines.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Spec.Validate();
        return Spec.ToJson();
    }

    public async Task RenderAndAttachAsync(CancellationToken ct)
    {
        _ = Json();
        var rendered = OrgChartRenderer.Render(Spec);
        var stem = rfp + " - Org Chart";
        var svgSaved = false;
        try
        {
            await using (var svg = new MemoryStream(Encoding.UTF8.GetBytes(rendered.Svg)))
                await files.AddAsync(engagementId, opportunityId, stem + ".svg", svg, svg.Length, "image/svg+xml", actor, ct).ConfigureAwait(true);
            svgSaved = true;
            await using (var png = new MemoryStream(rendered.Png, writable: false))
                await files.AddAsync(engagementId, opportunityId, stem + ".png", png, png.Length, "image/png", actor, ct).ConfigureAwait(true);
            Message = $"Attached {stem}.svg and {stem}.png.";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(svgSaved ? $"The SVG is attached, but the PNG could not be attached. {ex.Message}"
                : $"The org chart was rendered, but could not be attached. {ex.Message}", ex);
        }
    }
}
