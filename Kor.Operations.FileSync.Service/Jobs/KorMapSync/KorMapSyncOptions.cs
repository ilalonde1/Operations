#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.KorMapSync;

// Resolved per-run from FileSync.JobKnobs. Defaults match what was proven by
// hand on 2026-08-07 when the map was first populated (557 -> 2,612 pins), so a
// first run works before anyone touches the knobs.
//
// Tunables only. Credentials live in FileSyncOptions (KOR_FILESYNC_* env vars
// on KOR-APP01), the same place the Graph and SQL secrets already live.
internal sealed record KorMapSyncOptions(
    string DeltekDsn,
    string DeltekCatalog,
    string WordPressBaseUrl,
    int GeocodeBatchLimit,
    int PushChunkSize,
    int RegionChangeAbortThreshold,
    string ShadowOutputDir)
{
    public const string DefaultDeltekDsn = "Deltek";
    public const string DefaultDeltekCatalog = "C0000052267P_1_KOR00000000";
    public const string DefaultWordPressBaseUrl = "https://www.korstructural.com";

    // Cap per run so a bad address batch can't burn the geocoding quota in one go.
    public const int DefaultGeocodeBatchLimit = 400;
    public const int DefaultPushChunkSize = 250;

    // Pre-flight: if a run wants to re-region more than this many pins that are
    // already correct, something in the resolver has regressed. Abort rather
    // than rewrite the map. Two real regressions on 2026-08-07 (blanking good
    // values; trusting Deltek's State over its City) each moved far more than
    // this, so the threshold is deliberately tight.
    public const int DefaultRegionChangeAbortThreshold = 25;

    public static string DefaultShadowOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations",
        "FileSync",
        "shadow",
        "KorMapSync");

    public static KorMapSyncOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string S(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v!.Trim() : fallback;

        int I(string key, int fallback)
            => knobs.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? n : fallback;

        return new KorMapSyncOptions(
            DeltekDsn: S("DeltekDsn", DefaultDeltekDsn),
            DeltekCatalog: S("DeltekCatalog", DefaultDeltekCatalog),
            WordPressBaseUrl: S("WordPressBaseUrl", DefaultWordPressBaseUrl).TrimEnd('/'),
            GeocodeBatchLimit: I("GeocodeBatchLimit", DefaultGeocodeBatchLimit),
            PushChunkSize: I("PushChunkSize", DefaultPushChunkSize),
            RegionChangeAbortThreshold: I("RegionChangeAbortThreshold", DefaultRegionChangeAbortThreshold),
            ShadowOutputDir: S("ShadowOutputDir", DefaultShadowOutputDir));
    }

    // Deltek's ODBC DSN is installed on KOR-APP01 (System, 64-bit, DataDirect HDP 4.6).
    // Credentials come from FileSyncOptions (env vars), not from here.
    public string BuildOdbcConnectionString(string user, string password)
        => $"DSN={DeltekDsn};UID={user};PWD={password};";
}
