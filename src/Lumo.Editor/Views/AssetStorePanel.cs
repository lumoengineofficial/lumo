using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumo.Editor.Store;
using Lumo.Editor.Ui;

namespace Lumo.Editor.Views;

/// <summary>Browse, search and install assets from the Lumo Asset Store API
/// directly into the open project's Assets folder.</summary>
public sealed class AssetStorePanel : Border
{
    private static readonly string[] Categories =
        ["All Categories", "3D Models", "Textures & Materials", "Sprites & 2D", "Audio", "Plugins", "Templates"];
    private static readonly string[] Sorts = ["Newest", "Top downloads", "Rating"];
    private static readonly string[] SortKeys = ["newest", "popular", "rating"];

    private readonly Func<string> _projectRoot;
    private readonly Action<string> _log;
    private readonly Action? _onInstalled;

    private List<StoreAsset> _items = [];
    private readonly List<(StoreAsset Asset, Border Card)> _cards = [];
    private StoreAsset? _selected;
    private int _page = 1;
    private int _pageCount = 1;
    private int _total;
    private string _query = "";
    private string _category = "";
    private string _sort = "newest";
    private bool _busy;

    private readonly TextBlock _status;
    private readonly TextBox _search;
    private readonly ComboBox _categoryBox;
    private readonly ComboBox _sortBox;
    private readonly WrapPanel _cardsPanel;
    private readonly StackPanel _detail;
    private readonly Button _refreshBtn;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly TextBlock _pageText;

