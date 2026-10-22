using Kor.Opportunities.Data.Crm;
using Kor.Operations.App.BusinessDevelopment;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// All Pursuits showed Jim as "jdesroches" (2026-10-22): BdStaff holds a "Jim" row whose mailbox is jdesroches@… and a
/// "jdesroches@…" row named James DesRoches, and the clash on that address dropped it. Covers: a row's own key beats
/// another row's mailbox; a mailbox still names a key no row owns; two rows with the same key and different names still
/// name nobody. Does NOT cover: which of Jim's two directory rows is the right one to keep (a data question), or the
/// session cache in StaffDisplay.Short.
/// </summary>
public sealed class StaffDisplayNamesTests
{
    private static BdStaffRow Row(string key, string? mailbox, string name) => new(0, key, mailbox, name, true, "test");

    [Fact]
    public void AnAddressThatIsARowsOwnKeyTakesThatRowsName()
    {
        var names = StaffDisplay.BuildNames(new[]
        {
            Row("Jim", "jdesroches@korstructural.com", "Jim DesRoches"),
            Row("jdesroches@korstructural.com", "jdesroches@korstructural.com", "James DesRoches"),
        });

        Assert.Equal("James DesRoches", names["jdesroches@korstructural.com"]);
        Assert.Equal("Jim DesRoches", names["Jim"]);
    }

    [Fact]
    public void AMailboxNamesAnAddressNoRowOwns()
        => Assert.Equal("Rory Beirne", StaffDisplay.BuildNames(new[] { Row("Rory", "rbeirne@korstructural.com", "Rory Beirne") })["rbeirne@korstructural.com"]);

    [Fact]
    public void OneKeyWithTwoNamesNamesNobody()
        => Assert.False(StaffDisplay.BuildNames(new[] { Row("John", null, "John Markulin"), Row("John", null, "John Bryson") }).ContainsKey("John"));
}
