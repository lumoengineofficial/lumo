using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumo.Editor.Editing;
using Lumo.Editor.Ui;
using Lumo.Engine.VisualScripting;

namespace Lumo.Editor.VisualScripting;

/// <summary>
/// Bottom-panel visual scripting workspace: graph file list, searchable node
/// palette, graph canvas and a properties/variables/validation sidebar.
/// </summary>
public sealed class GraphsPanel : UserControl
{
    private const string FileExt = ".graph.json";

    private readonly string _graphsDir;
    private readonly GraphCanvas _canvas = new();
    private readonly List<NodeDefinition> _allDefs;
    private readonly List<string> _files = [];

    private ListBox _fileList = null!;
    private TextBox _search = null!;
    private ListBox _palette = null!;
    private StackPanel _propsHost = null!;
    private TextBlock _dirtyMark = null!;
    private TextBlock _zoomLabel = null!;
    private GraphInterpreter? _runner;

    private VisualGraph? _current;
    private string? _currentFile;
    private bool _dirty;
    private readonly HistoryStack _graphHistory = new();

    // palette grouping: category headers + selectable node rows
    private sealed record PaletteHeader(string Category);
    private sealed record PaletteItem(NodeDefinition Def);

    private static readonly string[] CategoryOrder =
        ["Events", "Flow", "Entity", "Actions", "Variables", "Logic", "Math", "Values"];

    public event Action<string>? Notify;

    public GraphsPanel(string projectPath)
    {
        _graphsDir = Path.Combine(projectPath, "Graphs");
        _allDefs = NodeRegistry.All.ToList();
        Width = double.NaN;
        BuildUi();
        _canvas.BeforeGraphEdit = PushGraphUndo;
        RefreshFileList();

        if (_files.Count == 0)
            NewGraph();
        else
            OpenGraph(_files[0]);
    }

    public GraphCanvas Canvas => _canvas;

    public void AttachRunner(GraphInterpreter? runner) => _runner = runner;

    public void ShowDebug(IReadOnlyCollection<string> activeNodeIds) => _canvas.SetActiveNodes(activeNodeIds);

    // ------------------------------------------------------------ undo/redo

    private void PushGraphUndo()
    {
        if (_current is null || _currentFile is null) return;
        _graphHistory.Push(_current.ToJson());
    }

    private void RestoreGraph(string json)
    {
        var graph = VisualGraph.FromJson(json);
        _current = graph;
        _canvas.Graph = graph;
        _canvas.ClearSelection();
        _dirty = true;
        _dirtyMark.Text = "modified";
        RefreshProps();
    }

    public void UndoGraph()
    {
        if (_current is null || _currentFile is null) { Notify?.Invoke("Graphs: nothing to undo."); return; }
        string? previous = _graphHistory.Undo(_current.ToJson());
        if (previous is null) { Notify?.Invoke("Graphs: nothing to undo."); return; }
        try { RestoreGraph(previous); Notify?.Invoke("Graphs: undo."); }
        catch (Exception ex) { Notify?.Invoke($"Graphs: undo failed: {ex.Message}"); }
    }

    public void RedoGraph()
    {
        if (_current is null || _currentFile is null) { Notify?.Invoke("Graphs: nothing to redo."); return; }
        string? next = _graphHistory.Redo(_current.ToJson());
        if (next is null) { Notify?.Invoke("Graphs: nothing to redo."); return; }
        try { RestoreGraph(next); Notify?.Invoke("Graphs: redo."); }
        catch (Exception ex) { Notify?.Invoke($"Graphs: redo failed: {ex.Message}"); }
    }

    // ------------------------------------------------------------ files

    private void RefreshFileList()
    {
        _files.Clear();
        if (Directory.Exists(_graphsDir))
        {
            foreach (string path in Directory.GetFiles(_graphsDir, "*" + FileExt))
                _files.Add(Path.GetFileName(path)[..^FileExt.Length]);
        }
        _files.Sort(StringComparer.Ordinal);

        _fileList.ItemsSource = null;
        _fileList.ItemsSource = _files;
        if (_currentFile is not null)
            _fileList.SelectedIndex = _files.IndexOf(_currentFile);
    }

