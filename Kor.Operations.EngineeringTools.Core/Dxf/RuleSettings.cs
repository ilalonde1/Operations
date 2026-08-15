using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One rule the tool applies, and where the value came from.</summary>
public sealed record RuleSetting(string Key, double Value, string Units, string Confidence, string Authority, string Because);

public sealed record QuestionAnswerRule(
    string Code,
    string Scope,
    string Topic,
    string Question,
    string WhatTheToolDid,
    string Answer,
    string? SettingKey,
    string? SettingValue,
    string? SettingUnits,
    string Confidence);

public sealed record RuleImportResult(int RowsRead, int AnswersFound, int RulesWritten, int SettingsWritten, IReadOnlyList<string> Skipped);

/// <summary>
/// The rules this tool applies, read from KorStandards rather than compiled into it.
/// </summary>
public static class RuleSettings
{
    /// <summary>Where to find KorStandards.</summary>
    public const string ConnectionEnvironmentVariable = "KOR_ENGINEERINGTOOLS_STANDARDSDB";

    /// <summary>
    /// Every setting the view offers, by key. Empty when the database is not configured or not reachable.
    /// Use <see cref="LoadRequired"/> for a production model.
    /// </summary>
    public static IReadOnlyDictionary<string, RuleSetting> Load(string? connectionString = null)
    {
        var (settings, _) = TryLoad(connectionString);
        return settings;
    }

    /// <summary>Load the rules a production run requires, or fail before any model is written.</summary>
    public static IReadOnlyDictionary<string, RuleSetting> LoadRequired(
        string? connectionString,
        IEnumerable<string> requiredKeys)
    {
        connectionString ??= Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"KorStandards is required for DXF-to-ETABS generation. Set {ConnectionEnvironmentVariable} " +
                "or pass --rules-db.");

        var (settings, invalid) = TryLoad(connectionString);
        if (invalid.Count > 0)
            throw new InvalidOperationException("KorStandards rule settings could not be read: " +
                                                string.Join("; ", invalid));

        var missing = requiredKeys
            .Where(k => !settings.ContainsKey(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "KorStandards is missing rule setting(s) required by DXF-to-ETABS: " +
                string.Join(", ", missing) + ".");

        return settings;
    }

