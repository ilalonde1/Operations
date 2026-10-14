using System.IO;
using System.Reflection;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Sources;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// BD fixes briefs 1 and 5: source gates for both creation entry points, caller
/// refusal, Dashboard save wiring and project/person picker invalidation;
/// deferred-store behavioral gates for Admin.
/// EXPECTED: only eligible/current input is submitted; UI confirmation matches the
/// acknowledged write. ACTUAL is included in assertions. These gates are authored,
/// not executed, in the Codex change (the brief prohibits builds/tests/DB access).
/// Does not execute SQL, modal dialogs, WPF bindings, document rendering, or scoring.
/// Would miss a template NameScope failure, a SQL trigger undoing a write, or a
/// same-ID picker refresh returning an older version of that same record.
/// The live-data companion is dismissed_opportunity_has_engagement in BdIntegrityCheck;
/// an empty live-data result alone cannot prove the pre-fix grab guard was safe.
/// </summary>
public sealed class BdWriteBoundaryRegressionTests
{
    // Brief 5 / BD-A01: covers both actual SQL constants and the caller's refusal
    // branch. Does not execute SQL or WPF; a trigger undoing ownership would escape.
    [Fact]
    public void ClaimAndGrab_UseTheSameEligibilityTransaction()
    {
        string Sql(string name) => (string)typeof(SqlPursuitGrabStore)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var grab = Sql("GrabSql");
        var claim = Sql("ClaimOwnedSql");
        Assert.True(claim == grab,
            "EXPECTED claim to execute the same guarded transaction as grab; ACTUAL SQL differs");
        var guard = Between(claim, " WHERE Id = @id", "IF @@ROWCOUNT");
        foreach (var predicate in new[] { "AND Status = 1", "AND OwnerStaffId IS NULL", "AND DismissedAtUtc IS NULL" })
            Assert.True(guard.Contains(predicate, StringComparison.Ordinal),
                $"EXPECTED {predicate} before engagement creation; ACTUAL:\n{guard}");
        Assert.Contains("ROLLBACK TRANSACTION", Between(claim, "IF @@ROWCOUNT = 0", "ELSE"));
    }

