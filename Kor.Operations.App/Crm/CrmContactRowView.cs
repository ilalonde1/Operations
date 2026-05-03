#nullable enable
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

public sealed class CrmContactRowView
{
    public CrmContactRowView(CrmContact model)
    {
        Model = model;
    }

    public CrmContact Model { get; }
    public long Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Role => Model.Role ?? "";
    public string Email => Model.Email ?? "";
    public string Phone => Model.Phone ?? "";
    public bool IsPrimary => Model.IsPrimary;
    public string PrimaryDisplay => Model.IsPrimary ? "★" : "";
}
