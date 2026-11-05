using System.Diagnostics.CodeAnalysis;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
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
                if (TryGetInputs(out var s4, out var c4)) Actions.Plan(s4, c4, _outRoot, AskTask(c4));
                return true;
            case "5":
                if (TryGetInputs(out var s5, out var c5))
                    Actions.Export(s5, c5, _outRoot, n =>
                        Confirm($"Run {n} export jobs now? Inventor must be running. Files go to {_outRoot}"),
                        AskTask(c5));
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

    /// <summary>Which task to run. Null = all. Not asked when the config has only one task.</summary>
    private static string? AskTask(ExportConfig config)
    {
        if (config.Tasks.Count < 2) return null;
        Console.WriteLine("Tasks: " + string.Join(", ", config.Tasks.Select(t => t.Name)));
        string answer = Ask("Task name (Enter = all)");
        return answer.Length == 0 ? null : answer;
    }
}
