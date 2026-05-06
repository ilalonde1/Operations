#nullable enable
#pragma warning disable SA1649

namespace Kor.Operations.Financials;

public sealed class FinancialsProjectRow
{
    public string Wbs1 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Fee { get; set; }
    public double HourlyRevenue { get; set; }
    public double TotalFee => Fee + HourlyRevenue;
    public double FeeBilled { get; set; }
    public double UnpostedFeeBilled { get; set; }
    public double PercentBilled { get; set; }
    public double EngHrs { get; set; }
    public double DraftHrs { get; set; }
    public double InspHrs { get; set; }
    public double DocPrepHrs { get; set; }
    public double GenHrs { get; set; }
    public double AdminHrs { get; set; }
    public double NonBillHrs { get; set; }
    public double DraftBudget { get; set; }
    public double EngBudget { get; set; }
    public double Gfa { get; set; }
    public string Org { get; set; } = string.Empty;
}
