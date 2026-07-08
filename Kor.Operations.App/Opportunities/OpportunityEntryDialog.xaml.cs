#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Opportunities;
using Serilog;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Modal entry/edit dialog for an <see cref="Opportunity"/>. Construct with
/// a null model to add a new row, or with an existing model to edit it.
/// On Save, populates <see cref="Result"/> and DialogResult=true; the caller
/// hands the result to <c>OpportunitiesViewModel.InsertAsync</c> or
/// <c>UpdateAsync</c>.
/// </summary>
public partial class OpportunityEntryDialog : Window, INotifyPropertyChanged
{
    private readonly Opportunity? _existing;
    private readonly IOpportunityDuplicateFinder? _duplicateFinder;
    private string _errorMessage = string.Empty;
    private string _duplicateHint = string.Empty;

    // Live-hint debounce + the last computed matches (reused by Save so a fresh
    // save doesn't wait on a re-query when nothing changed).
    private readonly DispatcherTimer _hintTimer;
    private CancellationTokenSource? _hintCts;
    private IReadOnlyList<OpportunityDuplicateCandidate> _lastMatches = Array.Empty<OpportunityDuplicateCandidate>();
    private bool _duplicateConfirmed;

    public OpportunityEntryDialog()
        : this(null, null)
    {
    }

    public OpportunityEntryDialog(Opportunity? existing)
        : this(existing, null)
    {
    }

    public OpportunityEntryDialog(Opportunity? existing, IOpportunityDuplicateFinder? duplicateFinder)
    {
        _existing = existing;
        _duplicateFinder = duplicateFinder;
        InitializeComponent();
        DataContext = this;

        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _hintTimer.Tick += async (_, _) => await RefreshDuplicateHintAsync().ConfigureAwait(true);
        Closed += (_, _) => { _hintTimer.Stop(); _hintCts?.Cancel(); _hintCts?.Dispose(); };

        PopulateChoiceBoxes();
        if (_existing is null)
        {
            HeaderText = "New Opportunity";
            KeyIsReadOnly = false;
            KeyBox.Text = SuggestKey();
            CurrencyBox.SelectedIndex = 0;
            BuyerTypeBox.SelectedItem = BuyerType.Unknown;
            DisciplineBox.SelectedItem = OpportunityDiscipline.Unknown;
            ProvinceBox.Text = "BC";
        }
        else
        {
            HeaderText = $"Edit: {_existing.OpportunityKey}";
            KeyIsReadOnly = true; // key is the business id; don't let users change it
            KeyBox.Text = _existing.OpportunityKey;
            NameBox.Text = _existing.Name;
            BuyerBox.Text = _existing.BuyerName;
            BuyerTypeBox.SelectedItem = _existing.BuyerType;
            CityBox.Text = _existing.ProjectCity ?? string.Empty;
            ProvinceBox.Text = _existing.ProjectProvince ?? string.Empty;
            DisciplineBox.SelectedItem = _existing.Discipline;
            ValueBox.Text = _existing.EstimatedValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            CurrencyBox.SelectedItem = CurrencyBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(c => string.Equals((string?)c.Content, _existing.EstimatedValueCurrency, StringComparison.OrdinalIgnoreCase))
                ?? CurrencyBox.Items[0];
            DeadlineBox.SelectedDate = _existing.SubmissionDeadlineUtc?.LocalDateTime;
            OwnerBox.Text = _existing.OwnerStaffId ?? string.Empty;
            OutcomeReasonBox.Text = _existing.OutcomeReason ?? string.Empty;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The persisted record to feed into the store on Save. Null when
    /// the user chose to open an existing opportunity instead (see
    /// <see cref="OpenExistingKey"/>).</summary>
    public Opportunity? Result { get; private set; }

    /// <summary>Set (with DialogResult=true, Result=null) when the user picked
    /// "Open the existing one" on the duplicate prompt — the caller navigates
    /// to this opportunity instead of inserting.</summary>
    public string? OpenExistingKey { get; private set; }

    public string DuplicateHint
    {
        get => _duplicateHint;
        private set
        {
            if (!string.Equals(_duplicateHint, value, StringComparison.Ordinal))
            {
                _duplicateHint = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDuplicateHint));
            }
        }
    }

