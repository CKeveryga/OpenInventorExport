using System;
using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum RollupMode { Occurrence, Structured, Flat }
public enum InseparableHandling { AsSingleItem, IncludeChildren }

public class RollupOptions
{
    public RollupMode Mode { get; set; } = RollupMode.Flat;
    /// <summary>Adds the top-level assembly (Level 0) as a row with qty 1. Off by default because Inventor's BOM does not list it.</summary>
    public bool IncludeRoot { get; set; } = false;
    public bool IncludeAssemblies { get; set; } = true;
    public bool ExcludePhantom { get; set; } = true;
    public bool ExcludeReference { get; set; } = true;
    public bool ExcludeSuppressed { get; set; } = true;

    /// <summary>Null = Inventor's behavior for the mode: Flat hides children of inseparable assemblies, other modes list them.</summary>
    public InseparableHandling? Inseparable { get; set; }

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
        var inseparable = options.Inseparable
            ?? (options.Mode == RollupMode.Flat ? InseparableHandling.AsSingleItem : InseparableHandling.IncludeChildren);

        // The top-level assembly (Level 0) is the thing being exported, not a row in it.
        var byPath = snapshot.Components
            .GroupBy(c => c.OccurrencePath)
            .ToDictionary(g => g.Key, g => g.First());

        bool InsideInseparable(ComponentRecord c)
        {
            var current = c;
            while (current.ParentPath != null && byPath.TryGetValue(current.ParentPath, out var parent))
            {
                if (parent.BomStructure == "kInseparableBOMStructure") return true;
                current = parent;
            }
            return false;
        }

        bool KeptInseparableAssembly(ComponentRecord c) =>
            c.BomStructure == "kInseparableBOMStructure"
            && inseparable == InseparableHandling.AsSingleItem;

        var candidates = snapshot.Components
            .Where(c => c.Level > 0 || options.IncludeRoot)
            .Where(c => !(options.ExcludePhantom && c.BomStructure == "kPhantomBOMStructure"))
            .Where(c => !(options.ExcludeReference && c.BomStructure == "kReferenceBOMStructure"))
            .Where(c => !(options.ExcludeSuppressed && c.IsSuppressed))
            .Where(c => inseparable == InseparableHandling.IncludeChildren || !InsideInseparable(c))
            .Where(c => options.IncludeAssemblies || c.DocumentType != "Assembly" || KeptInseparableAssembly(c))
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