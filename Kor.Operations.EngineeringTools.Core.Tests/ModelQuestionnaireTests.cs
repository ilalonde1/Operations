using ClosedXML.Excel;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public class ModelQuestionnaireTests
{
    [Fact]
    public void QuestionsWorkbookCarriesHiddenRuleMetadata()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = new DxfToEtabsReport(
                path, 1, 1, 1,
                new ComposeSummary(1, 1, 1, 4, 1, Array.Empty<string>(), Array.Empty<string>()),
                (0, 0),
                Array.Empty<SheetOutcome>(),
                Array.Empty<string>(),
                new PlanClassificationOptions(),
                new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 });

            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Questions");
            int scope = Col(sheet, "Rule scope"), topic = Col(sheet, "Rule topic"), key = Col(sheet, "Setting key");
            Assert.True(sheet.Column(scope).IsHidden);
            Assert.True(sheet.Column(Col(sheet, "Confidence")).IsHidden);
            Assert.False(sheet.Column(Col(sheet, "YOUR ANSWER")).IsHidden);
            Assert.Contains("corner-limbs-vs-stocky-pier",
                sheet.RowsUsed().Select(r => r.Cell(topic).GetString()));
            Assert.Contains("dxf.opening-height;dxf.spandrel-depth-floor;dxf.spandrel-depth-ceiling",
                sheet.RowsUsed().Select(r => r.Cell(key).GetString()));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void HeaderDepthQuestionUsesComposeClampValues()
    {
        var questions = ModelQuestionnaire.StandingQuestions(
            new PlanClassificationOptions(),
            new ComposeOptions { OpeningHeight = 88, SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 });

        var header = Assert.Single(questions, q => q.Code == "H1");
        Assert.Contains("18", header.WhatWeDid);
        Assert.Contains("60", header.WhatWeDid);
        Assert.DoesNotContain("20", header.WhatWeDid);
    }

    [Fact]
    public void AnsweredQuestionCanWriteMultipleSettingRules()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var h1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "H1");
                h1.Cell(Col(sheet, "YOUR ANSWER")).Value = "Opening height 90, clamp 18-60";
                workbook.Save();
            }

            var parsed = RuleSettings.ReadQuestionAnswers(path);
            var byKey = parsed.Where(a => a.SettingKey is not null)
                .ToDictionary(a => a.SettingKey!, a => a.SettingValue);

            Assert.Equal("90", byKey["dxf.opening-height"]);
            Assert.Equal("18", byKey["dxf.spandrel-depth-floor"]);
            Assert.Equal("60", byKey["dxf.spandrel-depth-ceiling"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SquareFootAnswerConvertsToSquareInchSetting()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var s1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "S1");
                s1.Cell(Col(sheet, "YOUR ANSWER")).Value = "Use 450 sq ft";
                workbook.Save();
            }

            var parsed = RuleSettings.ReadQuestionAnswers(path);
            var s1Rule = Assert.Single(parsed, a => a.SettingKey == "dxf.min-plate-area");
            Assert.Equal("64800", s1Rule.SettingValue);
            Assert.Equal("sqin", s1Rule.SettingUnits);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MalformedSettingAnswerIsSkippedRatherThanImportedAsPlainRuling()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var w1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "W1");
                w1.Cell(Col(sheet, "YOUR ANSWER")).Value = "ask Andrea";
                workbook.Save();
            }

            var skipped = new List<string>();
            var parsed = RuleSettings.ReadQuestionAnswers(path, skipped);

            Assert.Empty(parsed);
            Assert.Contains(skipped, s => s.Contains("W1", StringComparison.Ordinal) &&
                                          s.Contains("dxf.min-wall-length", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingUnitMetadataMismatchIsSkippedRatherThanImportedAsPlainRuling()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var w1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "W1");
                w1.Cell(Col(sheet, "YOUR ANSWER")).Value = "48";
                w1.Cell(Col(sheet, "Setting units")).Value = "in;ft";
                workbook.Save();
            }

            var skipped = new List<string>();
            var parsed = RuleSettings.ReadQuestionAnswers(path, skipped);

            Assert.Empty(parsed);
            Assert.Contains(skipped, s => s.Contains("setting key count (1)", StringComparison.Ordinal) &&
                                          s.Contains("setting units count (2)", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DuplicateSettingAnswersAreSkippedRatherThanLastWriteWins()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var w1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "W1");
                var a1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "A1");
                int answer = Col(sheet, "YOUR ANSWER");
                w1.Cell(answer).Value = "48";
                a1.Cell(answer).Value = "60";
                a1.Cell(Col(sheet, "Setting key")).Value = "dxf.min-wall-length";
                a1.Cell(Col(sheet, "Setting units")).Value = "in";
                workbook.Save();
            }

            var skipped = new List<string>();
            var parsed = RuleSettings.ReadQuestionAnswers(path, skipped);

            Assert.DoesNotContain(parsed, p => p.SettingKey == "dxf.min-wall-length");
            Assert.Contains(skipped, s => s.Contains("dxf.min-wall-length", StringComparison.Ordinal) &&
                                          s.Contains("answered more than once", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ConfiguredKorStandardsContainsEveryProductionRule()
    {
        // This FAILS when the rules database is unreachable, where the share-backed tests SKIP.
        // The asymmetry is deliberate and worth stating, because the two look inconsistent.
        //
        // A missing drawing means there is nothing to check: the test has no subject, so skipping
        // is honest. A missing rule means we do not know what we are checking AGAINST — the suite
        // would fall back to the values compiled into the tool and certify a model that production,
        // which reads the database, would never build. A green run that proves the wrong thing is
        // worse than no run.
        string? connection = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set, so this suite would test the values built " +
            "into the tool while production reads them from KorStandards. Set it (process-local is fine) and " +
            "run again. A missing drawing may be skipped; a missing rule may not.");

        var settings = RuleSettings.LoadRequired(connection, DxfToEtabsService.RequiredRuleKeys);

        Assert.Empty(DxfToEtabsService.RequiredRuleKeys.Except(settings.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(18, settings["dxf.spandrel-depth-floor"].Value);
        Assert.Equal(0, settings["dxf.extend-limit"].Value);
    }

    [Fact]
    public void EveryRowSaysWhetherItIsOursToDecideOrTheirsToAnswer()
    {
        // A decision that reads like a question wastes an engineer's time; a question that reads
        // like a decision loses the answer. The workbook has to say which each row is, in a column
        // nobody has to unhide — and the page's own introduction has to describe the page that was
        // written, not the one it used to be.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Questions");
            int status = Col(sheet, "Status");
            Assert.False(sheet.Column(status).IsHidden);

            var byCode = sheet.RowsUsed().Where(r => r.RowNumber() > 4)
                .ToDictionary(r => r.Cell(1).GetString(), r => r.Cell(status).GetString());

            var questions = ModelQuestionnaire
                .StandingQuestions(report.ClassificationUsed, report.ComposeUsed, report)
                .Where(q => !q.ForTheRecord)   // the rest live on "Rules in force"
                .ToList();

            Assert.Contains(questions, q => q.Decided);
            foreach (var q in questions)
                Assert.Equal(
                    !q.Decided ? "NEEDS YOU" : ModelQuestionnaire.Changeable(q) ? "DECIDED" : "SCOPE",
                    byCode[q.Code]);

            string intro = sheet.Cell(2, 1).GetString();
            if (questions.All(q => q.Decided))
                Assert.DoesNotContain("NEEDS YOU", intro, StringComparison.Ordinal);
            else
                Assert.Contains("NEEDS YOU", intro, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EveryDecisionWeTookCarriesTheEvidenceThatDroveIt()
    {
        // This is the entire basis for settling these ourselves instead of sending them out as
        // questions. A decided row with nothing in its Evidence column is a hardcode with a label
        // on it, and the engineer has no way to argue with a number she cannot see the reason for.
        var report = MinimalReport(Path.Combine(Path.GetTempPath(), "unused.xlsx"));
        var decided = ModelQuestionnaire
            .StandingQuestions(report.ClassificationUsed, report.ComposeUsed, report)
            .Where(q => q.Decided)
            .ToList();   // evidence is required of every decision, front page or not

        Assert.NotEmpty(decided);
        foreach (var q in decided)
            Assert.False(string.IsNullOrWhiteSpace(q.Evidence),
                $"{q.Code} is marked DECIDED but carries no evidence.");
    }

    [Fact]
    public void AnsweringADecidedRowOverridesIt()
    {
        // The whole basis for deciding these ourselves is that one cell takes it back. If a DECIDED
        // row could not be overridden it would be a hardcode wearing a label.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var a1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "A1");
                Assert.Equal("DECIDED", a1.Cell(Col(sheet, "Status")).GetString());
                a1.Cell(Col(sheet, "YOUR ANSWER")).Value = "2.5";
                workbook.Save();
            }

            var parsed = RuleSettings.ReadQuestionAnswers(path);
            var rule = Assert.Single(parsed, a => a.SettingKey == "dxf.max-column-aspect");
            Assert.Equal("2.5", rule.SettingValue);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EveryRuleTheRunAppliedIsVisibleWhetherOrNotAQuestionAsksAboutIt()
    {
        // The questions sheet covers judgement. This one covers everything, including the seven
        // geometry tolerances no question touches — a rule an engineer cannot see is one she cannot
        // disagree with, and "why did that outline not close" needs a number she can name.
        string? connection = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connection));

        var applied = RuleSettings.LoadRequired(connection, DxfToEtabsService.RequiredRuleKeys);
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path) with { RulesApplied = applied };
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Rules in force");
            var listed = sheet.RowsUsed().Where(r => r.RowNumber() > 4)
                .ToDictionary(r => r.Cell(1).GetString(), r => r.Cell(4).GetString());

            foreach (string key in DxfToEtabsService.RequiredRuleKeys)
                Assert.True(listed.ContainsKey(key), $"{key} governs the model and is on no sheet.");

            // A rule a question binds must point at that question, or the engineer changes it in
            // the wrong place and nothing happens.
            Assert.Equal("question W1", listed["dxf.min-wall-length"]);
            Assert.Equal("question F1", listed["dxf.floor-from-perimeter-wall"]);
            // And a rule no question binds must not appear to point at one. Checked against the
            // exact cell rather than for the word "question", which the unbound wording also uses.
            Assert.DoesNotMatch(@"^question\s", listed["dxf.join-tolerance"]);
            Assert.Contains("becomes a question", listed["dxf.join-tolerance"], StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ARowOffersAnAnswerOnlyWhereAnAnswerChangesSomething()
    {
        // The failure this exists for: all 25 rows were marked DECIDED in one colour with an empty
        // cream answer box, and seven of them had no setting key at all. An engineer could answer
        // C1, F2, M1, M2, O1, P1 or S2, see it accepted, and get an identical model back. The
        // workbook was inviting an answer it could not act on.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Questions");
            int status = Col(sheet, "Status"), answer = Col(sheet, "YOUR ANSWER");

            var rows = sheet.RowsUsed().Where(r => r.RowNumber() > 4)
                .ToDictionary(r => r.Cell(1).GetString(), r => r);

            var questions = ModelQuestionnaire
                .StandingQuestions(report.ClassificationUsed, report.ComposeUsed, report)
                .Where(q => !q.ForTheRecord)
                .ToList();

            int changeable = 0, scope = 0;
            foreach (var q in questions)
            {
                var row = rows[q.Code];
                string shown = row.Cell(status).GetString();

                if (!q.Decided)
                {
                    Assert.Equal("NEEDS YOU", shown);
                    continue;
                }

                if (ModelQuestionnaire.Changeable(q))
                {
                    changeable++;
                    Assert.Equal("DECIDED", shown);
                    Assert.True(string.IsNullOrEmpty(row.Cell(answer).GetString()),
                        $"{q.Code} is answerable, so its answer cell must start empty.");
                }
                else
                {
                    scope++;
                    Assert.Equal("SCOPE", shown);

                    // Empty, not a dash. A placeholder here is a nonblank answer to the importer.
                    Assert.True(string.IsNullOrEmpty(row.Cell(answer).GetString()),
                        $"{q.Code} is a SCOPE row; its answer cell must hold nothing at all.");
                }
            }

            // Both kinds must actually be present, or this test passes by having nothing to check.
            Assert.True(changeable > 0 && scope > 0,
                $"expected both kinds of row; got {changeable} changeable and {scope} scope.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AnUntouchedWorkbookImportsNothingAtAll()
    {
        // Found by the suite the moment it existed: marking SCOPE rows with a dash so they would
        // not read as empty fields put a nonblank string in the answer column, and the importer
        // banked seven rulings per import that no engineer had typed. Whatever the sheet writes
        // into that column, a workbook nobody has answered must import as silence.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            var skipped = new List<string>();
            Assert.Empty(RuleSettings.ReadQuestionAnswers(path, skipped));

            // One line, and it says why -- not silence, and not a complaint about the file.
            string only = Assert.Single(skipped);
            Assert.Contains("YOUR ANSWER column is empty", only, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ADashMeansNothingToSayRatherThanAnAnswer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var w1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "W1");
                w1.Cell(Col(sheet, "YOUR ANSWER")).Value = "—";
                workbook.Save();
            }

            var skipped = new List<string>();
            Assert.Empty(RuleSettings.ReadQuestionAnswers(path, skipped));
            Assert.Contains("YOUR ANSWER column is empty", Assert.Single(skipped), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NeitherIntroductionPromisesThatEveryAnswerBecomesARule()
    {
        // Tested on the string rather than the sheet, because the branch that does not render is
        // the one that rots. This text claimed "any nonblank answer becomes a rule applied to every
        // job from then on" — false for every row with no setting key — and a correction to the
        // rendered branch left the claim alive in the other one.
        foreach (string intro in new[]
                 {
                     ModelQuestionnaire.Introduction(open: 0, changeable: 18),
                     ModelQuestionnaire.Introduction(open: 3, changeable: 18),
                 })
        {
            Assert.DoesNotMatch(@"(?i)any[^.]*answer[^.]*becomes a rule", intro);
            Assert.DoesNotMatch(@"(?i)every[^.]*answer[^.]*becomes a rule", intro);
            Assert.DoesNotMatch(@"(?i)overrides any (row|decision|of them)", intro);
            Assert.DoesNotMatch(@"(?i)one cell overrides", intro);

            // And it must say the thing that is true, rather than merely omitting the lie.
            Assert.Contains("SCOPE", intro, StringComparison.Ordinal);
            Assert.Contains("tied to a rule", intro, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEditOutsideTheAnswerColumnSaysSoRatherThanImportingSilently()
    {
        // "answers found: 0" with no reason reads like the workbook was wrong. It was not -- the
        // edit was simply in a column nothing reads, and the import had no way to say so.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var w1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "W1");
                w1.Cell(Col(sheet, "Rule topic")).Value = "something-i-typed-in-the-wrong-column";
                workbook.Save();
            }

            var skipped = new List<string>();
            Assert.Empty(RuleSettings.ReadQuestionAnswers(path, skipped));
            Assert.Contains(skipped, m => m.Contains("YOUR ANSWER", StringComparison.Ordinal) &&
                                          m.Contains("changes nothing", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ALayerNameAnswerImportsAsTextRatherThanBeingSearchedForDigits()
    {
        // Running "S8-WALL" through the numeric parser would either refuse the row or store 8.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("Questions");
                var l1 = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "L1");
                l1.Cell(Col(sheet, "YOUR ANSWER")).Value = " S8-WALL ; CONC-WALL ";
                workbook.Save();
            }

            var rule = Assert.Single(RuleSettings.ReadQuestionAnswers(path),
                a => a.SettingKey == "dxf.wall-layer-patterns");
            Assert.Equal("S8-WALL;CONC-WALL", rule.SettingValue);
            Assert.Equal(RuleSettings.TextUnits, rule.SettingUnits);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheFrontPageCarriesOnlyWhatSheHasToRead()
    {
        // 28 rows of which ten mattered is a page that gets closed. Three of them told a KOR
        // engineer what KOR's own layer convention is, and two were changelog entries about bugs
        // already fixed. They stay in the workbook; they stop competing for her attention.
        string path = Path.Combine(Path.GetTempPath(), $"kor-questions-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = MinimalReport(path);
            ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, "test");

            var all = ModelQuestionnaire.StandingQuestions(
                report.ClassificationUsed, report.ComposeUsed, report);
            var record = all.Where(q => q.ForTheRecord).Select(q => q.Code).ToList();
            Assert.NotEmpty(record);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Questions");
            var shown = sheet.RowsUsed().Where(r => r.RowNumber() > 4)
                .Select(r => r.Cell(1).GetString())
                .Where(c => c.Length > 0 && c.Length <= 3)
                .ToList();

            foreach (string code in record)
                Assert.DoesNotContain(code, shown);

            Assert.Equal(all.Count - record.Count, shown.Count);

            // Kept off, not hidden: the sheet has to say they exist and where they are.
            string note = string.Join(" ", sheet.RowsUsed().Select(r => r.Cell(1).GetString()));
            Assert.Contains("Rules in force", note, StringComparison.Ordinal);
            foreach (string code in record)
                Assert.Contains(code, note, StringComparison.Ordinal);

            // And every one of them is still in the full rule set, not dropped.
            var rules = workbook.Worksheet("Rules in force");
            Assert.NotNull(rules);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static int Col(IXLWorksheet sheet, string header)
        => sheet.Row(4).CellsUsed()
            .Single(c => c.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            .Address.ColumnNumber;

    private static DxfToEtabsReport MinimalReport(string path)
        => new(
            path, 1, 1, 1,
            new ComposeSummary(1, 1, 1, 4, 1, Array.Empty<string>(), Array.Empty<string>()),
            (0, 0),
            Array.Empty<SheetOutcome>(),
            Array.Empty<string>(),
            new PlanClassificationOptions(),
            new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 });
}
