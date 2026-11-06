using System.Diagnostics;
using System.Text;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Inventor;

/// <summary>What <see cref="ExportWorkflow.Collect"/> produced.</summary>
public sealed class CollectResult
{
    public Snapshot Snapshot { get; set; } = new Snapshot();
    /// <summary>Where snapshot.json was written.</summary>
    public string SnapshotPath { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
    public double ComponentsPerSecond => Snapshot.Components.Count / Math.Max(Elapsed.TotalSeconds, 0.001);
}

/// <summary>The export plan for a snapshot and a config, plus every warning a front end should show. Nothing is exported.</summary>
public sealed class PlanResult
{
    public List<ExportTask> Tasks { get; set; } = new();
    public List<ExportJob> Jobs { get; set; } = new();
    public List<TableJob> Tables { get; set; } = new();
    /// <summary>What the task rules left out (the exclusion report).</summary>
    public List<ExclusionRecord> Exclusions { get; set; } = new();
    /// <summary>Components that could not be read (the list is incomplete). Empty when everything was read.</summary>
    public List<SkippedRecord> Unreadable { get; set; } = new();
    public UnresolvedPolicy Policy { get; set; }
    /// <summary>Warning about a different active Inventor project, only when drawing jobs are planned. Null = nothing to say.</summary>
    public string? ProjectWarning { get; set; }
    /// <summary>Parts without a Content Center flag, counted only when a task has a Content Center rule; otherwise 0.</summary>
    public int ContentCenterNotRecorded { get; set; }

    public int PlannedCount => Jobs.Count(j => j.State == JobState.Planned);
    public int ConflictCount => Jobs.Count(j => j.State == JobState.Conflict);
    /// <summary>True when exports would refuse to run (Unresolved = Block and components could not be read).</summary>
    public bool Blocked => Policy == UnresolvedPolicy.Block && Unreadable.Count > 0;
}

/// <summary>What <see cref="ExportWorkflow.Run"/> did.</summary>
public sealed class RunResult
{
    /// <summary>One result per file job, one per unreadable component, one per table, in that order.</summary>
    public List<ExportResult> Results { get; set; } = new();
    /// <summary>Time spent on the file jobs (tables not included).</summary>
    public TimeSpan FileJobsTime { get; set; }
    /// <summary>export-excluded.csv, or null when nothing was left out (an old report is then deleted).</summary>
    public string? ExclusionReportPath { get; set; }
    public string LogCsvPath { get; set; } = "";
    public string LogJsonPath { get; set; } = "";
    /// <summary>True when the run was cancelled: jobs after the cancel did not run and tables were not written (all logged as such).</summary>
    public bool Cancelled { get; set; }
}

/// <summary>One task after a run: complete, or the outputs that were not exported.</summary>
public sealed class TaskOutcome
{
    public string TaskName { get; set; } = "";
    public List<ExportResult> NotExported { get; set; } = new();
    public bool Complete => NotExported.Count == 0;
}

/// <summary>
/// The work behind collect, plan and export, shared by every front end (CLI, UI window).
/// Returns data and takes callbacks; never writes to the console. Read-only toward Inventor.
/// </summary>
public static class ExportWorkflow
{
    /// <summary>Collects the active assembly from the running Inventor and saves snapshot.json in <paramref name="outRoot"/>.</summary>
    public static CollectResult Collect(string outRoot)
    {
        Directory.CreateDirectory(outRoot);
        var clock = Stopwatch.StartNew();
        var snapshot = SnapshotCollector.CollectFromRunningInventor();
        var elapsed = clock.Elapsed;

        string path = Path.Combine(outRoot, "snapshot.json");
        File.WriteAllText(path, SnapshotJson.ToJson(snapshot));
        return new CollectResult { Snapshot = snapshot, SnapshotPath = path, Elapsed = elapsed };
    }

