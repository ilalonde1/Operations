#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class KorPursuitDialogViewModel : INotifyPropertyChanged
{
    private readonly IKorPursuitStore _store;
    private readonly ILogger<KorPursuitDialogViewModel> _logger;

    public KorPursuitDialogViewModel(IKorPursuitStore store, ILogger<KorPursuitDialogViewModel> logger)
    {
        _store = store;
        _logger = logger;
        Stage = PursuitStages.Considering;
        BidCurrency = "CAD";
        PursuitOpenedDate = DateTime.Today;
    }

    public string[] AllStages => PursuitStages.All;
    public string[] AllRoles => KorRoles.All;

    private string _stage = PursuitStages.Considering;
    public string Stage { get => _stage; set { _stage = value; OnChanged(); } }

    public long? OpportunityId { get; set; }
    public long? HistoricalOpportunityId { get; set; }
    public long? OpportunityAwardId { get; set; }
    public string? SourceExternalRef { get; set; }
    public long? BuyerCanonicalOrgId { get; set; }

    private string _buyerName = "";
    public string BuyerName { get => _buyerName; set { _buyerName = value ?? ""; OnChanged(); } }

    private string _title = "";
    public string Title { get => _title; set { _title = value ?? ""; OnChanged(); } }

    private decimal? _bidFee;
    public decimal? BidFee { get => _bidFee; set { _bidFee = value; OnChanged(); } }

    private string _bidCurrency = "CAD";
    public string BidCurrency { get => _bidCurrency; set { _bidCurrency = value ?? "CAD"; OnChanged(); } }

    private decimal? _estConstructionValue;
    public decimal? EstConstructionValue { get => _estConstructionValue; set { _estConstructionValue = value; OnChanged(); } }

    private string? _ourRole;
    public string? OurRole { get => _ourRole; set { _ourRole = value; OnChanged(); } }

    private string? _korBusinessDeveloper;
    public string? KorBusinessDeveloper { get => _korBusinessDeveloper; set { _korBusinessDeveloper = value; OnChanged(); } }

    private string? _korProposalManager;
    public string? KorProposalManager { get => _korProposalManager; set { _korProposalManager = value; OnChanged(); } }

    private DateTime? _pursuitOpenedDate;
    public DateTime? PursuitOpenedDate { get => _pursuitOpenedDate; set { _pursuitOpenedDate = value; OnChanged(); } }

    private DateTime? _dueDate;
    public DateTime? DueDate { get => _dueDate; set { _dueDate = value; OnChanged(); } }

    private DateTime? _submittedDate;
    public DateTime? SubmittedDate { get => _submittedDate; set { _submittedDate = value; OnChanged(); } }

    private string? _strengths;
    public string? Strengths { get => _strengths; set { _strengths = value; OnChanged(); } }

    private string? _weaknesses;
    public string? Weaknesses { get => _weaknesses; set { _weaknesses = value; OnChanged(); } }

    private string? _notes;
    public string? Notes { get => _notes; set { _notes = value; OnChanged(); } }

    public string CreatedByUser { get; set; } = Environment.UserName;

    public async Task<long?> SaveAsync()
    {
        try
        {
            var create = new KorPursuitCreate(
                Stage,
                OpportunityId,
                HistoricalOpportunityId,
                OpportunityAwardId,
                SourceExternalRef,
                BuyerCanonicalOrgId,
                BuyerName,
                Title,
                LostToCanonicalOrgId: null,
                LostToName: null,
                BidFee,
                BidCurrency,
                EstConstructionValue,
                OurRole,
                KorBusinessDeveloper,
                KorProposalManager,
                PursuitOpenedDate,
                DueDate,
                SubmittedDate,
                AwardDate: null,
                ClosedReason: null,
                Strengths,
                Weaknesses,
                Notes,
                CreatedByUser);

            return await _store.AddAsync(create, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KorPursuit add failed.");
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