    public AssetStorePanel(Func<string> projectRoot, Action<string> log, Action? onInstalled = null)
    {
        _projectRoot = projectRoot;
        _log = log;
        _onInstalled = onInstalled;

        Background = UiTheme.B(UiTheme.Bg);

        _status = UiTheme.Txt("Loading…", 11, UiTheme.Dim);

        _search = new TextBox
        {
            Watermark = "Search assets…",
            Width = 240,
            Background = UiTheme.B(UiTheme.Panel),
            Foreground = UiTheme.B(UiTheme.Text),
            BorderBrush = UiTheme.B(UiTheme.Border),
            Padding = new Thickness(9, 6),
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _query = _search.Text ?? "";
            _page = 1;
            _ = ReloadAsync();
            e.Handled = true;
        };

        _categoryBox = new ComboBox { ItemsSource = Categories, SelectedIndex = 0, Width = 170 };
        _categoryBox.SelectionChanged += (_, _) =>
        {
            string sel = _categoryBox.SelectedItem as string ?? "";
            _category = sel.StartsWith("All", StringComparison.Ordinal) ? "" : sel;
            _page = 1;
            _ = ReloadAsync();
        };

        _sortBox = new ComboBox { ItemsSource = Sorts, SelectedIndex = 0, Width = 130 };
        _sortBox.SelectionChanged += (_, _) =>
        {
            int i = Math.Clamp(_sortBox.SelectedIndex, 0, SortKeys.Length - 1);
            _sort = SortKeys[i];
            _page = 1;
            _ = ReloadAsync();
        };

        _refreshBtn = UiTheme.ActionButton("Refresh", null, () => _ = ReloadAsync());
        _prevBtn = UiTheme.ActionButton("Prev", null, () => { if (_page > 1) { _page--; _ = ReloadAsync(); } });
        _nextBtn = UiTheme.ActionButton("Next", null, () => { if (_page < _pageCount) { _page++; _ = ReloadAsync(); } });
        _pageText = UiTheme.Txt("", 11, UiTheme.Dim);

        var header = new DockPanel { Margin = new Thickness(18, 14, 18, 0) };
        DockPanel.SetDock(_status, Dock.Right);
        header.Children.Add(_status);
        header.Children.Add(UiTheme.SectionTitle("ASSET STORE"));

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(18, 4, 18, 10),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _search, _categoryBox, _sortBox, _refreshBtn },
        };

        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(18, 10, 18, 14),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _prevBtn, _pageText, _nextBtn },
        };

        _cardsPanel = new WrapPanel { ItemWidth = 190 };
        var cardsScroll = new ScrollViewer { Content = _cardsPanel, Padding = new Thickness(18, 0, 8, 0) };

        _detail = new StackPanel { Spacing = 8 };
        var detailScroll = new ScrollViewer { Content = _detail };
        var detailBorder = new Border
        {
            Background = UiTheme.B(UiTheme.Card),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(8, 0, 18, 0),
            Child = detailScroll,
        };

        var mid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(330)) },
            Children = { cardsScroll, detailBorder },
        };
        Grid.SetColumn(cardsScroll, 0);
        Grid.SetColumn(detailBorder, 1);

        var root = new DockPanel { Children = { header, controls, pager, mid } };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(pager, Dock.Bottom);
        Child = root;

        RenderDetail();
        _ = ReloadAsync();
    }

    private async System.Threading.Tasks.Task ReloadAsync()
    {
        if (_busy) return;
        _busy = true;
        SetBusyUi(true);
        _status.Foreground = UiTheme.B(UiTheme.Dim);
        _status.Text = "Loading…";

        try
        {
            var result = await StoreClient.GetListAsync(StoreClient.DefaultStoreUrl,
                _query, _category, _sort, _page);
            _items = result.Items;
            _total = result.Total;
            _pageCount = Math.Max(1, result.PageCount);
            RenderCards();
            _status.Text = $"{_total} asset(s) · page {_page}/{_pageCount}";
        }
        catch (StoreApiException ex)
        {
            _status.Foreground = UiTheme.B(UiTheme.Red);
            _status.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            _status.Foreground = UiTheme.B(UiTheme.Red);
            _status.Text = "Cannot reach lumoengine.vercel.app — check your internet connection.";
        }
        catch (Exception ex)
        {
            _status.Foreground = UiTheme.B(UiTheme.Red);
            _status.Text = $"Store error: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetBusyUi(false);
            _prevBtn.IsEnabled = _page > 1;
            _nextBtn.IsEnabled = _page < _pageCount;
            _pageText.Text = $"Page {_page} / {_pageCount}";
        }
    }

    private void SetBusyUi(bool busy)
    {
        _refreshBtn.IsEnabled = !busy;
        _categoryBox.IsEnabled = !busy;
        _sortBox.IsEnabled = !busy;
        _search.IsEnabled = !busy;
    }

    private void RenderCards()
    {
        _cardsPanel.Children.Clear();
        _cards.Clear();

        if (_items.Count == 0)
        {
            _cardsPanel.Children.Add(UiTheme.TxtAt("No assets found.", 12, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center));
            return;
        }

        foreach (var asset in _items)
        {
            var thumb = new Image { Height = 96, Stretch = Stretch.UniformToFill };
            if (!string.IsNullOrEmpty(asset.Thumbnail)) LoadThumbnail(thumb, asset.Thumbnail);

            var price = asset.IsFree
                ? UiTheme.Badge("FREE", Color.Parse("#14331f"), UiTheme.Green)
                : UiTheme.Badge($"${asset.Price:0.##}", Color.Parse("#3a2e15"), UiTheme.Orange);

            var title = UiTheme.Txt(asset.Title, 12, UiTheme.Text, FontWeight.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;

            var body = new StackPanel
            {
                Spacing = 5,
                Margin = new Thickness(9, 7, 9, 9),
                Children =
                {
                    title,
                    UiTheme.Txt(asset.Author?.Username ?? "Unknown", 10, UiTheme.Faint),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { price, UiTheme.Txt($"↓ {asset.Downloads:N0}", 10, UiTheme.Dim) },
                    },
                },
            };

            var card = UiTheme.CardBorder(new StackPanel { Children = { thumb, body } });
            card.Width = 190;
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerPressed += (_, _) => Select(asset);
            HighlightCard(card, asset);
            _cards.Add((asset, card));
            _cardsPanel.Children.Add(card);
        }
    }

    private void HighlightCard(Border card, StoreAsset asset)
    {
        bool active = _selected?.Id == asset.Id;
        card.BorderBrush = UiTheme.B(active ? UiTheme.Accent : UiTheme.Border);
        card.BorderThickness = new Thickness(active ? 2 : 1);
    }

    private void Select(StoreAsset asset)
    {
        _selected = asset;
        foreach (var (a, card) in _cards) HighlightCard(card, a);
        RenderDetail();
    }

    private void RenderDetail()
    {
        _detail.Children.Clear();

        if (_selected == null)
        {
            _detail.Children.Add(UiTheme.TxtAt("Select an asset", 13, UiTheme.Dim, FontWeight.Medium, HorizontalAlignment.Center));
            _detail.Children.Add(UiTheme.TxtAt("Details and install options appear here.", 11, UiTheme.Faint, FontWeight.Normal, HorizontalAlignment.Center));
            return;
        }

        var a = _selected;
        var title = UiTheme.Txt(a.Title, 15, UiTheme.Text, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;

        var tags = new WrapPanel();
        foreach (string tag in a.Tags ?? [])
        {
            var badge = UiTheme.Badge(tag, UiTheme.Panel, UiTheme.Dim);
            badge.Margin = new Thickness(0, 0, 6, 6);
            tags.Children.Add(badge);
        }

        var price = a.IsFree
            ? UiTheme.Badge("FREE", Color.Parse("#14331f"), UiTheme.Green)
            : UiTheme.Badge($"${a.Price:0.##} USD", Color.Parse("#3a2e15"), UiTheme.Orange);

        var downloadBtn = UiTheme.ActionButton(a.IsFree ? "Download & Install" : "Download (sign in on web)",
            null, () => _ = DownloadSelectedAsync(), primary: true);
        var openBtn = UiTheme.ActionButton("Open in Browser", null, OpenInBrowser);

        var description = UiTheme.Txt(a.Description?.Length > 0 ? a.Description : "No description provided.", 11, UiTheme.Dim);
        description.TextWrapping = TextWrapping.Wrap;

        _detail.Children.Add(title);
        _detail.Children.Add(UiTheme.Txt($"{a.Author?.Username ?? "Unknown"} · {a.Category}", 11, UiTheme.Cyan));
        _detail.Children.Add(tags);
        _detail.Children.Add(price);
        _detail.Children.Add(MetaRow("Version", a.Version));
        _detail.Children.Add(MetaRow("Size", HumanSize(a.FileSize)));
        _detail.Children.Add(MetaRow("License", a.License ?? "—"));
        _detail.Children.Add(MetaRow("Downloads", a.Downloads.ToString("N0")));
        _detail.Children.Add(MetaRow("Rating", a.Rating > 0 ? $"★ {a.Rating:0.0}" : "—"));
        _detail.Children.Add(UiTheme.Divider());
        _detail.Children.Add(description);
        _detail.Children.Add(downloadBtn);
        _detail.Children.Add(openBtn);
    }

    private static StackPanel MetaRow(string label, string value)
        => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { UiTheme.Txt(label, 11, UiTheme.Faint), UiTheme.Txt(value, 11, UiTheme.Text, FontWeight.Medium) },
        };

    private async System.Threading.Tasks.Task DownloadSelectedAsync()
    {
        var asset = _selected;
        if (asset == null || _busy) return;

        string root = _projectRoot();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            // No project open (e.g. store opened from the home screen) — let the user pick one.
            SetStatus("Select the project folder to install into…", UiTheme.Dim);
            root = await PickInstallFolderAsync();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                SetStatus("Open a project first — assets install into its Assets folder.", UiTheme.Dim);
                return;
            }
        }

        _busy = true;
        SetBusyUi(true);
        try
        {
            SetStatus($"Requesting '{asset.Title}'…", UiTheme.Dim);
            var info = await StoreClient.GetDownloadInfoAsync(StoreClient.DefaultStoreUrl, asset.Id);

            SetStatus($"Downloading {info.Filename} ({HumanSize(info.FileSize)})…", UiTheme.Dim);
            byte[] bytes = await StoreClient.DownloadFileAsync(info.Url);

            string fileName = string.IsNullOrEmpty(info.Filename)
                ? $"{asset.Slug}.zip"
                : Path.GetFileName(info.Filename);
            string storeDir = Path.Combine(root, "Assets", "Store");
            Directory.CreateDirectory(storeDir);
            string savedPath = Path.Combine(storeDir, fileName);
            File.WriteAllBytes(savedPath, bytes);

            string message;
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && bytes.Length > 2 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                string dest = Path.Combine(root, "Assets", SafeName(asset.Slug));
                ExtractZipSafe(savedPath, dest);
                message = $"Installed '{asset.Title}' → Assets/{Path.GetFileName(dest)}";
            }
            else
            {
                message = $"Saved '{asset.Title}' → Assets/Store/{fileName}";
            }

            SetStatus(message, UiTheme.Green);
            _log(message);
            _onInstalled?.Invoke();
        }
        catch (StoreApiException ex) when (ex.Code == "login_required")
        {
            SetStatus("This is a paid asset — sign in at lumoengine.vercel.app and try again.", UiTheme.Orange);
        }
        catch (StoreApiException ex)
        {
            SetStatus(ex.Message, UiTheme.Red);
        }
        catch (HttpRequestException)
        {
            SetStatus("Download failed — check your internet connection.", UiTheme.Red);
        }
        catch (Exception ex)
        {
            SetStatus($"Install failed: {ex.Message}", UiTheme.Red);
        }
        finally
        {
            _busy = false;
            SetBusyUi(false);
        }
    }

    private async System.Threading.Tasks.Task<string> PickInstallFolderAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return "";
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select the project folder to install into (contains Project.json)",
                    AllowMultiple = false,
                });
            if (folders.Count == 0) return "";
            string? local = folders[0].TryGetLocalPath();
            return string.IsNullOrEmpty(local) ? "" : local;
        }
        catch
        {
            return "";
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.Foreground = UiTheme.B(color);
    }

    private void OpenInBrowser()
    {
        if (_selected == null) return;
        string url = $"{StoreClient.DefaultStoreUrl}/asset/{_selected.Slug}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log($"Open browser failed: {ex.Message}"); }
    }

    private static void ExtractZipSafe(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string full = Path.GetFullPath(destDir);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(full, entry.FullName));
            if (!target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static void LoadThumbnail(Image image, string url)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] bytes = await StoreClient.DownloadFileAsync(url);
                var bmp = new Bitmap(new MemoryStream(bytes));
                await Dispatcher.UIThread.InvokeAsync(() => image.Source = bmp);
            }
            catch { }
        });
    }

    private static string SafeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        string safe = new(chars);
        return safe.Length > 0 ? safe : "asset";
    }

    private static string HumanSize(int bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.#} GB";
        if (bytes >= 1 << 20) return $"{bytes / (double)(1 << 20):0.#} MB";
        return $"{bytes / 1024.0:0.#} KB";
    }
}
