#nullable enable
namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Executive risk score derived from Delivery Confidence (aka Delivery Risk).
    /// </summary>
    public sealed class DeliveryConfidenceMetric : ICfoMetric
    {
        public string Name => "Delivery Confidence (Risk Score)";

        public string Description =>
            "WHAT:\n" +
            "An executive-friendly numeric risk score based on Delivery Confidence.\n\n" +
            "WHY IT MATTERS:\n" +
            "Normalizes the confidence label into a sortable score for dashboards and rollups.\n\n" +
            "HOW IT IS CALCULATED:\n" +
            "Maps the Delivery Confidence level to a score: High Confidence=0, Watch=1, At Risk=2, Critical=3.";

        public string Formula => "HighConfidence=0; Watch=1; AtRisk=2; Critical=3";

        public decimal ComputeValue(ProjectData data)
        {
            data ??= ProjectData.Create();
            return data.DeliveryConfidenceLevel switch
            {
                DeliveryConfidenceLevel.HighConfidence => 0m,
                DeliveryConfidenceLevel.Watch => 1m,
                DeliveryConfidenceLevel.AtRisk => 2m,
                DeliveryConfidenceLevel.Critical => 3m,
                _ => 0m
            };
        }
    }
}
