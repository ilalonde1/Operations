#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// One shared loader for the FIRM's staff roster as picker options
/// (IProposalStaffStore — the curated list the fee-proposal builder's
/// "Manage Staff" maintains). Used by the Overwatch reassign picker and the
/// CRM engagement-owner picker (fix F5) so both stamp the same identity
/// format. Failures degrade to an empty list — pickers stay editable.
/// </summary>
internal static class StaffDirectory
{
    public static async Task<IReadOnlyList<PersonOption>> LoadOptionsAsync(IProposalStaffStore staffStore)
    {
        try
        {
            var staff = await staffStore.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
            return staff
                .Where(s => !string.IsNullOrWhiteSpace(s.FullName))
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Email) ? s.FullName : s.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(s => new PersonOption(
                    s.FullName.Trim(),
                    string.IsNullOrWhiteSpace(s.Email) ? s.FullName.Trim() : s.Email.Trim()))
                .OrderBy(p => p.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<PersonOption>();
        }
    }
}
