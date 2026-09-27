using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Lumo.Editor.Editing;
using Lumo.Editor.Rendering;
using Lumo.Editor.Ui;
using Lumo.Editor.VisualScripting;
using Lumo.Engine.Assets;
using Lumo.Engine.Core;
using Lumo.Engine.Rendering.Abstractions;
using Lumo.Engine.Scene;
using Lumo.Engine.Scripting;
using Lumo.Engine.VisualScripting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Timers;
using Timer = System.Timers.Timer;
using LumoInput = Lumo.Engine.Input.InputState;
using LumoKey = Lumo.Engine.Input.Key;
using SceneType = Lumo.Engine.Scene.Scene;

namespace Lumo.Editor.Views;

public class WorkView : UserControl
{
    private readonly LumoEngine _engine;
    private SceneType _scene;
    private ProjectInfo _project;
    private readonly Action<string?> _onNavigateHome;
    private readonly Action _onClose;

    private TextBlock _statusText = null!;
    private TextBlock _fpsText = null!;
    private TextBlock _entityCountText = null!;
    private StackPanel _hierarchyList = null!;
    private StackPanel _fileTreeList = null!;
    private StackPanel _inspectorContent = null!;
    private TextBlock _consoleLog = null!;
    private ContentControl _bottomHost = null!;
    private ContentControl _centerHost = null!;
    private ContentControl _rightHost = null!;
    private Control? _leftHost;
    private Control? _sceneTabBar;
    private StackPanel _bottomTabsPanel = null!;
    private StackPanel _modeTabsPanel = null!;
    private Panel _viewportPanel = null!;
    private Button _playButton = null!;
    private GlViewport? _glViewport;
    private SoftwareViewport? _softwareViewport;
    private Entity? _selectedEntity;
    private bool _isPlaying;
    private ViewportMode _viewMode = ViewportMode.Scene;
    private string _topMode = "3D";              // "2D" | "3D" | "Script" | "Game" | "Asset Store"
    private string _leftTopTab = "Scene";         // "Scene" | "Import"
    private string _leftBottomTab = "FileSystem"; // "FileSystem" | "History"
    private string _rightTab = "Inspector";       // "Inspector" | "Signals"
    private string _bottomTab = "Output";         // "Output" | "Debugger" | "Audio" | "Animation" | "Shader Editor"
    private string _selectedFolder = "";
    private string _hierarchySearch = "";
    private string _fileFilter = "";
    private string _activeTool = "Move";
    private bool _showGrid = true;
    private bool _showGizmos = true;
    private bool _showStats = true;
    private readonly List<string> _logMessages = [];
    private string? _playSnapshot;
    private string? _currentScriptPath;
    private TextBox? _scriptEditor;
    private TextBlock? _scriptErrorText;
    private TextBlock? _profilerStats;
    private LumoInput? _playInput;
    private readonly ScriptHost _scriptHost = new();
    private GraphInterpreter? _graphRunner;
    private GraphsPanel? _graphsPanel;
    private MenuFlyout? _projectFlyout;
    private MenuFlyout? _editFlyout;
    private bool _exporting;
    private readonly HistoryStack _sceneHistory = new();
    private readonly HashSet<string> _vsLoggedErrors = new();

    public WorkView(ProjectInfo project, Action<string?> onNavigateHome, Action onClose)
    {
        _project = project;
        _onNavigateHome = onNavigateHome;
        _onClose = onClose;
        Focusable = true;
        _engine = new LumoEngine();
        _engine.Initialize();
        _scene = new SceneType { Name = project.Name };

        ScriptHost.MessageLogged += OnScriptMessage;

        LoadProject();
        BuildUI();
        StartFpsTimer();
        Log("Editor loaded.");
    }

    private void OnScriptMessage(string message) => Log(message);

    private void LoadProject()
    {
        var sceneFile = Path.Combine(_project.Path, "Scenes", "scene.json");
        if (File.Exists(sceneFile))
        {
            try { _scene = Scene.Load(sceneFile); }
            catch (Exception ex) { Log($"Scene load failed: {ex.Message}"); }
        }
        _selectedEntity = _scene.AllEntities.FirstOrDefault();
    }

    private void SaveProject()
    {
        var sceneFile = Path.Combine(_project.Path, "Scenes", "scene.json");
        if (_scene.AllEntities.Count == 0 && File.Exists(sceneFile))
        {
            try
            {
                int diskCount = Scene.Load(sceneFile).AllEntities.Count;
                if (diskCount > 0)
                {
                    Log($"Scene reload: editor was empty but disk has {diskCount} entities; skipping wipe.");
                    _scene = Scene.Load(sceneFile);
                    _selectedEntity = _scene.AllEntities.FirstOrDefault();
                    ProjectManager.UpdateLastModified(_project.Path);
                    return;
                }
            }
            catch { }
        }

        var scenesDir = Path.Combine(_project.Path, "Scenes");
        Directory.CreateDirectory(scenesDir);
        _scene.Save(sceneFile);
        ProjectManager.UpdateLastModified(_project.Path);
    }

