using System.Diagnostics;
using System.Text;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using OpenInventorExport.Inventor;
/// <summary>
/// The actual work behind every command. Both front ends (interactive menu and
/// one-shot arguments) call these, so behavior is identical either way.
/// </summary>
internal static class Actions
{
    public const string DefaultConfigName = "export.config.json";

    public static Snapshot Collect(string outRoot)
    {
        Directory.CreateDirectory(outRoot);

        var clock = Stopwatch.StartNew();
        var snapshot = SnapshotCollector.CollectFromRunningInventor();
        TimeSpan elapsed = clock.Elapsed;

        string path = Path.Combine(outRoot, "snapshot.json");
        File.WriteAllText(path, SnapshotJson.ToJson(snapshot));
        Console.WriteLine($"Snapshot saved: {path}");

        int n = snapshot.Components.Count;
        double perSecond = n / Math.Max(elapsed.TotalSeconds, 0.001);
        Console.WriteLine($"Collected {n} component(s) in {FormatDuration(elapsed)} ({perSecond:F1} per second). " +
                          $"Skipped records: {snapshot.Skipped.Count}.");
        return snapshot;
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F1} s";

    private static string Timing(ExportResult r) => r.Seconds == null ? "" : $" ({r.Seconds.Value:F1} s)";

    public static Snapshot Load(string file) =>
        SnapshotJson.FromJson(File.ReadAllText(file))
        ?? throw new InvalidDataException($"Could not read a snapshot from {file}.");

    /// <summary>
    /// Reads and validates a config file. Throws InvalidDataException listing every problem.
    /// The returned config holds resolved absolute paths: never write it back to disk.
    /// </summary>
    public static ExportConfig LoadConfig(string path) => ExportConfigFile.Load(path);

    /// <summary>config/export.config.json, found by walking up from the exe's folder. Null if absent.</summary>
    public static string? FindDefaultConfig() => FindConfigFile(DefaultConfigName);

    public static void Summary(Snapshot snapshot)
    {
        foreach (var line in SnapshotSummary.Describe(snapshot)) Console.WriteLine(line);
        foreach (var g in snapshot.Skipped.GroupBy(k => k.Reason).OrderBy(g => g.Key))
            Console.WriteLine($"Skipped {g.Key}: {g.Count()}");
    }

