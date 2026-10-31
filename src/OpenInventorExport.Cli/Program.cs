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

    public static void Plan(Snapshot snapshot, string outRoot)
    {
        var jobs = ExportPlanner.Plan(snapshot, SampleTask(), outRoot);
        PrintPlanCounts(jobs);
        foreach (var j in jobs)
            Console.WriteLine($"[{j.State}] {Path.GetFileName(j.ModelPath)} -> {j.OutputPath} {j.Detail}");
    }

    /// <param name="confirm">Optional last check, given the number of planned jobs. Return false to cancel.</param>
    public static void Export(Snapshot snapshot, string outRoot, Func<int, bool>? confirm = null)
    {
        var jobs = ExportPlanner.Plan(snapshot, SampleTask(), outRoot);
        PrintPlanCounts(jobs);

        int planned = jobs.Count(j => j.State == JobState.Planned);
        if (confirm != null && !confirm(planned))
        {
            Console.WriteLine("Cancelled. Nothing was exported.");
            return;
        }

        Directory.CreateDirectory(outRoot);
        var results = JobRunner.Run(jobs, r =>
            Console.WriteLine($"[{r.Status}] {r.Format} {Path.GetFileName(r.ModelPath)} -> {r.OutputPath} {r.Detail}"));

        // UTF-8 with BOM so Excel opens the CSV correctly.
        File.WriteAllText(Path.Combine(outRoot, "export-log.csv"), ExportResultLog.ToCsv(results), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outRoot, "export-log.json"), ExportResultLog.ToJson(results));

        foreach (var line in ExportResultLog.Summary(results))
            Console.WriteLine(line);
        Console.WriteLine($"Log written to: {outRoot}");
    }

    public static void Props(Snapshot snapshot)
    {
        // Hard-coded test schema until the config format exists.
        var schema = new PropertySchema
        {
            Properties =
            {
                new SchemaProperty
                {
                    Set = PropertySetNames.UserDefined,
                    Name = "Finish",
                    DataType = PropertyDataType.Text,
                    Required = true
                }
            }
        };

        var issues = PropertySchemaValidator.Validate(snapshot, schema);
        foreach (var i in issues)
            Console.WriteLine($"[{i.Kind}] {Path.GetFileName(i.FullPath)} {i.Set} / {i.Property} {i.Detail}");
        foreach (var g in issues.GroupBy(i => i.Kind))
            Console.WriteLine($"{g.Key}: {g.Count()}");
        if (issues.Count == 0)
            Console.WriteLine("No property issues.");
    }

    public static void Bom(Snapshot snapshot, string outRoot)
    {
        var rows = Rollup.Apply(snapshot, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });
        var columns = new (string Set, string Name)[]
        {
            (PropertySetNames.DesignTracking, "Part Number"),
            (PropertySetNames.DesignTracking, "Description"),
            (PropertySetNames.DesignTracking, "Material")
        };

        Directory.CreateDirectory(outRoot);
        string path = Path.Combine(outRoot, "bom-flat.csv");
        File.WriteAllText(path, CsvTableWriter.Write(rows, columns), new UTF8Encoding(true));
        Console.WriteLine($"Rows: {rows.Count}, total quantity: {rows.Sum(r => r.Quantity)}");
        Console.WriteLine($"Written: {path}");
    }

    /// <summary>Hard-coded sample task until the config file format exists (roadmap #3).</summary>
    private static ExportTask SampleTask()
    {
        // One-sheet ini files (ALL SHEETS=No), saved from Inventor's DWG/DXF dialogs.
        string? dwgIni = FindConfigFile("dwg-one-sheet.ini");
        string? dxfIni = FindConfigFile("dxf-one-sheet.ini");
        if (dwgIni == null || dxfIni == null)
            Console.WriteLine("Warning: config/dwg-one-sheet.ini or config/dxf-one-sheet.ini not found; " +
                              "DWG/DXF will use the translator's last-used sheet setting.");

        return new ExportTask
        {
            Name = "Drawings",
            FolderTemplate = "{task}/{format}",
            Selection = { DocumentTypes = { "Part", "SheetMetal" } },
            Outputs =
            {
                new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Drawing, PdfSheets = PdfSheetMode.AllInOne },
                new OutputSpec { Format = ExportFormat.Dwg, Source = ExportSource.Drawing, IniFile = dwgIni },
                new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing, IniFile = dxfIni }
            }
        };
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
}

/// <summary>Interactive menu: nothing to type on the command line, choices are made while it runs.</summary>
internal static class Interactive
{
    private static string _outRoot = Path.GetFullPath("output");
    private static Snapshot? _snapshot;
    private static string _snapshotSource = "none loaded";

    public static int Run()
    {
        Console.WriteLine("OpenInventorExport (read-only: never writes to Inventor files)");
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
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine(" 1  Collect snapshot from running Inventor");
        Console.WriteLine(" 2  Load a saved snapshot");
        Console.WriteLine(" 3  Show snapshot summary");
        Console.WriteLine(" 4  Plan exports (dry run, exports nothing)");
        Console.WriteLine(" 5  Run exports");
        Console.WriteLine(" 6  Check properties");
        Console.WriteLine(" 7  Write BOM CSV (parts only)");
        Console.WriteLine(" 8  Totals for saved snapshots");
        Console.WriteLine(" 9  Change output folder");
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
            case "4": { var s = EnsureSnapshot(); if (s != null) Actions.Plan(s, _outRoot); return true; }
            case "5":
                {
                    var s = EnsureSnapshot();
                    if (s != null)
                        Actions.Export(s, _outRoot, n =>
                            Confirm($"Run {n} export jobs now? Inventor must be running. Files go to {_outRoot}"));
                    return true;
                }
            case "6": { var s = EnsureSnapshot(); if (s != null) Actions.Props(s); return true; }
            case "7": { var s = EnsureSnapshot(); if (s != null) Actions.Bom(s, _outRoot); return true; }
            case "8": Totals(); return true;
            case "9": ChangeOutputFolder(); return true;
            default: return false;
        }
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
          props    [outDir]           Check properties against the test schema. Report only.
          bom      [outDir]           Write bom-flat.csv (parts only).

        Options (plan, export, props, bom):
          --from <snapshot.json>      Use a saved snapshot instead of collecting from Inventor.

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
        if (from != null && command == "snapshot")
            throw new ArgumentException("'snapshot' collects from Inventor, so --from makes no sense here.");

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
            case "plan": Actions.Plan(snapshot, outRoot); break;
            case "export": Actions.Export(snapshot, outRoot); break;
            case "props": Actions.Props(snapshot); break;
            case "bom": Actions.Bom(snapshot, outRoot); break;
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