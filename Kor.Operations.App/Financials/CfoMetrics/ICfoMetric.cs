namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// A CFO-facing metric that can be computed for a single project context.
    /// </summary>
    public interface ICfoMetric
    {
        /// <summary>
        /// Display name shown in the UI.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Executive-friendly description used for tooltips and documentation.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Compute the metric value for the given project data context.
        /// </summary>
        decimal ComputeValue(ProjectData data);
    }
}
