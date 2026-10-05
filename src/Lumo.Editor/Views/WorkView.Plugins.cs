using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumo.Editor.Ui;
using Lumo.Engine.Scene;
using Lumo.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lumo.Editor.Views;

/// <summary>Editor-side plugin integration: host bridge for plugin commands and
/// the automatic command bar that renders one button per registered command.</summary>
public partial class WorkView : IHostBridge
{
    private WrapPanel? _pluginCommandBar;
    private Border? _pluginCommandBarHost;
    private bool _uiBuilt;

    // ------------------------------------------------------- IHostBridge

    string? IHostBridge.ProjectPath => _project.Path;

    void IHostBridge.Log(string message) => Log(message);

    void IHostBridge.PushUndo() => PushSceneUndo();

    void IHostBridge.SaveProject() => SaveProject();

    void IHostBridge.RefreshUi()
    {
        if (!_uiBuilt) return;
        RefreshHierarchy();
        RefreshInspector();
        RefreshFileTree();
    }

    bool IHostBridge.EntityExists(string entityName) => _scene.FindByName(entityName) != null;

    void IHostBridge.CreateOrUpdateMeshEntity(string entityName, string meshName)
    {
        var existing = _scene.FindByName(entityName);
        if (existing != null)
        {
            if (existing.MeshRenderer == null)
                existing.MeshRenderer = new MeshRendererComponent { MeshName = meshName, Color = System.Numerics.Vector3.One };
            else
                existing.MeshRenderer.MeshName = meshName;
            return;
        }

        var entity = _scene.CreateEntity(entityName);
        entity.MeshRenderer = new MeshRendererComponent
        {
            MeshName = meshName,
            Color = System.Numerics.Vector3.One,
        };
    }

    void IHostBridge.DeleteEntitiesByPrefix(string prefix)
    {
        var doomed = _scene.AllEntities
            .Where(e => e.Name != null && e.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var entity in doomed)
            _scene.DestroyEntity(entity);
        if (_selectedEntity != null && doomed.Contains(_selectedEntity))
            _selectedEntity = null;
    }

    async Task<string?> IHostBridge.PickOpenFileAsync(string title, params (string Label, string[] Patterns)[] filters)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var fileTypes = new List<FilePickerFileType>();
        foreach (var (label, patterns) in filters)
            fileTypes.Add(new FilePickerFileType(label) { Patterns = patterns });

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    // ------------------------------------------------------- command bar

    private Border BuildPluginCommandBar()
    {
        _pluginCommandBar = new WrapPanel();
        _pluginCommandBarHost = new Border
        {
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 5),
            IsVisible = false,
            Child = _pluginCommandBar,
        };
        RebuildPluginCommandBar();
        return _pluginCommandBarHost;
    }

    private void RebuildPluginCommandBar()
    {
        if (_pluginCommandBar == null || _pluginCommandBarHost == null) return;

        _pluginCommandBar.Children.Clear();
        var commands = PluginCommands.Commands;
        _pluginCommandBarHost.IsVisible = commands.Count > 0;
        if (commands.Count == 0) return;

        var tag = UiTheme.Txt("PLUGINS", 9, UiTheme.Faint, FontWeight.SemiBold);
        tag.Margin = new Thickness(0, 0, 12, 0);
        tag.VerticalAlignment = VerticalAlignment.Center;
        _pluginCommandBar.Children.Add(tag);

        foreach (var command in commands)
        {
            var captured = command;
            bool primary = captured.Display.StartsWith("●", StringComparison.Ordinal);
            var button = UiTheme.ActionButton(captured.Display, null, () =>
            {
                try { captured.Callback(); }
                catch (Exception ex) { Log($"Plugin command failed: {ex.Message}"); }
                RebuildPluginCommandBar();
            }, primary: primary);
            button.Margin = new Thickness(0, 0, 6, 0);
            _pluginCommandBar.Children.Add(button);
        }
    }
}
