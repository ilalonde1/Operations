#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Knob-driven config for the real-time Watcher. Defaults match the
// FileSync.JobKnobs seed (handoff §7.7) and the PS1 watcher.ps1 constants.
// Cron is NULL in the seed; this is a long-running hosted service, not a
// Quartz job. Mode/Enabled flips on the FileSync.Jobs row are honored on
// the next config poll.
internal sealed record WatcherOptions(
    string WatchPath,
    string ControlFileName,
    string HoldingFolderName,
    int MaxSyncMinutes,
    int DebounceSeconds,
    int LockPollSeconds,
    int MaxLockTrackHours,
    int PostRunRetrySeconds,
    int FileStabilitySleepMs,
    int HeartbeatMinutes,
    int LivenessThresholdHours,
    int RestartBackoffMaxSeconds,
    int SimpleVsChunkedThresholdBytes,
    int PaginationTopN,
    int ConfigPollSeconds)
{
    public const string DefaultWatchPath = @"\\KOR-FS01\Projects\Projects";
    public const string DefaultControlFileName = "NOT SYNCED TO ACTIVE PROJECTS.txt";
    public const string DefaultHoldingFolderName = "_ToBeDeleted";
    public const int DefaultMaxSyncMinutes = 15;
    public const int DefaultDebounceSeconds = 5;
    public const int DefaultLockPollSeconds = 10;
    public const int DefaultMaxLockTrackHours = 12;
    public const int DefaultPostRunRetrySeconds = 120;
    public const int DefaultFileStabilitySleepMs = 2500;
    public const int DefaultHeartbeatMinutes = 5;
    public const int DefaultLivenessThresholdHours = 24;
    public const int DefaultRestartBackoffMaxSeconds = 300;
    public const int DefaultSimpleVsChunkedThresholdBytes = 3670016;
    public const int DefaultPaginationTopN = 200;
    public const int DefaultConfigPollSeconds = 60;

    public static WatcherOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        int GetInt(string key, int fallback)
        {
            if (knobs.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0) return n;
            return fallback;
        }

        return new WatcherOptions(
            WatchPath: Get("WatchPath", DefaultWatchPath),
            ControlFileName: Get("ControlFileName", DefaultControlFileName),
            HoldingFolderName: Get("HoldingFolderName", DefaultHoldingFolderName),
            MaxSyncMinutes: GetInt("MaxSyncMinutes", DefaultMaxSyncMinutes),
            DebounceSeconds: GetInt("DebounceSeconds", DefaultDebounceSeconds),
            LockPollSeconds: GetInt("LockPollSeconds", DefaultLockPollSeconds),
            MaxLockTrackHours: GetInt("MaxLockTrackHours", DefaultMaxLockTrackHours),
            PostRunRetrySeconds: GetInt("PostRunRetrySeconds", DefaultPostRunRetrySeconds),
            FileStabilitySleepMs: GetInt("FileStabilitySleepMs", DefaultFileStabilitySleepMs),
            HeartbeatMinutes: GetInt("HeartbeatMinutes", DefaultHeartbeatMinutes),
            LivenessThresholdHours: GetInt("LivenessThresholdHours", DefaultLivenessThresholdHours),
            RestartBackoffMaxSeconds: GetInt("RestartBackoffMaxSeconds", DefaultRestartBackoffMaxSeconds),
            SimpleVsChunkedThresholdBytes: GetInt("SimpleVsChunkedThresholdBytes", DefaultSimpleVsChunkedThresholdBytes),
            PaginationTopN: GetInt("PaginationTopN", DefaultPaginationTopN),
            ConfigPollSeconds: GetInt("ConfigPollSeconds", DefaultConfigPollSeconds));
    }
}
