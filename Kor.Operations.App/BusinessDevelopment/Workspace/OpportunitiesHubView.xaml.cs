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
    private void WorkSize_Changed(object sender, SelectionChangedEventArgs e) { if (!_updating && _vm is not null) ApplyFilters(); }
    private void SmallWorkReveal_Click(object sender, RoutedEventArgs e)
        => WorkSizeFilter.SelectedIndex = WorkSizeFilter.SelectedIndex == 0 ? 1 : 0;

    private void ApplyFilters()
    {
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as OpportunityKind?;
        _updating = true;
        _vm.ConfigureHub(_segment, Actor, kind, (OpportunityWorkFilter)WorkSizeFilter.SelectedIndex);
        _updating = false;
        if (ToGrabFilter.IsChecked == true && _segment != OpportunityHubSegment.Mine)
        {
            if (_grab is null)
            {
                _grab = _services.GetRequiredService<BazaarView>();
                ((BazaarViewModel)_grab.DataContext).HubChanged += RegistryChanged;
            }
            ((BazaarViewModel)_grab.DataContext).ConfigureHub(_segment, kind, (OpportunityWorkFilter)WorkSizeFilter.SelectedIndex);
            Host.Content = _grab;
        }
        else Host.Content = _all;
        UpdateHeader();
    }

    private void RegistryChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        _updating = true;
        WorkSizeFilter.SelectedIndex = (int)_vm.HubWorkFilter;
        _updating = false;
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
        SmallWorkPanel.Visibility = _segment == OpportunityHubSegment.Mine ? Visibility.Collapsed : Visibility.Visible;
        var grab = ToGrabFilter.IsChecked == true ? _grab?.DataContext as BazaarViewModel : null;
        var smaller = grab?.MatchingSmallWorkCount ?? _vm.MatchingSmallWorkCount;
        var visible = grab?.Grabbable.Count ?? _vm.VisibleHubCount;
        var bidsTab = _segment == OpportunityHubSegment.Bids;
        WorkSizeLabel.Text = bidsTab ? "Show:" : "Work size:";
        ((ComboBoxItem)WorkSizeFilter.Items[0]).Content = bidsTab ? "Structural work only" : "Smaller work collapsed";
        ((ComboBoxItem)WorkSizeFilter.Items[1]).Content = bidsTab ? "All tenders" : "All work";
        ((ComboBoxItem)WorkSizeFilter.Items[2]).Content = bidsTab ? "Not structural work only" : "Smaller work only";
        var hiddenWord = bidsTab ? "not structural work (roads, utilities, supplies)" : "smaller";
        SmallWorkReveal.Content = WorkSizeFilter.SelectedIndex == 0
            ? $"{smaller:N0} {(bidsTab ? "non-structural tenders" : "smaller items")} hidden · Show" : bidsTab ? "Hide tenders that are not structural work" : "Hide smaller items";
        SmallWorkCount.Text = $"{visible:N0} shown · {smaller:N0} classified {hiddenWord}";
        ModeHint.Text = _segment switch
        {
            OpportunityHubSegment.Signals => "Permits, rezonings and new projects · newest first",
            OpportunityHubSegment.Bids => "Tenders · soonest deadline first",
            _ => "The ones you have grabbed",
        };
        var signals = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Signals, Actor));
        var bids = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Bids, Actor));
        var mine = _vm.Opportunities.Count(r => OpportunityHubSelection.Matches(r, OpportunityHubSegment.Mine, Actor));
        CountHint.Text = $"Signals {signals:N0} · Bids {bids:N0} · Mine {mine:N0} (also in Signals / Bids) · Total {signals + bids:N0}";
    }
}
