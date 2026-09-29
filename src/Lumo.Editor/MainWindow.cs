using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumo.Engine.Core;
using Lumo.Editor.Views;
using System;
using System.IO;
using System.Linq;

namespace Lumo.Editor;

public class MainWindow : Window
{
    private ContentControl _contentSwitch = null!;

    public MainWindow()
    {
        Title = EngineConstants.Name;
        Width = 1600;
        Height = 920;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#0a0e17"));

        SetWindowIcon();
        BuildUI();
        ShowHome();
        OpenLastProjectIfRequested();
    }

    private void OpenLastProjectIfRequested()
    {
        if (!Environment.GetCommandLineArgs().Contains("--open-last")) return;
        try
        {
            var last = ProjectManager.GetAllProjects().OrderByDescending(p => p.LastModified).FirstOrDefault();
            if (last != null) ShowWork(last.Path);
        }
        catch { }
    }

    private void SetWindowIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("Lumo.Editor.Assets.lumo_icon.png");
            if (stream != null) Icon = new WindowIcon(stream);
        }
        catch { }
    }

    private void BuildUI()
    {
        _contentSwitch = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Content = _contentSwitch;
    }

    private void ShowHome()
    {
        var home = new HomeView(
            onNewProject: (name, basePath) =>
            {
                string finalName = string.IsNullOrWhiteSpace(name) ? "New Project" : name.Trim();
                string projectPath = ProjectManager.CreateProject(finalName, "", basePath);
                ShowWork(projectPath);
            },
            onOpenProject: (path) =>
            {
                ShowWork(path);
            }
        );
        _contentSwitch.Content = home;
    }

    private void ShowWork(string projectPath)
    {
        var info = ProjectManager.LoadProjectInfo(projectPath);
        if (info == null)
        {
            info = new ProjectInfo
            {
                Name = System.IO.Path.GetFileName(projectPath),
                Path = projectPath,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                Version = EngineConstants.Version
            };
            ProjectManager.SaveProjectInfo(info);
        }

        var work = new WorkView(info, (_) => ShowHome(), () => Close());
        _contentSwitch.Content = work;
    }
}
