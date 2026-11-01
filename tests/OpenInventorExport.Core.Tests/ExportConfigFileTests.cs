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
}