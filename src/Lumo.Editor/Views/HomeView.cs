using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumo.Editor.Ui;
using Lumo.Engine.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumo.Editor.Views;

public class HomeView : UserControl
{
    private List<ProjectInfo> _projects = [];
    private List<ProjectInfo> _filtered = [];
    private readonly Action<string, string> _onNewProject;
    private readonly Action<string> _onOpenProject;

    private StackPanel? _listPanel;
    private TextBox? _filterBox;
    private ComboBox? _sortBox;
    private ProjectInfo? _selected;

    private Button? _editBtn;
    private Button? _runBtn;
    private Button? _renameBtn;
    private Button? _duplicateBtn;
    private Button? _tagsBtn;
    private Button? _removeBtn;
    private Panel? _layer;
    private Border? _dialogOverlay;

    public HomeView(Action<string, string> onNewProject, Action<string> onOpenProject)
    {
        _onNewProject = onNewProject;
        _onOpenProject = onOpenProject;
        BuildUI();
    }

    private void BuildUI()
    {
        _projects = ProjectManager.GetAllProjects();
        _filtered = _projects;

        var root = new DockPanel { Background = UiTheme.B(UiTheme.Bg) };

        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var rightPanel = BuildRightPanel();
        DockPanel.SetDock(rightPanel, Dock.Right);
        root.Children.Add(rightPanel);

        root.Children.Add(BuildProjectList());

        _layer = new Panel { Children = { root } };
        Content = _layer;
        RefreshList();
    }

    // ---------- New project dialog: root folder gets the project name ----------
    private void ShowNewProjectDialog()
    {
        if (_layer == null || _dialogOverlay != null) return;

        var nameBox = new TextBox
        {
            Watermark = "Project name (used as folder name)",
            FontSize = 13,
            Background = UiTheme.B(UiTheme.Panel),
            Foreground = UiTheme.B(UiTheme.Text),
            CaretBrush = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border),
            Padding = new Thickness(10, 8),
            MaxLength = 64,
        };

        var pathBox = new TextBox
        {
            Text = ProjectManager.ProjectsRootPath,
            FontSize = 13,
            Width = 330,
            Background = UiTheme.B(UiTheme.Panel),
            Foreground = UiTheme.B(UiTheme.Text),
            CaretBrush = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border),
            Padding = new Thickness(10, 8),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        void CloseDialog()
        {
            if (_layer == null) return;
            _layer.Children.Remove(_dialogOverlay!);
            _dialogOverlay = null;
        }

