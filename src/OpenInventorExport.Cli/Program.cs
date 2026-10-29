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

var flat = Rollup.Apply(snapshot, new RollupOptions
{ Mode = RollupMode.Flat, IncludeAssemblies = false });
var structured = Rollup.Apply(snapshot, new RollupOptions
{ Mode = RollupMode.Structured, Inseparable = InseparableHandling.IncludeChildren });

var invBom = SnapshotCollector.ReadPartsOnlyBom();

Console.WriteLine($"Flat parts-only qty: ours {flat.Sum(r => r.Quantity)}, Inventor {invBom.Values.Sum()}");
//Console.WriteLine($"Structured qty:      ours {structured.Sum(r => r.Quantity)}   (compare to Inventor's Structured BOM by eye)");

foreach (var insep in new[] { InseparableHandling.AsSingleItem, InseparableHandling.IncludeChildren })
    foreach (bool exclPhantom in new[] { true, false })
    {
        var st = Rollup.Apply(snapshot, new RollupOptions
        {
            Mode = RollupMode.Structured,
            Inseparable = insep,
            ExcludePhantom = exclPhantom
        });
        Console.WriteLine($"Structured, Inseparable={insep}, ExcludePhantom={exclPhantom}: {st.Sum(r => r.Quantity)} (Inv 420)");
    }

foreach (bool excludeSuppressed in new[] { true, false })
{
    var f = Rollup.Apply(snapshot, new RollupOptions
    { Mode = RollupMode.Flat, IncludeAssemblies = false, ExcludeSuppressed = excludeSuppressed });
    Console.WriteLine($"  ExcludeSuppressed={excludeSuppressed}: flat {f.Sum(r => r.Quantity)}");
}

string csvPath = Path.ChangeExtension(outputPath, ".csv");
File.WriteAllText(
    csvPath,
    CsvTableWriter.Write(structured, new[] { "Part Number", "Description", "Mass" }),
    new System.Text.UTF8Encoding(true));
Console.WriteLine($"CSV written to: {csvPath}");

foreach (var s in snapshot.Skipped)
    Console.WriteLine($"SKIPPED [{s.Reason}] {s.OccurrencePath} :: {s.Detail}");

var ours = flat
    .GroupBy(r => NormalizeKey(r.Component))
    .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity), StringComparer.OrdinalIgnoreCase);

Console.WriteLine("--- Differences vs Inventor Parts Only BOM ---");
foreach (var key in invBom.Keys.Union(ours.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
{
    invBom.TryGetValue(key, out int inv);
    ours.TryGetValue(key, out int mine);
    if (inv != mine)
        Console.WriteLine($"{Path.GetFileName(key)}: Inventor {inv}, ours {mine}");
}
Console.WriteLine("--- end ---");

// Strips a trailing <member/model state> suffix so keys match Inventor's file paths.
static string NormalizeKey(OpenInventorExport.Core.Model.ComponentRecord c)
{
    if (c.IsVirtual) return "(virtual)";
    string path = c.FullPath ?? c.FileName;
    int i = path.LastIndexOf('<');
    return i > 0 && path.EndsWith(">") ? path.Substring(0, i) : path;
}