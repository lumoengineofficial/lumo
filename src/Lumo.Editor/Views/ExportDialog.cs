using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumo.Editor.Export;
using Lumo.Editor.Ui;

namespace Lumo.Editor.Views;

/// <summary>Pick a target platform and export the current project as a
/// self-contained standalone game (Windows or Linux).</summary>
public sealed class ExportDialog : Window
{
    private readonly string _projectPath;
    private readonly string _projectName;
    private readonly Action<string> _log;
    private readonly Action _beforeExport;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _exportBtn;
    private readonly Button _openFolderBtn;
    private readonly RadioButton[] _radios;
    private bool _exporting;
    private string? _lastOutput;

    public ExportDialog(string projectPath, string projectName, Action<string> log, Action beforeExport)
    {
        _projectPath = projectPath;
        _projectName = projectName;
        _log = log;
        _beforeExport = beforeExport;

        Title = "Export Game";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiTheme.B(UiTheme.Bg);

        _radios = new RadioButton[ExportTarget.DesktopTargets.Length];
        var targetPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < ExportTarget.DesktopTargets.Length; i++)
        {
            var t = ExportTarget.DesktopTargets[i];
            _radios[i] = new RadioButton
            {
                Content = UiTheme.Txt(t.Label, 13, UiTheme.Text, FontWeight.Medium),
                GroupName = "export-target",
                IsChecked = i == 0,
                Foreground = UiTheme.B(UiTheme.Text),
            };
            targetPanel.Children.Add(_radios[i]);
        }

        _status = UiTheme.Txt("Self-contained build — runs without .NET installed.", 11, UiTheme.Dim);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 8);

        _progress = new ProgressBar
        {
            IsIndeterminate = true,
            IsVisible = false,
            Height = 4,
            Margin = new Thickness(0, 0, 0, 12),
        };

        _exportBtn = UiTheme.ActionButton("Export", null, () => _ = RunExportAsync(), primary: true);
        _openFolderBtn = UiTheme.ActionButton("Open Folder", null, OpenFolder);
        _openFolderBtn.IsEnabled = false;
        var closeBtn = UiTheme.ActionButton("Close", null, Close);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _exportBtn, _openFolderBtn, closeBtn },
        };

        var body = new StackPanel
        {
            Margin = new Thickness(18, 16, 18, 18),
            Children =
            {
                UiTheme.SectionTitle("EXPORT GAME"),
                UiTheme.Txt("Target platform", 11, UiTheme.Faint, FontWeight.SemiBold),
                targetPanel,
                _status,
                _progress,
                buttons,
            },
        };

        Content = UiTheme.CardBorder(body, UiTheme.Card);
    }

    private ExportTarget SelectedTarget()
    {
        for (int i = 0; i < _radios.Length; i++)
            if (_radios[i].IsChecked == true)
                return ExportTarget.DesktopTargets[i];
        return ExportTarget.WindowsX64;
    }

    private async System.Threading.Tasks.Task RunExportAsync()
    {
        if (_exporting) return;
        _exporting = true;
        _exportBtn.IsEnabled = false;
        _openFolderBtn.IsEnabled = false;
        _progress.IsVisible = true;
        _status.Foreground = UiTheme.B(UiTheme.Dim);
        _status.Text = "Saving project…";

        try
        {
            _beforeExport();

            var target = SelectedTarget();
            var result = await GameExporter.ExportAsync(target, _projectPath, _projectName,
                line => Dispatcher.UIThread.Post(() =>
                {
                    _status.Text = line;
                    _log(line);
                }));

            _status.Text = result.Message;
            _status.Foreground = UiTheme.B(result.Ok ? UiTheme.Green : UiTheme.Red);
            if (result.Ok)
            {
                _lastOutput = result.OutputDir;
                _openFolderBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"Export failed: {ex.Message}";
            _status.Foreground = UiTheme.B(UiTheme.Red);
        }
        finally
        {
            _exporting = false;
            _exportBtn.IsEnabled = true;
            _progress.IsVisible = false;
        }
    }

    private void OpenFolder()
    {
        if (_lastOutput == null || !Directory.Exists(_lastOutput)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastOutput}\""));
        }
        catch (Exception ex) { _log($"Open folder failed: {ex.Message}"); }
    }
}
