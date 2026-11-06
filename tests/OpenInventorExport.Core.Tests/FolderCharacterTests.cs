using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

/// <summary>
/// Folder and FolderTemplate must not contain characters Windows forbids in folder names;
/// otherwise every job of that output would fail only when the export runs.
/// </summary>
public class FolderCharacterTests
{
    private static ExportTask Task(string folderTemplate = "{task}", string folder = "") => new ExportTask
    {
        Name = "Drawings",
        FolderTemplate = folderTemplate,
        Outputs = { new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Drawing, Folder = folder } }
    };

    [Theory]
    [InlineData("Vendor?")]
    [InlineData("A*B")]
    [InlineData("a<b>")]
    [InlineData("PDF|DWG")]
    [InlineData("say \"hi\"")]
    [InlineData("Vendor/Sub:1")]
    public void ForbiddenCharacterInFolder_IsRejected(string folder) =>
        Assert.Contains(ExportTaskValidator.Validate(Task(folder: folder)), p => p.Contains("does not allow"));

    [Fact]
    public void ForbiddenCharacterInFolderTemplate_IsRejected() =>
        Assert.Contains(ExportTaskValidator.Validate(Task(folderTemplate: "{task}/Out?")), p => p.Contains("FolderTemplate"));

    [Fact]
    public void TheProblemNamesTheCharacters()
    {
        var problems = ExportTaskValidator.Validate(Task(folder: "A*B?C*"));

        Assert.Contains(problems, p => p.Contains("'*', '?'"));
    }

    [Theory]
    [InlineData("PDF")]
    [InlineData("Vendor/PDF")]
    [InlineData(@"Vendor\PDF")]
    [InlineData("Vendor ABC (2026)")]
    public void OrdinaryAndNestedFolders_AreAccepted(string folder) =>
        Assert.Empty(ExportTaskValidator.Validate(Task(folder: folder)));

    [Fact]
    public void TokensInFolderTemplate_AreAccepted() =>
        Assert.Empty(ExportTaskValidator.Validate(Task(folderTemplate: "{task}/{format}")));
}
