// The takeoff verb `dxf-import-rules`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// IMPORT answered DXF -> ETABS questions into KorStandards. The generation command reads these
// rulings back through analysis.vw_RuleSetting on later runs.
// Usage: takeoff dxf-import-rules <questions.xlsx> --engineer <name> [--rules-db <connection>]
internal static class DxfImportRulesVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("dxf-import-rules", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: takeoff dxf-import-rules <questions.xlsx> --engineer <name> [--rules-db <connection>]");
            return 1;
        }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Questions workbook not found '{args[1]}'."); return 2; }

        string? engineer = null;
        string? rulesDb = null;
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            if (flag.Equals("--engineer", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) engineer = args[++i];
            else if (flag.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rulesDb = args[++i];
            else { Console.Error.WriteLine($"Unknown argument '{flag}'."); return 1; }
        }

        if (string.IsNullOrWhiteSpace(engineer))
        {
            Console.Error.WriteLine("--engineer is required; it is the authority written to analysis.Ruling.");
            return 1;
        }

        var import = RuleSettings.ImportQuestionAnswers(args[1], engineer, rulesDb);
        Console.WriteLine($"answers found : {import.AnswersFound}");
        Console.WriteLine($"rules written : {import.RulesWritten}");
        Console.WriteLine($"settings      : {import.SettingsWritten}");
        foreach (string skipped in import.Skipped) Console.WriteLine("skipped: " + skipped);
        return 0;
    }
}
