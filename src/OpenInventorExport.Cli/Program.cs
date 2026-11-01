using System.Diagnostics.CodeAnalysis;
using System.Text;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using OpenInventorExport.Inventor;

// No arguments: interactive menu. With arguments: one-shot command (for scripts).
return args.Length == 0 ? Interactive.Run() : Cli.Run(args);

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
        var snapshot = SnapshotCollector.CollectFromRunningInventor();
        string path = Path.Combine(outRoot, "snapshot.json");
        File.WriteAllText(path, SnapshotJson.ToJson(snapshot));
        Console.WriteLine($"Snapshot saved: {path}");
        return snapshot;
    }

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

    public static void Plan(Snapshot snapshot, ExportConfig config, string outRoot)
    {
        var exclusions = new List<ExclusionRecord>();
        var jobs = ExportPlanner.PlanAll(snapshot, config.Tasks, outRoot, exclusions);
        PrintPlanCounts(jobs);
        PrintIncompleteWarning(snapshot);
        PrintProjectWarning(snapshot, jobs);
        PrintExclusions(exclusions);
        foreach (var j in jobs)
            Console.WriteLine($"[{j.State}] {j.TaskName}: {Path.GetFileName(j.ModelPath)} -> {j.OutputPath} {j.Detail}");
    }

    /// <param name="confirm">Optional last check, given the number of planned jobs. Return false to cancel.</param>
    public static void Export(Snapshot snapshot, ExportConfig config, string outRoot, Func<int, bool>? confirm = null)
    {
        ThrowIfIncomplete(snapshot);
        var exclusions = new List<ExclusionRecord>();
        var jobs = ExportPlanner.PlanAll(snapshot, config.Tasks, outRoot, exclusions);
        PrintPlanCounts(jobs);
        PrintProjectWarning(snapshot, jobs);
        PrintExclusions(exclusions);

        int planned = jobs.Count(j => j.State == JobState.Planned);
        if (confirm != null && !confirm(planned))
        {
            Console.WriteLine("Cancelled. Nothing was exported.");
            return;
        }

        Directory.CreateDirectory(outRoot);
        WriteExclusions(exclusions, Path.Combine(outRoot, "export-excluded.csv"));
        var results = JobRunner.Run(jobs, r =>
            Console.WriteLine($"[{r.Status}] {r.TaskName}: {r.Format} {Path.GetFileName(r.ModelPath)} -> {r.OutputPath} {r.Detail}"));

        // UTF-8 with BOM so Excel opens the CSV correctly.
        File.WriteAllText(Path.Combine(outRoot, "export-log.csv"), ExportResultLog.ToCsv(results), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outRoot, "export-log.json"), ExportResultLog.ToJson(results));

        foreach (var line in ExportResultLog.Summary(results))
            Console.WriteLine(line);
        Console.WriteLine($"Log written to: {outRoot}");
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
        Console.WriteLine($"Rollup: {config.Rollup.Mode}, key: {config.Rollup.Key}");
        Console.WriteLine($"Rows: {rows.Count}, total quantity: {rows.Sum(r => r.Quantity)}");
        Console.WriteLine($"Written: {path}");

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

    private static void PrintIncompleteWarning(Snapshot snapshot)
    {
        int n = SnapshotChecks.BlockingProblems(snapshot).Count;
        if (n > 0)
            Console.WriteLine($"WARNING: {n} component(s) could not be read. 'Run exports' will refuse until they are resolved.");
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

/// <summary>Interactive menu: nothing to type on the command line, choices are made while it runs.</summary>
internal static class Interactive
{
    private static string _outRoot = @"C:\dev\OpenInventorExport\output";

    private static Snapshot? _snapshot;
    private static string _snapshotSource = "none loaded";

    private static ExportConfig? _config;
    private static string? _configPath;
    private static string _configSource = "none loaded";

    public static int Run()
    {
        Console.WriteLine("OpenInventorExport (read-only: never writes to Inventor files)");
        TryLoadDefaultConfig();
        while (true)
        {
            PrintMenu();
            string? choice = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (choice == null || choice is "q" or "quit" or "exit") return 0;

            Console.WriteLine();
            try
            {
                if (!Dispatch(choice))
                    Console.WriteLine("Not a menu option.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine($" Output folder: {_outRoot}");
        Console.WriteLine($" Snapshot:      {_snapshotSource}");
        Console.WriteLine($" Config:        {_configSource}");
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine(" 1  Collect snapshot from running Inventor");
        Console.WriteLine(" 2  Load a saved snapshot");
        Console.WriteLine(" 3  Show snapshot summary");
        Console.WriteLine(" 4  Plan exports (dry run, exports nothing)");
        Console.WriteLine(" 5  Run exports");
        Console.WriteLine(" 6  Check properties");
        Console.WriteLine(" 7  Write BOM CSV");
        Console.WriteLine(" 8  Totals for saved snapshots");
        Console.WriteLine(" 9  Change output folder");
        Console.WriteLine(" 10 Load (or reload) a config file");
        Console.WriteLine(" Q  Quit");
        Console.Write("Choose: ");
    }

    private static bool Dispatch(string choice)
    {
        switch (choice)
        {
            case "1": Collect(); return true;
            case "2": LoadSaved(); return true;
            case "3": { var s = EnsureSnapshot(); if (s != null) Actions.Summary(s); return true; }
            case "4":
                if (TryGetInputs(out var s4, out var c4)) Actions.Plan(s4, c4, _outRoot);
                return true;
            case "5":
                if (TryGetInputs(out var s5, out var c5))
                    Actions.Export(s5, c5, _outRoot, n =>
                        Confirm($"Run {n} export jobs now? Inventor must be running. Files go to {_outRoot}"));
                return true;
            case "6":
                if (TryGetInputs(out var s6, out var c6)) Actions.Props(s6, c6);
                return true;
            case "7":
                if (TryGetInputs(out var s7, out var c7)) Actions.Bom(s7, c7, _outRoot);
                return true;
            case "8": Totals(); return true;
            case "9": ChangeOutputFolder(); return true;
            case "10": LoadConfigMenu(); return true;
            default: return false;
        }
    }

    // ----------------------------------------------------------- config

    /// <summary>At startup: load config/export.config.json if it exists. A problem is shown, never fatal.</summary>
    private static void TryLoadDefaultConfig()
    {
        string? path = Actions.FindDefaultConfig();
        if (path == null)
        {
            Console.WriteLine($"No config/{Actions.DefaultConfigName} found. Use option 10 to load a config file.");
            return;
        }
        try { LoadConfigFrom(path); }
        catch (Exception ex) { Console.WriteLine("Config not loaded: " + ex.Message); }
    }

    /// <summary>Loads and validates; replaces the current config only if the new one is valid.</summary>
    private static void LoadConfigFrom(string path)
    {
        string full = Path.GetFullPath(path);
        var config = Actions.LoadConfig(full);
        _config = config;
        _configPath = full;
        _configSource = $"{full} ({config.Tasks.Count} task(s))";
    }

    private static void LoadConfigMenu()
    {
        string path = Ask("Config file", _configPath ?? Actions.FindDefaultConfig() ?? "");
        if (path.Length == 0) return;
        LoadConfigFrom(path);
        Console.WriteLine($"Config loaded: {_configSource}");
    }

    /// <summary>The loaded config, or asks for a file. Null if the user backs out.</summary>
    private static ExportConfig? EnsureConfig()
    {
        if (_config != null) return _config;
        Console.WriteLine("No config loaded yet.");
        LoadConfigMenu();
        return _config;
    }

    /// <summary>Config first (fail early on a bad file), then the snapshot.</summary>
    private static bool TryGetInputs(
        [NotNullWhen(true)] out Snapshot? snapshot,
        [NotNullWhen(true)] out ExportConfig? config)
    {
        snapshot = null;
        config = EnsureConfig();
        if (config == null) return false;
        snapshot = EnsureSnapshot();
        return snapshot != null;
    }

    // ----------------------------------------------------------- snapshot

    private static void Collect()
    {
        Console.WriteLine("Collecting from the active assembly in Inventor...");
        _snapshot = Actions.Collect(_outRoot);
        _snapshotSource = $"Inventor: {Path.GetFileName(_snapshot.SourceFile)} ({_snapshot.Components.Count} components, collected {DateTime.Now:HH:mm})";
    }

    private static void LoadSaved()
    {
        var files = PickSnapshots(multiple: false);
        if (files.Count == 0) return;
        _snapshot = Actions.Load(files[0]);
        _snapshotSource = $"file: {Path.GetFileName(files[0])} ({_snapshot.Components.Count} components)";
    }

    /// <summary>The loaded snapshot, or asks how to get one. Null if the user backs out.</summary>
    private static Snapshot? EnsureSnapshot()
    {
        if (_snapshot != null) return _snapshot;
        Console.WriteLine("No snapshot loaded yet.");
        string how = Ask("Collect from running Inventor (1) or load a saved file (2)?", "1");
        if (how == "2") LoadSaved(); else Collect();
        return _snapshot;
    }

    private static void Totals()
    {
        var files = PickSnapshots(multiple: true);
        if (files.Count > 0) Actions.Totals(files);
    }

    private static void ChangeOutputFolder()
    {
        string answer = Ask("Output folder", _outRoot);
        _outRoot = Path.GetFullPath(answer);
        Console.WriteLine($"Output folder is now {_outRoot}");
    }

    // ------------------------------------------------------------ prompts

    /// <summary>Lists snapshots in samples/snapshots to pick from, or accepts a pasted file path.</summary>
    private static List<string> PickSnapshots(bool multiple)
    {
        var found = Actions.FindSavedSnapshots();
        for (int i = 0; i < found.Length; i++)
            Console.WriteLine($" {i + 1}  {Path.GetFileName(found[i])}");

        string hint = found.Length == 0
            ? "File path"
            : multiple ? "Numbers (e.g. 1 3), 'all', or a file path" : "Number or file path";
        string input = Ask(hint);
        if (input.Length == 0) return new List<string>();

        if (input.Equals("all", StringComparison.OrdinalIgnoreCase) && multiple)
            return found.ToList();

        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (found.Length > 0 && tokens.All(t => int.TryParse(t, out _)))
        {
            var picked = new List<string>();
            foreach (var t in tokens)
            {
                int n = int.Parse(t);
                if (n < 1 || n > found.Length) throw new ArgumentException($"{n} is not in the list.");
                picked.Add(found[n - 1]);
            }
            return multiple ? picked : picked.Take(1).ToList();
        }

        if (!File.Exists(input)) throw new FileNotFoundException("File not found", input);
        return new List<string> { input };
    }

    /// <summary>Prompts and returns the answer, or the default if Enter is pressed. Pasted quotes are removed.</summary>
    private static string Ask(string prompt, string defaultValue = "")
    {
        Console.Write(defaultValue.Length > 0 ? $"{prompt} [{defaultValue}]: " : $"{prompt}: ");
        string answer = (Console.ReadLine() ?? "").Trim().Trim('"');
        return answer.Length == 0 ? defaultValue : answer;
    }

    private static bool Confirm(string prompt) =>
        Ask(prompt + " (y/N)", "n").StartsWith("y", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One-shot commands for scripts: the first argument is the command.</summary>
internal static class Cli
{
    private const string Usage = """
        OpenInventorExport

        Run with no arguments for the interactive menu. Or use one command:

          totals <snapshot.json>...   Rollup totals from saved snapshots. No Inventor needed.
          snapshot [outDir]           Collect from running Inventor and save snapshot.json.
          plan     [outDir]           Collect, save snapshot, print the export plan. Exports nothing.
          export   [outDir]           Collect, save snapshot, run the plan, write the logs.
          props    [outDir]           Check properties against the config's Properties. Report only.
          bom      [outDir]           Write the BOM CSV (rollup and columns from the config).

        Options (plan, export, props, bom):
          --from <snapshot.json>      Use a saved snapshot instead of collecting from Inventor.
          --config <file.json>        Config file. Default: config/export.config.json.

        outDir defaults to ./output. Commands that collect need Inventor running
        with the assembly as the active document.
        """;

    public static int Run(string[] args)
    {
        if (args[0] is "help" or "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();

        try
        {
            switch (command)
            {
                case "totals":
                    if (rest.Count == 0) throw new ArgumentException("totals needs at least one snapshot file.");
                    Actions.Totals(rest);
                    return 0;

                case "snapshot":
                case "plan":
                case "export":
                case "props":
                case "bom":
                    RunWithSnapshot(command, rest);
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    Console.WriteLine(Usage);
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static void RunWithSnapshot(string command, List<string> rest)
    {
        string? from = TakeOption(rest, "--from");
        string? configPath = TakeOption(rest, "--config");
        if (from != null && command == "snapshot")
            throw new ArgumentException("'snapshot' collects from Inventor, so --from makes no sense here.");
        if (configPath != null && command == "snapshot")
            throw new ArgumentException("'snapshot' does not use a config, so --config makes no sense here.");

        // Load the config first: a bad config should fail before anything is collected from Inventor.
        ExportConfig? config = null;
        if (command != "snapshot")
        {
            string path = configPath
                ?? Actions.FindDefaultConfig()
                ?? throw new ArgumentException(
                    $"No config file. Pass --config <file> or create config/{Actions.DefaultConfigName}.");
            config = Actions.LoadConfig(path);
            Console.WriteLine($"Using config: {Path.GetFullPath(path)}");
        }

        string outRoot = Path.GetFullPath(rest.Count > 0 ? rest[0] : "output");
        Directory.CreateDirectory(outRoot);

        Snapshot snapshot;
        if (from != null)
        {
            snapshot = Actions.Load(from);
            Console.WriteLine($"Using saved snapshot: {from}");
        }
        else
        {
            snapshot = Actions.Collect(outRoot);
        }

        switch (command)
        {
            case "snapshot": Actions.Summary(snapshot); break;
            case "plan": Actions.Plan(snapshot, config!, outRoot); break;
            case "export": Actions.Export(snapshot, config!, outRoot); break;
            case "props": Actions.Props(snapshot, config!); break;
            case "bom": Actions.Bom(snapshot, config!, outRoot); break;
        }
    }

    /// <summary>Removes "--name value" from the list and returns the value, or null if absent.</summary>
    private static string? TakeOption(List<string> args, string name)
    {
        int i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        if (i + 1 >= args.Count) throw new ArgumentException($"{name} needs a value.");
        string value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }
}