using System;
using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum RollupMode { Occurrence, Structured, Flat }

public class RollupOptions
{
    public RollupMode Mode { get; set; } = RollupMode.Flat;
    public bool IncludeAssemblies { get; set; } = true;
    public bool ExcludePhantom { get; set; } = true;

    /// <summary>What makes two components "the same item". Default: the file.</summary>
    public Func<ComponentRecord, string> Key { get; set; } =
        c => c.FullPath ?? c.FileName;
}

public class ExportRow
{
    public ExportRow(ComponentRecord component, int quantity, string? parentKey)
    {
        Component = component;
        Quantity = quantity;
        ParentKey = parentKey;
    }

    public ComponentRecord Component { get; }
    public int Quantity { get; }
    public string? ParentKey { get; }
}

public static class Rollup
{
    public static List<ExportRow> Apply(Snapshot snapshot, RollupOptions options)
    {
        // The top-level assembly (Level 0) is the thing being exported, not a row in it.
        var candidates = snapshot.Components
            .Where(c => c.Level > 0)
            .Where(c => options.IncludeAssemblies || c.DocumentType != "Assembly")
            .Where(c => !(options.ExcludePhantom && c.BomStructure == "kPhantomBOMStructure"))
            .ToList();

        switch (options.Mode)
        {
            case RollupMode.Occurrence:
                return candidates.Select(c => new ExportRow(c, 1, null)).ToList();

            case RollupMode.Flat:
                // Counting occurrences across the whole tree automatically
                // multiplies quantities through the levels.
                return candidates
                    .GroupBy(options.Key)
                    .Select(g => new ExportRow(g.First(), g.Count(), null))
                    .ToList();

            case RollupMode.Structured:
                return Structured(snapshot, candidates, options);

            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static List<ExportRow> Structured(
    Snapshot snapshot, List<ComponentRecord> candidates, RollupOptions options)
    {
        var byPath = snapshot.Components
            .GroupBy(c => c.OccurrencePath)
            .ToDictionary(g => g.Key, g => g.First());

        // A parent is identified by its full chain of ancestors, e.g. "/Top/Sub".
        var structKeys = new Dictionary<string, string>();
        string StructKey(ComponentRecord c)
        {
            if (structKeys.TryGetValue(c.OccurrencePath, out var cached)) return cached;
            string parentPart = c.ParentPath != null && byPath.TryGetValue(c.ParentPath, out var p)
                ? StructKey(p)
                : "";
            string key = parentPart + "/" + options.Key(c);
            structKeys[c.OccurrencePath] = key;
            return key;
        }

        var rows = new List<ExportRow>();
        var seen = new HashSet<(string parent, string child)>();

        foreach (var parentGroup in candidates.GroupBy(c => c.ParentPath ?? ""))
        {
            string parentKey = byPath.TryGetValue(parentGroup.Key, out var parent)
                ? StructKey(parent)
                : "";

            foreach (var child in parentGroup.GroupBy(options.Key))
            {
                if (!seen.Add((parentKey, child.Key))) continue;
                rows.Add(new ExportRow(child.First(), child.Count(), parentKey));
            }
        }
        return rows;
    }
}