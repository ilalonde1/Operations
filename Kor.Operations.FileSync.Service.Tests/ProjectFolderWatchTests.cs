#nullable enable
using Kor.Operations.FileSync.Service.Jobs.ProjectFolderWatch;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Xunit;

namespace Kor.Operations.FileSync.Service.Tests;

// The nightly project-folder check's decisions, on the real shapes found 2026-09-30.
//
// WHAT THIS COVERS: recognising a project folder name; flagging a project dropped directly inside
// another job's folder, and NOT flagging another phase of the same job unless asked; the folder an
// indexed email lives in; and "report only what is new" across runs. Plus: every catalogued cron
// job can actually be installed, so a new job cannot throw the whole service over at startup.
//
// WHAT IT DOES NOT COVER: the file-share walk and the SQL read (I/O, run live), the email itself,
// and -- the same-class fault it cannot see -- a project dropped into a NON-project folder
// (e.g. into "03 Drafting" of another job, or into a category root under a new name). Those show
// up only as "indexed folder gone", and only for projects that have filed emails.
public sealed class ProjectFolderWatchTests
{
    private const string Res = @"\\Kor-fs01\Projects\Projects\03 Residential";

    [Theory]
    [InlineData("30459-02 (Vic Hill The Lookout Structural Review)", true, "30459", "30459-02")]
    [InlineData("30427-05(The Drive)", true, "30427", "30427-05")]
    [InlineData("Newforma", false, "", "")]
    [InlineData("30xxx-01 (Project Name)", false, "", "")]
    [InlineData("30519.02", false, "", "")]
    public void Project_folder_names_are_recognised(string name, bool ok, string jobBase, string number)
    {
        Assert.Equal(ok, ProjectFolderAudit.TryGetProjectNumber(name, out var b, out var n));
        Assert.Equal(jobBase, b);
        Assert.Equal(number, n);
    }

    [Fact]
    public void A_project_dragged_into_another_job_is_flagged()
    {
        var parent = Res + @"\30427-05 (The Drive, North Vanouver- Parking Lot)";
        var found = ProjectFolderAudit.FindNested(
            new[] { (parent, (IEnumerable<string>)new[] { "01 General", "Newforma", "30459-02 (Vic Hill The Lookout Structural Review)" }) },
            includeSameJob: false);

        var n = Assert.Single(found);
        Assert.Equal(parent + @"\30459-02 (Vic Hill The Lookout Structural Review)", n.NestedFolder);
    }

    [Fact]
    public void Another_phase_of_the_same_job_is_left_alone_unless_asked()
    {
        var parent = Res + @"\30203-01 (Corus, VST Tower, Lot 44)";
        var input = new[] { (parent, (IEnumerable<string>)new[] { "30203-02 (Sales Centre)" }) };

        Assert.Empty(ProjectFolderAudit.FindNested(input, includeSameJob: false));
        Assert.Single(ProjectFolderAudit.FindNested(input, includeSameJob: true));
    }

    [Fact]
    public void A_non_project_parent_is_not_treated_as_a_project()
    {
        var input = new[] { (Res + @"\30xxx-01 (Project Name)", (IEnumerable<string>)new[] { "30459-02 (Vic Hill)" }) };
        Assert.Empty(ProjectFolderAudit.FindNested(input, includeSameJob: false));
    }

    [Theory]
    [InlineData(Res + @"\30459-02 (Vic Hill)\Newforma\email\2026-03\a.msg", Res + @"\30459-02 (Vic Hill)")]
    [InlineData(Res + @"\30459-02 (Vic Hill)\newforma\email\a.msg", Res + @"\30459-02 (Vic Hill)")]
    [InlineData(@"C:\temp\a.msg", null)]
    public void The_project_folder_of_an_indexed_email(string path, string? folder)
        => Assert.Equal(folder, ProjectFolderAudit.ProjectFolderOf(path));

    [Fact]
    public void Only_what_was_not_reported_before_is_new()
    {
        var (fresh, stillOpen) = ProjectFolderAudit.Diff(
            current: new[] { "B", "a", "C" },
            known: new[] { "A", "D" });

        Assert.Equal(new[] { "B", "C" }, fresh);
        Assert.Equal(1, stillOpen);
    }

    [Fact]
    public void Every_catalogued_cron_job_can_be_installed()
    {
        var services = new ServiceCollection();
        services.AddFileSyncScheduling();
        using var provider = services.BuildServiceProvider();

        // Forces the AddQuartz configuration delegate, which throws on a catalogued job type
        // the installer has no branch for -- the fault that would stop the service at startup.
        var quartz = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.Equal(FileSyncSchedulingCatalog.QuartzSchedules.Count, quartz.JobDetails.Count);
        Assert.Contains(quartz.JobDetails, j => j.Key.Name == ProjectFolderWatchRunner.Name);
    }
}
