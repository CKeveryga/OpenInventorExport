using OpenInventorExport.Core;
using OpenInventorExport.Inventor;

string outputPath = Path.GetFullPath(args.Length > 0 ? args[0] : "snapshot.json");

var snapshot = SnapshotCollector.CollectFromRunningInventor();

File.WriteAllText(outputPath, SnapshotJson.ToJson(snapshot));

//Console.WriteLine($"Components: {snapshot.Components.Count}");
//Console.WriteLine($"Skipped:    {snapshot.Skipped.Count}");
//Console.WriteLine($"Written to: {outputPath}");

//foreach (var line in SnapshotSummary.Describe(snapshot))
//    Console.WriteLine(line);


var flat = Rollup.Apply(snapshot, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });
Console.WriteLine($"Flat parts-only qty: {flat.Sum(r => r.Quantity)}   (Inventor Parts Only: 482)");

var structuredRows = Rollup.Apply(snapshot, new RollupOptions { Mode = RollupMode.Structured });
Console.WriteLine($"Structured qty:      {structuredRows.Sum(r => r.Quantity)}   (Inventor Structured: 424)");

string csvPath = Path.ChangeExtension(outputPath, ".csv");
File.WriteAllText(
    csvPath,
    CsvTableWriter.Write(structuredRows, new[] { "Part Number", "Description", "Material" }),
    new System.Text.UTF8Encoding(true));
Console.WriteLine($"CSV written to: {csvPath}");