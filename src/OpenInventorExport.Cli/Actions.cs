using System.Text;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using OpenInventorExport.Inventor;
/// <summary>
/// Every command as the console shows it. Both CLI front ends (interactive menu and one-shot
/// arguments) call these, so behavior is identical either way. The work itself (collect, plan,
/// export) is in ExportWorkflow, which the UI window uses too; this class only prints.
/// </summary>
internal static class Actions
{
    public const string DefaultConfigName = "export.config.json";

    public static Snapshot Collect(string outRoot)
    {
        var c = ExportWorkflow.Collect(outRoot);
        Console.WriteLine($"Snapshot saved: {c.SnapshotPath}");
        Console.WriteLine($"Collected {c.Snapshot.Components.Count} component(s) in {FormatDuration(c.Elapsed)} ({c.ComponentsPerSecond:F1} per second). " +
                          $"Skipped records: {c.Snapshot.Skipped.Count}.");
        return c.Snapshot;
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F1} s";

    private static string Timing(ExportResult r) => r.Seconds == null ? "" : $" ({r.Seconds.Value:F1} s)";

    public static Snapshot Load(string file) => ExportWorkflow.LoadSnapshot(file);

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
    public static List<ExportTask> SelectTasks(ExportConfig config, string? only) => ExportWorkflow.SelectTasks(config, only);

    public static void Plan(Snapshot snapshot, ExportConfig config, string outRoot, string? only = null)
    {
        var plan = ExportWorkflow.Plan(snapshot, config, outRoot, only);
        PrintPlanCounts(plan);
        PrintIncompleteWarning(plan);
        PrintProjectWarning(plan);
        PrintContentCenterNote(plan.ContentCenterNotRecorded);
        PrintExclusions(plan.Exclusions);
        foreach (var j in plan.Jobs)
            Console.WriteLine($"[{j.State}] {j.TaskName}: {Path.GetFileName(j.ModelPath)} -> {j.OutputPath} {j.Detail}");
        foreach (var t in plan.Tables)
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
        ExportWorkflow.SelectTasks(config, only);           // a wrong name fails before anything else happens
        ExportWorkflow.ThrowIfBlocked(snapshot, config);    // Block: refuse before planning anything

        var plan = ExportWorkflow.Plan(snapshot, config, outRoot, only);
        PrintIncompleteBanner(plan.Unreadable);             // Warn: carry on, and log every unreadable component
        PrintPlanCounts(plan);
        PrintProjectWarning(plan);
        PrintContentCenterNote(plan.ContentCenterNotRecorded);
        PrintExclusions(plan.Exclusions);

        if (confirm != null && !confirm(plan.PlannedCount))
        {
            Console.WriteLine("Cancelled. Nothing was exported.");
            return 0;
        }

        var run = ExportWorkflow.Run(plan, snapshot, config, outRoot, PrintResult);

        PrintTaskFailures(ExportWorkflow.TaskOutcomes(plan.Tasks, run.Results));

        foreach (var line in ExportResultLog.TimingSummary(run.Results))
            Console.WriteLine("Timing: " + line);
        Console.WriteLine($"Timing: all file jobs took {FormatDuration(run.FileJobsTime)}.");

        foreach (var line in ExportResultLog.Summary(run.Results))
            Console.WriteLine(line);
        if (run.ExclusionReportPath != null)
            Console.WriteLine($"Exclusion report: {run.ExclusionReportPath}");
        Console.WriteLine($"Log written to: {outRoot}");
        PrintIncompleteBanner(plan.Unreadable);             // again at the end, where it is seen after a long run
        return plan.Unreadable.Count > 0 ? ExitIncomplete : 0;
    }

    /// <summary>One console line per result, as it happens: file jobs, unreadable components, tables.</summary>
    private static void PrintResult(ExportResult r)
    {
        if (r.Status == ExportStatus.Unreadable)
            Console.WriteLine($"[{r.Status}] {r.ModelPath} {r.Detail}");
        else if (r.Format is ExportFormat.Csv or ExportFormat.Xlsx)
            Console.WriteLine($"[{r.Status}] {r.TaskName}: table {r.OutputPath} {r.Detail}");
        else
            Console.WriteLine($"[{r.Status}] {r.TaskName}: {r.Format} {Path.GetFileName(r.ModelPath)} -> {r.OutputPath} {r.Detail}{Timing(r)}");
    }

    /// <summary>For each task with anything not exported: a banner and the first 10 problems. The full list is in the export log.</summary>
    private static void PrintTaskFailures(List<TaskOutcome> outcomes)
    {
        foreach (var o in outcomes)
        {
            if (o.Complete)
            {
                Console.WriteLine($"Task '{o.TaskName}': complete.");
                continue;
            }
            Console.WriteLine($"!! TASK '{o.TaskName}' IS INCOMPLETE: {o.NotExported.Count} output(s) not exported (see export-log.csv):");
            foreach (var r in o.NotExported.Take(10))
                Console.WriteLine($"   [{r.Status}] {r.Format} {Path.GetFileName(r.ModelPath)} {r.Detail}");
            if (o.NotExported.Count > 10)
                Console.WriteLine($"   ...and {o.NotExported.Count - 10} more.");
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
        PrintContentCenterNote(config.Rollup.ContentCenter != ContentCenterMode.Include ? ContentCenterFilter.NotRecorded(snapshot) : 0);
        PrintExclusions(exclusions);
        string? report = ExportWorkflow.WriteExclusionReport(exclusions, Path.Combine(outRoot, $"bom-{mode}-excluded.csv"));
        if (report != null)
            Console.WriteLine($"Exclusion report: {report}");
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

    private static void PrintPlanCounts(PlanResult plan) =>
        Console.WriteLine($"Planned: {plan.PlannedCount}, Conflicts: {plan.ConflictCount}");

    private static void PrintIncompleteWarning(PlanResult plan)
    {
        int n = plan.Unreadable.Count;
        if (n == 0) return;
        Console.WriteLine(plan.Policy == UnresolvedPolicy.Block
            ? $"WARNING: {n} component(s) could not be read. 'Run exports' will refuse until they are resolved (Unresolved = Block)."
            : $"WARNING: {n} component(s) could not be read. 'Run exports' will go ahead and log each one (Unresolved = Warn).");
    }

    private static void PrintIncompleteBanner(List<SkippedRecord> unreadable)
    {
        if (unreadable.Count == 0) return;
        Console.WriteLine("!! THE COMPONENT LIST IS INCOMPLETE !!");
        Console.WriteLine($"   {unreadable.Count} component(s) could not be read. An unresolved assembly's children cannot be seen at all, so more may be missing.");
    }

    private static void PrintContentCenterNote(int notRecorded)
    {
        if (notRecorded > 0)
            Console.WriteLine($"NOTE: a Content Center rule is active, but {notRecorded} part(s) have no Content Center flag " +
                              "(snapshot collected before v0.4.1?). They are treated as NOT Content Center. Collect again to be sure.");
    }

    private static void PrintProjectWarning(PlanResult plan)
    {
        if (plan.ProjectWarning != null)
            Console.WriteLine(plan.ProjectWarning);
    }
}