    private static (IReadOnlyDictionary<string, RuleSetting> Settings, IReadOnlyList<string> Invalid) TryLoad(string? connectionString = null)
    {
        var settings = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();

        connectionString ??= Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return (settings, invalid);

        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SettingKey, SettingValue, SettingUnits, Confidence, Authority, Because, Source " +
                "FROM analysis.vw_RuleSetting";
            command.CommandTimeout = 15;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (reader.IsDBNull(1)) continue;

                string units = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                if (!TryParseSettingValue(reader.GetValue(1), units, out double value))
                {
                    invalid.Add($"{key}='{reader.GetValue(1)}'");
                    continue;
                }

                // The view unions conventions and rulings, and a ruling is the engineer speaking,
                // so it wins where both carry the same key.
                string confidence = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                string source = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                if (settings.TryGetValue(key, out var already) &&
                    IsAtLeastAsAuthoritative(already.Confidence, already.Authority, confidence, source))
                    continue;

                settings[key] = new RuleSetting(
                    key, value, units,
                    confidence,
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
            }
        }
        catch (Exception ex)
        {
            invalid.Add(ex.Message);
            return (new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase), invalid);
        }

        return (settings, invalid);
    }

    /// <summary>The value for a key, or the built-in one where the database does not carry it.</summary>
    public static double ValueOr(this IReadOnlyDictionary<string, RuleSetting> settings, string key, double fallback)
        => settings.TryGetValue(key, out var s) ? s.Value : fallback;

    /// <summary>The same, for the settings that are switches rather than sizes.</summary>
    public static bool FlagOr(this IReadOnlyDictionary<string, RuleSetting> settings, string key, bool fallback)
        => settings.TryGetValue(key, out var s) ? Math.Abs(s.Value) > 0.5 : fallback;

    public static RuleImportResult ImportQuestionAnswers(
        string workbookPath,
        string engineer,
        string? connectionString = null,
        string? sourcePath = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Cannot write rules: set {ConnectionEnvironmentVariable} or pass --rules-db.");
        if (string.IsNullOrWhiteSpace(engineer))
            throw new ArgumentException("Engineer is required.", nameof(engineer));

        var skipped = new List<string>();
        var answers = ReadQuestionAnswers(workbookPath, skipped);
        int written = 0, settings = 0;

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var answer in answers)
        {
            Guid rulingId = UpsertRuling(connection, transaction, engineer, answer);
            InsertEvidence(connection, transaction, rulingId, sourcePath ?? workbookPath, answer);
            written++;
            if (!string.IsNullOrWhiteSpace(answer.SettingKey)) settings++;
        }

        transaction.Commit();
        return new RuleImportResult(answers.Count + skipped.Count, answers.Count, written, settings, skipped);
    }

    public static List<QuestionAnswerRule> ReadQuestionAnswers(string workbookPath, List<string>? skipped = null)
    {
        skipped ??= new List<string>();
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
            w.Name.Equals("Questions", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The workbook has no 'Questions' sheet.");

        var headerRow = sheet.Row(4);
        var headers = headerRow.CellsUsed()
            .ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        int answerCol = RequireHeader(headers, "YOUR ANSWER");
        int codeCol = RequireHeader(headers, "Ref");
        int questionCol = RequireHeader(headers, "Question");
        int didCol = RequireHeader(headers, "What the tool did");

        int? scopeCol = OptionalHeader(headers, "Rule scope");
        int? topicCol = OptionalHeader(headers, "Rule topic");
        int? keyCol = OptionalHeader(headers, "Setting key");
        int? unitsCol = OptionalHeader(headers, "Setting units");
        int? confidenceCol = OptionalHeader(headers, "Confidence");

        var result = new List<QuestionAnswerRule>();
        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 4;
        for (int row = 5; row <= lastRow; row++)
        {
            string answer = sheet.Cell(row, answerCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(answer)) continue;

            string code = sheet.Cell(row, codeCol).GetString().Trim();
            string question = sheet.Cell(row, questionCol).GetString().Trim();
            string did = sheet.Cell(row, didCol).GetString().Trim();
            string scope = ValueAt(sheet, row, scopeCol) ?? "etabs-modelling";
            string topic = ValueAt(sheet, row, topicCol) ?? StableTopic(code, question);
            string? settingKeys = ValueAt(sheet, row, keyCol);
            string? settingUnitsText = ValueAt(sheet, row, unitsCol);
            string confidence = ValueAt(sheet, row, confidenceCol) ?? "engineer-confirmed";

            var keys = SplitMetadata(settingKeys);
            var units = SplitMetadata(settingUnitsText);

            if (keys.Count == 0)
            {
                result.Add(new QuestionAnswerRule(
                    code, scope, topic, question, did, answer,
                    null, null, null, confidence));
                continue;
            }

            if (units.Count is not 0 && units.Count != keys.Count)
            {
                skipped.Add($"{code}: setting key count ({keys.Count}) does not match setting units count ({units.Count}).");
                result.Add(new QuestionAnswerRule(
                    code, scope, topic, question, did, answer,
                    null, null, null, confidence));
                continue;
            }

            var values = ParseSettingValues(answer, keys.Count, units);
            if (values.Count < keys.Count)
            {
                skipped.Add($"{code}: answer does not contain {keys.Count} parseable value(s) for {string.Join(", ", keys)}.");
                result.Add(new QuestionAnswerRule(
                    code, scope, topic, question, did, answer,
                    null, null, null, confidence));
                continue;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                string unit = units.Count == 0 ? string.Empty : units[i];
                result.Add(new QuestionAnswerRule(
                    code, scope, keys.Count == 1 ? topic : $"{topic}:{keys[i]}",
                    question, did, answer,
                    keys[i], FormatSettingValue(values[i], unit), unit, confidence));
            }
        }

        return result;
    }

    private static Guid UpsertRuling(
        SqlConnection connection,
        SqlTransaction transaction,
        string engineer,
        QuestionAnswerRule answer)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
DECLARE @id UNIQUEIDENTIFIER;
SELECT @id = Id
  FROM analysis.Ruling WITH (UPDLOCK, HOLDLOCK)
 WHERE Engineer = @Engineer
   AND Scope = @Scope
   AND Topic = @Topic
   AND RetiredAtUtc IS NULL;

IF @id IS NULL
BEGIN
    DECLARE @inserted TABLE (Id UNIQUEIDENTIFIER);
    INSERT INTO analysis.Ruling
        (Engineer, Scope, Topic, Ruling, Quote, ActionType, Confidence, RuledOn, CreatedBy,
         SettingKey, SettingValue, SettingUnits)
    OUTPUT inserted.Id INTO @inserted
    VALUES
        (@Engineer, @Scope, @Topic, @Ruling, @Quote, 'APPLY', @Confidence, @RuledOn, @CreatedBy,
         @SettingKey, @SettingValue, @SettingUnits);
    SELECT @id = Id FROM @inserted;
END
ELSE
BEGIN
    INSERT INTO analysis.RulingHistory (RulingId, FromConfidence, ToConfidence, Basis, ChangedBy)
    SELECT Id, Confidence, @Confidence,
           CONCAT(N'Questionnaire import superseded the active ruling. Previous ruling: ',
                  Ruling, N' Previous setting: ', ISNULL(SettingKey, N''), N'=',
                  ISNULL(SettingValue, N''), N' ', ISNULL(SettingUnits, N''),
                  N'. New ruling: ', @Ruling),
           @CreatedBy
      FROM analysis.Ruling
     WHERE Id = @id
       AND (ISNULL(Ruling, N'') <> ISNULL(@Ruling, N'')
            OR ISNULL(Quote, N'') <> ISNULL(@Quote, N'')
            OR ISNULL(Confidence, N'') <> ISNULL(@Confidence, N'')
            OR ISNULL(SettingKey, N'') <> ISNULL(@SettingKey, N'')
            OR ISNULL(SettingValue, N'') <> ISNULL(@SettingValue, N'')
            OR ISNULL(SettingUnits, N'') <> ISNULL(@SettingUnits, N''));

    UPDATE analysis.Ruling
       SET Ruling = @Ruling,
           Quote = @Quote,
           ActionType = 'APPLY',
           Confidence = @Confidence,
           RuledOn = @RuledOn,
           UpdatedAtUtc = SYSDATETIMEOFFSET(),
           SettingKey = @SettingKey,
           SettingValue = @SettingValue,
           SettingUnits = @SettingUnits
     WHERE Id = @id;
END

SELECT @id;
""";
        Add(command, "@Engineer", engineer);
        Add(command, "@Scope", answer.Scope);
        Add(command, "@Topic", answer.Topic);
        Add(command, "@Ruling", answer.Answer);
        Add(command, "@Quote", answer.Answer);
        Add(command, "@Confidence", answer.Confidence);
        Add(command, "@RuledOn", DateTime.UtcNow.Date);
        Add(command, "@CreatedBy", "DXF-to-ETABS questionnaire import");
        Add(command, "@SettingKey", answer.SettingKey);
        Add(command, "@SettingValue", answer.SettingValue);
        Add(command, "@SettingUnits", answer.SettingUnits);
        return (Guid)command.ExecuteScalar()!;
    }

    private static void InsertEvidence(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid rulingId,
        string sourcePath,
        QuestionAnswerRule answer)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
IF NOT EXISTS (
    SELECT 1 FROM analysis.RulingEvidence
     WHERE RulingId = @RulingId AND SourcePath = @SourcePath AND Excerpt = @Excerpt)
BEGIN
    INSERT INTO analysis.RulingEvidence (RulingId, SourcePath, Excerpt, ObservedAtUtc)
    VALUES (@RulingId, @SourcePath, @Excerpt, SYSDATETIMEOFFSET());
END
""";
        Add(command, "@RulingId", rulingId);
        Add(command, "@SourcePath", sourcePath);
        Add(command, "@Excerpt",
            $"{answer.Code}: {answer.Question} Tool did: {answer.WhatTheToolDid} Answer: {answer.Answer}");
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// What the run used and where each value came from, for the report. Both halves matter: a
    /// setting taken from the database should be visible, and so should one that fell back.
    /// </summary>
    public static IEnumerable<string> Describe(
        IReadOnlyDictionary<string, RuleSetting> settings, IReadOnlyDictionary<string, double> builtIn)
    {
        if (settings.Count == 0)
        {
            yield return $"rules: all {builtIn.Count} from the values built into this tool — " +
                         $"KorStandards was not consulted.";
            yield break;
        }

        var fromDb = builtIn.Keys.Where(settings.ContainsKey).ToList();
        var fell = builtIn.Keys.Where(k => !settings.ContainsKey(k)).ToList();

        yield return fell.Count == 0
            ? $"rules: all {fromDb.Count} read from KorStandards."
            : $"rules: {fromDb.Count} read from KorStandards, {fell.Count} from the values built into this tool.";

        foreach (string key in fromDb.OrderBy(k => k))
        {
            var s = settings[key];
            string moved = Math.Abs(s.Value - builtIn[key]) > 1e-9
                ? $"  (the built-in value is {builtIn[key]:0.###} — the database wins)" : string.Empty;
            yield return $"   {key} = {s.Value:0.###} {s.Units} [{s.Confidence}, {s.Authority}]{moved}";
        }

        foreach (string key in fell.OrderBy(k => k))
            yield return $"   {key} = {builtIn[key]:0.###} — not in KorStandards, built-in value used";
    }

    private static bool TryParseSettingValue(object value, string units, out double parsed)
        => TryParseSettingText(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, units, out parsed);

    private static List<double> ParseSettingValues(string text, int count, IReadOnlyList<string> units)
    {
        if (count == 1)
        {
            string unit = units.Count == 0 ? string.Empty : units[0];
            return TryParseSettingText(text, unit, out double parsed) ? new List<double> { parsed } : new List<double>();
        }

        var matches = Regex.Matches(text, @"(?<![\d.])[-+]?\d+(?:\.\d+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToList();

        for (int i = 0; i < Math.Min(matches.Count, units.Count); i++)
            matches[i] = ConvertForUnits(matches[i], text, units[i]);

        return matches.Take(count).ToList();
    }

    private static bool TryParseSettingText(string text, string units, out double parsed)
    {
        text = text.Trim();
        if (units.Equals("bool", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("apply", StringComparison.OrdinalIgnoreCase) ||
                text == "1" ||
                Regex.IsMatch(text, @"\b(yes|true|apply|on)\b", RegexOptions.IgnoreCase))
            {
                parsed = 1;
                return true;
            }
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                text == "0" ||
                Regex.IsMatch(text, @"\b(no|false|off)\b", RegexOptions.IgnoreCase))
            {
                parsed = 0;
                return true;
            }
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return true;

        var match = Regex.Match(text, @"(?<![\d.])[-+]?\d+(?:\.\d+)?");
        if (!match.Success ||
            !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return false;

        parsed = ConvertForUnits(parsed, text, units);
        return true;
    }

    private static double ConvertForUnits(double value, string sourceText, string units)
    {
        if (units.Equals("sqin", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(sourceText, @"\b(sq\.?\s*ft|sf|ft2|ft\^2)\b", RegexOptions.IgnoreCase))
            return value * 144.0;

        return value;
    }

    private static List<string> SplitMetadata(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool IsAtLeastAsAuthoritative(string existingConfidence, string existingAuthority, string newConfidence, string newSource)
    {
        int ExistingRank(string confidence, string authority)
        {
            int confidenceRank = confidence.Equals("engineer-confirmed", StringComparison.OrdinalIgnoreCase) ? 3
                : confidence.Equals("replay-verified", StringComparison.OrdinalIgnoreCase) ? 2
                : 1;
            int sourceRank = !authority.Contains('/', StringComparison.Ordinal) ? 1 : 0;
            return confidenceRank * 10 + sourceRank;
        }

        int NewRank()
        {
            int confidenceRank = newConfidence.Equals("engineer-confirmed", StringComparison.OrdinalIgnoreCase) ? 3
                : newConfidence.Equals("replay-verified", StringComparison.OrdinalIgnoreCase) ? 2
                : 1;
            int sourceRank = newSource.Equals("ruling", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            return confidenceRank * 10 + sourceRank;
        }

        return ExistingRank(existingConfidence, existingAuthority) >= NewRank();
    }

    private static int RequireHeader(IReadOnlyDictionary<string, int> headers, string name)
        => headers.TryGetValue(name, out int column)
            ? column
            : throw new InvalidOperationException($"The Questions sheet is missing the '{name}' column.");

    private static int? OptionalHeader(IReadOnlyDictionary<string, int> headers, string name)
        => headers.TryGetValue(name, out int column) ? column : null;

    private static string? ValueAt(IXLWorksheet sheet, int row, int? column)
    {
        if (column is null) return null;
        string value = sheet.Cell(row, column.Value).GetString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string StableTopic(string code, string question)
    {
        string text = string.IsNullOrWhiteSpace(question) ? code : question;
        string slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 64) slug = slug[..64].Trim('-');
        return $"question-{code.ToLowerInvariant()}-{slug}";
    }

    private static string FormatSettingValue(double value, string? units)
        => units?.Equals("bool", StringComparison.OrdinalIgnoreCase) == true
            ? (Math.Abs(value) > 0.5 ? "1" : "0")
            : value.ToString("0.########", CultureInfo.InvariantCulture);

    private static void Add(SqlCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
