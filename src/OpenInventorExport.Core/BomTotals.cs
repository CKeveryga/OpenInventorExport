using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>The two totals we compare against Inventor's BOM views, using default rollup rules.</summary>
public static class BomTotals
{
    /// <summary>Matches Inventor's "Parts Only" view: flat, no assemblies.</summary>
    public static int PartsOnly(Snapshot s) =>
        Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false })
              .Sum(r => r.Quantity);

    /// <summary>Matches Inventor's "Structured" view: per-parent counts, assemblies included.</summary>
    public static int Structured(Snapshot s) =>
        Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Structured })
              .Sum(r => r.Quantity);
}