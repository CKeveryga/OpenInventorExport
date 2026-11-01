using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>The two BOM views we compare against Inventor, using default rollup rules.</summary>
public static class BomTotals
{
    /// <summary>Rows of Inventor's "Parts Only" view: flat, no assemblies.</summary>
    public static List<ExportRow> PartsOnlyRows(Snapshot s) =>
        Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

    /// <summary>Rows of Inventor's "Structured" view: per-parent counts, assemblies included.</summary>
    public static List<ExportRow> StructuredRows(Snapshot s) =>
        Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Structured });

    public static int PartsOnly(Snapshot s) => PartsOnlyRows(s).Sum(r => r.Quantity);

    public static int Structured(Snapshot s) => StructuredRows(s).Sum(r => r.Quantity);
}