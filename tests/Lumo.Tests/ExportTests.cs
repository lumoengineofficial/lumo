using Lumo.Editor.Export;

namespace Lumo.Tests;

public class ExportTests
{
    [Fact]
    public void FindDotnet_ReturnsExistingExecutable()
    {
        string? dotnet = GameExporter.FindDotnet();

        Assert.NotNull(dotnet);
        Assert.True(File.Exists(dotnet), $"expected dotnet at '{dotnet}'");
    }

    [Fact]
    public void FindRuntimeProject_FindsRuntimeCsprojFromRepoLayout()
    {
        string? proj = GameExporter.FindRuntimeProject();

        Assert.NotNull(proj);
        Assert.EndsWith("Lumo.Runtime.csproj", proj);
        Assert.True(File.Exists(proj));
    }

    [Fact]
    public void OutputDir_UsesSanitizedProjectNameAndTargetId()
    {
        string dir = GameExporter.OutputDir(
            Path.Combine(Path.GetTempPath(), "My Game!"), "My Game!", ExportTarget.WindowsX64);

        Assert.EndsWith(Path.Combine("Builds", "MyGame-win-x64"), dir);
    }

    [Fact]
    public void DesktopTargets_CoverWindowsAndLinuxWithMatchingExeNames()
    {
        Assert.Equal(2, ExportTarget.DesktopTargets.Length);

        var win = ExportTarget.WindowsX64;
        Assert.Equal("win-x64", win.Rid);
        Assert.Equal("Lumo.Runtime.exe", win.ExeName);

        var linux = ExportTarget.LinuxX64;
        Assert.Equal("linux-x64", linux.Rid);
        Assert.Equal("Lumo.Runtime", linux.ExeName);
    }
}
