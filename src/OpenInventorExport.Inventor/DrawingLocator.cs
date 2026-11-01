using OpenInventorExport.Core;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class DrawingLocator
{
    public static Dictionary<string, string?> FindForRunningInventor(IEnumerable<string> modelPaths)
    {
        var app = InventorConnection.GetRunningInventor();
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string model in modelPaths)
            result[model] = Find(app, model);
        return result;
    }

    /// <summary>
    /// Looks for a drawing named like the model, anywhere in the active project's paths.
    /// Returns the path, or null if none was found.
    /// </summary>
    internal static string? Find(Inv.Application app, string modelPath)
    {
        string baseName = PathUtil.DrawingBaseName(modelPath);
        var pm = app.DesignProjectManager;
        string projectPath = pm.ActiveDesignProject.FullFileName;

        foreach (string ext in new[] { ".idw", ".dwg" })
        {
            try
            {
                string found = pm.ResolveFile(projectPath, baseName + ext);
                if (!string.IsNullOrEmpty(found) && File.Exists(found))
                    return found;
            }
            catch { /* not found with this extension: try the next */ }
        }
        return null;
    }

    /// <summary>The active project's file name without extension, for messages. Never throws.</summary>
    internal static string ActiveProjectName(Inv.Application app)
    {
        try { return Path.GetFileNameWithoutExtension(app.DesignProjectManager.ActiveDesignProject.FullFileName); }
        catch { return "unknown"; }
    }
}