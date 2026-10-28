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
}