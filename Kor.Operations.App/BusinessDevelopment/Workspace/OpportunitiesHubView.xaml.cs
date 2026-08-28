#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// One "Opportunities" surface that unifies what used to be two nav items:
/// "To grab" (the un-claimed, relevance-ranked pool — BazaarView) and
/// "All tenders" (the full registry — OpportunitiesView). Same underlying
/// opportunities.Opportunities table; "To grab" is just the un-claimed slice.
/// Toggling swaps the hosted view; each is resolved lazily and reused.
/// </summary>
public partial class OpportunitiesHubView : UserControl
{
    private readonly IServiceProvider _services;
    private BazaarView? _grab;
    private App.Opportunities.OpportunitiesView? _all;

    public OpportunitiesHubView(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        ShowGrab();
    }

    /// <summary>Open directly on a mode (deep-links: "All tenders" from the dashboard).</summary>
    public void ShowMode(bool allTenders)
    {
        if (allTenders) { ShowAll(); } else { ShowGrab(); }
    }

    /// <summary>
    /// Land on "All tenders" with one opportunity pre-selected — the
    /// kor://opp/&lt;id&gt; deep link. The full registry is used rather than
    /// "To grab" because a linked tender may already be claimed, and the
    /// un-claimed slice would silently hide it. The id is stamped on the view
    /// model and consumed by its next load, the same handshake
    /// <c>NavigateToPursuit</c> uses.
    /// </summary>
    public void ShowOpportunity(long opportunityId)
    {
        ShowAll();
        if (_all?.DataContext is App.Opportunities.OpportunitiesViewModel vm)
        {
            vm.PendingSelectOpportunityId = opportunityId;

            // The view may already be loaded and idle (the hub reuses it), in
            // which case no further load would run to consume the stamp.
            if (vm.Opportunities.Count > 0 && vm.SelectById(opportunityId))
            {
                vm.PendingSelectOpportunityId = null;
            }
        }
    }

    private void ToGrab_Click(object sender, RoutedEventArgs e) => ShowGrab();
    private void All_Click(object sender, RoutedEventArgs e) => ShowAll();

    private void ShowGrab()
    {
        _grab ??= _services.GetRequiredService<BazaarView>();
        Host.Content = _grab;
        StyleTabs(grab: true);
    }

    private void ShowAll()
    {
        _all ??= _services.GetRequiredService<App.Opportunities.OpportunitiesView>();
        Host.Content = _all;
        StyleTabs(grab: false);
    }

    private void StyleTabs(bool grab)
    {
        ToGrabBtn.Style = (Style)FindResource(grab ? "SegBtnActive" : "SegBtn");
        AllBtn.Style = (Style)FindResource(grab ? "SegBtn" : "SegBtnActive");
        ModeHint.Text = grab
            ? "Un-claimed opportunities ranked by fit — grab one to start a pursuit."
            : "Every open tender in the registry, filterable.";
    }
}