    private void NewGraph()
    {
        Directory.CreateDirectory(_graphsDir);
        int n = 1;
        while (_files.Contains($"Graph{n}"))
            n++;
        string name = $"Graph{n}";
        var graph = new VisualGraph { Name = name };
        string path = Path.Combine(_graphsDir, name + FileExt);
        graph.Save(path);
        RefreshFileList();
        OpenGraph(name);
        Notify?.Invoke($"Graphs: created {name}.");
    }

    private void OpenGraph(string name)
    {
        string path = Path.Combine(_graphsDir, name + FileExt);
        try
        {
            VisualGraph graph = VisualGraph.Load(path);
            _current = graph;
            _currentFile = name;
            _dirty = false;
            _canvas.Graph = graph;
            _graphHistory.Clear();
            _fileList.SelectedIndex = _files.IndexOf(name);
            RefreshProps();
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Graphs: failed to open {name}: {ex.Message}");
        }
    }

    public void SaveCurrentGraph() => SaveGraph();

    private void SaveGraph()
    {
        if (_current is null || _currentFile is null) return;
        Directory.CreateDirectory(_graphsDir);
        try
        {
            _current.Save(Path.Combine(_graphsDir, _currentFile + FileExt));
            _dirty = false;
            RefreshProps();
            Notify?.Invoke($"Graphs: saved {_currentFile}.");
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Graphs: save failed: {ex.Message}");
        }
    }

    private void DeleteGraphFile()
    {
        if (_currentFile is null) return;
        string path = Path.Combine(_graphsDir, _currentFile + FileExt);
        string removed = _currentFile;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            _current = null;
            _currentFile = null;
            _canvas.Graph = null;
            RefreshFileList();
            RefreshProps();
            if (_files.Count > 0)
                OpenGraph(_files[0]);
            else
                NewGraph();
            Notify?.Invoke($"Graphs: deleted {removed}.");
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Graphs: delete failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------ ui

    private void BuildUi()
    {
        _canvas.GraphEdited += OnGraphEdited;
        _canvas.SelectionChanged += () => RefreshProps();

        // toolbar
        var newBtn = UiTheme.ActionButton("New", Icons.FilePlus, NewGraph);
        var saveBtn = UiTheme.ActionButton("Save", Icons.Save, SaveGraph, primary: true);
        var validateBtn = UiTheme.ActionButton("Validate", null, () => RefreshProps());
        _dirtyMark = UiTheme.Txt("", 11, UiTheme.Orange, FontWeight.SemiBold);
        _dirtyMark.Margin = new Thickness(8, 0, 0, 0);
        _dirtyMark.VerticalAlignment = VerticalAlignment.Center;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6),
            Children = { newBtn, saveBtn, validateBtn, _dirtyMark }
        };

