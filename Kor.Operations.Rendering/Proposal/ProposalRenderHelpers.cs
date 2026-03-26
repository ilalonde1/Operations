#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Rendering.Proposal
{
    internal static class ProposalRenderHelpers
    {
        public static string DefaultIfEmpty(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        public static string JoinText(params string?[] parts) =>
            string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        public static string FormatCurrency(decimal value) =>
            value == 0 ? string.Empty : $"${value:N0}";

        public static string FormatRate(decimal value) =>
            $"${value:N0}";

        public static string BuildStaffDisplay(ProposalStaffMember? staff) =>
            staff is null ? "[Staff not found]" : JoinText(staff.FullName?.TrimEnd(','), staff.Credentials);

        public static string BuildPreparedByLine(ProposalStaffMember? staff) =>
            staff is null
                ? "[Staff not found]"
                : string.Join(
                    "\n",
                    new[]
                    {
                        BuildStaffDisplay(staff),
                        staff.Title,
                        string.Join(" | ", new[] { staff.Email, staff.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())),
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public static string BuildContactLine(ProposalStaffMember? staff) =>
            staff is null ? "[Staff not found]" : BuildStaffDisplay(staff);

        public static string BuildAttentionLine(string name, string title, string email) =>
            string.Join("\n", new[] { name, title, email }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        public static string BuildCcLine(string name, string title, string email) =>
            string.Join("\n", new[] { name, title, email }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        public static Dictionary<string, ProposalStaffMember> BuildStaffIndex(
            IReadOnlyList<ProposalStaffMember> staff) =>
            staff.ToDictionary(s => s.Id, s => s);
    }
}
