using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Inventor;

public static class SnapshotCollector
{
    /// <summary>
    /// Connects to the running Inventor and snapshots the active assembly.
    /// </summary>
    public static Snapshot CollectFromRunningInventor()
    {
        var app = InventorConnection.GetRunningInventor();
        return new AssemblyTraverser().Traverse(app);
    }

    /// <summary>
    /// Full file name of the project active in the running Inventor, or null if it cannot be read.
    /// Throws if Inventor is not running (callers that only want a best-effort check should catch).
    /// </summary>
    public static string? GetActiveProjectFile() =>
        AssemblyTraverser.TryGetProjectFile(InventorConnection.GetRunningInventor());

    /// <summary>
    /// Reads Inventor's own Parts Only BOM: file path -> total quantity.
    /// Note: enabling the BOM view can mark the assembly as modified (do not save it).
    /// </summary>
    public static Dictionary<string, int> ReadPartsOnlyBom()
    {
        var app = InventorConnection.GetRunningInventor();
        var asm = (global::Inventor.AssemblyDocument)app.ActiveDocument;
        var bom = asm.ComponentDefinition.BOM;
        bom.PartsOnlyViewEnabled = true;

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (global::Inventor.BOMRow row in bom.BOMViews["Parts Only"].BOMRows)
        {
            int qty = int.TryParse(row.TotalQuantity, out int parsed) ? parsed : 0;

            string key;
            try
            {
                var def = row.ComponentDefinitions[1];
                key = def is global::Inventor.VirtualComponentDefinition
                    ? "(virtual)"
                    : ((global::Inventor.Document)def.Document).FullFileName;
            }
            catch
            {
                key = "(unreadable BOM row)";
            }

            result[key] = (result.TryGetValue(key, out int existing) ? existing : 0) + qty;
        }
        return result;
    }
}