    private void StartFpsTimer()
    {
        var timer = new Timer(500);
        timer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _fpsText.Text = $"FPS: {_engine.Time.FPS:F0}";
                _entityCountText.Text = $"{_scene.AllEntities.Count} objects";
                if (_profilerStats != null)
                {
                    _profilerStats.Text =
                        $"FPS: {_engine.Time.FPS:F0}\n" +
                        $"Frame: {_engine.Time.DeltaTime * 1000f:F2} ms\n" +
                        $"Entities: {_scene.AllEntities.Count}\n" +
                        $"Scripts: {_scriptHost.InstanceCount}\n" +
                        $"Playing: {(_isPlaying ? "Yes" : "No")}";
                }
            });
        timer.Start();

        var renderTimer = new Timer(16.0);
        renderTimer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                float dt = _engine.Tick();

                if (_isPlaying)
                {
                    try
                    {
                        // Graphs tick before BeginFrame so key presses since the
                        // last frame are still visible to event nodes.
                        _graphRunner?.Tick(dt);
                        if (_graphRunner != null)
                        {
                            _graphsPanel?.ShowDebug(_graphRunner.ActiveNodes);
                            foreach (string err in _graphRunner.Errors)
                            {
                                if (_vsLoggedErrors.Add(err))
                                    Log($"VS: {err}");
                            }
                        }
                    }
                    catch (Exception ex) { Log($"VS tick failed: {ex.Message}"); }
                    _playInput?.BeginFrame();
                    _scriptHost.Update(dt, (float)_engine.Time.ElapsedTime);
                }

                if (_glViewport != null && _glViewport.IsVisible && _glViewport.IsReady)
                {
                    _glViewport.SetSceneObjects(_scene);
                    _glViewport.RenderFrame();
                }
                else if (_softwareViewport != null && _softwareViewport.IsVisible)
                {
                    _softwareViewport.SetScene(_scene);
                    _softwareViewport.InvalidateVisual();
                }
            }, Avalonia.Threading.DispatcherPriority.Render);
        renderTimer.Start();
    }

    // ================= Play mode =================
    private void TogglePlay()
    {
        if (!_isPlaying) StartPlay();
        else StopPlay();
        RefreshPlayButton();
    }

    private void StartPlay()
    {
        try { _playSnapshot = _scene.Serialize(); }
        catch { _playSnapshot = null; }

        var scriptsDir = Path.Combine(_project.Path, "Scripts");
        Directory.CreateDirectory(scriptsDir);
        var sources = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        if (sources.Count > 0)
        {
            if (!_scriptHost.Compile(sources))
            {
                foreach (var err in _scriptHost.Errors)
                    Log($"CS: {err}");
                Log($"Script compile failed ({_scriptHost.Errors.Count} error(s)). Fix and retry.");
                _playSnapshot = null;
                return;
            }

            _playInput = new LumoInput();
            _scriptHost.Input = _playInput;
            _scriptHost.Bind(_scene);

            if (_scriptHost.Errors.Count > 0)
                foreach (var err in _scriptHost.Errors)
                    Log(err);
        }

        _playInput ??= new LumoInput();

        // Load visual scripting graphs for this play session.
        _graphRunner = new GraphInterpreter
        {
            Scene = _scene,
            Input = _playInput,
            Self = _selectedEntity,
            BaseDirectory = _project.Path
        };
        _graphRunner.MessageLogged += OnScriptMessage;
        _vsLoggedErrors.Clear();
        string graphsDir = Path.Combine(_project.Path, "Graphs");
        int graphCount = 0;
        if (Directory.Exists(graphsDir))
        {
            foreach (string file in Directory.GetFiles(graphsDir, "*.graph.json"))
            {
                try
                {
                    _graphRunner.AddGraph(VisualGraph.Load(file));
                    graphCount++;
                }
                catch (Exception ex)
                {
                    Log($"Graph load failed ({Path.GetFileName(file)}): {ex.Message}");
                }
            }
        }
        _graphRunner.Start();
        _graphsPanel?.AttachRunner(_graphRunner);

        _engine.Start();
        _scriptHost.Start((float)_engine.Time.ElapsedTime);
        _isPlaying = true;
        SetTopMode("Game");
        if (sources.Count > 0)
            Log($"Playing — {sources.Count} script file(s), {_scriptHost.InstanceCount} instance(s), {graphCount} graph(s).");
        else if (graphCount > 0)
            Log($"Playing — {graphCount} visual graph(s).");
        else
            Log("Playing (no scripts in Scripts/, no graphs in Graphs/).");
    }

    private void StopPlay()
    {
        _scriptHost.Stop();
        if (_graphRunner != null)
        {
            foreach (string err in _graphRunner.Errors)
            {
                if (_vsLoggedErrors.Add(err))
                    Log($"VS: {err}");
            }
            _graphRunner.MessageLogged -= OnScriptMessage;
            _graphRunner.Clear();
            _graphRunner = null;
        }
        _graphsPanel?.AttachRunner(null);
        _graphsPanel?.ShowDebug([]);
        _engine.Stop();
        _isPlaying = false;

        if (_playSnapshot != null)
        {
            try
            {
                var data = JsonSerializer.Deserialize<SceneData>(_playSnapshot,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data != null)
                {
                    _scene = SceneSerializer.Deserialize(data);
                    if (_selectedEntity != null)
                        _selectedEntity = _scene.FindById(_selectedEntity.Id);
                    RefreshHierarchy();
                    RefreshInspector();
                }
            }
            catch (Exception ex) { Log($"Restore failed: {ex.Message}"); }
            _playSnapshot = null;
        }

        _playInput = null;
        _scriptHost.Input = null;
        Log("Stopped — scene restored.");
    }

    private void RefreshPlayButton()
    {
        if (_playButton == null) return;
        _playButton.Content = UiTheme.Ico(_isPlaying ? Icons.Stop : Icons.Play, 16, Colors.White);
        _playButton.Background = UiTheme.B(_isPlaying ? UiTheme.Red : UiTheme.Accent);
    }

    // ================= UI root =================
    private void BuildUI()
    {
        _consoleLog = new TextBlock { Text = "", Foreground = UiTheme.B(UiTheme.Green), FontFamily = UiTheme.Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8) };

        var root = new DockPanel { Background = UiTheme.B(UiTheme.Bg) };

        var topbar = BuildTopBar();
        DockPanel.SetDock(topbar, Dock.Top);
        root.Children.Add(topbar);

        var bottom = BuildBottomPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _leftHost = BuildLeftPanel();
        DockPanel.SetDock(_leftHost, Dock.Left);
        root.Children.Add(_leftHost);

        _rightHost = new ContentControl();
        DockPanel.SetDock(_rightHost, Dock.Right);
        root.Children.Add(_rightHost);
        RebuildRight();

        root.Children.Add(BuildCenterArea());
        Content = root;
    }

    // ---------- Top bar: menu + mode tabs + playback ----------
    private Control BuildTopBar()
    {
        var projectButton = new Button
        {
            Content = UiTheme.Txt("Project", 12, UiTheme.Dim),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        projectButton.Click += (_, _) => _projectFlyout?.ShowAt(projectButton);
        _projectFlyout = BuildProjectMenu();

        var editButton = new Button
        {
            Content = UiTheme.Txt("Edit", 12, UiTheme.Dim),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        editButton.Click += (_, _) => _editFlyout?.ShowAt(editButton);
        _editFlyout = BuildEditMenu();

        var menu = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children =
            {
                MenuLabel("Scene", NewScene),
                projectButton,
                editButton,
                MenuLabel("Graphs", () => SetTopMode("Graphs")),
                MenuLabel("Debug", () => { _bottomTab = "Debugger"; RebuildBottom(); }),
                MenuLabel("Editor", OpenSettings),
                MenuLabel("Help", () => Log($"{EngineConstants.Name} v{EngineConstants.Version}")),
            }
        };
        DockPanel.SetDock(menu, Dock.Left);

        _modeTabsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        RebuildModeTabs();

        _playButton = new Button
        {
            Content = UiTheme.Ico(Icons.Play, 16, Colors.White),
            Width = 34, Height = 28,
            Background = UiTheme.B(UiTheme.Accent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _playButton.Click += (_, _) => TogglePlay();

        var stopButton = new Button
        {
            Content = UiTheme.Ico(Icons.Stop, 15, UiTheme.Dim),
            Width = 30, Height = 28,
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        stopButton.Click += (_, _) => { if (_isPlaying) TogglePlay(); };

        var statusText = UiTheme.Txt("Ready", 11, UiTheme.Faint);
        _statusText = statusText;
        statusText.MaxWidth = 180;
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        statusText.TextWrapping = TextWrapping.NoWrap;
        statusText.VerticalAlignment = VerticalAlignment.Center;

        _fpsText = UiTheme.Txt("FPS: 0", 11, UiTheme.Green, FontWeight.Medium);
        _fpsText.VerticalAlignment = VerticalAlignment.Center;
        _fpsText.Margin = new Thickness(8, 0, 0, 0);

        _entityCountText = UiTheme.Txt("0 objects", 11, UiTheme.Dim);
        _entityCountText.VerticalAlignment = VerticalAlignment.Center;
        _entityCountText.Margin = new Thickness(8, 0, 0, 0);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Children =
            {
                statusText,
                _entityCountText,
                _fpsText,
                _playButton,
                stopButton,
                UiTheme.IconBtn(Icons.Rocket, ExportStandalone, 15),
                UiTheme.IconBtn(Icons.Gear, OpenSettings, 15),
            }
        };
        DockPanel.SetDock(right, Dock.Right);

        var bar = new DockPanel { Height = 40, Background = UiTheme.B(UiTheme.Sidebar) };
        bar.Children.Add(menu);
        bar.Children.Add(right);
        bar.Children.Add(new Border { Child = _modeTabsPanel, HorizontalAlignment = HorizontalAlignment.Center });

        return new Border { BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    private static Button MenuLabel(string text, Action onClick)
    {
        var b = new Button
        {
            Content = UiTheme.Txt(text, 12, UiTheme.Dim),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void RebuildModeTabs()
    {
        if (_modeTabsPanel == null) return;
        _modeTabsPanel.Children.Clear();
        foreach (var mode in new[] { "2D", "3D", "Script", "Graphs", "Game" })
            _modeTabsPanel.Children.Add(ModeTab(mode));
        _modeTabsPanel.Children.Add(new Border { Width = 1, Height = 16, Background = UiTheme.B(UiTheme.Border), Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center });
        _modeTabsPanel.Children.Add(ModeTab("Asset Store", Icons.Upload));
    }

    private Control ModeTab(string label, string? icon = null)
    {
        bool active = label == _topMode;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) content.Children.Add(UiTheme.Ico(icon, 13, active ? UiTheme.Cyan : UiTheme.Dim));
        content.Children.Add(UiTheme.Txt(label, 12, active ? UiTheme.Cyan : UiTheme.Dim, active ? FontWeight.SemiBold : FontWeight.Normal));

        var b = new Button
        {
            Content = content,
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => SetTopMode(label);
        return b;
    }

    private void SetTopMode(string mode)
    {
        _topMode = mode;
        RebuildModeTabs();
        _viewMode = mode switch { "2D" => ViewportMode.Mode2D, "Game" => ViewportMode.Game, _ => ViewportMode.Scene };
        if (_softwareViewport != null) { _softwareViewport.Mode = _viewMode; _softwareViewport.InvalidateVisual(); }
        RebuildCenter();

        // Graphs mode: only header + graph workspace (hide side/bottom panels for max canvas)
        bool graphs = mode == "Graphs";
        if (_leftHost != null) _leftHost.IsVisible = !graphs;
        _rightHost.IsVisible = !graphs;
        _bottomHost.IsVisible = !graphs;
        if (_sceneTabBar != null) _sceneTabBar.IsVisible = !graphs;
    }

    // ---------- Left panel: Scene tree (top) + FileSystem (bottom) ----------
    private Control BuildLeftPanel()
    {
        var grid = new Grid
        {
            Width = 286,
            RowDefinitions =
            {
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
            }
        };

        var top = BuildSceneDock();
        var bottom = BuildFileSystemDock();
        Grid.SetRow(top, 0);
        Grid.SetRow(bottom, 1);
        grid.Children.Add(top);
        grid.Children.Add(bottom);

        var splitLine = new Border { Background = UiTheme.B(UiTheme.Border), Height = 1, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(splitLine, 1);
        grid.Children.Add(splitLine);

        return new Border { Background = UiTheme.B(UiTheme.Sidebar), BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
    }

    private Control BuildSceneDock()
    {
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 30,
            Children = { DockTab("Scene", () => { _leftTopTab = "Scene"; RebuildLeftTop(); }, () => _leftTopTab), DockTab("Import", () => { _leftTopTab = "Import"; RebuildLeftTop(); }, () => _leftTopTab) }
        };

        var toolbar = new DockPanel { Height = 32, Margin = new Thickness(8, 2) };
        var more = UiTheme.IconBtn(Icons.Dots, () => { }, 14);
        DockPanel.SetDock(more, Dock.Right);
        var search = new TextBox
        {
            Watermark = "Filter Nodes", FontSize = 11, Height = 26,
            Background = UiTheme.B(UiTheme.Bg), Foreground = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3),
            Margin = new Thickness(6, 0),
        };
        search.TextChanged += (_, _) => { _hierarchySearch = search.Text ?? ""; RefreshHierarchy(); };
        var addBtn = UiTheme.IconBtn(Icons.Plus, () => CreateEntity("Empty Actor"), 15);
        toolbar.Children.Add(more);
        toolbar.Children.Add(addBtn);
        toolbar.Children.Add(search);

        _hierarchyList = new StackPanel { Spacing = 1 };
        var scroll = new ScrollViewer { Content = _hierarchyList, Background = UiTheme.B(UiTheme.Bg) };

        var content = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        content.Children.Add(tabs);
        content.Children.Add(toolbar);
        content.Children.Add(_leftTopTab == "Scene" ? scroll : ImportPlaceholder());

        RefreshHierarchy();
        return content;
    }

    private void RebuildLeftTop()
    {
        // rebuild whole left panel to reflect tab switch (simple + reliable)
        RebuildLeft();
    }

    private void RebuildLeft()
    {
        if (Content is not DockPanel dp) return;
        var old = dp.Children.FirstOrDefault(c => DockPanel.GetDock(c) == Dock.Left);
        if (old != null) dp.Children.Remove(old);
        _leftHost = BuildLeftPanel();
        _leftHost.IsVisible = _topMode != "Graphs";
        DockPanel.SetDock(_leftHost, Dock.Left);
        dp.Children.Insert(0, _leftHost);
    }

    private static Control ImportPlaceholder() => new StackPanel
    {
        Margin = new Thickness(14, 30), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
        Children = { UiTheme.Ico(Icons.Upload, 26, UiTheme.Faint), UiTheme.TxtAt("Select a file to see import options.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center) }
    };

    private Control BuildFileSystemDock()
    {
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 30,
            Children = { DockTab("FileSystem", () => { _leftBottomTab = "FileSystem"; RebuildLeft(); }, () => _leftBottomTab), DockTab("History", () => { _leftBottomTab = "History"; RebuildLeft(); }, () => _leftBottomTab) }
        };

        var pathRow = new DockPanel { Height = 30, Margin = new Thickness(8, 4, 8, 2) };
        var newFolderBtn = UiTheme.IconBtn(Icons.FolderOutline, () => { }, 14);
        DockPanel.SetDock(newFolderBtn, Dock.Right);
        var backBtn = UiTheme.IconBtn(Icons.ChevronRight, () => { }, 13);
        var pathBox = new Border
        {
            Background = UiTheme.B(UiTheme.Bg), BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4), Margin = new Thickness(6, 0),
            Child = UiTheme.Txt("res://", 11, UiTheme.Text),
        };
        pathRow.Children.Add(newFolderBtn);
        pathRow.Children.Add(pathBox);

        var filterRow = new DockPanel { Height = 30, Margin = new Thickness(8, 0, 8, 4) };
        var sortBtn = UiTheme.IconBtn(Icons.Dots, () => { }, 14);
        DockPanel.SetDock(sortBtn, Dock.Right);
        var filter = new TextBox
        {
            Watermark = "Filter Files", FontSize = 11, Height = 26,
            Background = UiTheme.B(UiTheme.Bg), Foreground = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 0),
        };
        filter.TextChanged += (_, _) => { _fileFilter = filter.Text ?? ""; RefreshFileTree(); };
        filterRow.Children.Add(sortBtn);
        filterRow.Children.Add(filter);

        _fileTreeList = new StackPanel { Spacing = 1 };
        var scroll = new ScrollViewer { Content = _fileTreeList, Background = UiTheme.B(UiTheme.Bg) };

        var content = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        DockPanel.SetDock(pathRow, Dock.Top);
        DockPanel.SetDock(filterRow, Dock.Top);
        content.Children.Add(tabs);
        if (_leftBottomTab == "FileSystem")
        {
            content.Children.Add(pathRow);
            content.Children.Add(filterRow);
            content.Children.Add(scroll);
            RefreshFileTree();
        }
        else
        {
            content.Children.Add(new StackPanel { Margin = new Thickness(14, 30), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { UiTheme.Ico(Icons.Terminal, 26, UiTheme.Faint), UiTheme.TxtAt("No file history yet.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center) } });
        }

        return content;
    }

    private static Control DockTab(string label, Action onClick, Func<string> currentGetter)
    {
        bool active = label == currentGetter();
        var b = new Button
        {
            Content = UiTheme.Txt(label, 11, active ? UiTheme.Text : UiTheme.Faint, active ? FontWeight.SemiBold : FontWeight.Normal),
            Background = UiTheme.B(active ? UiTheme.Panel : Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(11, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void RefreshFileTree()
    {
        if (_fileTreeList == null) return;
        _fileTreeList.Children.Clear();

        _fileTreeList.Children.Add(FsRow("Favorites", Icons.Star, 0, header: true));

        string root = _project.Path;
        string label = string.IsNullOrEmpty(root) ? "res://" : "res://";
        _fileTreeList.Children.Add(FsRow(label, Icons.FolderOutline, 0, path: root, isFolder: true));

        if (!Directory.Exists(root)) return;

        var dirs = Directory.GetDirectories(root).Where(d => !d.EndsWith("bin") && !d.EndsWith("obj")).OrderBy(Path.GetFileName);
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (_fileFilter.Length > 0 && !name.Contains(_fileFilter, StringComparison.OrdinalIgnoreCase)) continue;
            _fileTreeList.Children.Add(FsRow(name + "/", Icons.FolderOutline, 1, path: d, isFolder: true));
        }

        var files = Directory.GetFiles(root).OrderBy(Path.GetFileName);
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (_fileFilter.Length > 0 && !name.Contains(_fileFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(f).ToLowerInvariant();
            _fileTreeList.Children.Add(FsRow(name, ext == ".cs" ? Icons.Code : Icons.File, 1, path: f, isFolder: false));
        }
    }

    private Control FsRow(string label, string icon, int indent, string? path = null, bool isFolder = false, bool header = false)
    {
        bool selected = path != null && _selectedFolder == path;
        var row = new Border
        {
            Background = selected ? UiTheme.B(Color.Parse("#1c2a6b")) : UiTheme.B(Colors.Transparent),
            Padding = new Thickness(10 + indent * 16, 5, 10, 5),
            Margin = new Thickness(4, 0),
            CornerRadius = new CornerRadius(5),
            Cursor = path != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { UiTheme.Ico(icon, 13, header ? UiTheme.Orange : (selected ? UiTheme.Cyan : UiTheme.Dim)), UiTheme.Txt(label, 11, selected ? UiTheme.Text : UiTheme.Dim) },
            },
        };
        if (path != null && !selected)
        {
            row.PointerEntered += (_, _) => row.Background = UiTheme.B(Color.Parse("#151d38"));
            row.PointerExited += (_, _) => row.Background = UiTheme.B(Colors.Transparent);
        }
        if (path != null)
        {
            row.PointerPressed += (_, _) =>
            {
                _selectedFolder = path;
                if (!isFolder)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".cs") { OpenScript(path); SetTopMode("Script"); }
                    else if (ext == ".obj") ImportObjFile(path);
                    else Log($"Opened {Path.GetFileName(path)}");
                }
                RefreshFileTree();
            };
        }
        return row;
    }

    // ---------- Center: tab strip + (viewport toolbar + viewport) | script editor | asset store ----------
    private Control BuildCenterArea()
    {
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1, GridUnitType.Star)) } };

        _sceneTabBar = BuildSceneTabBar();
        Grid.SetRow(_sceneTabBar, 0);
        grid.Children.Add(_sceneTabBar);

        _centerHost = new ContentControl();
        Grid.SetRow(_centerHost, 1);
        grid.Children.Add(_centerHost);

        RebuildCenter();
        return grid;
    }

    private Control BuildSceneTabBar()
    {
        var sceneTab = new Border
        {
            Background = UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(7, 7, 0, 0),
            Padding = new Thickness(12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { UiTheme.Ico(Icons.Scene, 13, UiTheme.Cyan), UiTheme.Txt($"[{_project.Name}] (*)", 12, UiTheme.Text, FontWeight.Medium), UiTheme.Ico(Icons.Close, 13, UiTheme.Faint) }
            },
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var plusTab = new Button
        {
            Content = UiTheme.Ico(Icons.Plus, 14, UiTheme.Dim),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        plusTab.Click += (_, _) => NewScene();

        return new Border
        {
            Background = UiTheme.B(UiTheme.Bg),
            Padding = new Thickness(10, 6, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { sceneTab, plusTab } },
        };
    }

    private void RebuildCenter()
    {
        if (_centerHost == null) return;
        _centerHost.Content = _topMode switch
        {
            "Script" => BuildScriptsPanel(),
            "Graphs" => BuildGraphsPanel(),
            "Asset Store" => BuildAssetStorePlaceholder(),
            _ => BuildViewportArea(),
        };
    }

    private Control BuildAssetStorePlaceholder() => new StackPanel
    {
        Margin = new Thickness(30, 60), Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
        Children = { UiTheme.Ico(Icons.Upload, 40, UiTheme.Faint), UiTheme.TxtAt("Lumo Asset Store", 15, UiTheme.Dim, FontWeight.Medium, HorizontalAlignment.Center), UiTheme.TxtAt("Not connected yet.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center) }
    };

    // ---------- Visual scripting (Graphs mode) ----------
    private Control BuildGraphsPanel()
    {
        if (_graphsPanel == null)
        {
            var panel = new GraphsPanel(_project.Path);
            panel.Notify += Log;
            _graphsPanel = panel;
        }
        _graphsPanel.AttachRunner(_graphRunner);
        return _graphsPanel;
    }

    private Control BuildViewportArea()
    {
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1, GridUnitType.Star)), new RowDefinition(new GridLength(220)) } };

        var toolbar = BuildViewportToolbar();
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var viewport = BuildViewport();
        Grid.SetRow(viewport, 1);
        grid.Children.Add(viewport);

        return grid;
    }

    private Control BuildViewportToolbar()
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var (ic, tool) in new[] { (Icons.MoveTool, "Select"), (Icons.MoveTool, "Move"), (Icons.Cube, "Rotate"), (Icons.Grid, "Scale") })
        {
            string t = tool;
            bool active = t == _activeTool;
            tools.Children.Add(UiTheme.IconBtn(ic, () => { _activeTool = t; Log($"Tool: {t}"); InvalidateOverlay(); }, 15, active ? UiTheme.Cyan : null));
        }
        tools.Children.Add(new Border { Width = 1, Height = 16, Background = UiTheme.B(UiTheme.Border), Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(UiTheme.IconBtn(Icons.FilePlus, () => CreateEntity("Empty Actor"), 15));
        tools.Children.Add(UiTheme.IconBtn(Icons.Dots, () => { }, 15));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                new Border { Padding = new Thickness(9, 4), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { UiTheme.Txt("Transform", 11, UiTheme.Dim), UiTheme.Ico(Icons.ChevronDown, 11, UiTheme.Faint) } } },
                new Border { Padding = new Thickness(9, 4), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { UiTheme.Txt("View", 11, UiTheme.Dim), UiTheme.Ico(Icons.ChevronDown, 11, UiTheme.Faint) } } },
            }
        };
        DockPanel.SetDock(right, Dock.Right);

        var bar = new DockPanel { Height = 34, Margin = new Thickness(8, 4) };
        bar.Children.Add(right);
        bar.Children.Add(tools);

        return new Border { Background = UiTheme.B(UiTheme.Panel), BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    // ---------- Viewport ----------
    private Control BuildViewport()
    {
        _glViewport = new GlViewport();
        _softwareViewport = new SoftwareViewport();
        _softwareViewport.Mode = _viewMode;
        _softwareViewport.ProjectRoot = _project.Path;
        _softwareViewport.EntityPicked += entity =>
        {
            _selectedEntity = entity;
            RefreshHierarchy();
            RefreshInspector();
            if (entity != null) Log($"Selected {entity.Name} (viewport).");
        };
        _softwareViewport.EntityMoved += _ => RefreshInspector();
        _softwareViewport.TransformDragStarted += () => PushSceneUndo();
        _softwareViewport.CanEditTransform = () => !_isPlaying;

        _viewportPanel = new Panel { Background = UiTheme.B(Color.Parse("#0b1020")) };

        if (!_glViewport.IsReady)
        {
            _viewportPanel.Children.Add(_softwareViewport);
            _glViewport.IsVisible = false;
        }
        else
        {
            _viewportPanel.Children.Add(_glViewport);
            _softwareViewport.IsVisible = false;
        }

        _viewportPanel.Children.Add(BuildViewportOverlay());

        _viewportPanel.AttachedToVisualTree += (_, _) =>
            System.Threading.Tasks.Task.Delay(250).ContinueWith(_ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_glViewport!.IsReady)
                    {
                        _softwareViewport!.IsVisible = false;
                        _glViewport.IsVisible = true;
                        Log("OpenGL viewport active.");
                    }
                    else
                    {
                        _softwareViewport!.IsVisible = true;
                        _glViewport.IsVisible = false;
                        Log("OpenGL unavailable — software renderer active.");
                    }
                }));

        return _viewportPanel;
    }

    private Control BuildViewportOverlay()
    {
        var rightIcons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        rightIcons.Children.Add(UiTheme.IconBtn(Icons.Camera, CreateCamera, 15));
        rightIcons.Children.Add(UiTheme.IconBtn(Icons.Bulb, () =>
        {
            _showGizmos = !_showGizmos;
            if (_softwareViewport != null) { _softwareViewport.ShowGizmos = _showGizmos; _softwareViewport.InvalidateVisual(); }
            Log($"Gizmos: {(_showGizmos ? "on" : "off")}");
            InvalidateOverlay();
        }, 15, _showGizmos ? UiTheme.Orange : null));
        rightIcons.Children.Add(UiTheme.IconBtn(Icons.Grid, () =>
        {
            _showGrid = !_showGrid;
            if (_softwareViewport != null) { _softwareViewport.ShowGrid = _showGrid; _softwareViewport.InvalidateVisual(); }
            Log($"Grid: {(_showGrid ? "on" : "off")}");
            InvalidateOverlay();
        }, 15, _showGrid ? UiTheme.Cyan : null));
        rightIcons.Children.Add(UiTheme.IconBtn(Icons.Monitor, () => { _showStats = !_showStats; Log($"Stats overlay: {(_showStats ? "on" : "off")}"); InvalidateOverlay(); }, 15, _showStats ? UiTheme.Cyan : null));
        var rightHost = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0), Background = UiTheme.B(Color.Parse("#e610172a")), CornerRadius = new CornerRadius(8), Padding = new Thickness(5), Child = rightIcons };

        string projLabel = _viewMode switch { ViewportMode.Mode2D => "Orthographic", ViewportMode.Game => "Game Camera", _ => "Perspective" };
        var persp = new Border
        {
            Background = UiTheme.B(Color.Parse("#10172acc")),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 12, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { UiTheme.Ico(Icons.Grid, 13, UiTheme.Cyan), UiTheme.Txt(projLabel, 11, UiTheme.Text), UiTheme.Ico(Icons.ChevronDown, 12, UiTheme.Dim) } },
        };

        var hint = new TextBlock
        {
            Text = _viewMode == ViewportMode.Mode2D ? "LMB/MMB: Pan  |  Scroll: Zoom" : "LMB: Orbit  |  MMB: Pan  |  Scroll: Zoom",
            FontSize = 10,
            Foreground = UiTheme.B(UiTheme.Faint),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14),
            IsVisible = _showStats,
        };

        return new Panel { Children = { persp, rightHost, hint } };
    }

    private void InvalidateOverlay()
    {
        if (_viewportPanel == null) return;
        int last = _viewportPanel.Children.Count - 1;
        if (last < 0) return;
        _viewportPanel.Children.RemoveAt(last);
        _viewportPanel.Children.Add(BuildViewportOverlay());
    }

    // ---------- Hierarchy ----------
    private void RefreshHierarchy()
    {
        if (_hierarchyList == null) return;
        _hierarchyList.Children.Clear();
        foreach (var entity in _scene.AllEntities)
        {
            if (entity.Parent != null) continue;
            if (_hierarchySearch.Length > 0 && !entity.Name.Contains(_hierarchySearch, StringComparison.OrdinalIgnoreCase))
                continue;
            _hierarchyList.Children.Add(HierItem(entity, 0));
            foreach (var child in entity.Children)
                _hierarchyList.Children.Add(HierItem(child, 1));
        }
    }

    private static string EntityIcon(Entity e) => e switch
    {
        { Camera: not null } => Icons.Camera,
        { Light: not null } => Icons.Bulb,
        { MeshRenderer: not null } => Icons.Cube,
        _ => Icons.Box,
    };

    private Border HierItem(Entity entity, int indent)
    {
        bool sel = _selectedEntity?.Id == entity.Id;
        var name = UiTheme.Txt(entity.Name, 12, sel ? Colors.White : UiTheme.Text, sel ? FontWeight.SemiBold : FontWeight.Normal);
        name.TextTrimming = TextTrimming.CharacterEllipsis;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8 + indent * 14, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { UiTheme.Ico(EntityIcon(entity), 14, sel ? UiTheme.Cyan : UiTheme.Dim), name },
        };

        var eye = UiTheme.IconBtn(Icons.Eye, () =>
        {
            if (entity.MeshRenderer != null)
            {
                entity.MeshRenderer.IsVisible = !entity.MeshRenderer.IsVisible;
                Log($"{entity.Name}: {(entity.MeshRenderer.IsVisible ? "visible" : "hidden")}");
            }
        }, 14);
        DockPanel.SetDock(eye, Dock.Right);

        var wrap = new DockPanel { Children = { eye, row } };

        var bg = new Border
        {
            Background = sel ? UiTheme.B(Color.Parse("#1c2a6b")) : UiTheme.B(Colors.Transparent),
            Child = wrap,
            Padding = new Thickness(3, 6),
            Margin = new Thickness(5, 0),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (!sel)
        {
            bg.PointerEntered += (_, _) => bg.Background = UiTheme.B(Color.Parse("#151d38"));
            bg.PointerExited += (_, _) => bg.Background = UiTheme.B(Colors.Transparent);
        }
        bg.PointerPressed += (_, _) =>
        {
            _selectedEntity = entity;
            RefreshHierarchy();
            RefreshInspector();
        };
        return bg;
    }

    // ---------- Right dock: Inspector / Signals ----------
    private Control BuildRightPanel()
    {
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 30,
            Children = { DockTab("Inspector", () => { _rightTab = "Inspector"; RebuildRight(); }, () => _rightTab), DockTab("Signals", () => { _rightTab = "Signals"; RebuildRight(); }, () => _rightTab) }
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 30,
            Spacing = 2,
            Margin = new Thickness(6, 0),
            Children =
            {
                UiTheme.IconBtn(Icons.FilePlus, () => { }, 13),
                UiTheme.IconBtn(Icons.FolderOutline, () => { }, 13),
                UiTheme.IconBtn(Icons.Save, SaveProject, 13),
                UiTheme.IconBtn(Icons.Dots, () => { }, 13),
            }
        };

        var content = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        content.Children.Add(tabs);
        content.Children.Add(toolbar);

        if (_rightTab == "Inspector")
        {
            _inspectorContent = new StackPanel { Spacing = 0 };
            var scroll = new ScrollViewer { Content = _inspectorContent, Background = UiTheme.B(UiTheme.Panel) };
            content.Children.Add(scroll);
            RefreshInspector();
        }
        else
        {
            content.Children.Add(new StackPanel
            {
                Margin = new Thickness(14, 30), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { UiTheme.Ico(Icons.Bell, 26, UiTheme.Faint), UiTheme.TxtAt("No signals for this selection.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center) }
            });
        }

        return new Border { Width = 320, Background = UiTheme.B(UiTheme.Panel), BorderBrush = UiTheme.B(UiTheme.Border), BorderThickness = new Thickness(1, 0, 0, 0), Child = content };
    }

    private void RebuildRight()
    {
        if (_rightHost == null) return;
        _rightHost.Content = BuildRightPanel();
    }

    private void RefreshInspector()
    {
        if (_softwareViewport != null && !ReferenceEquals(_softwareViewport.SelectedEntity, _selectedEntity))
            _softwareViewport.SelectedEntity = _selectedEntity;
        if (_inspectorContent == null) return;
        _inspectorContent.Children.Clear();
        if (_selectedEntity == null)
        {
            _inspectorContent.Children.Add(new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(16, 40),
                Children = { UiTheme.Ico(Icons.Box, 34, UiTheme.Faint), UiTheme.TxtAt("No actor selected", 13, UiTheme.Dim, FontWeight.Medium, HorizontalAlignment.Center), UiTheme.TxtAt("Select an actor in the Scene tree.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center) }
            });
            return;
        }

        var e = _selectedEntity;

        _inspectorContent.Children.Add(new Border
        {
            Background = UiTheme.B(Color.Parse("#161d33")),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(10, 10, 10, 8),
            CornerRadius = new CornerRadius(9),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { UiTheme.Ico(EntityIcon(e), 20, UiTheme.Cyan), UiTheme.Txt(e.Name, 15, UiTheme.Text, FontWeight.Bold) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { MiniChip("Tag", "Player"), MiniChip("Layer", "Default") } },
                }
            }
        });

        var t = InsSection("Transform");
        var eul = e.Transform.GetEulerAngles();
        Vec3Row(t, "Position", e.Transform.Position.X, e.Transform.Position.Y, e.Transform.Position.Z);
        Vec3Row(t, "Rotation", eul.X, eul.Y, eul.Z);
        Vec3Row(t, "Scale", e.Transform.Scale.X, e.Transform.Scale.Y, e.Transform.Scale.Z);
        _inspectorContent.Children.Add(t);

        if (e.MeshRenderer != null)
        {
            var s = InsSection("Static Mesh");
            InsRow(s, "Mesh", e.MeshRenderer.MeshName ?? "None");
            InsRow(s, "Visible", e.MeshRenderer.IsVisible ? "Yes" : "No");
            _inspectorContent.Children.Add(s);
        }
        if (e.Light != null)
        {
            var s = InsSection("Light");
            InsRow(s, "Type", e.Light.LightType.ToString());
            InsRow(s, "Intensity", e.Light.Intensity.ToString("F2"));
            _inspectorContent.Children.Add(s);
        }
        if (e.Camera != null)
        {
            var s = InsSection("Camera");
            InsRow(s, "Primary", e.Camera.IsPrimary ? "Yes" : "No");
            InsRow(s, "FOV", e.Camera.FieldOfView + "°");
            InsRow(s, "Clip", $"{e.Camera.NearPlane} — {e.Camera.FarPlane}");
            _inspectorContent.Children.Add(s);
        }
        if (e.SpriteRenderer != null)
        {
            var sp = e.SpriteRenderer;
            var s = InsSection("Sprite");
            InsRow(s, "Texture", string.IsNullOrEmpty(sp.SpritePath) ? "None" : sp.SpritePath);
            InsRow(s, "Size", $"{sp.Width:F2} × {sp.Height:F2}");
            s.Children.Add(ActionSmall("Import Texture…", () => { _ = ImportTextureAsync(e); }));
            _inspectorContent.Children.Add(s);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 14, 12, 20),
            Children = { ActionSmall("+ Add Component", ShowAddComponentMenu, true), ActionSmall("Duplicate", DuplicateSelected), ActionSmall("Delete", DeleteSelected) }
        };
        _inspectorContent.Children.Add(actions);
    }

    private static Border MiniChip(string label, string value)
        => new()
        {
            Background = UiTheme.B(UiTheme.Card),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { UiTheme.Txt(label, 10, UiTheme.Faint), UiTheme.Txt(value, 11, UiTheme.Text) } },
        };

    private static StackPanel InsSection(string title)
    {
        var body = new StackPanel { Background = UiTheme.B(UiTheme.Panel), Margin = new Thickness(0, 0, 0, 6) };
        body.Children.Add(new Border
        {
            Background = UiTheme.B(Color.Parse("#161d33")),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(6, 0, 6, 6),
            Padding = new Thickness(9, 7),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiTheme.Ico(Icons.ChevronDown, 12, UiTheme.Dim), UiTheme.Txt(title.ToUpperInvariant(), 11, UiTheme.Dim, FontWeight.SemiBold) } }
        });
        return body;
    }

    private static void InsRow(StackPanel section, string label, string value)
    {
        section.Children.Add(new DockPanel
        {
            Margin = new Thickness(12, 3),
            Children = { UiTheme.TxtAt(value, 11, UiTheme.Text, FontWeight.Medium, HorizontalAlignment.Right), UiTheme.Txt(label, 11, UiTheme.Dim) }
        });
    }

    private static void Vec3Row(StackPanel section, string label, float x, float y, float z)
    {
        section.Children.Add(UiTheme.TxtAt(label, 11, UiTheme.Dim, FontWeight.Normal, HorizontalAlignment.Left, new Thickness(12, 6, 0, 3)));
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)) },
            Margin = new Thickness(12, 0, 12, 4),
        };
        void Add(int col, string axis, float val, Color c)
        {
            var cell = new Border
            {
                Background = UiTheme.B(UiTheme.Bg),
                BorderBrush = UiTheme.B(UiTheme.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 4),
                Margin = new Thickness(0, 0, 5, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { UiTheme.Txt(axis, 10, c, FontWeight.Bold), UiTheme.Txt(val.ToString("F2"), 11, UiTheme.Text) } },
            };
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }
        Add(0, "X", x, Color.Parse("#e06666"));
        Add(1, "Y", y, Color.Parse("#66c266"));
        Add(2, "Z", z, Color.Parse("#6699e6"));
        section.Children.Add(grid);
    }

    private static Button ActionSmall(string label, Action act, bool primary = false)
    {
        var b = new Button
        {
            Content = UiTheme.Txt(label, 11, primary ? Colors.White : UiTheme.Text, FontWeight.Medium),
            Background = UiTheme.B(primary ? UiTheme.Accent : UiTheme.Card),
            Foreground = UiTheme.B(primary ? Colors.White : UiTheme.Text),
            BorderBrush = UiTheme.B(primary ? UiTheme.Accent : UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => act();
        return b;
    }

    // ---------- Center wraps right dock too (built alongside) ----------
    // NOTE: right dock is attached from BuildEditorRoot below.

    // ---------- Bottom panel: Output / Debugger / Audio / Animation / Shader Editor ----------
    private Control BuildBottomPanel()
    {
        _bottomTabsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        RebuildBottomTabs();

        var versionTxt = UiTheme.Txt($"{EngineConstants.Name} {EngineConstants.Version}", 10, UiTheme.Faint);
        DockPanel.SetDock(versionTxt, Dock.Right);

        var clear = UiTheme.ActionButton("Clear", null, () => { _logMessages.Clear(); _consoleLog.Text = ""; });
        clear.Padding = new Thickness(11, 4);
        clear.IsVisible = _bottomTab == "Output";
        DockPanel.SetDock(clear, Dock.Right);

        var head = new Border
        {
            Background = UiTheme.B(Color.Parse("#161d33")),
            Height = 30,
            Padding = new Thickness(8, 0),
            Child = new DockPanel { Children = { versionTxt, clear, _bottomTabsPanel } },
        };
        DockPanel.SetDock(head, Dock.Top);

        _bottomHost = new ContentControl { Content = BuildConsolePanel(), MaxHeight = 200 };

        var panel = new DockPanel { Background = UiTheme.B(UiTheme.Panel) };
        panel.Children.Add(head);
        panel.Children.Add(_bottomHost);
        return panel;
    }

    private Control BottomTab(string name)
    {
        bool active = name == _bottomTab;
        string icon = name switch { "Output" => Icons.Terminal, "Debugger" => Icons.Dashboard, "Audio" => Icons.Bell, "Animation" => Icons.PlayCircle, _ => Icons.Code };
        var b = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { UiTheme.Ico(icon, 13, active ? UiTheme.Cyan : UiTheme.Faint), UiTheme.Txt(name, 11, active ? UiTheme.Text : UiTheme.Faint, active ? FontWeight.SemiBold : FontWeight.Normal) }
            },
            Background = UiTheme.B(active ? UiTheme.Panel : Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => { _bottomTab = name; RebuildBottom(); };
        return b;
    }

    private void RebuildBottom()
    {
        if (_bottomHost == null) return;
        _bottomHost.Content = _bottomTab switch
        {
            "Debugger" => BuildProfilerPanel(),
            "Audio" => BuildPlaceholderPanel("Audio", "No audio buses configured yet."),
            "Animation" => BuildAnimationPanel(),
            "Shader Editor" => BuildPlaceholderPanel("Shader Editor", "Select a shader asset to edit."),
            _ => BuildConsolePanel(),
        };
        RebuildBottomTabs();
    }

    private void RebuildBottomTabs()
    {
        if (_bottomTabsPanel == null) return;
        _bottomTabsPanel.Children.Clear();
        foreach (var name in new[] { "Output", "Debugger", "Audio", "Animation", "Shader Editor" })
            _bottomTabsPanel.Children.Add(BottomTab(name));
    }

    private static Control BuildPlaceholderPanel(string title, string message) => new ScrollViewer
    {
        Background = UiTheme.B(UiTheme.Bg),
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children = { UiTheme.Txt(title, 13, UiTheme.Text, FontWeight.SemiBold), UiTheme.Txt(message, 11, UiTheme.Faint) }
        }
    };

    private ScrollViewer? _consoleScroll;
    private Control BuildConsolePanel()
    {
        // Singleton: re-wrapping the same TextBlock in a fresh ScrollViewer while the
        // old one is still attached throws "already has a visual parent".
        _consoleScroll ??= new ScrollViewer { Content = _consoleLog, Background = UiTheme.B(UiTheme.Bg) };
        return _consoleScroll;
    }

    // ---------- Scripts (full-panel, shown when top mode = "Script") ----------
    private string ScriptsDir => Path.Combine(_project.Path, "Scripts");

    private Control BuildScriptsPanel()
    {
        Directory.CreateDirectory(ScriptsDir);
        var files = Directory.GetFiles(ScriptsDir, "*.cs").OrderBy(Path.GetFileName).ToArray();

        var list = new StackPanel { Spacing = 2 };
        var newBtn = UiTheme.ActionButton("New Script", Icons.Plus, AddScript);
        newBtn.Padding = new Thickness(10, 5);
        newBtn.Margin = new Thickness(8, 8, 8, 4);
        list.Children.Add(newBtn);

        foreach (var f in files)
        {
            string path = f;
            string name = Path.GetFileName(f);
            bool sel = _currentScriptPath == path;
            var row = new Border
            {
                Background = sel ? UiTheme.B(Color.Parse("#1c2a6b")) : UiTheme.B(Colors.Transparent),
                Padding = new Thickness(12, 6),
                Margin = new Thickness(5, 0),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiTheme.Ico(Icons.Code, 13, UiTheme.Purple), UiTheme.Txt(name, 11, sel ? Colors.White : UiTheme.Text) } },
            };
            if (!sel)
            {
                row.PointerEntered += (_, _) => row.Background = UiTheme.B(Color.Parse("#151d38"));
                row.PointerExited += (_, _) => row.Background = UiTheme.B(Colors.Transparent);
            }
            row.PointerPressed += (_, _) => OpenScript(path);
            list.Children.Add(row);
        }

        var listScroll = new ScrollViewer { Content = list, Width = 200, Background = UiTheme.B(UiTheme.Bg) };

        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = UiTheme.Mono,
            FontSize = 12,
            Background = UiTheme.B(UiTheme.Bg),
            Foreground = UiTheme.B(UiTheme.Text),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12),
        };
        editor.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        editor.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        string? scriptPath = _currentScriptPath;
        editor.Text = scriptPath != null && File.Exists(scriptPath)
            ? File.ReadAllText(scriptPath)
            : "// Select or create a script.\n// Scripts compile when you press Play.\n";
        _scriptEditor = editor;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 10, 12, 0) };
        var saveBtn = UiTheme.ActionButton("Save", Icons.Save, () =>
        {
            if (_currentScriptPath == null) { Log("No script open."); return; }
            File.WriteAllText(_currentScriptPath, editor.Text);
            Log($"Saved {Path.GetFileName(_currentScriptPath)}");
        });
        saveBtn.Padding = new Thickness(12, 6);
        var compileBtn = UiTheme.ActionButton("Compile", Icons.Code, () =>
        {
            if (_currentScriptPath != null) File.WriteAllText(_currentScriptPath, editor.Text);
            CompileScriptsNow();
        });
        compileBtn.Padding = new Thickness(12, 6);
        toolbar.Children.Add(saveBtn);
        toolbar.Children.Add(compileBtn);
        if (_currentScriptPath != null)
            toolbar.Children.Add(UiTheme.Txt(Path.GetFileName(_currentScriptPath), 11, UiTheme.Dim));

        _scriptErrorText = UiTheme.Txt("", 11, UiTheme.Red);
        _scriptErrorText.Margin = new Thickness(12, 6, 12, 0);
        _scriptErrorText.TextWrapping = TextWrapping.Wrap;
        _scriptErrorText.IsVisible = false;

        var right = new StackPanel();
        right.Children.Add(toolbar);
        right.Children.Add(_scriptErrorText);
        right.Children.Add(new Border
        {
            Margin = new Thickness(12, 8),
            Background = UiTheme.B(UiTheme.Bg),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new ScrollViewer { Content = editor },
        });

        var split = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(200)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)) } };
        Grid.SetColumn(listScroll, 0);
        Grid.SetColumn(right, 1);
        split.Children.Add(listScroll);
        split.Children.Add(right);
        var line = new Border { Background = UiTheme.B(UiTheme.Border), Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(line, 1);
        split.Children.Add(line);
        return split;
    }

    private void OpenScript(string path)
    {
        _currentScriptPath = path;
        _topMode = "Script";
        RebuildModeTabs();
        RebuildCenter();
    }

    private void AddScript()
    {
        Directory.CreateDirectory(ScriptsDir);
        int n = 1;
        string path;
        do { path = Path.Combine(ScriptsDir, $"Script{n}.cs"); n++; } while (File.Exists(path));

        string cls = Path.GetFileNameWithoutExtension(path);
        File.WriteAllText(path, $$"""
            using Lumo.Engine.Scripting;

            public class {{cls}} : LumoScript
            {
                public override void OnStart()
                {
                    Log("{{cls}} started");
                }

                public override void OnUpdate(float dt)
                {
                    // Your game logic here, e.g.:
                    // Position += new System.Numerics.Vector3(dt, 0, 0);
                }
            }
            """);

        OpenScript(path);
        Log($"Created {Path.GetFileName(path)}");
    }

    private void CompileScriptsNow()
    {
        Directory.CreateDirectory(ScriptsDir);
        var sources = Directory.GetFiles(ScriptsDir, "*.cs").Select(File.ReadAllText).ToList();
        if (sources.Count == 0) { Log("No scripts to compile."); return; }

        if (_scriptHost.Compile(sources))
        {
            if (_scriptErrorText != null) { _scriptErrorText.IsVisible = false; _scriptErrorText.Text = ""; }
            Log($"Compiled {sources.Count} script(s) OK.");
        }
        else
        {
            var msg = string.Join("\n", _scriptHost.Errors.Take(6));
            if (_scriptErrorText != null) { _scriptErrorText.Text = msg; _scriptErrorText.IsVisible = true; }
            foreach (var e in _scriptHost.Errors) Log($"CS: {e}");
        }
    }

    private void AttachScriptToSelected(string className)
    {
        if (_selectedEntity == null) { Log("Select an entity first."); return; }
        _selectedEntity.Scripts ??= new Lumo.Engine.Scripting.ScriptComponent();
        if (!_selectedEntity.Scripts.ScriptNames.Contains(className))
            _selectedEntity.Scripts.ScriptNames.Add(className);
        RefreshInspector();
        Log($"Attached {className} to {_selectedEntity.Name}");
    }

    // ---------- Animation ----------
    private Control BuildAnimationPanel() => new ScrollViewer
    {
        Background = UiTheme.B(UiTheme.Bg),
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { UiTheme.Txt("Animation", 13, UiTheme.Text, FontWeight.SemiBold), UiTheme.Txt("No animation clips in this project yet.", 11, UiTheme.Faint) } }
    };

    private Control BuildProfilerPanel()
    {
        _profilerStats = UiTheme.Txt("FPS: 0", 12, UiTheme.Green);
        return new ScrollViewer
        {
            Background = UiTheme.B(UiTheme.Bg),
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { UiTheme.Txt("Debugger", 13, UiTheme.Text, FontWeight.SemiBold), _profilerStats } }
        };
    }

    // ---------- Project-level actions ----------
    private void NewScene()
    {
        if (_isPlaying) { Log("Stop play mode first."); return; }
        _scene = new SceneType { Name = _project.Name };
        _sceneHistory.Clear();
        _selectedEntity = null;
        RefreshHierarchy();
        RefreshInspector();
        SaveProject();
        Log("New scene created.");
    }

    private void BuildProject()
    {
        Log($"Building {_project.Name} (Windows x64)...");
        try
        {
            Directory.CreateDirectory(ScriptsDir);
            var sources = Directory.GetFiles(ScriptsDir, "*.cs").Select(File.ReadAllText).ToList();
            if (sources.Count > 0 && !_scriptHost.Compile(sources))
            {
                Log($"Build failed: {_scriptHost.Errors.Count} script error(s).");
                foreach (var e in _scriptHost.Errors.Take(3)) Log($"CS: {e}");
                return;
            }
            SaveProject();
            Log($"Build succeeded: {sources.Count} script(s), {_scene.AllEntities.Count} entity(ies).");
        }
        catch (Exception ex) { Log($"Build failed: {ex.Message}"); }
    }

    private void OpenSettings()
    {
        Log($"Settings — {EngineConstants.Name} v{EngineConstants.Version} | Renderer: {(_glViewport != null && _glViewport.IsReady ? "OpenGL" : "Software")} | Project: {_project.Path}");
    }

    // ---------- Export (publish standalone game) ----------
    private MenuFlyout BuildProjectMenu()
    {
        var saveItem = new MenuItem { Header = "Save Project" };
        saveItem.Click += (_, _) => SaveProject();
        var buildItem = new MenuItem { Header = "Build Scripts" };
        buildItem.Click += (_, _) => BuildProject();
        var exportItem = new MenuItem { Header = "Export Standalone (win-x64)..." };
        exportItem.Click += (_, _) => ExportStandalone();
        return new MenuFlyout { ItemsSource = new object[] { saveItem, buildItem, new Separator(), exportItem } };
    }

    private MenuFlyout BuildEditMenu()
    {
        var undoItem = new MenuItem { Header = "Undo    Ctrl+Z" };
        undoItem.Click += (_, _) => RouteUndo();
        var redoItem = new MenuItem { Header = "Redo    Ctrl+Y" };
        redoItem.Click += (_, _) => RouteRedo();
        return new MenuFlyout { ItemsSource = new object[] { undoItem, redoItem } };
    }

    // ---------- Undo / redo ----------
    private void PushSceneUndo()
    {
        if (_isPlaying) return;
        try { _sceneHistory.Push(_scene.Serialize()); }
        catch (Exception ex) { Log($"Undo snapshot failed: {ex.Message}"); }
    }

    private void RouteUndo()
    {
        if (_topMode == "Graphs")
            _graphsPanel?.UndoGraph();
        else
            UndoScene();
    }

    private void RouteRedo()
    {
        if (_topMode == "Graphs")
            _graphsPanel?.RedoGraph();
        else
            RedoScene();
    }

    private void UndoScene()
    {
        if (_isPlaying) { Log("Stop play mode first."); return; }
        string? previous = _sceneHistory.Undo(_scene.Serialize());
        if (previous is null) { Log("Nothing to undo."); return; }
        ApplySceneJson(previous);
        Log("Undo.");
    }

    private void RedoScene()
    {
        if (_isPlaying) { Log("Stop play mode first."); return; }
        string? next = _sceneHistory.Redo(_scene.Serialize());
        if (next is null) { Log("Nothing to redo."); return; }
        ApplySceneJson(next);
        Log("Redo.");
    }

    private void ApplySceneJson(string json)
    {
        try
        {
            _scene = SceneType.Deserialize(json);
            _selectedEntity = null;
            RefreshHierarchy();
            RefreshInspector();
        }
        catch (Exception ex) { Log($"Undo/redo failed: {ex.Message}"); }
    }

    private async void ExportStandalone()
    {
        if (_exporting) { Log("Export already running."); return; }

        string? runtimeProj = FindRuntimeProject();
        if (runtimeProj == null)
        {
            Log("Export failed: Lumo.Runtime project not found (dev layout required).");
            return;
        }
        string? dotnet = FindDotnet();
        if (dotnet == null)
        {
            Log("Export failed: dotnet SDK not found.");
            return;
        }

        _exporting = true;
        try
        {
            SaveProject();
            _graphsPanel?.SaveCurrentGraph();

            string safe = new string(_project.Name.Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0) safe = "Game";
            string outDir = Path.Combine(_project.Path, "Builds", safe + "-win-x64");
            Log($"Export: publishing Lumo.Runtime → {outDir}");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var psi = new ProcessStartInfo
            {
                FileName = dotnet,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add(runtimeProj);
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-r"); psi.ArgumentList.Add("win-x64");
            psi.ArgumentList.Add("--self-contained"); psi.ArgumentList.Add("true");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add("--nologo");

            using var proc = Process.Start(psi);
            if (proc == null) { Log("Export failed: could not start dotnet."); return; }

            int printed = 0;
            void HandleLine(string? line)
            {
                if (line == null) return;
                int n = Interlocked.Increment(ref printed);
                bool important = line.Contains("error", StringComparison.OrdinalIgnoreCase)
                              || line.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
                              || line.Contains("warning", StringComparison.OrdinalIgnoreCase);
                if (n > 40 && !important) return;
                string text = line;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Log($"[pub] {text}"));
            }

            proc.OutputDataReceived += (_, e) => HandleLine(e.Data);
            proc.ErrorDataReceived += (_, e) => HandleLine(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0) { Log($"Export failed (exit {proc.ExitCode})."); return; }

            CopyProjectContent(outDir);
            string exe = Path.Combine(outDir, "Lumo.Runtime.exe");
            Log(File.Exists(exe) ? $"Export OK: {exe}" : $"Publish finished but exe not found: {exe}");
        }
        catch (Exception ex) { Log($"Export failed: {ex.Message}"); }
        finally { _exporting = false; }
    }

    private void CopyProjectContent(string outDir)
    {
        string root = _project.Path;
        foreach (string dir in new[] { "Scenes", "Graphs", "Scripts", "Assets" })
        {
            string src = Path.Combine(root, dir);
            if (Directory.Exists(src))
                CopyDirectory(src, Path.Combine(outDir, dir));
        }
        string projectFile = Path.Combine(root, "Project.json");
        if (File.Exists(projectFile))
            File.Copy(projectFile, Path.Combine(outDir, "Project.json"), true);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (string sub in Directory.GetDirectories(src))
            CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
    }

    private static string? FindRuntimeProject()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "Lumo.Runtime", "Lumo.Runtime.csproj");
            if (File.Exists(candidate)) return candidate;
            string flat = Path.Combine(dir, "Lumo.Runtime.csproj");
            if (File.Exists(flat)) return flat;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return null;
    }

    private static string? FindDotnet()
    {
        var candidates = new List<string>();
        string env = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "";
        if (env.Length > 0) candidates.Add(env);
        string self = Environment.ProcessPath ?? "";
        if (self.Length > 0 && Path.GetFileName(self).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            candidates.Add(self);
        candidates.Add(@"C:\Program Files\dotnet\dotnet.exe");
        candidates.Add(@"C:\Program Files (x86)\dotnet\dotnet.exe");
        candidates.Add("/usr/share/dotnet/dotnet");
        candidates.Add("/usr/local/share/dotnet/dotnet");
        foreach (string c in candidates)
            if (File.Exists(c)) return c;

        foreach (string pathDir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (pathDir.Length == 0) continue;
            string c = Path.Combine(pathDir, "dotnet.exe");
            if (File.Exists(c)) return c;
            c = Path.Combine(pathDir, "dotnet");
            if (File.Exists(c)) return c;
        }
        return null;
    }

    // ---------- Entity actions ----------
    private void CreateEntity(string name)
    {
        PushSceneUndo();
        var entity = _scene.CreateEntity(name);
        _selectedEntity = entity;
        RefreshHierarchy();
        RefreshInspector();
        Log($"Created: {name}");
    }

    private void CreateEntityWithMesh(string name, string mesh)
    {
        PushSceneUndo();
        var entity = _scene.CreateEntity(name);
        entity.MeshRenderer = new MeshRendererComponent { MeshName = mesh };
        _selectedEntity = entity;
        RefreshHierarchy();
        RefreshInspector();
        Log($"Created {name} with {mesh} mesh.");
    }

    private void CreateCamera()
    {
        PushSceneUndo();
        var e = _scene.CreateEntity("Camera");
        e.Camera = new CameraComponent { IsPrimary = false };
        e.Transform.Position = new System.Numerics.Vector3(0, 1, 5);
        _selectedEntity = e;
        RefreshHierarchy();
        RefreshInspector();
        Log("Created Camera.");
    }

    private void CreateLight()
    {
        PushSceneUndo();
        var e = _scene.CreateEntity("Directional Light");
        e.Light = new LightComponent { LightType = LightType.Directional, Intensity = 1.0f };
        e.Transform.SetRotationFromEuler(-45, 0, 0);
        _selectedEntity = e;
        RefreshHierarchy();
        RefreshInspector();
        Log("Created Directional Light.");
    }

    private void DuplicateSelected()
    {
        if (_selectedEntity == null) { Log("Nothing to duplicate."); return; }
        PushSceneUndo();
        var copy = _scene.CreateEntity($"{_selectedEntity.Name} (Copy)");
        copy.Transform.Position = _selectedEntity.Transform.Position + new System.Numerics.Vector3(1, 0, 0);
        _selectedEntity = copy;
        RefreshHierarchy();
        RefreshInspector();
        Log($"Duplicated: {copy.Name}");
    }

    private void DeleteSelected()
    {
        if (_selectedEntity == null) { Log("Nothing to delete."); return; }
        PushSceneUndo();
        var name = _selectedEntity.Name;
        _scene.DestroyEntity(_selectedEntity);
        _selectedEntity = null;
        RefreshHierarchy();
        RefreshInspector();
        Log($"Deleted: {name}");
    }

    private void Log(string message)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        _logMessages.Add($"[{ts}] {message}");
        if (_consoleLog != null) _consoleLog.Text = string.Join("\n", _logMessages);
        if (_statusText != null) _statusText.Text = message;
    }

    private void ExportSceneAsObj()
    {
        try
        {
            var sb = new StringBuilder();
            int vertexOffset = 0;
            sb.AppendLine("# Lumo Engine OBJ Export");
            sb.AppendLine($"# Objects: {_scene.AllEntities.Count}");
            sb.AppendLine();

            foreach (var entity in _scene.AllEntities)
            {
                if (entity.MeshRenderer == null || entity.Transform == null) continue;
                var pos = entity.Transform.Position;
                var scale = entity.Transform.Scale;
                float s = scale.X;

                float[,] cubeVerts = {
                    {-0.5f,-0.5f,-0.5f}, {0.5f,-0.5f,-0.5f}, {0.5f,0.5f,-0.5f}, {-0.5f,0.5f,-0.5f},
                    {-0.5f,-0.5f,0.5f}, {0.5f,-0.5f,0.5f}, {0.5f,0.5f,0.5f}, {-0.5f,0.5f,0.5f}
                };
                sb.AppendLine($"o {entity.Name}");
                for (int i = 0; i < 8; i++)
                    sb.AppendLine($"v {cubeVerts[i,0]*s+pos.X} {cubeVerts[i,1]*s+pos.Y} {cubeVerts[i,2]*s+pos.Z}");
                sb.AppendLine("usemtl Cube");
                for (int i = 0; i < 6; i++)
                {
                    int b = vertexOffset + i * 4 + 1;
                    sb.AppendLine($"f {b} {b+1} {b+2} {b+3}");
                }
                vertexOffset += 8;
            }

            var savePath = Path.Combine(_project.Path, "Scenes", "scene.obj");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            File.WriteAllText(savePath, sb.ToString());
            Log($"Exported scene to {savePath}");
        }
        catch (Exception ex) { Log($"Export failed: {ex.Message}"); }
    }

    private async System.Threading.Tasks.Task ImportAssetAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) { Log("No top level for file dialog."); return; }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Asset",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("3D Models") { Patterns = ["*.obj"] },
                    new FilePickerFileType("All Files") { Patterns = ["*.*"] },
                ],
            });

            if (files.Count == 0) { Log("Import cancelled."); return; }
            var path = files[0].TryGetLocalPath();
            if (path == null) { Log("Could not resolve file path."); return; }

            if (Path.GetExtension(path).ToLowerInvariant() != ".obj")
            {
                Log($"Unsupported format: {Path.GetExtension(path)} (OBJ supported).");
                return;
            }

            ImportObjFile(path);
        }
        catch (Exception ex) { Log($"Import failed: {ex.Message}"); }
    }

    private async System.Threading.Tasks.Task ImportTextureAsync(Entity e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) { Log("No top level for file dialog."); return; }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Texture",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                    new FilePickerFileType("All Files") { Patterns = ["*.*"] },
                ],
            });

            if (files.Count == 0) { Log("Import cancelled."); return; }
            var path = files[0].TryGetLocalPath();
            if (path == null) { Log("Could not resolve file path."); return; }

            var assetsDir = Path.Combine(_project.Path, "Assets");
            Directory.CreateDirectory(assetsDir);
            var dest = Path.Combine(assetsDir, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(dest))
                    dest = UniqueAssetPath(assetsDir, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path));
                File.Copy(path, dest);
            }

            e.SpriteRenderer ??= new SpriteRendererComponent();
            e.SpriteRenderer.SpritePath = "Assets/" + Path.GetFileName(dest);
            SaveProject();
            RefreshInspector();
            _softwareViewport?.InvalidateVisual();
            Log($"Texture set: {e.SpriteRenderer.SpritePath}");
        }
        catch (Exception ex) { Log($"Texture import failed: {ex.Message}"); }
    }

    private static string UniqueAssetPath(string dir, string baseName, string ext)
    {
        string p = Path.Combine(dir, baseName + ext);
        for (int i = 1; File.Exists(p); i++)
            p = Path.Combine(dir, $"{baseName}_{i}{ext}");
        return p;
    }

    private void ImportObjFile(string path)
    {
        try
        {
            var mesh = ObjImporter.Load(path);
            MeshLibrary.Register(mesh);

            PushSceneUndo();
            var entity = _scene.CreateEntity(mesh.Name);
            entity.MeshRenderer = new MeshRendererComponent { MeshName = mesh.Name };

            _selectedEntity = entity;
            RefreshHierarchy();
            RefreshInspector();
            SaveProject();
            Log($"Imported {mesh.Name}: {mesh.VertexCount} verts, {mesh.TriangleCount} tris.");
        }
        catch (Exception ex) { Log($"OBJ import failed: {ex.Message}"); }
    }

    // ---------- Keyboard (play mode) ----------
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) RouteRedo();
            else RouteUndo();
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))))
        {
            RouteRedo();
            e.Handled = true;
            return;
        }

        if (!_isPlaying || _playInput == null) return;
        var key = MapKey(e.Key);
        if (key != LumoKey.Unknown) { _playInput.KeyPressed(key); e.Handled = true; }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_playInput == null) return;
        var key = MapKey(e.Key);
        if (key != LumoKey.Unknown) { _playInput.KeyReleased(key); e.Handled = true; }
    }

    private static LumoKey MapKey(Key key) => key switch
    {
        Key.W => LumoKey.W, Key.A => LumoKey.A, Key.S => LumoKey.S, Key.D => LumoKey.D,
        Key.Q => LumoKey.Q, Key.E => LumoKey.E, Key.R => LumoKey.R, Key.F => LumoKey.F,
        Key.Z => LumoKey.Z, Key.X => LumoKey.X, Key.C => LumoKey.C, Key.V => LumoKey.V,
        Key.B => LumoKey.B, Key.N => LumoKey.N, Key.M => LumoKey.M, Key.P => LumoKey.P,
        Key.G => LumoKey.G, Key.H => LumoKey.H, Key.J => LumoKey.J, Key.K => LumoKey.K,
        Key.L => LumoKey.L, Key.Y => LumoKey.Y, Key.T => LumoKey.T, Key.U => LumoKey.U,
        Key.I => LumoKey.I, Key.O => LumoKey.O,
        Key.Space => LumoKey.Space, Key.Escape => LumoKey.Escape, Key.Enter => LumoKey.Enter,
        Key.Tab => LumoKey.Tab, Key.Back => LumoKey.Backspace,
        Key.Left => LumoKey.Left, Key.Right => LumoKey.Right, Key.Up => LumoKey.Up, Key.Down => LumoKey.Down,
        Key.LeftShift => LumoKey.LeftShift, Key.LeftCtrl => LumoKey.LeftControl, Key.LeftAlt => LumoKey.LeftAlt,
        _ => LumoKey.Unknown,
    };

    // ---------- Add component ----------
    private void ShowAddComponentMenu()
    {
        if (_selectedEntity == null) { Log("Select an entity first."); return; }

        var menu = new MenuFlyout { Placement = PlacementMode.Bottom };
        void Item(string label, Action act)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => { PushSceneUndo(); act(); };
            menu.Items.Add(mi);
        }

        var e = _selectedEntity;
        if (e.MeshRenderer == null) Item("Static Mesh", () => { e.MeshRenderer = new MeshRendererComponent { MeshName = "Cube" }; RefreshInspector(); RefreshHierarchy(); Log("Added Static Mesh (Cube)."); });
        if (e.Camera == null) Item("Camera", () => { e.Camera = new CameraComponent { IsPrimary = _scene.AllEntities.All(x => x.Camera == null) }; RefreshInspector(); RefreshHierarchy(); Log("Added Camera."); });
        if (e.Light == null) Item("Light (Directional)", () => { e.Light = new LightComponent { LightType = LightType.Directional }; RefreshInspector(); RefreshHierarchy(); Log("Added Light."); });
        if (e.SpriteRenderer == null) Item("Sprite Renderer", () => { e.SpriteRenderer = new SpriteRendererComponent(); RefreshInspector(); RefreshHierarchy(); Log("Added Sprite Renderer."); });
        Item("Script", ShowAttachScriptMenu);

        menu.ShowAt(this);
    }

    private void ShowAttachScriptMenu(object? sender, EventArgs e) => ShowAttachScriptMenu();

    private void ShowAttachScriptMenu()
    {
        if (_selectedEntity == null) return;
        Directory.CreateDirectory(ScriptsDir);
        var files = Directory.GetFiles(ScriptsDir, "*.cs").OrderBy(Path.GetFileName).ToArray();

        var menu = new MenuFlyout { Placement = PlacementMode.Bottom };
        if (files.Length == 0)
        {
            var none = new MenuItem { Header = "No scripts — create one", IsEnabled = true };
            none.Click += (_, _) => AddScript();
            menu.Items.Add(none);
        }
        foreach (var f in files)
        {
            string cls = Path.GetFileNameWithoutExtension(f);
            var mi = new MenuItem { Header = $"Attach {cls}" };
            mi.Click += (_, _) => AttachScriptToSelected(cls);
            menu.Items.Add(mi);
        }
        menu.ShowAt(this);
    }
}