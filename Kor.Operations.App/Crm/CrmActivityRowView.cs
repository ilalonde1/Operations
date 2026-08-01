#nullable enable
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
    public string TypeDisplay => Model.ActivityType.ToString();
    public string OccurredDisplay => Model.OccurredAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Subject => Model.Subject;
    public string Body => Model.Body ?? "";
    public string CreatedBy => Model.CreatedBy;
}