    public bool HasDuplicateHint => !string.IsNullOrWhiteSpace(_duplicateHint);

    public string HeaderText { get; private set; } = "New Opportunity";

    public bool KeyIsReadOnly { get; private set; }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void PopulateChoiceBoxes()
    {
        BuyerTypeBox.ItemsSource = Enum.GetValues<BuyerType>();
        DisciplineBox.ItemsSource = Enum.GetValues<OpportunityDiscipline>();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Restart the live-hint debounce; a fresh edit re-arms the guard
    /// even after a prior "Save anyway".</summary>
    private void DuplicateInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_duplicateFinder is null || _existing is not null)
        {
            return;
        }

        _duplicateConfirmed = false;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private async Task RefreshDuplicateHintAsync()
    {
        _hintTimer.Stop();
        if (_duplicateFinder is null || _existing is not null)
        {
            return;
        }

        var name = (NameBox.Text ?? string.Empty).Trim();
        var buyer = (BuyerBox.Text ?? string.Empty).Trim();
        if (name.Length < 4 || buyer.Length < 3)
        {
            _lastMatches = Array.Empty<OpportunityDuplicateCandidate>();
            DuplicateHint = string.Empty;
            return;
        }

        _hintCts?.Cancel();
        _hintCts?.Dispose();
        _hintCts = new CancellationTokenSource();
        var ct = _hintCts.Token;
        try
        {
            var matches = await _duplicateFinder.FindAsync(buyer, name, NullIfEmpty(CityBox.Text), null, ct)
                .ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _lastMatches = matches;
            DuplicateHint = matches.Count == 0
                ? string.Empty
                : $"⚠ {matches.Count} possible existing match{(matches.Count == 1 ? "" : "es")} — you can open it on Save.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Live duplicate-hint check failed.");
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorMessage = string.Empty;

        var key = (KeyBox.Text ?? string.Empty).Trim();
        var name = (NameBox.Text ?? string.Empty).Trim();
        var buyer = (BuyerBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            ErrorMessage = "Opportunity key is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(buyer))
        {
            ErrorMessage = "Buyer name is required.";
            return;
        }

        // Duplicate guard (new entries only): a fresh check on Save, then the
        // picker. Never blocks — the user can always open the existing one or
        // save anyway. A prior "Save anyway" on the same text short-circuits.
        if (_duplicateFinder is not null && _existing is null && !_duplicateConfirmed)
        {
            IReadOnlyList<OpportunityDuplicateCandidate> matches;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                matches = await _duplicateFinder.FindAsync(buyer, name, NullIfEmpty(CityBox.Text), null, cts.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // A duplicate-check failure must never block a legitimate save.
                Log.Warning(ex, "Duplicate check on save failed; allowing the save.");
                matches = Array.Empty<OpportunityDuplicateCandidate>();
            }

            if (matches.Count > 0)
            {
                var prompt = new OpportunityDuplicateDialog(name, matches) { Owner = this };
                var shown = prompt.ShowDialog();
                if (shown != true || prompt.Choice == DuplicateChoice.Cancel)
                {
                    return; // back to editing
                }

                if (prompt.Choice == DuplicateChoice.OpenExisting)
                {
                    OpenExistingKey = prompt.SelectedKey;
                    Result = null;
                    DialogResult = true;
                    Close();
                    return;
                }

                _duplicateConfirmed = true; // SaveAnyway
            }
        }

        decimal? estimatedValue = null;
        var rawValue = (ValueBox.Text ?? string.Empty).Trim();
        if (rawValue.Length > 0)
        {
            // Strip $ and , so users can paste "1,250,000" or "$250000" without re-typing.
            var clean = new string(rawValue.Where(c => c != '$' && c != ',' && !char.IsWhiteSpace(c)).ToArray());
            if (!decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                ErrorMessage = "Estimated value must be a number.";
                return;
            }

            estimatedValue = parsed;
        }

        var currency = ((CurrencyBox.SelectedItem as ComboBoxItem)?.Content as string) ?? "CAD";

        DateTimeOffset? deadline = DeadlineBox.SelectedDate.HasValue
            ? new DateTimeOffset(DeadlineBox.SelectedDate.Value, TimeZoneInfo.Local.GetUtcOffset(DeadlineBox.SelectedDate.Value))
            : null;

        var draft = new Opportunity
        {
            Id = _existing?.Id ?? 0,
            OpportunityKey = key,
            Name = name,
            BuyerName = buyer,
            BuyerType = (BuyerType?)BuyerTypeBox.SelectedItem ?? BuyerType.Unknown,
            ProjectCity = NullIfEmpty(CityBox.Text),
            ProjectProvince = NullIfEmpty(ProvinceBox.Text),
            Discipline = (OpportunityDiscipline?)DisciplineBox.SelectedItem ?? OpportunityDiscipline.Unknown,
            EstimatedValue = estimatedValue,
            EstimatedValueCurrency = currency,
            SubmissionDeadlineUtc = deadline,
            OwnerStaffId = NullIfEmpty(OwnerBox.Text),
            OutcomeReason = NullIfEmpty(OutcomeReasonBox.Text),

            // Preserve fields the dialog doesn't expose yet; for new rows these
            // fall back to the record's init defaults.
            DeltekClientId = _existing?.DeltekClientId,
            ProjectAddress = _existing?.ProjectAddress,
            ProjectPostalCode = _existing?.ProjectPostalCode,
            ProjectLatitude = _existing?.ProjectLatitude,
            ProjectLongitude = _existing?.ProjectLongitude,
            ConstructionType = _existing?.ConstructionType,
            ProjectCategory = _existing?.ProjectCategory,
            RfpReleaseDate = _existing?.RfpReleaseDate,
            Status = _existing?.Status ?? OpportunityStatus.New,
            IdentifiedAtUtc = _existing?.IdentifiedAtUtc ?? DateTimeOffset.UtcNow,
            ReviewingSinceUtc = _existing?.ReviewingSinceUtc,
            QualifiedAtUtc = _existing?.QualifiedAtUtc,
            PursuingSinceUtc = _existing?.PursuingSinceUtc,
            ProposalSubmittedAtUtc = _existing?.ProposalSubmittedAtUtc,
            OutcomeAtUtc = _existing?.OutcomeAtUtc,
            ReviewerStaffIds = _existing?.ReviewerStaffIds,
            RelevanceScore = _existing?.RelevanceScore,
            RelevanceTier = _existing?.RelevanceTier,
            WonLostOutcome = _existing?.WonLostOutcome,
            WonProjectWbs1 = _existing?.WonProjectWbs1,
            AwardedValue = _existing?.AwardedValue,
            CreatedAtUtc = _existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            CreatedBy = _existing?.CreatedBy ?? string.Empty,
            UpdatedAtUtc = _existing?.UpdatedAtUtc ?? DateTimeOffset.UtcNow,
            UpdatedBy = _existing?.UpdatedBy ?? string.Empty,
            RowVersion = _existing?.RowVersion ?? Array.Empty<byte>(),
        };

        Result = draft;
        DialogResult = true;
        Close();
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Suggests a stable manual-entry key: <c>MAN-yyyyMMdd-{4-char-hex}</c>.
    /// User can edit before saving; collisions just bounce off the UNIQUE
    /// index and surface as an InsertAsync error.
    /// </summary>
    private static string SuggestKey()
    {
        var rnd = Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
        return $"MAN-{DateTime.Now:yyyyMMdd}-{rnd}";
    }
}
