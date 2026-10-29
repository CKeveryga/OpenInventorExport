using OpenInventorExport.Core;
using OpenInventorExport.Inventor;

string outputPath = Path.GetFullPath(args.Length > 0 ? args[0] : "snapshot.json");

var snapshot = SnapshotCollector.CollectFromRunningInventor();

File.WriteAllText(outputPath, SnapshotJson.ToJson(snapshot));

Console.WriteLine($"Components: {snapshot.Components.Count}");
Console.WriteLine($"Skipped:    {snapshot.Skipped.Count}");
Console.WriteLine($"Written to: {outputPath}");

foreach (var line in SnapshotSummary.Describe(snapshot))
    Console.WriteLine(line);


//var flat = Rollup.Apply(snapshot, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });
//Console.WriteLine($"Flat parts-only qty: {flat.Sum(r => r.Quantity)}   (Inventor Parts Only: 482)");

var structured = Rollup.Apply(snapshot, new RollupOptions { Mode = RollupMode.Structured });
//Console.WriteLine($"Structured qty:      {structuredRows.Sum(r => r.Quantity)}   (Inventor Structured: 424)");

foreach (bool excludeSuppressed in new[] { true, false })
{
    var flat = Rollup.Apply(snapshot, new RollupOptions
    { Mode = RollupMode.Flat, IncludeAssemblies = false, ExcludeSuppressed = excludeSuppressed });
    structured = Rollup.Apply(snapshot, new RollupOptions
    { Mode = RollupMode.Structured, ExcludeSuppressed = excludeSuppressed });
    Console.WriteLine($"ExcludeSuppressed={excludeSuppressed}: flat {flat.Sum(r => r.Quantity)} (Inv 444), structured {structured.Sum(r => r.Quantity)} (Inv 420)");
}

string csvPath = Path.ChangeExtension(outputPath, ".csv");
File.WriteAllText(
    csvPath,
    CsvTableWriter.Write(structured, new[] { "Part Number", "Description", "Mass" }),
    new System.Text.UTF8Encoding(true));
Console.WriteLine($"CSV written to: {csvPath}");

foreach (var s in snapshot.Skipped)
    Console.WriteLine($"SKIPPED [{s.Reason}] {s.OccurrencePath} :: {s.Detail}");

var invBom = SnapshotCollector.ReadPartsOnlyBom();
var ours = Rollup.Apply(snapshot, new RollupOptions
{ Mode = RollupMode.Flat, IncludeAssemblies = false, ExcludeSuppressed = true })
    .GroupBy(r => (r.Component.FullPath ?? r.Component.FileName).ToLowerInvariant())
    .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

Console.WriteLine("--- Differences vs Inventor Parts Only BOM ---");
foreach (var key in invBom.Keys.Union(ours.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
{
    invBom.TryGetValue(key, out int inv);
    ours.TryGetValue(key.ToLowerInvariant(), out int mine);
    if (inv != mine)
        Console.WriteLine($"{Path.GetFileName(key)}: Inventor {inv}, ours {mine}");
}
Console.WriteLine("--- end ---");