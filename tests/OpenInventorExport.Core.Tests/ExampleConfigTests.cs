using System;
using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExampleConfigTests
{
    private static string FindExample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "config", "export.config.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "config", "export.config.json");
    }

    [Fact]
    public void ShippedExampleConfig_LoadsAndResolvesItsIniFiles()
    {
        var config = ExportConfigFile.Load(FindExample());

        Assert.Contains(config.Tasks, t => t.Name == "Drawings");
        var ini = config.Tasks.SelectMany(t => t.Outputs).Where(o => o.IniFile != null).ToList();
        Assert.NotEmpty(ini);
        Assert.All(ini, o => Assert.True(Path.IsPathRooted(o.IniFile) && File.Exists(o.IniFile)));
        Assert.Equal(3, config.BomColumns.Count);
        Assert.False(config.Rollup.IncludeAssemblies);
    }
}