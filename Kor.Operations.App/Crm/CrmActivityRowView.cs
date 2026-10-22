#nullable enable
using Kor.Operations.App.BusinessDevelopment;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

public sealed class CrmActivityRowView
{
    public CrmActivityRowView(CrmActivity model)
    {
        Model = model;
    }

    public CrmActivity Model { get; }
    public long Id => Model.Id;
    public string TypeDisplay => Model.ActivityType switch
    {
        CrmActivityType.EmailOut => "Email out",
        CrmActivityType.EmailIn => "Email in",
        var t => t.ToString(),
    };
    public string OccurredDisplay => BdDateDisplay.DayAndTime(Model.OccurredAtUtc);
    public string Subject => Model.Subject;
    public string Body => Model.Body ?? "";
    public string CreatedBy => Model.CreatedBy;
    public string CreatedByDisplay => StaffDisplay.Short(Model.CreatedBy);
}
