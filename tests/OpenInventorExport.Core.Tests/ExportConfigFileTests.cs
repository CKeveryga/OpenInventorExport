using System;
using System.IO;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportConfigFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oie-tests-" + Guid.NewGuid().ToString("N"));

    public ExportConfigFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp folder: best effort */ }
    }

    private string WriteConfig(string folder, string iniFile)
    {
        Directory.CreateDirectory(folder);
        var config = new ExportConfig
        {
            Tasks =
            {
                new ExportTask
                {
                    Name = "Drawings",
                    Outputs = { new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing, IniFile = iniFile } }
                }
            }
        };
        string path = Path.Combine(folder, "export.config.json");
        File.WriteAllText(path, ExportConfigJson.ToJson(config));
        return path;
    }

    [Fact]
    public void RelativeIniPath_IsResolvedAgainstTheConfigFolder()
    {
        string cfgFolder = Path.Combine(_dir, "cfg");
        Directory.CreateDirectory(cfgFolder);
        File.WriteAllText(Path.Combine(cfgFolder, "one.ini"), "");

        var loaded = ExportConfigFile.Load(WriteConfig(cfgFolder, "one.ini"));

        Assert.Equal(Path.Combine(cfgFolder, "one.ini"), loaded.Tasks[0].Outputs[0].IniFile);
    }

    [Fact]
    public void AbsoluteIniPath_IsKept()
    {
        string shared = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(shared);
        string ini = Path.Combine(shared, "vault.ini");
        File.WriteAllText(ini, "");

        var loaded = ExportConfigFile.Load(WriteConfig(Path.Combine(_dir, "cfg"), ini));

        Assert.Equal(ini, loaded.Tasks[0].Outputs[0].IniFile);
    }

    [Fact]
    public void MissingIniFile_IsReported_NotIgnored()
    {
        string path = WriteConfig(Path.Combine(_dir, "cfg"), "missing.ini");

        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigFile.Load(path));

        Assert.Contains("missing.ini", ex.Message);
    }

    private static ExportConfig DxfConfig(string iniFile) => new ExportConfig
    {
        Tasks =
        {
            new ExportTask
            {
                Name = "Drawings",
                Outputs = { new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing, IniFile = iniFile } }
            }
        }
    };

    [Fact]
    public void CheckIniFiles_ReportsAMissingFile_AndLeavesThePathAlone()
    {
        var config = DxfConfig("missing.ini");

        var problems = ExportConfigFile.CheckIniFiles(config, Path.Combine(_dir, "export.config.json"));

        Assert.Contains(problems, p => p.Contains("missing.ini"));
        Assert.Equal("missing.ini", config.Tasks[0].Outputs[0].IniFile);
    }

    [Fact]
    public void CheckIniFiles_IsQuiet_WhenTheFileExistsNextToTheConfig()
    {
        File.WriteAllText(Path.Combine(_dir, "one.ini"), "");

        var problems = ExportConfigFile.CheckIniFiles(DxfConfig("one.ini"), Path.Combine(_dir, "export.config.json"));

        Assert.Empty(problems);
    }

    [Fact]
    public void Save_KeepsTheIniPathRelative_AndTheFileLoadsAgain()
    {
        File.WriteAllText(Path.Combine(_dir, "one.ini"), "");
        string path = Path.Combine(_dir, "export.config.json");

        ExportConfigFile.Save(DxfConfig("one.ini"), path);

        Assert.Equal("one.ini", ExportConfigJson.Parse(File.ReadAllText(path)).Tasks[0].Outputs[0].IniFile);
        Assert.Equal(Path.Combine(_dir, "one.ini"), ExportConfigFile.Load(path).Tasks[0].Outputs[0].IniFile);
    }

    [Fact]
    public void Save_CopiesTheOldFileToBak_First()
    {
        File.WriteAllText(Path.Combine(_dir, "one.ini"), "");
        string path = Path.Combine(_dir, "export.config.json");
        File.WriteAllText(path, "// the old file");

        ExportConfigFile.Save(DxfConfig("one.ini"), path);

        Assert.Equal("// the old file", File.ReadAllText(path + ".bak"));
        Assert.Contains("\"SchemaVersion\": 1", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WithoutAnOldFile_WritesNoBak()
    {
        File.WriteAllText(Path.Combine(_dir, "one.ini"), "");
        string path = Path.Combine(_dir, "export.config.json");

        ExportConfigFile.Save(DxfConfig("one.ini"), path);

        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Save_Refuses_WhileTheConfigHasProblems_AndTouchesNothing()
    {
        string path = Path.Combine(_dir, "export.config.json");
        File.WriteAllText(path, "// the old file");

        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigFile.Save(DxfConfig("missing.ini"), path));

        Assert.Contains("missing.ini", ex.Message);
        Assert.Equal("// the old file", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }
}