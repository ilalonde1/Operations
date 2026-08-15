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
            Assert.Equal("Rule scope", sheet.Cell(4, 5).GetString());
            Assert.Equal("Rule topic", sheet.Cell(4, 6).GetString());
            Assert.Equal("Setting key", sheet.Cell(4, 7).GetString());
            Assert.True(sheet.Column(5).IsHidden);
            Assert.True(sheet.Column(9).IsHidden);
            Assert.Contains("corner-limbs-vs-stocky-pier",
                sheet.RowsUsed().Select(r => r.Cell(6).GetString()));
            Assert.Contains("dxf.opening-height;dxf.spandrel-depth-floor;dxf.spandrel-depth-ceiling",
                sheet.RowsUsed().Select(r => r.Cell(7).GetString()));
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
                h1.Cell(4).Value = "Opening height 90, clamp 18-60";
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
                s1.Cell(4).Value = "Use 450 sq ft";
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
                w1.Cell(4).Value = "ask Andrea";
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
                w1.Cell(4).Value = "48";
                w1.Cell(8).Value = "in;ft";
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
                w1.Cell(4).Value = "48";
                a1.Cell(4).Value = "60";
                a1.Cell(7).Value = "dxf.min-wall-length";
                a1.Cell(8).Value = "in";
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
        string? connection = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} must be set for the DB-authoritative DXF-to-ETABS rule gate.");

        var settings = RuleSettings.LoadRequired(connection, DxfToEtabsService.RequiredRuleKeys);

        Assert.Empty(DxfToEtabsService.RequiredRuleKeys.Except(settings.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(18, settings["dxf.spandrel-depth-floor"].Value);
        Assert.Equal(0, settings["dxf.extend-limit"].Value);
    }

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
