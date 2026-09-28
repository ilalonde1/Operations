using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Exercises migration 102 — the intake the catalogue never had — against a scratch KorStandards
/// built on LocalDB from the real migration files, so the procedures under test are byte-for-byte
/// the ones that will run on KOR-APP01\SQLEXPRESS.
///
/// WHAT IT COVERS: minting (first number is MAX+1, the next is sequential, a retired number is
/// never reused), the duplicate-view refusal, every validation refusal AND that the refusal left
/// no row behind, the detail-to-occurrence binding, placeability through detail.vw_PaletteCatalog,
/// retire and restore, title and discipline editing including the no-op case, the occurrence
/// upsert and removal that reconcile depends on, and that each act wrote the governance rows it
/// should and no others.
///
/// WHAT IT DOES NOT COVER: concurrency — the UPDLOCK/HOLDLOCK path that stops two gatekeepers
/// minting the same number is reasoned, not exercised; anything the app does around these
/// procedures (the bridge view picker, stamping View Prefix back onto the Revit view, art
/// capture); and the production catalogue's own 612 rows.
///
/// A same-class fault it would NOT catch: a procedure that journals the right number of rows with
/// the wrong ChangedBy. Every test here connects as the same principal, so an actor mix-up between
/// two different callers is invisible to it.
///
/// It needs LocalDB. If LocalDB is absent the tests FAIL rather than skip — a governance check that
/// silently passes when it did not run is the failure mode conformance.Result calls
/// 'not-implemented' and treats as worse than a loud failure.
/// </summary>
public sealed class KorStandardsScratchDatabase : IDisposable
{
    private const string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=True;Connect Timeout=60";

    // The migrations guard on DB_NAME() = 'KorStandards' and refuse to run anywhere else, so the
    // scratch copy has to carry that exact name. That makes it shared state across processes:
    // every intake test therefore lives in ONE class, which xUnit never runs in parallel with
    // itself. Do not spread them across classes — that is the static-Vocabulary fault again.
    public const string ConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=KorStandards;Integrated Security=true;TrustServerCertificate=True;Connect Timeout=60";

    // 004 makes the tables, 066 the promote path the intake hands off to, 076/077 Kind and IsSheet,
    // 079 the journal 102 widens, 102 the intake itself.
    private static readonly string[] Chain =
    [
        "004_CreateDetailAndConformance.sql",
        "066_PromoteDetailProcAndPromoter.sql",
        "076_DetailKind.sql",
        "077_DetailIsSheet.sql",
        "079_GovernanceLog.sql",
        "102_ADrawingBecomesAStandardDetail.sql",
    ];

    public KorStandardsScratchDatabase()
    {
        var db = MigrationDirectory();

        Exec(Master, "IF DB_ID('KorStandards') IS NOT NULL BEGIN ALTER DATABASE KorStandards SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE KorStandards; END; CREATE DATABASE KorStandards;");

        // 076 and 077 refuse to run without these two database users. On the server they are real
        // logins; here they only have to exist for the guard and the GRANTs to resolve.
        Exec(ConnectionString, """
            IF DATABASE_PRINCIPAL_ID(N'standards_reader') IS NULL CREATE USER standards_reader WITHOUT LOGIN;
            IF DATABASE_PRINCIPAL_ID(N'standards_promoter') IS NULL CREATE USER standards_promoter WITHOUT LOGIN;
            """);

        foreach (var file in Chain)
        {
            var path = Path.Combine(db, file);
            Assert.True(File.Exists(path), $"Migration {file} not found at {path}.");
            foreach (var batch in SplitOnGo(File.ReadAllText(path)))
            {
                Exec(ConnectionString, batch);
            }
        }
    }

    /// <summary>Wipes the catalogue between tests so each one states its own starting position.</summary>
    public void Reset() => Exec(ConnectionString, """
        DELETE FROM detail.GovernanceLog;
        DELETE FROM detail.DetailOccurrence;
        DELETE FROM detail.DetailHistory;
        DELETE FROM detail.Detail;
        """);

    public static void Exec(string connectionString, string sql)
    {
        using var cn = new SqlConnection(connectionString);
        cn.Open();
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(string sql)
    {
        using var cn = new SqlConnection(ConnectionString);
        cn.Open();
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        var value = cmd.ExecuteScalar();
        return value is null || value is DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>Calls one intake proc, returning the first column of the first row it selected.</summary>
    public static string? CallProc(string proc, params (string Name, object? Value)[] args)
    {
        using var cn = new SqlConnection(ConnectionString);
        cn.Open();
        using var cmd = new SqlCommand(proc, cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = cmd.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0) ? reader.GetValue(0).ToString() : null;
    }

    /// <summary>The message a proc refused with, or null if it did not refuse.</summary>
    public static string? RefusalFrom(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        // Left in place on purpose: when a test fails, the scratch catalogue is the evidence.
    }

    private static IEnumerable<string> SplitOnGo(string script)
    {
        var batch = new List<string>();
        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.Any(x => x.Trim().Length > 0))
                {
                    yield return string.Join('\n', batch);
                }

                batch.Clear();
                continue;
            }

            batch.Add(line);
        }

