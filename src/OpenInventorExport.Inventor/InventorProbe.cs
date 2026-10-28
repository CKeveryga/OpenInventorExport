using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class InventorProbe
{
    public static string Describe()
    {
        Inv.Application app = InventorConnection.GetRunningInventor();
        Inv.Document? doc = app.ActiveDocument;

        return $"Connected to: {app.SoftwareVersion.DisplayName}\n" +
               $"Active document: {doc?.FullFileName ?? "(none open)"}";
    }
}