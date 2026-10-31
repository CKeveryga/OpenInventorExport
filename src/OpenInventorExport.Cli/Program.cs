using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using OpenInventorExport.Inventor;
using System.Reflection.Metadata;
using System.Text;

if (args.Length > 1 && args[0].Equals("totals", StringComparison.OrdinalIgnoreCase))
{
    foreach (string file in args.Skip(1))
    {
        var s = SnapshotJson.FromJson(File.ReadAllText(file))!;
        Console.WriteLine(Path.GetFileName(file));
        Console.WriteLine($"  Components: {s.Components.Count}");
        Console.WriteLine($"  Parts Only total: {BomTotals.PartsOnly(s)}");
        Console.WriteLine($"  Structured total: {BomTotals.Structured(s)}");
        foreach (var g in s.Skipped.GroupBy(k => k.Reason).OrderBy(g => g.Key))
            Console.WriteLine($"  Skipped {g.Key}: {g.Count()}");
    }
    return;
}

string outRoot = Path.GetFullPath(args.Length > 0 ? args[0] : "output");
bool planOnly = args.Length > 1 && args[1].Equals("plan", StringComparison.OrdinalIgnoreCase);
Directory.CreateDirectory(outRoot);

var snapshot = SnapshotCollector.CollectFromRunningInventor();
File.WriteAllText(Path.Combine(outRoot, "snapshot.json"), SnapshotJson.ToJson(snapshot));

string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "export";
if (mode == "props")
{
    // Hard-coded test schema until config exists. Use a property you actually care about.
    var schema = new PropertySchema
    {
        Properties = { new SchemaProperty { Set = PropertySetNames.UserDefined, Name = "Finish", DataType = PropertyDataType.Text, Required = true } }
    };
    var issues = PropertySchemaValidator.Validate(snapshot, schema);
    foreach (var i in issues)
        Console.WriteLine($"[{i.Kind}] {Path.GetFileName(i.FullPath)} {i.Set} / {i.Property} {i.Detail}");
    foreach (var g in issues.GroupBy(i => i.Kind))
        Console.WriteLine($"{g.Key}: {g.Count()}");
    return;
}

// Hard-coded sample task until the config file format exists (milestone 3).
var task = new ExportTask
{
    Name = "Drawings",
    FolderTemplate = "{task}/{format}",
    Selection = { DocumentTypes = { "Part", "SheetMetal" } },
    Outputs =
    {
        new OutputSpec { Format = ExportFormat.Pdf,  Source = ExportSource.Drawing, PdfSheets = PdfSheetMode.AllInOne },
        new OutputSpec { Format = ExportFormat.Dwg,  Source = ExportSource.Drawing },
        new OutputSpec { Format = ExportFormat.Dxf,  Source = ExportSource.Drawing }
    }
};

var jobs = ExportPlanner.Plan(snapshot, task, outRoot);
Console.WriteLine($"Planned: {jobs.Count(j => j.State == JobState.Planned)}, " +
                  $"Conflicts: {jobs.Count(j => j.State == JobState.Conflict)}");

if (planOnly)
{
    foreach (var j in jobs)
        Console.WriteLine($"[{j.State}] {Path.GetFileName(j.ModelPath)} -> {j.OutputPath} {j.Detail}");
    return;
}

var results = JobRunner.Run(jobs, r =>
    Console.WriteLine($"[{r.Status}] {r.Format} {Path.GetFileName(r.ModelPath)} -> {r.OutputPath} {r.Detail}"));

File.WriteAllText(Path.Combine(outRoot, "export-log.csv"), ExportResultLog.ToCsv(results), new UTF8Encoding(true));
File.WriteAllText(Path.Combine(outRoot, "export-log.json"), ExportResultLog.ToJson(results));

foreach (var line in ExportResultLog.Summary(results))
    Console.WriteLine(line);
Console.WriteLine($"Log written to: {outRoot}");