        if (batch.Any(x => x.Trim().Length > 0))
        {
            yield return string.Join('\n', batch);
        }
    }

    /// <summary>
    /// The migrations live in the KOR.Drafter repo, not this one — found by walking up to the
    /// folder that holds both, never by a hard-coded path that is right on exactly one machine.
    /// </summary>
    private static string MigrationDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "KOR.Drafter", "db")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find KOR.Drafter\\db by walking up from the test output directory.");
        return Path.Combine(dir!.FullName, "KOR.Drafter", "db");
    }
}

[Trait("Speed", "Slow")]
public sealed class StandardDetailsIntakeTests : IClassFixture<KorStandardsScratchDatabase>
{
    private const string Doc = "Kor_Structural_Standards_Template_R25.rvt";
    private readonly KorStandardsScratchDatabase _db;

    public StandardDetailsIntakeTests(KorStandardsScratchDatabase db)
    {
        _db = db;
        _db.Reset();
        // One row from the August crawl, so a mint has to be MAX+1 and not merely "the first".
        KorStandardsScratchDatabase.Exec(KorStandardsScratchDatabase.ConnectionString,
            """
            INSERT INTO detail.Detail (DetailNumber, Title, Discipline, Confidence, CreatedBy)
            VALUES (N'KOR-D-00612', N'SEEDED BY THE AUGUST CRAWL', N'Concrete', N'content-verified', N'mint-018 (matcher collapse)');
            """);
    }

    private static string? Add(string title, long viewId, string viewName, string? discipline = "Concrete", string? kind = null, string viewKind = "DraftingView")
        => KorStandardsScratchDatabase.CallProc("detail.AddDetail",
            ("@Title", title), ("@Discipline", discipline), ("@Kind", kind),
            ("@DocumentName", Doc), ("@ViewElementId", viewId), ("@ViewName", viewName),
            ("@ViewKind", viewKind), ("@ChangedBy", "lfinnigan"), ("@Basis", "made standard"));

    [Fact]
    public void The_first_added_detail_takes_the_next_number_after_the_highest_that_exists()
    {
        Assert.Equal("KOR-D-00613", Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)"));
        Assert.Equal("KOR-D-00614", Add("TYPICAL SHEAR WALL BOUNDARY", 900002, "TYPICAL SHEAR WALL BOUNDARY"));
    }

