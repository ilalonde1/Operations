#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Kor.Operations.Compensation;

public sealed class CompensationFirmContext : INotifyPropertyChanged
{
    private readonly Func<IEnumerable<EmployeeCompensationRow>> _rowsAccessor;

    public CompensationFirmContext(Func<IEnumerable<EmployeeCompensationRow>> rowsAccessor)
    {
        _rowsAccessor = rowsAccessor ?? throw new ArgumentNullException(nameof(rowsAccessor));
    }

    public double FirmRevenue { get; set; }
    public double FirmLaborCost { get; set; }
    public double FirmGrossProfit => FirmRevenue - FirmLaborCost;
    public double PoolRate { get; set; }
    public double FirmBonusPool => Math.Max(0.0, PoolRate * FirmGrossProfit);
    public double FirmUtilization { get; set; }
    public double FirmAnnualizedHoursPerHead { get; set; }
    public double EmployeeMarginPerBillableHour { get; set; }
    public DateTime? ProductivitySnapshotDate { get; set; }

    public double TotalContributionScore => _rowsAccessor()
        .Where(r => !r.IsPartner)
        .Sum(r => Math.Max(0.0, r.ContributionScore));

    public string FirmRevenueDisplay => $"{FirmRevenue:C0}";
    public string FirmLaborCostDisplay => $"{FirmLaborCost:C0}";
    public string FirmGrossProfitDisplay => $"{FirmGrossProfit:C0}";
    public string FirmBonusPoolDisplay => $"{FirmBonusPool:C0}";
    public string FirmUtilizationDisplay => $"{FirmUtilization:P0}";
    public string FirmAnnualizedHoursPerHeadDisplay => $"{FirmAnnualizedHoursPerHead:0,0} hr/yr";
    public string EmployeeMarginPerBillableHourDisplay => $"{EmployeeMarginPerBillableHour:C0}/hr";
    public string ProductivitySnapshotDateDisplay => ProductivitySnapshotDate?.ToString("yyyy-MM-dd") ?? "—";

    internal void RecomputeTotal()
    {
        OnPropertyChanged(nameof(TotalContributionScore));
        foreach (var row in _rowsAccessor())
            row.NotifyFirmContextChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