        var toolbarBorder = new Border
        {
            Background = UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
        DockPanel.SetDock(toolbarBorder, Dock.Top);

        // graph file list
        var filesHead = UiTheme.Txt("Graphs", 10, UiTheme.Faint, FontWeight.SemiBold);
        filesHead.Margin = new Thickness(10, 8, 0, 4);
        _fileList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 0),
            FontSize = 11,
            Foreground = UiTheme.B(UiTheme.Text)
        };
        _fileList.SelectionChanged += (_, e) =>
        {
            if (_fileList.SelectedItem is string name && name != _currentFile && e.AddedItems.Count > 0)
                OpenGraph(name);
        };

        var filesCol = new StackPanel { Children = { filesHead, _fileList } };
        var filesBorder = new Border
        {
            Width = 132,
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = filesCol
        };
        DockPanel.SetDock(filesBorder, Dock.Left);

        // palette
        _search = new TextBox
        {
            Watermark = "Search nodes...",
            FontSize = 11,
            Margin = new Thickness(8, 8, 8, 4),
            Background = UiTheme.B(UiTheme.Card),
            Foreground = UiTheme.B(UiTheme.Text),
            CaretBrush = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border)
        };
        _search.TextChanged += (_, _) => RebuildPalette();

        var paletteHint = UiTheme.Txt("Click to add at center", 9, UiTheme.Faint);
        paletteHint.Margin = new Thickness(10, 0, 0, 2);

        _palette = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 0),
            FontSize = 11,
            Foreground = UiTheme.B(UiTheme.Text)
        };
        _palette.SelectionChanged += PaletteSelected;
        _palette.ItemTemplate = new FuncDataTemplate<object>((o, _) => BuildPaletteRow(o), true);

        RebuildPalette();

        var paletteCol = new StackPanel { Children = { _search, paletteHint, _palette } };
        var paletteBorder = new Border
        {
            Width = 150,
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = paletteCol
        };
        DockPanel.SetDock(paletteBorder, Dock.Left);

        // properties sidebar
        _propsHost = new StackPanel { Margin = new Thickness(8), Spacing = 6 };
        var propsScroll = new ScrollViewer { Content = _propsHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var propsBorder = new Border
        {
            Width = 236,
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = propsScroll
        };
        DockPanel.SetDock(propsBorder, Dock.Right);

        var root = new DockPanel { Background = UiTheme.B(UiTheme.Panel) };
        root.Children.Add(toolbarBorder);
        root.Children.Add(filesBorder);
        root.Children.Add(paletteBorder);
        root.Children.Add(propsBorder);
        root.Children.Add(BuildCanvasHost());
        Content = root;
    }

    private Control BuildCanvasHost()
    {
        _zoomLabel = UiTheme.Txt("100%", 11, UiTheme.Dim, FontWeight.SemiBold);
        _zoomLabel.MinWidth = 40;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = VerticalAlignment.Center;
        _canvas.ZoomChanged += z => _zoomLabel.Text = $"{z * 100:F0}%";

        var hint = UiTheme.Txt("wheel zoom · drag bg pan · ctrl±/0/9", 9, UiTheme.Faint);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(4, 0, 2, 0);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                UiTheme.ActionButton("−", null, () => _canvas.ZoomBy(1 / 1.2)),
                UiTheme.ActionButton("100%", null, _canvas.ResetZoom),
                UiTheme.ActionButton("+", null, () => _canvas.ZoomBy(1.2)),
                UiTheme.ActionButton("Fit", null, _canvas.FitToView),
                _zoomLabel,
                hint
            }
        };

        var overlay = new Border
        {
            Background = UiTheme.B(Color.Parse("#e610172a")),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
            Child = bar
        };

        return new Panel { Children = { _canvas, overlay } };
    }

    private void RebuildPalette()
    {
        string q = (_search?.Text ?? "").Trim();
        IEnumerable<NodeDefinition> defs = string.IsNullOrEmpty(q)
            ? _allDefs
            : _allDefs.Where(d =>
                d.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Type.Contains(q, StringComparison.OrdinalIgnoreCase));

        var rows = new List<object>();
        foreach (var group in defs
            .GroupBy(d => d.Category)
            .OrderBy(g =>
            {
                int i = Array.IndexOf(CategoryOrder, g.Key);
                return i >= 0 ? i : CategoryOrder.Length;
            })
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new PaletteHeader(group.Key));
            foreach (var def in group.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase))
                rows.Add(new PaletteItem(def));
        }

        _palette.ItemsSource = null;
        _palette.ItemsSource = rows;
    }

    private Control BuildPaletteRow(object? row)
    {
        if (row is PaletteHeader header)
        {
            Color color = GraphCanvas.CategoryColor(header.Category);
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = UiTheme.B(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = UiTheme.Txt(header.Category.ToUpperInvariant(), 9, UiTheme.Faint, FontWeight.SemiBold);
            label.LetterSpacing = 1.1;
            label.VerticalAlignment = VerticalAlignment.Center;
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(8, 10, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { dot, label }
            };
            return new Border { Background = UiTheme.B(Colors.Transparent), Padding = new Thickness(2, 0), Cursor = new Cursor(StandardCursorType.Arrow), Child = stack };
        }

        if (row is PaletteItem item)
        {
            var title = UiTheme.Txt(item.Def.Title, 11, UiTheme.Text);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            return new Border
            {
                Background = UiTheme.B(Colors.Transparent),
                Padding = new Thickness(18, 3, 4, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = title
            };
        }

        return new Border { Height = 0 };
    }

    private void PaletteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_palette.SelectedItem is not PaletteItem item)
        {
            _palette.SelectedIndex = -1;
            return;
        }
        if (_current is null)
        {
            Notify?.Invoke("Graphs: open a graph first.");
            _palette.SelectedIndex = -1;
            return;
        }
        try
        {
            _canvas.AddNode(item.Def.Type);
            RefreshProps();
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Graphs: {ex.Message}");
        }
        _palette.SelectedIndex = -1;
    }

    private void OnGraphEdited()
    {
        _dirty = true;
        _dirtyMark.Text = "modified";
        RefreshProps();
    }

    // ------------------------------------------------------------ properties

    private void RefreshProps()
    {
        _propsHost.Children.Clear();
        if (_dirtyMark is not null)
            _dirtyMark.Text = _dirty ? "modified" : "";

        if (_current is null)
        {
            _propsHost.Children.Add(UiTheme.Txt("No graph open.", 11, UiTheme.Faint));
            return;
        }

        _propsHost.Children.Add(UiTheme.SectionTitle(_currentFile ?? _current.Name));

        VSNode? node = _canvas.SelectedNode;
        if (node is not null)
        {
            AddNodeProperties(node);
        }
        else if (_canvas.SelectedWire is { } wire)
        {
            _propsHost.Children.Add(UiTheme.Txt("Wire selected", 11, UiTheme.Cyan, FontWeight.SemiBold));
            _propsHost.Children.Add(UiTheme.Txt($"{wire.FromNode}.{wire.FromPin} -> {wire.ToNode}.{wire.ToPin}", 10, UiTheme.Dim));
            var delWire = UiTheme.ActionButton("Delete wire", Icons.Trash, () => _canvas.DeleteSelection());
            _propsHost.Children.Add(delWire);
        }
        else
        {
            _propsHost.Children.Add(UiTheme.Txt("Select a node to edit.", 10, UiTheme.Faint));
        }

        _propsHost.Children.Add(UiTheme.Divider());
        AddVariablesSection();

        _propsHost.Children.Add(UiTheme.Divider());
        AddValidationSection();
    }

    private void AddNodeProperties(VSNode node)
    {
        string title = node.TypeId.Length > 0 && NodeRegistry.TryGet(node.TypeId, out var def)
            ? def!.Title : node.TypeId;
        string category = node.TypeId.Length > 0 && NodeRegistry.TryGet(node.TypeId, out var def2)
            ? def2!.Category : "";

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(UiTheme.Txt(title, 12, UiTheme.Text, FontWeight.SemiBold));
        if (category.Length > 0)
            head.Children.Add(UiTheme.Badge(category, UiTheme.AccentSoft, UiTheme.Text));
        _propsHost.Children.Add(head);

        if (node.TypeId.Length > 0 && NodeRegistry.TryGet(node.TypeId, out var def3) &&
            def3!.Description.Length > 0)
        {
            var desc = UiTheme.Txt(def3.Description, 10, UiTheme.Dim);
            desc.TextWrapping = TextWrapping.Wrap;
            _propsHost.Children.Add(desc);
        }

        foreach (Pin pin in node.InputPins)
        {
            if (pin.Kind != PinKind.Data)
                continue;
            AddValueEditor(node, pin);
        }

        var del = UiTheme.ActionButton("Delete node", Icons.Trash, () => _canvas.DeleteSelection());
        del.Margin = new Thickness(0, 4, 0, 0);
        _propsHost.Children.Add(del);
    }

    private void AddValueEditor(VSNode node, Pin pin)
    {
        bool connected = _current is not null &&
            _current.FindInputConnection(node.Id, pin.Name) is not null;

        var row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3, 0, 0) };
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        labelRow.Children.Add(UiTheme.Txt(pin.Name, 10, UiTheme.Dim));
        labelRow.Children.Add(UiTheme.Txt(pin.DataType.ToString(), 9,
            pin.DataType == PinDataType.Any ? Color.Parse("#9aa8ff") : UiTheme.Faint));
        if (connected)
            labelRow.Children.Add(UiTheme.Txt("(wired)", 9, UiTheme.Green));
        row.Children.Add(labelRow);

        if (connected)
            return;

        string current = node.Values.TryGetValue(pin.Name, out string? v) ? v : "";
        var box = new TextBox
        {
            Text = current,
            FontSize = 11,
            Height = 24,
            Background = UiTheme.B(UiTheme.Card),
            Foreground = UiTheme.B(UiTheme.Text),
            CaretBrush = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border),
            Padding = new Thickness(6, 2)
        };
        box.GotFocus += (_, _) => PushGraphUndo();
        box.TextChanged += (_, _) =>
        {
            node.Values[pin.Name] = box.Text ?? "";
            _dirty = true;
            _canvas.Refresh();
        };
        row.Children.Add(box);
        _propsHost.Children.Add(row);
    }

    private void AddVariablesSection()
    {
        _propsHost.Children.Add(UiTheme.SectionTitle("Variables"));
        if (_current is null) return;

        if (_current.Variables.Count == 0)
            _propsHost.Children.Add(UiTheme.Txt("None declared.", 10, UiTheme.Faint));

        foreach (GraphVariable variable in _current.Variables.ToList())
        {
            var row = new Border
            {
                Background = UiTheme.B(UiTheme.Card),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4)
            };
            var dock = new DockPanel();
            row.Child = dock;
            var remove = UiTheme.IconBtn(Icons.Close, () =>
            {
                _current.Variables.Remove(variable);
                _dirty = true;
                RefreshProps();
            }, size: 12, color: UiTheme.Faint);
            remove.Width = 20;
            DockPanel.SetDock(remove, Dock.Right);

            var text = new StackPanel();
            text.Children.Add(UiTheme.Txt(variable.Name, 11, UiTheme.Text, FontWeight.Medium));
            text.Children.Add(UiTheme.Txt(
                $"{variable.DataType} · {variable.Scope} · \"{variable.DefaultValue}\"",
                9, UiTheme.Dim));
            dock.Children.Add(remove);
            dock.Children.Add(text);
            _propsHost.Children.Add(row);
        }

        // add row
        var nameBox = NewSmallBox("name");
        var defaultBox = NewSmallBox("0");
        var typeBox = new ComboBox
        {
            ItemsSource = new[] { "Float", "Int", "Bool", "String" },
            SelectedIndex = 0,
            FontSize = 10,
            Height = 24,
            MinWidth = 70
        };
        var scopeBox = new ComboBox
        {
            ItemsSource = new[] { "Local", "Blackboard" },
            SelectedIndex = 0,
            FontSize = 10,
            Height = 24,
            MinWidth = 70
        };
        var add = UiTheme.ActionButton("Add var", Icons.Plus, () =>
        {
            string name = (nameBox.Text ?? "").Trim();
            if (_current is null || name.Length == 0) return;
            var dataType = (typeBox.SelectedItem as string) switch
            {
                "Int" => PinDataType.Int,
                "Bool" => PinDataType.Bool,
                "String" => PinDataType.String,
                _ => PinDataType.Float
            };
            _current.Variables.Add(new GraphVariable
            {
                Name = name,
                DataType = dataType,
                Scope = (scopeBox.SelectedItem as string) == "Blackboard" ? VariableScope.Blackboard : VariableScope.Local,
                DefaultValue = defaultBox.Text ?? ""
            });
            _dirty = true;
            RefreshProps();
        });

        var grid = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        grid.Children.Add(nameBox);
        var comboRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        comboRow.Children.Add(typeBox);
        comboRow.Children.Add(scopeBox);
        grid.Children.Add(comboRow);
        grid.Children.Add(defaultBox);
        grid.Children.Add(add);
        _propsHost.Children.Add(grid);
    }

    private void AddValidationSection()
    {
        _propsHost.Children.Add(UiTheme.SectionTitle("Validation"));
        if (_current is null) return;

        List<ValidationIssue> issues = GraphValidator.Validate(_current);
        if (issues.Count == 0)
        {
            _propsHost.Children.Add(UiTheme.Txt("No issues.", 10, UiTheme.Green));
            return;
        }
        foreach (ValidationIssue issue in issues)
        {
            var text = UiTheme.Txt(issue.ToString(), 10,
                issue.IsError ? UiTheme.Red : UiTheme.Orange);
            text.TextWrapping = TextWrapping.Wrap;
            _propsHost.Children.Add(text);
        }
    }

    private static TextBox NewSmallBox(string hint) => new()
    {
        Watermark = hint,
        FontSize = 11,
        Height = 24,
        Background = UiTheme.B(UiTheme.Card),
        Foreground = UiTheme.B(UiTheme.Text),
        CaretBrush = UiTheme.B(UiTheme.Text),
        BorderBrush = UiTheme.B(UiTheme.Border),
        Padding = new Thickness(6, 2)
    };
}
