using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenInventorExport.Core;

/// <summary>
/// Loads and saves a config file. Relative paths inside it (such as IniFile) are relative to the
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

    /// <summary>
    /// Reports every ini file that does not exist, judged from the folder of <paramref name="configPath"/>.
    /// Unlike Load, it does NOT change the paths in the config, so the config can still be saved.
    /// </summary>
    public static List<string> CheckIniFiles(ExportConfig config, string configPath)
    {
        string baseFolder = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var problems = new List<string>();
        foreach (var task in config.Tasks)
            foreach (var output in task.Outputs)
            {
                if (string.IsNullOrWhiteSpace(output.IniFile)) continue;
                string resolved = Resolve(baseFolder, output.IniFile!);
                if (!File.Exists(resolved))
                    problems.Add($"Task '{task.Name}': ini file not found: {resolved}");
            }
        return problems;
    }

    /// <summary>
    /// Every problem the config has if it were saved at <paramref name="configPath"/>: content
    /// (ExportConfigJson.Validate) plus missing ini files. Empty list = Load would accept it.
    /// </summary>
    public static List<string> Problems(ExportConfig config, string configPath) =>
        ExportConfigJson.Validate(config).Concat(CheckIniFiles(config, configPath)).ToList();

    /// <summary>
    /// Writes the config to <paramref name="configPath"/>. Refuses (InvalidDataException) while the config
    /// has problems, so a saved file can always be loaded. If the file exists, it is first copied to
    /// "&lt;file&gt;.bak" (replacing an older .bak). Comments in the old file are not kept.
    /// Never pass a config that came from Load: its ini paths are absolute.
    /// </summary>
    public static void Save(ExportConfig config, string configPath)
    {
        string fullConfigPath = Path.GetFullPath(configPath);

        var problems = Problems(config, fullConfigPath);
        if (problems.Count > 0)
            throw new InvalidDataException("Config not saved, it has problems: " + string.Join(" ", problems));

        if (File.Exists(fullConfigPath))
            File.Copy(fullConfigPath, fullConfigPath + ".bak", overwrite: true);

        File.WriteAllText(fullConfigPath, ExportConfigJson.ToJson(config));
    }

    private static List<string> ResolvePaths(ExportConfig config, string baseFolder)
    {
        var problems = new List<string>();
        foreach (var task in config.Tasks)
        {
            foreach (var output in task.Outputs)
            {
                if (string.IsNullOrWhiteSpace(output.IniFile)) continue;

                string resolved = Resolve(baseFolder, output.IniFile!);
                if (!File.Exists(resolved))
                    problems.Add($"Task '{task.Name}': ini file not found: {resolved}");
                output.IniFile = resolved;
            }
        }
        return problems;
    }

    // Combine returns the second path unchanged when it is already absolute.
    private static string Resolve(string baseFolder, string iniFile) =>
        Path.GetFullPath(Path.Combine(baseFolder, iniFile));
}