    /// <summary>Reads a saved snapshot. Throws if the file is not a snapshot of the current schema version.</summary>
    public static Snapshot LoadSnapshot(string file) =>
        SnapshotJson.FromJson(File.ReadAllText(file))
        ?? throw new InvalidDataException($"Could not read a snapshot from {file}.");

    /// <summary>The task named <paramref name="only"/> (ignoring case), or all tasks if none is named. Throws if the name is unknown.</summary>
    public static List<ExportTask> SelectTasks(ExportConfig config, string? only)
    {
        if (string.IsNullOrWhiteSpace(only)) return config.Tasks;
        var match = config.Tasks.Where(t => t.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 0)
            throw new ArgumentException(
                $"No task named '{only}'. Tasks in the config: {string.Join(", ", config.Tasks.Select(t => t.Name))}.");
        return match;
    }

    /// <summary>
    /// Plans every selected task (files and tables) and gathers the warnings. Exports nothing.
    /// Asks a running Inventor for its active project only when drawing jobs are planned; silent if Inventor is not running.
    /// </summary>
    public static PlanResult Plan(Snapshot snapshot, ExportConfig config, string outRoot, string? only = null)
    {
        var tasks = SelectTasks(config, only);
        var exclusions = new List<ExclusionRecord>();
        var jobs = ExportPlanner.PlanAll(snapshot, tasks, outRoot, exclusions);
        bool contentCenterRule = tasks.Any(t => t.Selection.ContentCenter != ContentCenterMode.Include);

        return new PlanResult
        {
            Tasks = tasks,
            Jobs = jobs,
            Tables = ExportPlanner.PlanTables(tasks, outRoot),
            Exclusions = exclusions,
            Unreadable = SnapshotChecks.BlockingProblems(snapshot),
            Policy = config.Unresolved,
            ProjectWarning = ProjectWarning(snapshot, jobs),
            ContentCenterNotRecorded = contentCenterRule ? ContentCenterFilter.NotRecorded(snapshot) : 0,
        };
    }

    /// <summary>Throws (InvalidOperationException, first 10 problems listed) when Unresolved = Block and components could not be read.</summary>
    public static void ThrowIfBlocked(Snapshot snapshot, ExportConfig config)
    {
        if (config.Unresolved != UnresolvedPolicy.Block) return;
        var problems = SnapshotChecks.BlockingProblems(snapshot);
        if (problems.Count == 0) return;

        var lines = problems.Take(10).Select(k => $"  [{k.Reason}] {k.OccurrencePath} {k.Detail}");
        string more = problems.Count > 10 ? Environment.NewLine + $"  ...and {problems.Count - 10} more." : "";
        throw new InvalidOperationException(
            $"Export blocked: {problems.Count} component(s) could not be read, so the component list is incomplete." +
            Environment.NewLine + string.Join(Environment.NewLine, lines) + more +
            Environment.NewLine + "Resolve the files in Inventor, collect again, then export.");
    }