    [Fact]
    public void Adding_binds_the_number_to_the_view_in_the_same_act()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        // Without the occurrence the detail exists in the register and nowhere a drafter can reach:
        // vw_PaletteCatalog and vw_DetailPlaceable both JOIN it.
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            """
            SELECT COUNT(*) FROM detail.DetailOccurrence o
            JOIN detail.Detail d ON d.Id = o.DetailId
            WHERE d.DetailNumber = N'KOR-D-00613' AND o.ViewElementId = 900001 AND o.ViewKind = N'DraftingView';
            """));
    }

    [Fact]
    public void A_new_detail_is_listed_but_not_placeable_until_a_person_confirms_it()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.vw_PaletteCatalog WHERE DetailNumber = N'KOR-D-00613' AND IsPlaceable = 0;"));

        // 'human-confirmed', not 'content-verified': detail.PromoteDetail accepts only
        // human-confirmed or rejected. content-verified was the state the August crawl wrote for
        // details it had matched to a catalogue PDF, and there is no crawl behind a detail somebody
        // has just drawn — so a hand-added detail climbs unverified -> human-confirmed.
        KorStandardsScratchDatabase.CallProc("detail.PromoteDetail",
            ("@DetailNumber", "KOR-D-00613"), ("@ToConfidence", "human-confirmed"),
            ("@Basis", "art captured and read back"), ("@ChangedBy", "lfinnigan"));

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.vw_PaletteCatalog WHERE DetailNumber = N'KOR-D-00613' AND IsPlaceable = 1;"));
    }

    [Fact]
    public void One_drawing_cannot_be_given_a_second_number()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        var refusal = KorStandardsScratchDatabase.RefusalFrom(() =>
            Add("A SECOND NUMBER FOR THE SAME DRAWING", 900001, "SLAB EDGE AT PARKADE RAMP (E)"));

        Assert.NotNull(refusal);
        Assert.Contains("already catalogued as KOR-D-00613", refusal);
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.Detail WHERE CreatedBy = N'lfinnigan';"));
    }

    [Fact]
    public void A_refused_add_leaves_nothing_behind()
    {
        // RAISERROR at severity 16 does not stop a procedure by itself. If a guard ever loses its
        // RETURN, the proc raises and then inserts the row it just rejected — and the caller sees
        // an error while the catalogue quietly grows. This is the test for that.
        var before = KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.Detail;");

        Assert.NotNull(KorStandardsScratchDatabase.RefusalFrom(() => Add("MISFILED", 900003, "MISFILED", discipline: "Conrete")));
        Assert.NotNull(KorStandardsScratchDatabase.RefusalFrom(() => Add("NO TITLE", 900004, "NO TITLE", discipline: null, kind: "invented-kind")));
        Assert.NotNull(KorStandardsScratchDatabase.RefusalFrom(() => Add("BAD VIEW KIND", 900005, "BAD VIEW KIND", viewKind: "FloorPlan")));

        Assert.Equal(before, KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.Detail;"));
    }

    [Fact]
    public void Retiring_without_a_reason_is_refused_and_the_detail_stays_live()
    {
        // KOR-D-00003 was retired by a hand-edit in SQL with RetiredReason NULL. Nobody can now say
        // why it went. That is what this refusal exists to prevent.
        Add("TYPICAL SHEAR WALL BOUNDARY", 900002, "TYPICAL SHEAR WALL BOUNDARY");

        var refusal = KorStandardsScratchDatabase.RefusalFrom(() =>
            KorStandardsScratchDatabase.CallProc("detail.RetireDetail",
                ("@DetailNumber", "KOR-D-00613"), ("@Reason", "   "), ("@ChangedBy", "lfinnigan")));

        Assert.NotNull(refusal);
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.Detail WHERE DetailNumber = N'KOR-D-00613' AND RetiredAtUtc IS NULL;"));
    }

    [Fact]
    public void Retiring_with_a_reason_records_it_removes_it_from_the_palette_and_can_be_undone()
    {
        Add("TYPICAL SHEAR WALL BOUNDARY", 900002, "TYPICAL SHEAR WALL BOUNDARY");

        KorStandardsScratchDatabase.CallProc("detail.RetireDetail",
            ("@DetailNumber", "KOR-D-00613"), ("@Reason", "superseded by KOR-D-00600"), ("@ChangedBy", "lfinnigan"));

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.Detail WHERE DetailNumber = N'KOR-D-00613' AND RetiredReason = N'superseded by KOR-D-00600';"));
        Assert.Equal(0, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.vw_PaletteCatalog WHERE DetailNumber = N'KOR-D-00613';"));

        KorStandardsScratchDatabase.CallProc("detail.RestoreDetail",
            ("@DetailNumber", "KOR-D-00613"), ("@Basis", "retired in error"), ("@ChangedBy", "lfinnigan"));

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.Detail WHERE DetailNumber = N'KOR-D-00613' AND RetiredAtUtc IS NULL AND RetiredReason IS NULL;"));
    }

    [Fact]
    public void A_retired_number_is_never_handed_out_again()
    {
        Add("TYPICAL SHEAR WALL BOUNDARY", 900002, "TYPICAL SHEAR WALL BOUNDARY");
        KorStandardsScratchDatabase.CallProc("detail.RetireDetail",
            ("@DetailNumber", "KOR-D-00613"), ("@Reason", "withdrawn"), ("@ChangedBy", "lfinnigan"));

        // It has been printed on issued drawings. Reusing it would make two different details
        // answer to one number in the field.
        Assert.Equal("KOR-D-00614", Add("AFTER A RETIREMENT", 900004, "AFTER A RETIREMENT"));
    }

    [Fact]
    public void Editing_the_title_and_the_discipline_journals_them_as_two_separate_facts()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        KorStandardsScratchDatabase.CallProc("detail.SetDetailTitleDiscipline",
            ("@DetailNumber", "KOR-D-00613"), ("@Title", "SLAB EDGE AT PARKADE RAMP - REVISED"),
            ("@Discipline", "Steel"), ("@ChangedBy", "lfinnigan"), ("@Basis", "reviewed with the EOR"));

        // Two rows, not one: a register that says "Title and Discipline changed" on one line cannot
        // answer "who renamed this?".
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.GovernanceLog WHERE EntityKey = N'KOR-D-00613' AND Field = N'Title';"));
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.GovernanceLog WHERE EntityKey = N'KOR-D-00613' AND Field = N'Discipline';"));
    }

    [Fact]
    public void Re_saving_an_unchanged_title_journals_nothing()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        KorStandardsScratchDatabase.CallProc("detail.SetDetailTitleDiscipline",
            ("@DetailNumber", "KOR-D-00613"), ("@Title", "SLAB EDGE AT PARKADE RAMP"), ("@ChangedBy", "lfinnigan"));

        // A log that fills with no-ops is a log nobody reads.
        Assert.Equal(0, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.GovernanceLog WHERE EntityKey = N'KOR-D-00613' AND Field IN (N'Title', N'Discipline');"));
    }

    [Fact]
    public void A_refused_edit_changes_nothing()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)", discipline: "Steel");

        Assert.NotNull(KorStandardsScratchDatabase.RefusalFrom(() =>
            KorStandardsScratchDatabase.CallProc("detail.SetDetailTitleDiscipline",
                ("@DetailNumber", "KOR-D-00613"), ("@Discipline", "Timber"), ("@ChangedBy", "lfinnigan"))));

        Assert.Equal("Steel", KorStandardsScratchDatabase.Scalar<string>(
            "SELECT Discipline FROM detail.Detail WHERE DetailNumber = N'KOR-D-00613';"));
    }

    [Fact]
    public void Reconcile_can_heal_a_renamed_view_without_creating_a_second_occurrence()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        // The durable key is (DocumentName, ViewElementId). The name is not identity — it is the
        // thing that drifts, which is exactly what went unnoticed between Aug 6 and now.
        KorStandardsScratchDatabase.CallProc("detail.RecordOccurrence",
            ("@DocumentName", Doc), ("@ViewElementId", 900001L),
            ("@ViewName", "SLAB EDGE AT PARKADE RAMP (E) - RENAMED"), ("@ViewKind", "DraftingView"),
            ("@DetailNumber", "KOR-D-00613"), ("@ChangedBy", "lfinnigan"), ("@Basis", "reconcile: renamed in model"));

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.DetailOccurrence WHERE ViewElementId = 900001;"));
        Assert.Equal("SLAB EDGE AT PARKADE RAMP (E) - RENAMED", KorStandardsScratchDatabase.Scalar<string>(
            "SELECT ViewName FROM detail.DetailOccurrence WHERE ViewElementId = 900001;"));
    }

    [Fact]
    public void Reconcile_can_record_a_view_nobody_catalogued_without_inventing_a_number_for_it()
    {
        KorStandardsScratchDatabase.CallProc("detail.RecordOccurrence",
            ("@DocumentName", Doc), ("@ViewElementId", 900099L),
            ("@ViewName", "A VIEW NOBODY CATALOGUED"), ("@ViewKind", "DraftingView"),
            ("@ChangedBy", "lfinnigan"), ("@Basis", "reconcile: found in model, no KOR-D"));

        // DetailId is nullable on purpose (004): a view is an observation, and requiring the answer
        // at insert time would force a guess.
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.DetailOccurrence WHERE ViewElementId = 900099 AND DetailId IS NULL;"));
    }

    [Fact]
    public void Severing_a_number_from_its_drawing_demands_a_reason()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");

        Assert.NotNull(KorStandardsScratchDatabase.RefusalFrom(() =>
            KorStandardsScratchDatabase.CallProc("detail.RemoveOccurrence",
                ("@DocumentName", Doc), ("@ViewElementId", 900001L), ("@Basis", ""), ("@ChangedBy", "lfinnigan"))));

        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.DetailOccurrence WHERE ViewElementId = 900001;"));

        KorStandardsScratchDatabase.CallProc("detail.RemoveOccurrence",
            ("@DocumentName", Doc), ("@ViewElementId", 900001L),
            ("@Basis", "view deleted from the model"), ("@ChangedBy", "lfinnigan"));

        Assert.Equal(0, KorStandardsScratchDatabase.Scalar<int>(
            "SELECT COUNT(*) FROM detail.DetailOccurrence WHERE ViewElementId = 900001;"));
    }

    [Fact]
    public void Every_intake_act_leaves_a_governance_row_and_none_of_them_are_silent()
    {
        Add("SLAB EDGE AT PARKADE RAMP", 900001, "SLAB EDGE AT PARKADE RAMP (E)");
        Add("TYPICAL SHEAR WALL BOUNDARY", 900002, "TYPICAL SHEAR WALL BOUNDARY");
        KorStandardsScratchDatabase.CallProc("detail.RetireDetail",
            ("@DetailNumber", "KOR-D-00614"), ("@Reason", "superseded"), ("@ChangedBy", "lfinnigan"));
        KorStandardsScratchDatabase.CallProc("detail.SetDetailTitleDiscipline",
            ("@DetailNumber", "KOR-D-00613"), ("@Title", "RENAMED"), ("@ChangedBy", "lfinnigan"));

        Assert.Equal(2, KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.GovernanceLog WHERE Field = N'Created';"));
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.GovernanceLog WHERE Field = N'Retired';"));
        Assert.Equal(1, KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.GovernanceLog WHERE Field = N'Title';"));
        Assert.Equal(0, KorStandardsScratchDatabase.Scalar<int>("SELECT COUNT(*) FROM detail.GovernanceLog WHERE ChangedBy <> N'lfinnigan';"));
    }
}