    public static void Totals(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            var s = Load(file);
            Console.WriteLine(Path.GetFileName(file));
            Console.WriteLine($"  Components: {s.Components.Count}");
            Console.WriteLine($"  Parts Only total: {BomTotals.PartsOnly(s)}");
            Console.WriteLine($"  Structured total: {BomTotals.Structured(s)}");
            foreach (var g in s.Skipped.GroupBy(k => k.Reason).OrderBy(g => g.Key))
                Console.WriteLine($"  Skipped {g.Key}: {g.Count()}");
        }
    }

    /// <summary>The task named by --only (ignoring case), or all tasks if none is named. Throws if the name is unknown.</summary>
    public static List<ExportTask> SelectTasks(ExportConfig config, string? only)
    {
        if (string.IsNullOrWhiteSpace(only)) return config.Tasks;
        var match = config.Tasks.Where(t => t.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 0)
            throw new ArgumentException(
                $"No task named '{only}'. Tasks in the config: {string.Join(", ", config.Tasks.Select(t => t.Name))}.");
        return match;
    }

    public static void Plan(Snapshot snapshot, ExportConfig config, string outRoot, string? only = null)
    {
        var tasks = SelectTasks(config, only);
        var exclusions = new List<ExclusionRecord>();
        var jobs = ExportPlanner.PlanAll(snapshot, tasks, outRoot, exclusions);
        var tables = ExportPlanner.PlanTables(tasks, outRoot);
        PrintPlanCounts(jobs);
        PrintIncompleteWarning(snapshot, config);
        PrintProjectWarning(snapshot, jobs);
        PrintContentCenterNote(snapshot, tasks.Any(t => t.Selection.ContentCenter != ContentCenterMode.Include));
        PrintExclusions(exclusions);
        foreach (var j in jobs)
            Console.WriteLine($"[{j.State}] {j.TaskName}: {Path.GetFileName(j.ModelPath)} -> {j.OutputPath} {j.Detail}");
        foreach (var t in tables)
            Console.WriteLine($"[{t.State}] {t.TaskName}: table -> {t.OutputPath} {t.Detail}");
    }

    /// <summary>Exit code for a run that went ahead although components could not be read (Unresolved = Warn).</summary>
    public const int ExitIncomplete = 2;

    /// <param name="confirm">Optional last check, given the number of planned jobs. Return false to cancel.</param>
    /// <param name="only">Optional task name: run just that task.</param>
    /// <returns>0 for a complete run, ExitIncomplete if components were unreadable.</returns>
    public static int Export(Snapshot snapshot, ExportConfig config, string outRoot,
        Func<int, bool>? confirm = null, string? only = null)
    {
        var tasks = SelectTasks(config, only);   // a wrong name fails before anything else happens

        // Block: refuse before planning anything. Warn: carry on, and log every unreadable component.
        if (config.Unresolved == UnresolvedPolicy.Block)
            ThrowIfIncomplete(snapshot);
        var unreadable = SnapshotChecks.BlockingProblems(snapshot);
        PrintIncompleteBanner(unreadable);

        var exclusions = new List<ExclusionRecord>();
        var jobs = ExportPlanner.PlanAll(snapshot, tasks, outRoot, exclusions);
        var tableJobs = ExportPlanner.PlanTables(tasks, outRoot);
        PrintPlanCounts(jobs);
        PrintProjectWarning(snapshot, jobs);
        PrintContentCenterNote(snapshot, tasks.Any(t => t.Selection.ContentCenter != ContentCenterMode.Include));
        PrintExclusions(exclusions);

        int planned = jobs.Count(j => j.State == JobState.Planned);
        if (confirm != null && !confirm(planned))
        {
            Console.WriteLine("Cancelled. Nothing was exported.");
            return 0;
        }

        Directory.CreateDirectory(outRoot);
        WriteExclusions(exclusions, Path.Combine(outRoot, "export-excluded.csv"));
        var runClock = Stopwatch.StartNew();
        var results = JobRunner.Run(jobs, r =>
            Console.WriteLine($"[{r.Status}] {r.TaskName}: {r.Format} {Path.GetFileName(r.ModelPath)} -> {r.OutputPath} {r.Detail}{Timing(r)}"));
        TimeSpan runTime = runClock.Elapsed;

        foreach (var k in unreadable)
        {
            var r = ExportResult.ForUnreadable(k);
            results.Add(r);
            Console.WriteLine($"[{r.Status}] {r.ModelPath} {r.Detail}");
        }

        WriteTables(snapshot, config, tasks, tableJobs, results);

        // UTF-8 with BOM so Excel opens the CSV correctly.
        File.WriteAllText(Path.Combine(outRoot, "export-log.csv"), ExportResultLog.ToCsv(results), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outRoot, "export-log.json"), ExportResultLog.ToJson(results));

        PrintTaskFailures(tasks, results);

        foreach (var line in ExportResultLog.TimingSummary(results))
            Console.WriteLine("Timing: " + line);
        Console.WriteLine($"Timing: all file jobs took {FormatDuration(runTime)}.");

        foreach (var line in ExportResultLog.Summary(results))
            Console.WriteLine(line);
        Console.WriteLine($"Log written to: {outRoot}");
        PrintIncompleteBanner(unreadable);   // again at the end, where it is seen after a long run
        return unreadable.Count > 0 ? ExitIncomplete : 0;
    }

    /// <summary>
    /// Writes each planned table. Every table lists all the files its task selects, whatever was exported,
    /// and gets its own line in the export log (ModelPath "(table)").
    /// </summary>
    private static void WriteTables(Snapshot snapshot, ExportConfig config, IEnumerable<ExportTask> tasks,
        List<TableJob> tableJobs, List<ExportResult> results)
    {
        foreach (var job in tableJobs)
        {
            var result = new ExportResult
            {
                TaskName = job.TaskName,
                Format = job.Output.Format,
                ModelPath = "(table)",
                OutputPath = job.OutputPath
            };
            results.Add(result);

            if (job.State == JobState.Conflict)
            {
                result.Status = ExportStatus.Conflict;
                result.Detail = job.Detail;
                Console.WriteLine($"[{result.Status}] {job.TaskName}: table {job.OutputPath} {job.Detail}");
                continue;
            }

            try
            {
                var task = tasks.First(t => t.Name == job.TaskName);
                var rows = TaskTable.Rows(snapshot, task, job.Output, config);
                var columns = TaskTable.Columns(job.Output, config);

                Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
                if (job.Output.Format == ExportFormat.Xlsx)
                    XlsxTableWriter.Write(job.OutputPath, rows, columns);
                else
                    File.WriteAllText(job.OutputPath, CsvTableWriter.Write(rows, columns), new UTF8Encoding(true));

                result.Status = ExportStatus.Exported;
                result.Detail = $"{rows.Count} row(s)";
            }
            catch (Exception ex)
            {
                result.Status = ExportStatus.Failed;
                result.Detail = ex.Message;
            }
            Console.WriteLine($"[{result.Status}] {job.TaskName}: table {job.OutputPath} {result.Detail}");
        }
    }

    /// <summary>For each task with anything not exported: a banner and the first 10 problems. The full list is in the export log.</summary>
    private static void PrintTaskFailures(IEnumerable<ExportTask> tasks, List<ExportResult> results)
    {
        foreach (var task in tasks)
        {
            var failed = results.Where(r => r.TaskName == task.Name && r.Status != ExportStatus.Exported).ToList();
            if (failed.Count == 0)
            {
                Console.WriteLine($"Task '{task.Name}': complete.");
                continue;
            }
            Console.WriteLine($"!! TASK '{task.Name}' IS INCOMPLETE: {failed.Count} output(s) not exported (see export-log.csv):");
            foreach (var r in failed.Take(10))
                Console.WriteLine($"   [{r.Status}] {r.Format} {Path.GetFileName(r.ModelPath)} {r.Detail}");
            if (failed.Count > 10)
                Console.WriteLine($"   ...and {failed.Count - 10} more.");
        }
    }

    public static void Props(Snapshot snapshot, ExportConfig config)
    {
        if (config.Properties.Count == 0)
        {
            Console.WriteLine("The config has no Properties section, so there is nothing to check.");
            return;
        }

        var issues = PropertySchemaValidator.Validate(snapshot, config.ToPropertySchema());
        foreach (var i in issues)
            Console.WriteLine($"[{i.Kind}] {Path.GetFileName(i.FullPath)} {i.Set} / {i.Property} {i.Detail}");
        foreach (var g in issues.GroupBy(i => i.Kind))
            Console.WriteLine($"{g.Key}: {g.Count()}");
        if (issues.Count == 0)
            Console.WriteLine("No property issues.");
    }

    public static void Bom(Snapshot snapshot, ExportConfig config, string outRoot)
    {
        var exclusions = new List<ExclusionRecord>();
        var rows = Rollup.Apply(snapshot, config.Rollup, exclusions);

        Directory.CreateDirectory(outRoot);
        string mode = config.Rollup.Mode.ToString().ToLowerInvariant();
        string path = Path.Combine(outRoot, $"bom-{mode}.csv");
        File.WriteAllText(path, CsvTableWriter.Write(rows, config.ToCsvColumns()), new UTF8Encoding(true));
        string xlsxPath = Path.ChangeExtension(path, ".xlsx");
        XlsxTableWriter.Write(xlsxPath, rows, config.ToCsvColumns(), exclusions);
        Console.WriteLine($"Written: {xlsxPath}");
        Console.WriteLine($"Rollup: {config.Rollup.Mode}, key: {config.Rollup.Key}");
        Console.WriteLine($"Rows: {rows.Count}, total quantity: {rows.Sum(r => r.Quantity)}");
        Console.WriteLine($"Written: {path}");

        PrintIncompleteBanner(SnapshotChecks.BlockingProblems(snapshot));
        PrintContentCenterNote(snapshot, config.Rollup.ContentCenter != ContentCenterMode.Include);
        PrintExclusions(exclusions);
        WriteExclusions(exclusions, Path.Combine(outRoot, $"bom-{mode}-excluded.csv"));
    }


    private static void PrintExclusions(List<ExclusionRecord> exclusions)
    {
        if (exclusions.Count == 0)
        {
            Console.WriteLine("Left out by rules: nothing.");
            return;
        }
        foreach (var line in ExclusionReport.Summary(exclusions))
            Console.WriteLine("Left out by rules: " + line);
    }

    /// <summary>Writes the exclusion report. With nothing excluded, an old report from an earlier run is removed so it cannot mislead.</summary>
    private static void WriteExclusions(List<ExclusionRecord> exclusions, string path)
    {
        if (exclusions.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        File.WriteAllText(path, ExclusionReport.ToCsv(exclusions), new UTF8Encoding(true));
        Console.WriteLine($"Exclusion report: {path}");
    }

    /// <summary>Looks for config/&lt;name&gt; in the exe's folder and every folder above it.</summary>
    private static string? FindConfigFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Snapshot fixtures in samples/snapshots, found by walking up from the exe's folder.</summary>
    public static string[] FindSavedSnapshots()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "snapshots");
            if (Directory.Exists(candidate))
                return Directory.GetFiles(candidate, "*.json").OrderBy(f => f).ToArray();
            dir = dir.Parent;
        }
        return Array.Empty<string>();
    }

    private static void PrintPlanCounts(List<ExportJob> jobs) =>
        Console.WriteLine($"Planned: {jobs.Count(j => j.State == JobState.Planned)}, " +
                          $"Conflicts: {jobs.Count(j => j.State == JobState.Conflict)}");

    /// <summary>Components that could not be read mean the component list is incomplete, so nothing is exported.</summary>
    private static void ThrowIfIncomplete(Snapshot snapshot)
    {
        var problems = SnapshotChecks.BlockingProblems(snapshot);
        if (problems.Count == 0) return;

        var lines = problems.Take(10).Select(k => $"  [{k.Reason}] {k.OccurrencePath} {k.Detail}");
        string more = problems.Count > 10 ? Environment.NewLine + $"  ...and {problems.Count - 10} more." : "";
        throw new InvalidOperationException(
            $"Export blocked: {problems.Count} component(s) could not be read, so the component list is incomplete." +
            Environment.NewLine + string.Join(Environment.NewLine, lines) + more +
            Environment.NewLine + "Resolve the files in Inventor, collect again, then export.");
    }

    private static void PrintIncompleteWarning(Snapshot snapshot, ExportConfig config)
    {
        int n = SnapshotChecks.BlockingProblems(snapshot).Count;
        if (n == 0) return;
        Console.WriteLine(config.Unresolved == UnresolvedPolicy.Block
            ? $"WARNING: {n} component(s) could not be read. 'Run exports' will refuse until they are resolved (Unresolved = Block)."
            : $"WARNING: {n} component(s) could not be read. 'Run exports' will go ahead and log each one (Unresolved = Warn).");
    }

    private static void PrintIncompleteBanner(List<SkippedRecord> unreadable)
    {
        if (unreadable.Count == 0) return;
        Console.WriteLine("!! THE COMPONENT LIST IS INCOMPLETE !!");
        Console.WriteLine($"   {unreadable.Count} component(s) could not be read. An unresolved assembly's children cannot be seen at all, so more may be missing.");
    }

    private static void PrintContentCenterNote(Snapshot snapshot, bool ruleActive)
    {
        if (!ruleActive) return;
        int n = ContentCenterFilter.NotRecorded(snapshot);
        if (n > 0)
            Console.WriteLine($"NOTE: a Content Center rule is active, but {n} part(s) have no Content Center flag " +
                              "(snapshot collected before v0.4.1?). They are treated as NOT Content Center. Collect again to be sure.");
    }

    /// <summary>
    /// Warns if drawing exports are planned and the active Inventor project differs from the snapshot's.
    /// Silent if Inventor is not running or the project is unknown on either side: nothing to compare.
    /// </summary>
    private static void PrintProjectWarning(Snapshot snapshot, List<ExportJob> jobs)
    {
        if (!jobs.Any(j => j.State == JobState.Planned && j.Output.Source == ExportSource.Drawing))
            return;

        string? active;
        try { active = SnapshotCollector.GetActiveProjectFile(); }
        catch { return; }

        var warning = SnapshotChecks.ProjectMismatch(snapshot, active);
        if (warning != null)
            Console.WriteLine(warning);
    }
}