        async void Browse()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select parent folder for the project",
                    AllowMultiple = false,
                });
            if (folders.Count > 0)
            {
                string? local = folders[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(local)) pathBox.Text = local;
            }
        }

        void Create()
        {
            string name = (nameBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                nameBox.BorderBrush = UiTheme.B(UiTheme.Red);
                return;
            }
            string basePath = (pathBox.Text ?? "").Trim();
            CloseDialog();
            _onNewProject(name, basePath);
        }

        void OnBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; Create(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseDialog(); }
        }

        nameBox.KeyDown += OnBoxKeyDown;
        pathBox.KeyDown += OnBoxKeyDown;

        var pathRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                pathBox,
                UiTheme.ActionButton("Browse", null, Browse),
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                UiTheme.ActionButton("Cancel", null, CloseDialog),
                UiTheme.ActionButton("Create", Icons.Plus, Create, primary: true),
            }
        };

        var hint = UiTheme.Txt(
            "The project folder is created inside the selected parent folder (with Scenes, Graphs, Scripts, Assets and Plugins).",
            11, UiTheme.Faint);
        hint.TextWrapping = TextWrapping.Wrap;

        var card = new Border
        {
            Background = UiTheme.B(UiTheme.Card),
            BorderBrush = UiTheme.B(UiTheme.Border),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24, 22),
            Width = 470,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    UiTheme.Txt("New Project", 15, UiTheme.Text, FontWeight.SemiBold),
                    hint,
                    nameBox,
                    UiTheme.Txt("Location", 11, UiTheme.Faint),
                    pathRow,
                    buttons,
                }
            }
        };

        var overlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#b3060a12")),
            Child = new Panel
            {
                Children =
                {
                    new Border
                    {
                        Child = card,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                }
            }
        };

        _dialogOverlay = overlay;
        _layer.Children.Add(overlay);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => nameBox.Focus());
    }

    // ---------- Top bar (logo + tabs + settings) ----------
    private Control BuildTopBar()
    {
        var logoRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0),
            Children =
            {
                UiTheme.Logo(24),
                UiTheme.Txt("LUMO", 17, UiTheme.Text, FontWeight.Bold),
            }
        };
        DockPanel.SetDock(logoRow, Dock.Left);

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                TopTab(Icons.Folder, "Projects", true),
                TopTab(Icons.Grid, "Asset Store", false),
            }
        };

        var settingsBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { UiTheme.Ico(Icons.Gear, 14, UiTheme.Dim), UiTheme.Txt("Settings", 12, UiTheme.Dim) }
            },
            Padding = new Thickness(10, 6),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Children = { settingsBtn }
        };
        DockPanel.SetDock(right, Dock.Right);

        var bar = new DockPanel { Height = 50 };
        bar.Children.Add(logoRow);
        bar.Children.Add(right);
        bar.Children.Add(new Border { Child = tabs, HorizontalAlignment = HorizontalAlignment.Center });

        return new Border
        {
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Control TopTab(string icon, string label, bool active)
    {
        return new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    UiTheme.Ico(icon, 14, active ? UiTheme.Cyan : UiTheme.Dim),
                    UiTheme.Txt(label, 13, active ? UiTheme.Text : UiTheme.Dim, active ? FontWeight.SemiBold : FontWeight.Normal),
                }
            },
            Padding = new Thickness(12, 6),
            Background = UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
    }

    // ---------- Toolbar: Create / Import / Scan  +  Filter  +  Sort ----------
    private Control BuildToolbar()
    {
        var createBtn = MakeToolButton(Icons.Plus, "Create", true, ShowNewProjectDialog);
        var importBtn = MakeToolButton(Icons.Upload, "Import", false, () => { });
        var scanBtn = MakeToolButton(Icons.Folder, "Scan", false, () => { });

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { createBtn, importBtn, scanBtn },
        };
        DockPanel.SetDock(left, Dock.Left);

        var sortLabel = UiTheme.Txt("Sort:", 12, UiTheme.Dim);
        sortLabel.VerticalAlignment = VerticalAlignment.Center;

        _sortBox = new ComboBox
        {
            Width = 140,
            SelectedIndex = 0,
            ItemsSource = new[] { "Last Edited", "Name", "Date Created" },
            Background = UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sortBox.SelectionChanged += (_, _) => RefreshList();

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Children = { sortLabel, _sortBox },
        };
        DockPanel.SetDock(right, Dock.Right);

        _filterBox = new TextBox
        {
            Watermark = "Filter Projects",
            Background = UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            Foreground = UiTheme.B(UiTheme.Text),
            Margin = new Thickness(14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Height = 32,
        };
        _filterBox.TextChanged += (_, _) => RefreshList();

        var bar = new DockPanel { Height = 52, Margin = new Thickness(16, 8) };
        bar.Children.Add(left);
        bar.Children.Add(right);
        bar.Children.Add(_filterBox);

        return new Border
        {
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Button MakeToolButton(string icon, string label, bool primary, Action onClick)
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    UiTheme.Ico(icon, 14, primary ? Colors.White : UiTheme.Text),
                    UiTheme.Txt(label, 12, primary ? Colors.White : UiTheme.Text, FontWeight.Medium),
                },
            },
            Padding = new Thickness(12, 7),
            Background = primary ? UiTheme.B(UiTheme.Accent) : UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(primary ? 0 : 1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // ---------- Project list (table-style rows, like Godot's project manager) ----------
    private Control BuildProjectList()
    {
        _listPanel = new StackPanel { Spacing = 0 };
        return new ScrollViewer { Content = _listPanel, Background = UiTheme.B(UiTheme.Bg) };
    }

    private void RefreshList()
    {
        if (_listPanel == null) return;
        _listPanel.Children.Clear();

        var filterText = _filterBox?.Text?.Trim() ?? "";
        IEnumerable<ProjectInfo> query = _projects;
        if (!string.IsNullOrWhiteSpace(filterText))
            query = query.Where(p => p.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase));

        query = _sortBox?.SelectedIndex switch
        {
            1 => query.OrderBy(p => p.Name),
            2 => query.OrderBy(p => p.LastModified),
            _ => query.OrderByDescending(p => p.LastModified),
        };

        _filtered = query.ToList();

        if (_filtered.Count == 0)
        {
            _listPanel.Children.Add(new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 60),
                Children =
                {
                    UiTheme.Ico(Icons.Star, 34, UiTheme.Faint),
                    UiTheme.Txt("No projects found", 14, UiTheme.Dim, FontWeight.Medium),
                }
            });
            SetSelectionQuiet(null);
            return;
        }

        foreach (var proj in _filtered)
            _listPanel.Children.Add(ProjectRow(proj));

        if (_selected == null || !_filtered.Any(p => p.Path == _selected.Path))
            SetSelectionQuiet(_filtered[0]);
    }

    private Control ProjectRow(ProjectInfo proj)
    {
        var accent = UiTheme.PickGradient(proj.Name);
        bool isSelected = _selected != null && _selected.Path == proj.Path;

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(accent, 0), new GradientStop(Color.Parse("#0d1220"), 1) },
            },
            Child = UiTheme.Ico(Icons.Scene, 18, Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var texts = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                UiTheme.Txt(proj.Name, 13, UiTheme.Text, FontWeight.SemiBold),
                UiTheme.Txt(proj.Path, 11, UiTheme.Faint),
            }
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Children = { icon, texts },
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Children =
            {
                UiTheme.Badge("C#", Color.Parse("#2b3a8a"), Color.Parse("#9fb4ff")),
                UiTheme.Txt(Ago(proj.LastModified), 11, UiTheme.Faint),
            }
        };

        var row = new DockPanel { Height = 58 };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(left);

        var wrap = new Border
        {
            Background = isSelected ? UiTheme.B(UiTheme.CardHover) : UiTheme.B(Colors.Transparent),
            BorderBrush = isSelected ? UiTheme.B(UiTheme.Accent) : UiTheme.B(Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row,
        };
        wrap.PointerPressed += (_, e) =>
        {
            SetSelection(proj);
            if (e.ClickCount == 2) _onOpenProject(proj.Path);
        };
        wrap.PointerEntered += (_, _) => { if (_selected?.Path != proj.Path) wrap.Background = UiTheme.B(Color.Parse("#151d38")); };
        wrap.PointerExited += (_, _) => { if (_selected?.Path != proj.Path) wrap.Background = UiTheme.B(Colors.Transparent); };

        return wrap;
    }

    // Update selection + refresh list (used on click)
    private void SetSelection(ProjectInfo? proj)
    {
        _selected = proj;
        UpdateActionButtons();
        RefreshList();
    }

    // Update selection without forcing another RefreshList (used inside RefreshList to avoid recursion)
    private void SetSelectionQuiet(ProjectInfo? proj)
    {
        _selected = proj;
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        bool has = _selected != null;
        if (_editBtn != null) _editBtn.IsEnabled = has;
        if (_runBtn != null) _runBtn.IsEnabled = has;
        if (_renameBtn != null) _renameBtn.IsEnabled = has;
        if (_duplicateBtn != null) _duplicateBtn.IsEnabled = has;
        if (_tagsBtn != null) _tagsBtn.IsEnabled = has;
        if (_removeBtn != null) _removeBtn.IsEnabled = has;
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} hours ago";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays} days ago";
        return t.ToString("MMM d, yyyy");
    }

    // ---------- Right action panel (Edit / Run / Rename / Duplicate / Tags / Remove) ----------
    private Control BuildRightPanel()
    {
        var panel = new StackPanel { Width = 160, Spacing = 8, Margin = new Thickness(12, 16) };

        _editBtn = ActionBtn(Icons.File, "Edit", () => { if (_selected != null) _onOpenProject(_selected.Path); });
        _runBtn = ActionBtn(Icons.Play, "Run", () => { if (_selected != null) _onOpenProject(_selected.Path); }, primary: true);
        _renameBtn = ActionBtn(Icons.File, "Rename", () => { });
        _duplicateBtn = ActionBtn(Icons.File, "Duplicate", () => { });
        _tagsBtn = ActionBtn(Icons.Star, "Manage Tags", () => { });
        _removeBtn = ActionBtn(Icons.Dots, "Remove", () => { });

        panel.Children.Add(_editBtn);
        panel.Children.Add(_runBtn);
        panel.Children.Add(_renameBtn);
        panel.Children.Add(_duplicateBtn);
        panel.Children.Add(_tagsBtn);
        panel.Children.Add(UiTheme.Divider());
        panel.Children.Add(_removeBtn);

        UpdateActionButtons();

        return new Border
        {
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private Button ActionBtn(string icon, string label, Action onClick, bool primary = false)
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    UiTheme.Ico(icon, 14, primary ? Colors.White : UiTheme.Text),
                    UiTheme.Txt(label, 12, primary ? Colors.White : UiTheme.Text, FontWeight.Medium),
                },
            },
            Padding = new Thickness(10, 8),
            Background = primary ? UiTheme.B(UiTheme.Accent) : UiTheme.B(UiTheme.Panel),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(primary ? 0 : 1),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // ---------- Footer (version + project count) ----------
    private Control BuildFooter()
    {
        var countTxt = UiTheme.Txt($"{_projects.Count} project(s)", 10, UiTheme.Faint);
        countTxt.Margin = new Thickness(16, 0, 0, 0);

        var versionTxt = UiTheme.TxtAt($"v{EngineConstants.Version}", 10, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Right, new Thickness(0, 0, 16, 0));

        return new Border
        {
            Height = 30,
            Background = UiTheme.B(UiTheme.Sidebar),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel
            {
                Children = { versionTxt, countTxt }
            }
        };
    }
}