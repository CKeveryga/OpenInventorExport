using System.Collections.Generic;
using System.IO;

namespace OpenInventorExport.Core;

/// <summary>
/// Loads a config file from disk. Relative paths inside it (such as IniFile) are relative to the
/// folder containing the config file. Absolute paths (local, vault, network share) are kept as they are.
/// </summary>
public static class ExportConfigFile
{
    /// <summary>
    /// Reads, validates and resolves paths. Throws InvalidDataException listing every problem.
    /// In the returned config, IniFile values are absolute. Do not save that object back to disk.
    /// </summary>
    public static ExportConfig Load(string configPath)
    {
        string fullConfigPath = Path.GetFullPath(configPath);
        var config = ExportConfigJson.FromJson(File.ReadAllText(fullConfigPath));

        string baseFolder = Path.GetDirectoryName(fullConfigPath)!;
        var problems = ResolvePaths(config, baseFolder);
        if (problems.Count > 0)
            throw new InvalidDataException("Config file has problems: " + string.Join(" ", problems));

        return config;
    }

    private static List<string> ResolvePaths(ExportConfig config, string baseFolder)
    {
        var problems = new List<string>();
        foreach (var task in config.Tasks)
        {
            foreach (var output in task.Outputs)
            {
                if (string.IsNullOrWhiteSpace(output.IniFile)) continue;

                // Combine returns the second path unchanged when it is already absolute.
                string resolved = Path.GetFullPath(Path.Combine(baseFolder, output.IniFile!));
                if (!File.Exists(resolved))
                    problems.Add($"Task '{task.Name}': ini file not found: {resolved}");
                output.IniFile = resolved;
            }
        }
        return problems;
    }
}