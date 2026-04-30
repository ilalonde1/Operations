#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kor.Operations.Compensation;

public sealed class EmployeeCompensationRow : INotifyPropertyChanged
{
    private const double BaselineProductivityScore = 75.0;
    private const double ExpectedProfitFloorRate = 0.5;

    private double _productivityScoreActual = BaselineProductivityScore;
    private double? _productivityScoreOverride;
    private double _annualizedHoursActual;
    private double? _annualizedHoursOverride;
    private double _utilizationActual;
    private double? _utilizationOverride;
    private double _billingRateActual;
    private double? _billingRateOverride;
    private double _costRateActual;
    private double? _costRateOverride;
    private CompensationFirmContext? _firm;

    public string EmployeeId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? HireDate { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
        ? EmployeeId
        : $"{FirstName} {LastName}".Trim();

    public bool IsPartner => !string.IsNullOrWhiteSpace(EmployeeId)
        && EmployeeId.StartsWith("P", StringComparison.OrdinalIgnoreCase);

    public double TenureYears => HireDate.HasValue
        ? Math.Max(0.0, (DateTime.Today - HireDate.Value.Date).TotalDays / 365.25)
        : 0.0;

    public double TenureMultiplier => 1.0 + Math.Min(0.02 * TenureYears, 0.20);

    public string TenureDisplay => HireDate.HasValue ? $"{TenureYears:0.#} yr" : "—";
    public string TenureMultiplierDisplay => $"×{TenureMultiplier:0.00}";

    public double ProductivityScoreActual
    {
        get => _productivityScoreActual;
        set
        {
            if (Math.Abs(_productivityScoreActual - value) < 1e-6) return;
            _productivityScoreActual = value;
            RaiseAllDerived();
        }
    }

    public double? ProductivityScoreOverride
    {
        get => _productivityScoreOverride;
        set
        {
            if (Nullable.Equals(_productivityScoreOverride, value)) return;
            _productivityScoreOverride = value;
            RaiseAllDerived();
        }
    }

    public double ProductivityScoreEffective => _productivityScoreOverride ?? _productivityScoreActual;
    public bool IsProductivityScoreOverridden => _productivityScoreOverride.HasValue;
    public string ProductivityScoreActualDisplay => $"{_productivityScoreActual:0}";
    public string ProductivityScoreEffectiveDisplay => $"{ProductivityScoreEffective:0}";

    public string ProductivityGradeEffective => ProductivityScoreEffective switch
    {
        >= 93 => "A+",
        >= 87 => "A",
        >= 83 => "A-",
        >= 77 => "B+",
        >= 73 => "B",
        >= 70 => "B-",
        >= 67 => "C+",
        >= 63 => "C",
        >= 60 => "C-",
        >= 57 => "D+",
        >= 53 => "D",
        >= 50 => "D-",
        _ => "F"
    };

    public double AnnualizedHoursActual
    {
        get => _annualizedHoursActual;
        set
        {
            if (Math.Abs(_annualizedHoursActual - value) < 1e-6) return;
            _annualizedHoursActual = value;
            RaiseAllDerived(includeBaseline: true);
        }
    }

    public double? AnnualizedHoursOverride
    {
        get => _annualizedHoursOverride;
        set
        {
            if (Nullable.Equals(_annualizedHoursOverride, value)) return;
            _annualizedHoursOverride = value;
            RaiseAllDerived();
        }
    }

    public double AnnualizedHoursEffective => _annualizedHoursOverride ?? _annualizedHoursActual;
    public bool IsHoursOverridden => _annualizedHoursOverride.HasValue;

    public double UtilizationActual
    {
        get => _utilizationActual;
        set
        {
            if (Math.Abs(_utilizationActual - value) < 1e-6) return;
            _utilizationActual = value;
            RaiseAllDerived(includeBaseline: true);
        }
    }

    public double? UtilizationOverride
    {
        get => _utilizationOverride;
        set
        {
            if (Nullable.Equals(_utilizationOverride, value)) return;
            _utilizationOverride = value;
            RaiseAllDerived();
        }
    }

    public double UtilizationEffective => _utilizationOverride ?? _utilizationActual;
    public bool IsUtilizationOverridden => _utilizationOverride.HasValue;

    public double BillingRateActual
    {
        get => _billingRateActual;
        set
        {
            if (Math.Abs(_billingRateActual - value) < 1e-6) return;
            _billingRateActual = value;
            RaiseAllDerived(includeBaseline: true);
        }
    }

    public double? BillingRateOverride
    {
        get => _billingRateOverride;
        set
        {
            if (Nullable.Equals(_billingRateOverride, value)) return;
            _billingRateOverride = value;
            RaiseAllDerived();
        }
    }

    public double BillingRateEffective => _billingRateOverride ?? _billingRateActual;
    public bool IsBillingRateOverridden => _billingRateOverride.HasValue;

    public double CostRateActual
    {
        get => _costRateActual;
        set
        {
            if (Math.Abs(_costRateActual - value) < 1e-6) return;
            _costRateActual = value;
            RaiseAllDerived(includeBaseline: true);
        }
    }

    public double? CostRateOverride
    {
        get => _costRateOverride;
        set
        {
            if (Nullable.Equals(_costRateOverride, value)) return;
            _costRateOverride = value;
            RaiseAllDerived();
        }
    }

    public double CostRateEffective => _costRateOverride ?? _costRateActual;
    public bool IsCostRateOverridden => _costRateOverride.HasValue;

    public double AnnualizedRevenueActual { get; set; }
    public double AnnualizedFixedFeeRevenueActual { get; set; }
    public double AnnualizedLaborCostActual { get; set; }
    public double AnnualizedBillableHoursActual { get; set; }
    public double OvertimeHoursActual { get; set; }
    public double OvertimeCostActual { get; set; }

    public CompensationFirmContext? Firm
    {
        get => _firm;
        set
        {
            if (ReferenceEquals(_firm, value)) return;
            _firm = value;
            NotifyFirmContextChanged();
        }
    }

    public double RevenueScaleFactor
    {
        get
        {
            var denom = _annualizedHoursActual * _utilizationActual * _billingRateActual;
            return denom > 0 ? AnnualizedRevenueActual / denom : 1.0;
        }
    }

    public double CostScaleFactor
    {
        get
        {
            var denom = _annualizedHoursActual * _costRateActual;
            return denom > 0 ? AnnualizedLaborCostActual / denom : 1.0;
        }
    }

    public double PersonRevenue => AnnualizedHoursEffective * UtilizationEffective * BillingRateEffective * RevenueScaleFactor;
    public double PersonCost => AnnualizedHoursEffective * CostRateEffective * CostScaleFactor;
    public double RealizedProfit => PersonRevenue - PersonCost;

    public double ExpectedProfit
    {
        get
        {
            if (Firm == null) return 0.0;
            return (AnnualizedHoursEffective * UtilizationEffective)
                 * Firm.EmployeeMarginPerBillableHour
                 * (ProductivityScoreEffective / BaselineProductivityScore);
        }
    }

    public double ContributionDollars => Math.Max(RealizedProfit, ExpectedProfit * ExpectedProfitFloorRate);
    public bool FloorApplied => ExpectedProfit * ExpectedProfitFloorRate > RealizedProfit;
    public double ContributionScore => Math.Max(0.0, ContributionDollars) * TenureMultiplier;

    public double CapacityScore => Firm != null && Firm.FirmAnnualizedHoursPerHead > 0
        ? AnnualizedHoursEffective / Firm.FirmAnnualizedHoursPerHead
        : 1.0;

    public double UtilizationScore => Firm != null && Firm.FirmUtilization > 0
        ? UtilizationEffective / Firm.FirmUtilization
        : 1.0;

    public double BonusShare
    {
        get
        {
            if (Firm == null || IsPartner) return 0.0;
            var total = Firm.TotalContributionScore;
            return total > 0 ? Math.Max(0.0, ContributionScore) / total : 0.0;
        }
    }

    public double Bonus => Firm == null ? 0.0 : BonusShare * Firm.FirmBonusPool;

    public double HoursOverrideValue
    {
        get => AnnualizedHoursEffective;
        set => AnnualizedHoursOverride = value;
    }

    public double UtilizationOverridePercentValue
    {
        get => UtilizationEffective * 100.0;
        set => UtilizationOverride = Math.Clamp(value / 100.0, 0.0, 1.25);
    }

    public double BillingRateOverrideValue
    {
        get => BillingRateEffective;
        set => BillingRateOverride = Math.Max(0.0, value);
    }

    public double CostRateOverrideValue
    {
        get => CostRateEffective;
        set => CostRateOverride = Math.Max(0.0, value);
    }

    public double ProductivityScoreOverrideValue
    {
        get => ProductivityScoreEffective;
        set => ProductivityScoreOverride = Math.Clamp(value, 0.0, 100.0);
    }

    public string AnnualizedHoursActualDisplay => $"{_annualizedHoursActual:0,0} hr/yr";
    public string AnnualizedHoursEffectiveDisplay => $"{AnnualizedHoursEffective:0,0} hr/yr";
    public string UtilizationActualDisplay => $"{_utilizationActual:P0}";
    public string UtilizationEffectiveDisplay => $"{UtilizationEffective:P0}";
    public string BillingRateActualDisplay => $"{_billingRateActual:C0}/hr";
    public string BillingRateEffectiveDisplay => $"{BillingRateEffective:C0}/hr";
    public string CostRateActualDisplay => $"{_costRateActual:C0}/hr";
    public string CostRateEffectiveDisplay => $"{CostRateEffective:C0}/hr";
    public string RevenueScaleFactorDisplay => $"{RevenueScaleFactor:0.00}×";
    public string CostScaleFactorDisplay => $"{CostScaleFactor:0.00}×";
    public string PersonRevenueDisplay => $"{PersonRevenue:C0}";
    public string PersonCostDisplay => $"{PersonCost:C0}";
    public string RealizedProfitDisplay => $"{RealizedProfit:C0}";
    public string ExpectedProfitDisplay => $"{ExpectedProfit:C0}";
    public string ContributionDollarsDisplay => $"{ContributionDollars:C0}";
    public string ContributionScoreDisplay => $"{ContributionScore:C0}";
    public string CapacityScoreDisplay => $"×{CapacityScore:0.00}";
    public string UtilizationScoreDisplay => $"×{UtilizationScore:0.00}";
    public string BonusShareDisplay => $"{BonusShare:P1}";
    public string BonusDisplay => $"{Bonus:C0}";
    public double PersonProfit => RealizedProfit;
    public string PersonProfitDisplay => RealizedProfitDisplay;

    public void ResetOverrides()
    {
        AnnualizedHoursOverride = null;
        UtilizationOverride = null;
        BillingRateOverride = null;
        CostRateOverride = null;
        ProductivityScoreOverride = null;
    }

    internal void NotifyFirmContextChanged()
    {
        OnPropertyChanged(nameof(CapacityScore));
        OnPropertyChanged(nameof(CapacityScoreDisplay));
        OnPropertyChanged(nameof(UtilizationScore));
        OnPropertyChanged(nameof(UtilizationScoreDisplay));
        OnPropertyChanged(nameof(ExpectedProfit));
        OnPropertyChanged(nameof(ExpectedProfitDisplay));
        OnPropertyChanged(nameof(ContributionDollars));
        OnPropertyChanged(nameof(ContributionDollarsDisplay));
        OnPropertyChanged(nameof(ContributionScore));
        OnPropertyChanged(nameof(ContributionScoreDisplay));
        OnPropertyChanged(nameof(BonusShare));
        OnPropertyChanged(nameof(BonusShareDisplay));
        OnPropertyChanged(nameof(Bonus));
        OnPropertyChanged(nameof(BonusDisplay));
    }

    private void RaiseAllDerived(bool includeBaseline = false)
    {
        if (includeBaseline)
        {
            OnPropertyChanged(nameof(AnnualizedHoursActualDisplay));
            OnPropertyChanged(nameof(UtilizationActualDisplay));
            OnPropertyChanged(nameof(BillingRateActualDisplay));
            OnPropertyChanged(nameof(CostRateActualDisplay));
        }

        OnPropertyChanged(nameof(AnnualizedHoursEffective));
        OnPropertyChanged(nameof(AnnualizedHoursEffectiveDisplay));
        OnPropertyChanged(nameof(IsHoursOverridden));
        OnPropertyChanged(nameof(HoursOverrideValue));
        OnPropertyChanged(nameof(UtilizationEffective));
        OnPropertyChanged(nameof(UtilizationEffectiveDisplay));
        OnPropertyChanged(nameof(IsUtilizationOverridden));
        OnPropertyChanged(nameof(UtilizationOverridePercentValue));
        OnPropertyChanged(nameof(BillingRateEffective));
        OnPropertyChanged(nameof(BillingRateEffectiveDisplay));
        OnPropertyChanged(nameof(IsBillingRateOverridden));
        OnPropertyChanged(nameof(BillingRateOverrideValue));
        OnPropertyChanged(nameof(CostRateEffective));
        OnPropertyChanged(nameof(CostRateEffectiveDisplay));
        OnPropertyChanged(nameof(IsCostRateOverridden));
        OnPropertyChanged(nameof(CostRateOverrideValue));
        OnPropertyChanged(nameof(ProductivityScoreEffective));
        OnPropertyChanged(nameof(ProductivityScoreEffectiveDisplay));
        OnPropertyChanged(nameof(ProductivityGradeEffective));
        OnPropertyChanged(nameof(IsProductivityScoreOverridden));
        OnPropertyChanged(nameof(ProductivityScoreOverrideValue));
        OnPropertyChanged(nameof(RevenueScaleFactor));
        OnPropertyChanged(nameof(RevenueScaleFactorDisplay));
        OnPropertyChanged(nameof(CostScaleFactor));
        OnPropertyChanged(nameof(CostScaleFactorDisplay));
        OnPropertyChanged(nameof(PersonRevenue));
        OnPropertyChanged(nameof(PersonRevenueDisplay));
        OnPropertyChanged(nameof(PersonCost));
        OnPropertyChanged(nameof(PersonCostDisplay));
        OnPropertyChanged(nameof(PersonProfit));
        OnPropertyChanged(nameof(PersonProfitDisplay));
        OnPropertyChanged(nameof(RealizedProfit));
        OnPropertyChanged(nameof(RealizedProfitDisplay));
        OnPropertyChanged(nameof(ExpectedProfit));
        OnPropertyChanged(nameof(ExpectedProfitDisplay));
        OnPropertyChanged(nameof(ContributionDollars));
        OnPropertyChanged(nameof(ContributionDollarsDisplay));
        OnPropertyChanged(nameof(FloorApplied));
        OnPropertyChanged(nameof(ContributionScore));
        OnPropertyChanged(nameof(ContributionScoreDisplay));
        OnPropertyChanged(nameof(CapacityScore));
        OnPropertyChanged(nameof(CapacityScoreDisplay));
        OnPropertyChanged(nameof(UtilizationScore));
        OnPropertyChanged(nameof(UtilizationScoreDisplay));
        OnPropertyChanged(nameof(BonusShare));
        OnPropertyChanged(nameof(BonusShareDisplay));
        OnPropertyChanged(nameof(Bonus));
        OnPropertyChanged(nameof(BonusDisplay));
        Firm?.RecomputeTotal();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