    [Fact]
    public void Promotion_RefusedGrabCannotFallThroughToAnotherCreator()
    {
        var source = File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
            "Kor.Operations.App", "Opportunities", "OpportunitiesViewModel.cs"));
        Assert.True(!source.Contains("await _grabStore.ClaimOwnedAsync", StringComparison.Ordinal),
            "EXPECTED no creation fallback after a refused grab; ACTUAL fallback still present");
        var refusal = Between(source, "if (grab.Outcome != GrabOutcome.Grabbed)",
            "saved = await _engagementStore.GetByIdAsync");
        Assert.Contains("throw new InvalidOperationException", refusal);
        Assert.Contains("Refresh", refusal);
    }

    [Fact]
    public void GrabSql_UpdateGuardRejectsDismissedRowsBeforeCreatingAnEngagement()
    {
        var sql = (string)typeof(SqlPursuitGrabStore)
            .GetField("GrabSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var guard = sql[sql.IndexOf(" WHERE Id = @id", StringComparison.Ordinal)..
            sql.IndexOf("IF @@ROWCOUNT", StringComparison.Ordinal)];
        Assert.True(guard.Contains("AND DismissedAtUtc IS NULL", StringComparison.Ordinal),
            $"EXPECTED dismissal guard in the UPDATE predicate; ACTUAL:\n{guard}");
    }

    [Fact]
    public void DashboardDialogSave_AwaitsExistingScoredUpdatePathBeforeReload()
    {
        var source = AppSource("Workspace/DashboardView.xaml.cs");
        var edit = Between(source, "        var dlg = new OpportunityEntryDialog(opp)",
            "    private void OpenOrgDossier(");
        Assert.Contains("dlg.ShowDialog() != true || dlg.Result is null", edit);
        Assert.Contains("GetRequiredService<OpportunitiesViewModel>()", edit);
        var save = edit.IndexOf("await editor.UpdateAsync(dlg.Result, ResolveActor(), CancellationToken.None)", StringComparison.Ordinal);
        var reload = edit.IndexOf("await vm.LoadAsync(CancellationToken.None)", StringComparison.Ordinal);
        Assert.True(save >= 0 && reload > save,
            $"EXPECTED acknowledged UpdateAsync before dashboard reload; ACTUAL save={save}, reload={reload}");
        Assert.Contains("catch (OpportunityConcurrencyException)", edit);
        Assert.Contains("catch (Exception ex)", edit);
        Assert.Contains("await OpenOpportunityDialogAsync(row.OpportunityId)", source);
    }

    [Theory]
    [InlineData("Project", "_selectedProject", "ProjectPreview", "GetProjectBriefAsync", "proj.Id")]
    [InlineData("Person", "_selectedPerson", "PersonPreview", "GetPersonBriefAsync", "person.IntelPersonId")]
    public void BriefPicker_ClearsPreviousDataBeforeAwait_AndNamesExportFromCapturedRecord(
        string kind, string field, string preview, string load, string capturedId)
    {
        var source = AppSource("Briefs/BriefsMakerWindow.xaml.cs");
        var handler = Between(source, $"    private async void {kind}Picker_{kind}Selected(", "\n    private ");
        var clear = handler.IndexOf(field + " = null;", StringComparison.Ordinal);
        var hide = handler.IndexOf(preview + ".Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var update = handler.IndexOf("UpdateGenerateButtonsEnabled();", StringComparison.Ordinal);
        var request = handler.IndexOf("await _briefStore." + load, StringComparison.Ordinal);
        Assert.True(clear >= 0 && hide > clear && update > hide && request > update,
            $"EXPECTED {kind} data/preview invalidated and buttons updated before request; " +
            $"ACTUAL clear={clear}, hide={hide}, buttons={update}, request={request}");
        Assert.Contains($"KOR-{kind}-Brief-{{{capturedId}}}", source);
        Assert.DoesNotContain($"{{{field}Id ??", source);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Admin_ReentrantToggleCannotChangeTheAcknowledgedValue(bool otherJob, bool failWrite)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedules = new Mock<IJobScheduleStore>(MockBehavior.Strict);
        schedules.Setup(s => s.UpdateEnabledAsync("A", true, It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var vm = Admin(schedules.Object);
        var first = Job("A");
        first.IsEnabled = true; // The TwoWay checkbox changes its source before invoking Command.
        var pending = Invoke(vm, "ToggleEnabledAsync", first);
        Assert.False(pending.IsCompleted);
        Assert.DoesNotContain("Enabled =", vm.StatusMessage);

        var second = otherJob ? Job("B") : first;
        second.IsEnabled = otherJob; // B false->true, or A true->false.
        await Invoke(vm, "ToggleEnabledAsync", second);
        Assert.False(second.IsEnabled);

        if (failWrite) completion.SetException(new InvalidOperationException("write failed"));
        else completion.SetResult();
        await pending;

        Assert.True(first.IsEnabled == !failWrite,
            $"EXPECTED A enabled={!failWrite}; ACTUAL enabled={first.IsEnabled}, status={vm.StatusMessage}");
        if (otherJob) Assert.False(second.IsEnabled);
        if (failWrite)
        {
            Assert.Contains("Failed to toggle A", vm.StatusMessage);
            Assert.DoesNotContain("Enabled =", vm.StatusMessage);
        }
        else Assert.Contains("A: Enabled = True", vm.StatusMessage);
        schedules.Verify(s => s.UpdateEnabledAsync("A", true, It.IsAny<CancellationToken>()), Times.Once);
        schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Admin_RunNowPendingRejectsAndRestoresAnotherJobsToggle()
    {
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedules = new Mock<IJobScheduleStore>(MockBehavior.Strict);
        schedules.Setup(s => s.EnqueueTriggerAsync("A", "AdminUI", It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var vm = Admin(schedules.Object);
        var pending = Invoke(vm, "RunNowAsync", Job("A"));
        var second = Job("B");
        second.IsEnabled = true;
        await Invoke(vm, "ToggleEnabledAsync", second);
        Assert.True(!second.IsEnabled,
            $"EXPECTED B restored to false without a write; ACTUAL {second.IsEnabled}: {vm.StatusMessage}");
        Assert.Contains("not saved", vm.StatusMessage);
        completion.SetResult(1);
        await pending;
        Assert.Contains("Queued A", vm.StatusMessage);
        schedules.Verify(s => s.EnqueueTriggerAsync("A", "AdminUI", It.IsAny<CancellationToken>()), Times.Once);
        schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public void AdminCheckbox_IsDisabledWhileScheduleWriteIsPending()
    {
        var xaml = AppSource("Workspace/AdminView.xaml");
        var checkbox = Between(xaml, "<CheckBox IsChecked=", "/>");
        Assert.True(checkbox.Contains("DataContext.CanWriteSchedules", StringComparison.Ordinal),
            $"EXPECTED busy-state IsEnabled binding; ACTUAL:\n{checkbox}");
    }

    [Fact]
    public void ScheduleEnabledUpdate_RejectsZeroAffectedRowsInsteadOfConfirmingSuccess()
    {
        var source = File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
            "Kor.Opportunities.Data", "Ingestion", "SqlJobScheduleStore.cs"));
        var update = Between(source, "    public async Task UpdateEnabledAsync(",
            "    public async Task UpdateLastReportPathAsync(");
        Assert.Contains("IF @@ROWCOUNT = 0", update);
        Assert.Contains("THROW", update);
    }

    private static AdminViewModel Admin(IJobScheduleStore schedules)
        => new(Mock.Of<IOpportunitySourceStore>(), Mock.Of<IIngestionRunStore>(),
            schedules, Mock.Of<IBdDashboardStore>());

    private static AdminViewModel.ScheduledJobRow Job(string name)
        => new(name, "", false, null, null, "", "", null);

    private static Task Invoke(AdminViewModel vm, string method, AdminViewModel.ScheduledJobRow row)
        => (Task)typeof(AdminViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [row])!;

    private static string AppSource(string relative)
        => File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
            "Kor.Operations.App", "BusinessDevelopment", relative));

    // Deliberately a narrow source-boundary check, not a C# control-flow analyzer.
    private static string Between(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, $"EXPECTED source boundary {start}; ACTUAL missing");
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first, $"EXPECTED source boundary {end}; ACTUAL missing after {start}");
        return source[first..last];
    }
}
