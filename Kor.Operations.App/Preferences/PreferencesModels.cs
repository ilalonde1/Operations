#nullable enable
using System;
using System.Collections.ObjectModel;

namespace Kor.Operations;

public sealed class FavoriteProject
{
    public string ProjectNo { get; set; } = "";
    public string ProjectName { get; set; } = "";
}

public sealed class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    // Shared imported "common" teams from Newforma
    public bool IsCommon { get; set; }

    public ObservableCollection<TeamMember> Members { get; } = new();
    public override string ToString() => Name;
}

public sealed class TeamMember
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
}
