#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.Options;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>Kind first; claim state is an optional filter. Both existing views retain their actions.</summary>
public partial class OpportunitiesHubView : UserControl
{
    private readonly IServiceProvider _services;
    private BazaarView? _grab;
    private readonly OpportunitiesView _all;
    private readonly OpportunitiesViewModel _vm;
    private OpportunityHubSegment _segment = OpportunityHubSegment.Signals;
    private bool _updating;
    private string Actor => !string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn)
        ? global::Kor.Operations.OperationsApp.SignedInUserUpn.Trim()
        : _services.GetRequiredService<UserOptions>().UserUpnOverride?.Trim() is { Length: > 0 } upn ? upn : Environment.UserName;

    public OpportunitiesHubView(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        _all = _services.GetRequiredService<OpportunitiesView>();
        _vm = (OpportunitiesViewModel)_all.DataContext;
        _vm.HubChanged += RegistryChanged;
        ShowSegment(OpportunityHubSegment.Signals);
    }

    // Preserve callers of the old two-door API: the tender shortcut now opens Bids.
    public void ShowMode(bool allTenders) => ShowSegment(allTenders ? OpportunityHubSegment.Bids : OpportunityHubSegment.Signals);

    public void ShowOpportunity(long opportunityId)
    {
        ShowSegment(OpportunityHubSegment.Signals);
        _vm.PendingSelectOpportunityId = opportunityId;
        if (_vm.SelectById(opportunityId)) _vm.PendingSelectOpportunityId = null;
    }

    private void Signals_Click(object sender, RoutedEventArgs e) => ShowSegment(OpportunityHubSegment.Signals);
    private void Bids_Click(object sender, RoutedEventArgs e) => ShowSegment(OpportunityHubSegment.Bids);
    private void Mine_Click(object sender, RoutedEventArgs e) => ShowSegment(OpportunityHubSegment.Mine);

    private void ShowSegment(OpportunityHubSegment segment)
    {
        _segment = segment;
        _updating = true;
        ToGrabFilter.IsChecked = false;
        SetKindOptions();
        _updating = false;
        ApplyFilters();
    }

    private void SetKindOptions()
    {
        KindFilter.Items.Clear();
        KindFilter.Items.Add(new ComboBoxItem { Content = "All kinds" });
        if (_segment != OpportunityHubSegment.Bids)
        {
            KindFilter.Items.Add(new ComboBoxItem { Content = "Applications / rezonings", Tag = OpportunityKind.Application });
            KindFilter.Items.Add(new ComboBoxItem { Content = "Other / unclassified", Tag = OpportunityKind.Other });
        }
        if (_segment != OpportunityHubSegment.Signals)
        {
            KindFilter.Items.Add(new ComboBoxItem { Content = "Bids / RFPs", Tag = OpportunityKind.Bid });
            KindFilter.Items.Add(new ComboBoxItem { Content = "Awards / bid history", Tag = OpportunityKind.Award });
        }
        KindFilter.SelectedIndex = 0;
    }

    private void KindFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_updating && _vm is not null) ApplyFilters(); }
    private void ToGrab_Changed(object sender, RoutedEventArgs e) { if (!_updating && _vm is not null) ApplyFilters(); }

    private void ApplyFilters()
    {
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as OpportunityKind?;
        _updating = true;
        _vm.ConfigureHub(_segment, Actor, kind);
        _updating = false;
        if (ToGrabFilter.IsChecked == true && _segment != OpportunityHubSegment.Mine)
        {
            _grab ??= _services.GetRequiredService<BazaarView>();
            ((BazaarViewModel)_grab.DataContext).ConfigureHub(_segment, kind);
            Host.Content = _grab;
        }
        else Host.Content = _all;
        UpdateHeader();
    }

    private void RegistryChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        if (_vm.HubSegment is { } segment && (segment != _segment
            || (_vm.HubKind is null && (KindFilter.SelectedItem as ComboBoxItem)?.Tag is not null)))
        {
            _segment = segment;
            _updating = true;
            ToGrabFilter.IsChecked = false;
            SetKindOptions();
            _updating = false;
        }
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        SignalsBtn.Style = (Style)FindResource(_segment == OpportunityHubSegment.Signals ? "SegBtnActive" : "SegBtn");
        BidsBtn.Style = (Style)FindResource(_segment == OpportunityHubSegment.Bids ? "SegBtnActive" : "SegBtn");
        MineBtn.Style = (Style)FindResource(_segment == OpportunityHubSegment.Mine ? "SegBtnActive" : "SegBtn");
        ToGrabFilter.Visibility = _segment == OpportunityHubSegment.Mine ? Visibility.Collapsed : Visibility.Visible;
        ModeHint.Text = _segment switch
        {
            OpportunityHubSegment.Signals => "Applications / rezonings and other leads · newest filed first; unknown dates last",
            OpportunityHubSegment.Bids => "Bids / RFPs and awards · upcoming deadlines first; expired and undated rows follow",
            _ => "Your claimed opportunities · most recently touched first",
        };
        var signals = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Signals, Actor));
        var bids = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Bids, Actor));
        var mine = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Mine, Actor));
        CountHint.Text = $"Signals {signals:N0} · Bids {bids:N0} · Mine {mine:N0} (also in Signals / Bids) · Total {signals + bids:N0}";
    }
}
