#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class RegistersWindow : Window
{
    private readonly KorStandardsReadRepository _korRepo;
    private readonly StandardDetailsRepository _txRepo;
    private readonly KorStandardsPromoterRepository? _promoter;
    private readonly bool _canApproveReject;
    private readonly string _actorIdentity;

    internal RegistersWindow(
        KorStandardsReadRepository korRepo,
        StandardDetailsRepository txRepo,
        KorStandardsPromoterRepository? promoter,
        bool canApproveReject,
        string actorIdentity)
    {
        _korRepo = korRepo ?? throw new ArgumentNullException(nameof(korRepo));
        _txRepo = txRepo ?? throw new ArgumentNullException(nameof(txRepo));
        _promoter = promoter;
        _canApproveReject = canApproveReject;
        _actorIdentity = string.IsNullOrWhiteSpace(actorIdentity) ? "operations" : actorIdentity;
        InitializeComponent();

        var canAct = _canApproveReject && _promoter != null;
        ApproveButton.Visibility = canAct ? Visibility.Visible : Visibility.Collapsed;
        RejectButton.Visibility = canAct ? Visibility.Visible : Visibility.Collapsed;
        ApprovalHint.Text = canAct
            ? "Select a detail, then Approve (sets human-confirmed) or Reject."
            : "Read-only — approve/reject needs the Approver, Publisher, or Admin role.";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync(string.Empty);
        await LoadComponentsAsync(string.Empty);
    }

    private async Task LoadDetailsAsync(string query)
    {
        try
        {
            var details = await _korRepo.LoadPaletteDetailsAsync(query);
            var links = await _txRepo.LoadLinkedDetailNumbersAsync();
            var byNumber = links
                .GroupBy(l => l.DetailNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = details.Select(d =>
            {
                byNumber.TryGetValue(d.DetailNumber, out var link);
                return new DetailRegisterDisplayRow
                {
                    DetailNumber = d.DetailNumber,
                    Title = d.Title,
                    Discipline = d.Discipline,
                    Confidence = d.Confidence,
                    PlaceableText = d.IsPlaceable ? "Yes" : "No",
                    DivergesText = d.VariantsDiverge ? "Yes" : "No",
                    LinkedDocument = link?.DocumentTitle ?? string.Empty,
                    DocStatus = link is null ? string.Empty : StatusText(link.LatestStatus)
                };
            }).ToList();

            DetailsGrid.ItemsSource = rows;
            var placeable = rows.Count(r => r.PlaceableText == "Yes");
            var linked = rows.Count(r => r.LinkedDocument.Length > 0);
            DetailSummary.Text = $"{rows.Count} details · {placeable} placeable · {linked} linked";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standards Registers - Details", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadComponentsAsync(string query)
    {
        try
        {
            var components = await _korRepo.LoadComponentRegisterAsync(query);
            var rows = components.Select(c => new ComponentRegisterDisplayRow
            {
                Palette = c.Palette,
                Label = c.Label,
                FamilyName = c.FamilyName,
                TypeName = c.TypeName,
                Origin = c.Origin,
                RetiredText = c.IsRetired ? "Yes" : "No",
                InstanceCount = c.InstanceCount,
                UsedInDetails = c.UsedInDetails
            }).ToList();

            ComponentsGrid.ItemsSource = rows;
            var retired = rows.Count(r => r.RetiredText == "Yes");
            ComponentSummary.Text = $"{rows.Count} components · {retired} retired";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standards Registers - Components", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string StatusText(byte? status) => status switch
    {
        0 => "Draft",
        1 => "Submitted",
        2 => "Approved",
        3 => "Rejected",
        4 => "Published",
        5 => "Archived",
        _ => "None"
    };

    private async void DetailSearch_Click(object sender, RoutedEventArgs e) => await LoadDetailsAsync(DetailSearchBox.Text?.Trim() ?? string.Empty);
    private async void ComponentSearch_Click(object sender, RoutedEventArgs e) => await LoadComponentsAsync(ComponentSearchBox.Text?.Trim() ?? string.Empty);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync(DetailSearchBox.Text?.Trim() ?? string.Empty);
        await LoadComponentsAsync(ComponentSearchBox.Text?.Trim() ?? string.Empty);
    }

    private void DetailsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var enable = _canApproveReject && _promoter != null && DetailsGrid.SelectedItem is DetailRegisterDisplayRow;
        ApproveButton.IsEnabled = enable;
        RejectButton.IsEnabled = enable;
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await DecideSelectedDetailAsync("human-confirmed", "Approve");

    private async void Reject_Click(object sender, RoutedEventArgs e) => await DecideSelectedDetailAsync("rejected", "Reject");

    private async Task DecideSelectedDetailAsync(string toConfidence, string verb)
    {
        if (_promoter is null || !_canApproveReject)
        {
            return;
        }

        if (DetailsGrid.SelectedItem is not DetailRegisterDisplayRow row)
        {
            MessageBox.Show(this, "Select a detail first.", "Standards Registers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"{verb} {row.DetailNumber} — {row.Title}?" + Environment.NewLine + Environment.NewLine
                + $"This sets its confidence to '{toConfidence}' in KorStandards and journals a DetailHistory row.",
            $"Standards Registers — {verb}",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        ApproveButton.IsEnabled = false;
        RejectButton.IsEnabled = false;
        try
        {
            var basis = $"Set to {toConfidence} in Standard Details Registers by {_actorIdentity}";
            var (ok, message) = await _promoter.PromoteAsync(row.DetailNumber, toConfidence, basis, _actorIdentity);
            if (!ok)
            {
                MessageBox.Show(this, message, $"Standards Registers — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await LoadDetailsAsync(DetailSearchBox.Text?.Trim() ?? string.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"Standards Registers — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class DetailRegisterDisplayRow
    {
        public string DetailNumber { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Discipline { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
        public string PlaceableText { get; init; } = "No";
        public string DivergesText { get; init; } = "No";
        public string LinkedDocument { get; init; } = string.Empty;
        public string DocStatus { get; init; } = string.Empty;
    }

    private sealed class ComponentRegisterDisplayRow
    {
        public string Palette { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string FamilyName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string Origin { get; init; } = string.Empty;
        public string RetiredText { get; init; } = "No";
        public int InstanceCount { get; init; }
        public int UsedInDetails { get; init; }
    }
}
