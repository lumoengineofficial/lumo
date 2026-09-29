using Lumo.Engine.Core;
using Xunit;

namespace Lumo.Tests;

public class ProjectManagerTests
{
    private static readonly string[] Dirs = ["Assets", "Scenes", "Graphs", "Scripts", "Plugins"];

    [Fact]
    public void CreateProject_CustomPath_CreatesStructureAndRegisters()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "lumo_proj_" + Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(baseDir, "My_Game");
        try
        {
            string created = ProjectManager.CreateProject("My Game", "", baseDir);

            Assert.Equal(projectPath, created);
            Assert.True(File.Exists(Path.Combine(projectPath, "Project.json")));
            foreach (string d in Dirs)
                Assert.True(Directory.Exists(Path.Combine(projectPath, d)), $"missing {d}");

            var info = ProjectManager.LoadProjectInfo(created);
            Assert.NotNull(info);
            Assert.Equal("My Game", info!.Name);

            Assert.Contains(ProjectManager.GetAllProjects(), p =>
                string.Equals(Path.GetFullPath(p.Path), Path.GetFullPath(projectPath),
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ProjectManager.UnregisterProject(projectPath);
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CreateProject_ExistingEmptyFolder_InitializesInside()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "lumo_proj_" + Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(baseDir, "Already");
        try
        {
            Directory.CreateDirectory(projectPath);
            string created = ProjectManager.CreateProject("Already", "", baseDir);

            Assert.Equal(projectPath, created);
            Assert.True(File.Exists(Path.Combine(projectPath, "Project.json")));
        }
        finally
        {
            ProjectManager.UnregisterProject(projectPath);
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CreateProject_ExistingProject_ReturnsSamePath()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "lumo_proj_" + Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(baseDir, "Twice");
        try
        {
            string first = ProjectManager.CreateProject("Twice", "", baseDir);
            string second = ProjectManager.CreateProject("Twice", "changed desc", baseDir);

            Assert.Equal(first, second);
            Assert.Equal("Twice", ProjectManager.LoadProjectInfo(second)!.Name);
        }
        finally
        {
            ProjectManager.UnregisterProject(projectPath);
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CreateProject_DefaultRoot_WhenBasePathMissing()
    {
        string created = ProjectManager.CreateProject("Root Default Check");
        try
        {
            Assert.StartsWith(ProjectManager.ProjectsRootPath, created);
            Assert.True(File.Exists(Path.Combine(created, "Project.json")));
        }
        finally
        {
            ProjectManager.UnregisterProject(created);
            if (Directory.Exists(created)) Directory.Delete(created, true);
        }
    }
}
