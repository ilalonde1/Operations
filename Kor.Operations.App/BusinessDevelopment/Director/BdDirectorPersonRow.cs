#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class BdDirectorPersonRow
{
    public BdDirectorPersonRow(BdDirectorPerson person) => Person = person;
    public BdDirectorPerson Person { get; }
    public string PersonKey => Person.PersonKey;
    public string DisplayName => Person.DisplayName;
    public IReadOnlyList<string> OwnerKeys => Person.OwnerKeys;
    public int Live => Person.Live;
    public int OverdueNextAction => Person.OverdueNextAction;
    public int NoNextAction => Person.NoNextAction;
    public int Untouched14d => Person.Untouched14d;
    public int Activities30 => Person.Activities30;
    public int Activities90 => Person.Activities90;
    public int StageMoves90 => Person.StageMoves90;
    public int BuyersMailed90 => Person.BuyersMailed90;
    public int Opens30 => Person.Opens30;
}

public sealed class BdDirectorPursuitRow
{
    public BdDirectorPursuitRow(PursuitOverwatchRow pursuit) => Pursuit = pursuit;
    public PursuitOverwatchRow Pursuit { get; }
    public long PursuitId => Pursuit.EngagementId;
    public string ProjectName => Pursuit.ProjectName;
    public string Buyer => Pursuit.Buyer;
    public string Stage => ((CrmEngagementStage)Pursuit.Stage).ToString();
    public string LastActivity => Pursuit.LastActivityUtc is { } at ? BdDateDisplay.Day(at) : "never";
    public string Opened => BdDateDisplay.Day(Pursuit.OpenedAtUtc);
}

public sealed record BdDirectorStageRow(string Stage, int Pursuits, double? MedianDays)
{
    public string MedianDisplay => MedianDays?.ToString("0.0", CultureInfo.CurrentCulture) ?? "—";
}

public sealed record BdDirectorDeadlineRow(long PursuitId, string ProjectName, string Buyer, string Owner, DateTimeOffset Deadline);