    /// <summary>
    /// Runs a plan: writes the exclusion report, runs the file jobs in Inventor, adds one Unreadable result per
    /// unreadable component, writes the tables, then export-log.csv and export-log.json.
    /// <paramref name="onResult"/> is called for every result as it happens (file jobs, unreadable, tables).
    /// Refuses (throws) under Unresolved = Block with unreadable components, whatever the caller checked before.
    /// <paramref name="cancel"/> stops the run between jobs (the job in progress finishes; Inventor cannot be
    /// interrupted mid-save). Every job that did not run, and every table, then gets a Failed "Cancelled" result,
    /// so the log still accounts for everything planned.
    /// </summary>
    public static RunResult Run(PlanResult plan, Snapshot snapshot, ExportConfig config, string outRoot,
        Action<ExportResult>? onResult = null, CancellationToken cancel = default)
    {
        ThrowIfBlocked(snapshot, config);

        Directory.CreateDirectory(outRoot);
        var run = new RunResult
        {
            ExclusionReportPath = WriteExclusionReport(plan.Exclusions, Path.Combine(outRoot, "export-excluded.csv")),
        };

        // JobRunner reads the jobs one at a time, so stopping the sequence stops the run between jobs.
        var clock = Stopwatch.StartNew();
        run.Results = JobRunner.Run(plan.Jobs.TakeWhile(_ => !cancel.IsCancellationRequested), onResult);
        run.FileJobsTime = clock.Elapsed;

        run.Cancelled = run.Results.Count < plan.Jobs.Count;
        foreach (var job in plan.Jobs.Skip(run.Results.Count))
        {
            var r = ExportResult.ForJob(job);   // Failed "Not run" (Conflict stays Conflict)
            if (job.State == JobState.Planned) r.Detail = "Cancelled before it ran";
            run.Results.Add(r);
            onResult?.Invoke(r);
        }

        foreach (var k in plan.Unreadable)
        {
            var r = ExportResult.ForUnreadable(k);
            run.Results.Add(r);
            onResult?.Invoke(r);
        }

        if (run.Cancelled || cancel.IsCancellationRequested)
        {
            // A table lists every file its task selects; after a cancel it would suggest a complete delivery.
            run.Cancelled = true;
            foreach (var t in plan.Tables)
            {
                var r = new ExportResult
                {
                    TaskName = t.TaskName, Format = t.Output.Format, ModelPath = "(table)", OutputPath = t.OutputPath,
                    Status = t.State == JobState.Conflict ? ExportStatus.Conflict : ExportStatus.Failed,
                    Detail = t.State == JobState.Conflict ? t.Detail : "Cancelled: table not written",
                };
                run.Results.Add(r);
                onResult?.Invoke(r);
            }
        }
        else
        {
            run.Results.AddRange(ExportTables.Write(snapshot, config, plan.Tasks, plan.Tables, onResult));
        }

        // UTF-8 with BOM so Excel opens the CSV correctly.
        run.LogCsvPath = Path.Combine(outRoot, "export-log.csv");
        run.LogJsonPath = Path.Combine(outRoot, "export-log.json");
        File.WriteAllText(run.LogCsvPath, ExportResultLog.ToCsv(run.Results), new UTF8Encoding(true));
        File.WriteAllText(run.LogJsonPath, ExportResultLog.ToJson(run.Results));
        return run;
    }

    /// <summary>Per task: complete, or every output that was not exported (the full list is in the export log).</summary>
    public static List<TaskOutcome> TaskOutcomes(IEnumerable<ExportTask> tasks, IEnumerable<ExportResult> results) =>
        tasks.Select(task => new TaskOutcome
        {
            TaskName = task.Name,
            NotExported = results.Where(r => r.TaskName == task.Name && r.Status != ExportStatus.Exported).ToList(),
        }).ToList();

    /// <summary>
    /// Writes an exclusion report and returns its path. With nothing excluded, an old report from an
    /// earlier run is deleted so it cannot mislead, and null is returned.
    /// </summary>
    public static string? WriteExclusionReport(List<ExclusionRecord> exclusions, string path)
    {
        if (exclusions.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        File.WriteAllText(path, ExclusionReport.ToCsv(exclusions), new UTF8Encoding(true));
        return path;
    }

    /// <summary>
    /// Warning when drawing jobs are planned and the active Inventor project differs from the snapshot's.
    /// Null if no drawing jobs, Inventor is not running, or the project is unknown on either side.
    /// </summary>
    private static string? ProjectWarning(Snapshot snapshot, List<ExportJob> jobs)
    {
        if (!jobs.Any(j => j.State == JobState.Planned && j.Output.Source == ExportSource.Drawing))
            return null;

        string? active;
        try { active = SnapshotCollector.GetActiveProjectFile(); }
        catch { return null; }
        return SnapshotChecks.ProjectMismatch(snapshot, active);
    }
}
