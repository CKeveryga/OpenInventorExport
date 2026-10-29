using System.IO;

namespace OpenInventorExport.Core;

public static class PathUtil
{
    /// <summary>Removes a trailing "&lt;member or model state&gt;" suffix, e.g. "Motor.ipt&lt;No Cover&gt;" becomes "Motor.ipt".</summary>
    public static string StripMemberSuffix(string path)
    {
        int i = path.LastIndexOf('<');
        return i > 0 && path.EndsWith(">") ? path.Substring(0, i) : path;
    }

    /// <summary>The drawing base name for a model path: same file name, no extension, no suffix.</summary>
    public static string DrawingBaseName(string modelPath) =>
        Path.GetFileNameWithoutExtension(StripMemberSuffix(modelPath));
}