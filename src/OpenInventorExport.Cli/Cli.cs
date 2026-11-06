using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
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
          --only <task>               (plan/export) run just that task.

        outDir defaults to ./output. Commands that collect need Inventor running
        with the assembly as the active document.
        
        Exit codes: 0 = done, 1 = error, 2 = export ran but some components could not be read
        (Unresolved = Warn), so the component list is incomplete.
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
                    return RunWithSnapshot(command, rest);

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

    private static int RunWithSnapshot(string command, List<string> rest)
    {
        string? from = TakeOption(rest, "--from");
        string? configPath = TakeOption(rest, "--config");
        string? only = TakeOption(rest, "--only");
        if (only != null && command is not ("plan" or "export"))
            throw new ArgumentException("--only works with 'plan' and 'export' only.");
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
            if (only != null) Actions.SelectTasks(config, only);
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
            case "plan": Actions.Plan(snapshot, config!, outRoot, only); break;
            case "export": return Actions.Export(snapshot, config!, outRoot, null, only);
            case "props": Actions.Props(snapshot, config!); break;
            case "bom": Actions.Bom(snapshot, config!, outRoot); break;
        }
        return 0;
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