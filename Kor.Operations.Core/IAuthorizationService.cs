#nullable enable
namespace Kor.Operations.Core;

public interface IAuthorizationService
{
    /// <summary>Returns true if the current user is a member of the specified AD group.</summary>
    bool IsInGroup(string groupName);

    /// <summary>Returns true if the current user is authorized for the named operation.</summary>
    bool IsAuthorized(string operation);

    /// <summary>Throws UnauthorizedAccessException if the user is not authorized.</summary>
    void Demand(string operation);
}
