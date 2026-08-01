#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs;

// Maps FileSync.Jobs.JobName -> the IJobRunner registered for it.
// Returns the NoOp fallback for any seeded job that has no runner wired
// (e.g. ConcreteTestReports / MoveReportsTo* / RenameReportsUploads while
// those steps are still pending). This way a Manual fire from the Command
// Center always produces a JobRuns row -- never silently drops.
internal sealed class JobRunnerRegistry
{
    private readonly Dictionary<string, IJobRunner> _byName;
    private readonly NoOpJobRunner _noop;

    public JobRunnerRegistry(IEnumerable<IJobRunner> runners, NoOpJobRunner noop)
    {
        _byName = new Dictionary<string, IJobRunner>(StringComparer.OrdinalIgnoreCase);
        foreach (var runner in runners)
        {
            // Don't index the noop; it's always the fallback.
            if (ReferenceEquals(runner, noop))
                continue;
            _byName[runner.JobName] = runner;
        }

        _noop = noop;
    }

    public IJobRunner Resolve(string jobName)
    {
        return _byName.TryGetValue(jobName, out var runner) ? runner : _noop;
    }

    public bool HasRealRunner(string jobName) => _byName.ContainsKey(jobName);

    public int RegisteredCount => _byName.Count;
}
