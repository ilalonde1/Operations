#nullable enable
using System;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// One row of Deltek's Projects_Deadlines grid: the date a deliverable is expected, its issue
    /// type (Progress, BP, IFC, ...) and the PM's remarks. See FinancialsService.GetNextDeadlinesAsync.
    /// </summary>
    public sealed record ProjectDeadline(string Wbs1, DateTime Date, string Issue, string Remarks);
}
