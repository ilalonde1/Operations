#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;

// Knob-driven config for the CTR runner. Defaults match the values
// hard-coded in File-ConcreteTestReports.ps1 so a freshly-seeded job
// works without any rows in FileSync.JobKnobs.
internal sealed record ConcreteTestReportsOptions(
    string SourceRoot,
    string MappingXlsx,
    string MappingSheetName,
    string OutputRoot,
    string SenderAddress,
    string GlobalCc,
    IReadOnlyList<string> Extensions,
    bool RenameOnConflict,
    string ShadowOutputDir)
{
    public const string DefaultSourceRoot = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports";
    public const string DefaultMappingXlsx = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Notes\Concrete Test Report Data.xlsx";
    public const string DefaultMappingSheetName = "CTR";
    public const string DefaultOutputRoot = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Processed";
    public const string DefaultSenderAddress = "app-admin@korstructural.com";
    public const string DefaultGlobalCc = "app-admin@korstructural.com";
    public const string DefaultExtensions = ".pdf;.PDF";
    public const bool DefaultRenameOnConflict = false;

    public static string DefaultShadowOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations", "FileSync", "shadow", "ConcreteTestReports");

    public static ConcreteTestReportsOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        bool GetBool(string key, bool fallback)
        {
            if (knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && bool.TryParse(v, out var b)) return b;
            return fallback;
        }

        var extRaw = Get("Extensions", DefaultExtensions);
        var exts = extRaw
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToArray();

        return new ConcreteTestReportsOptions(
            SourceRoot: Get("SourceRoot", DefaultSourceRoot),
            MappingXlsx: Get("MappingXlsx", DefaultMappingXlsx),
            MappingSheetName: Get("MappingSheetName", DefaultMappingSheetName),
            OutputRoot: Get("OutputRoot", DefaultOutputRoot),
            SenderAddress: Get("SenderAddress", DefaultSenderAddress),
            GlobalCc: Get("GlobalCc", DefaultGlobalCc),
            Extensions: exts,
            RenameOnConflict: GetBool("RenameOnConflict", DefaultRenameOnConflict),
            ShadowOutputDir: Get("ShadowOutputDir", DefaultShadowOutputDir));
    }
}
