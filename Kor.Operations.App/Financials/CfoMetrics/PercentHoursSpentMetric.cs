#nullable enable
namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Percent of budgeted hours already consumed on the project.
    /// </summary>
    public sealed class PercentHoursSpentMetric : ICfoMetric
    {
        public string Name => "Percent Hours Spent";

        public string Description =>
            "WHAT:\n" +
            "Percent of budgeted hours already used.\n\n" +
            "WHY IT MATTERS:\n" +
            "Fast signal of burn versus plan; rising faster than billing typically indicates margin risk.\n\n" +
            "HOW IT IS CALCULATED:\n" +
            "Hours Spent / Hours Budgeted.";

        public string Formula => "PercentHoursSpent = HoursSpent / HoursBudgeted";

        public decimal ComputeValue(ProjectData data)
        {
            data ??= ProjectData.Create();
            return data.PercentHoursSpent;
        }
    }
}
