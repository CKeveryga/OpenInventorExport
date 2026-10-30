using System.Text;
using OpenInventorExport.Core;
using OpenInventorExport.Inventor;

string outRoot = Path.GetFullPath(args.Length > 0 ? args[0] : "output");
bool planOnly = args.Length > 1 && args[1].Equals("plan", StringComparison.OrdinalIgnoreCase);
Directory.CreateDirectory(outRoot);

var snapshot = SnapshotCollector.CollectFromRunningInventor();
File.WriteAllText(Path.Combine(outRoot, "snapshot.json"), SnapshotJson.ToJson(snapshot));

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