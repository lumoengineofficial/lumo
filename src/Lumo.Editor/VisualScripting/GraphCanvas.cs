using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lumo.Editor.Ui;
using Lumo.Engine.VisualScripting;

namespace Lumo.Editor.VisualScripting;

/// <summary>
/// Immediate-mode node-graph canvas: pan (drag background), zoom (wheel),
/// node dragging, wire creation by dragging between pins, selection and
/// debug highlighting of nodes active during play.
/// </summary>
public sealed class GraphCanvas : Control
{
    private const double NodeWidth = 178;
    private const double TitleHeight = 24;
    private const double PinRow = 22;
    private const double PinRadius = 5;
    private const double WireHitTolerance = 6;

    private VisualGraph? _graph;
    private double _zoom = 1.0;
    private Point _pan = new(40, 40);

    private VSNode? _selectedNode;
    private Connection? _selectedWire;
    private VSNode? _dragNode;
    private Point _dragOffset;
    private bool _panning;
    private Point _panStartScreen;
    private Point _panStartValue;
    private (VSNode Node, string Pin)? _linkSource;
    private Point? _linkCursor;
    private readonly HashSet<string> _activeNodes = new(StringComparer.Ordinal);

    public event Action? SelectionChanged;
    public event Action? GraphEdited;
    public event Action<double>? ZoomChanged;

    public double Zoom => _zoom;

    public GraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public VisualGraph? Graph
    {
        get => _graph;
        set
        {
            _graph = value;
            _selectedNode = null;
            _selectedWire = null;
            _linkSource = null;
            _activeNodes.Clear();
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
    }

    public VSNode? SelectedNode => _selectedNode;
    public Connection? SelectedWire => _selectedWire;

    public void SetActiveNodes(IEnumerable<string> nodeIds)
    {
        _activeNodes.Clear();
        foreach (string id in nodeIds)
            _activeNodes.Add(id);
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    public void ClearSelection()
    {
        _selectedNode = null;
        _selectedWire = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Invoked immediately before a graph mutation (undo snapshot point).</summary>
    public Action? BeforeGraphEdit;

    /// <summary>Places a new node near the viewport center and selects it.</summary>
    public VSNode AddNode(string typeId)
    {
        if (_graph is null || !NodeRegistry.TryGet(typeId, out var def))
            throw new InvalidOperationException("No graph loaded or unknown node type.");

        BeforeGraphEdit?.Invoke();
        VSNode node = def.Create();
        Point center = ToWorld(new Point(Bounds.Width / 2, Bounds.Height / 2));
        int offset = _graph.Nodes.Count % 8;
        node.X = Math.Round(center.X - NodeWidth / 2 + offset * 24);
        node.Y = Math.Round(center.Y - 30 + offset * 24);
        _graph.AddNode(node);
        Select(node);
        GraphEdited?.Invoke();
        return node;
    }

    public void DeleteSelection()
    {
        if (_graph is null) return;
        if (_selectedWire is null && _selectedNode is null) return;
        BeforeGraphEdit?.Invoke();
        if (_selectedWire is not null)
        {
            _graph.Connections.Remove(_selectedWire);
            _selectedWire = null;
            SelectionChanged?.Invoke();
            GraphEdited?.Invoke();
            InvalidateVisual();
            return;
        }
        if (_selectedNode is not null)
        {
            _graph.RemoveNode(_selectedNode.Id);
            _selectedNode = null;
            SelectionChanged?.Invoke();
            GraphEdited?.Invoke();
            InvalidateVisual();
        }
    }

    public void Select(VSNode? node)
    {
        _selectedNode = node;
        _selectedWire = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ------------------------------------------------------------ transform

    private Point ToWorld(Point screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
    private Point ToScreen(Point world) => new(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);

    // ------------------------------------------------------------ layout

    private static double NodeHeight(VSNode node)
    {
        int rows = Math.Max(node.InputPins.Count(), node.OutputPins.Count());
        return TitleHeight + rows * PinRow + 10;
    }

    private static Rect NodeRect(VSNode node) => new(node.X, node.Y, NodeWidth, NodeHeight(node));

    private static Point PinPosition(VSNode node, Pin pin)
    {
        int index = (pin.Direction == PinDirection.Input ? node.InputPins : node.OutputPins)
            .TakeWhile(p => p != pin).Count();
        double y = node.Y + TitleHeight + 11 + index * PinRow;
        double x = pin.Direction == PinDirection.Input ? node.X : node.X + NodeWidth;
        return new Point(x, y);
    }

    private (VSNode Node, Pin Pin)? HitPin(Point world)
    {
        if (_graph is null) return null;
        double r = PinRadius + 3 / _zoom;
        foreach (VSNode node in _graph.Nodes)
        {
            foreach (Pin pin in node.Pins)
            {
                Point p = PinPosition(node, pin);
                if (Math.Abs(p.X - world.X) <= r && Math.Abs(p.Y - world.Y) <= r)
                    return (node, pin);
            }
        }
        return null;
    }

    private VSNode? HitNode(Point world)
    {
        if (_graph is null) return null;
        for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
        {
            VSNode node = _graph.Nodes[i];
            if (NodeRect(node).Contains(world))
                return node;
        }
        return null;
    }

    private Connection? HitWire(Point screen)
    {
        if (_graph is null) return null;
        foreach (Connection conn in _graph.Connections)
        {
            VSNode? from = _graph.FindNode(conn.FromNode);
            VSNode? to = _graph.FindNode(conn.ToNode);
            Pin? fromPin = from?.GetPin(conn.FromPin);
            Pin? toPin = to?.GetPin(conn.ToPin);
            if (from is null || to is null || fromPin is null || toPin is null)
                continue;
            if (DistanceToWire(ToScreen(PinPosition(from, fromPin)), ToScreen(PinPosition(to, toPin)), screen) <= WireHitTolerance)
                return conn;
        }
        return null;
    }

    private static Point Bezier(Point p0, Point p1, double t)
    {
        double dx = Math.Max(40, Math.Abs(p1.X - p0.X) * 0.5);
        var c1 = new Point(p0.X + dx, p0.Y);
        var c2 = new Point(p1.X - dx, p1.Y);
        double u = 1 - t;
        double x = u * u * u * p0.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p1.X;
        double y = u * u * u * p0.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p1.Y;
        return new Point(x, y);
    }

    private static double DistanceToWire(Point p0, Point p1, Point target)
    {
        double best = double.MaxValue;
        for (int i = 0; i <= 14; i++)
        {
            Point p = Bezier(p0, p1, i / 14.0);
            best = Math.Min(best, Math.Sqrt((p.X - target.X) * (p.X - target.X) + (p.Y - target.Y) * (p.Y - target.Y)));
        }
        return best;
    }

    // ------------------------------------------------------------ input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        Point screen = point.Position;
        Point world = ToWorld(screen);

        if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
        {
            // double-click background: nothing to add inline; palette handles adding
        }

        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            (VSNode Node, Pin Pin)? pinHit = HitPin(world);
            if (pinHit is { } hit && point.Properties.IsLeftButtonPressed)
            {
                var (node, pin) = hit;
                if (pin.Direction == PinDirection.Output)
                {
                    _linkSource = (node, pin.Name);
                    _linkCursor = screen;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    return;
                }

                // input pin: detach existing wire for rewiring, else start from its source
                if (_graph is not null && _graph.FindInputConnection(node.Id, pin.Name) is Connection existing)
                {
                    _graph.Connections.Remove(existing);
                    _linkSource = (_graph.FindNode(existing.FromNode)!, existing.FromPin);
                    GraphEdited?.Invoke();
                }
                else if (pin.Kind == PinKind.Exec)
                {
                    // exec inputs have no literal; ignore empty clicks
                }
                _linkCursor = screen;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            if (point.Properties.IsLeftButtonPressed && HitWire(screen) is Connection wire)
            {
                _selectedWire = wire;
                _selectedNode = null;
                SelectionChanged?.Invoke();
                InvalidateVisual();
                return;
            }

            if (point.Properties.IsLeftButtonPressed && HitNode(world) is VSNode node2)
            {
                Select(node2);
                BeforeGraphEdit?.Invoke();
                _dragNode = node2;
                _dragOffset = new Point(world.X - node2.X, world.Y - node2.Y);
                e.Pointer.Capture(this);
                return;
            }

            // background: pan
            _panning = true;
            _panStartScreen = screen;
            _panStartValue = _pan;
            _selectedNode = null;
            _selectedWire = null;
            SelectionChanged?.Invoke();
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            _linkSource = null;
            _linkCursor = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point screen = e.GetPosition(this);

        if (_linkSource is { } link)
        {
            _linkCursor = screen;
            InvalidateVisual();
            return;
        }

        if (_dragNode is not null)
        {
            Point world = ToWorld(screen);
            _dragNode.X = Math.Round(world.X - _dragOffset.X);
            _dragNode.Y = Math.Round(world.Y - _dragOffset.Y);
            InvalidateVisual();
            return;
        }

        if (_panning)
        {
            _pan = _panStartValue + (screen - _panStartScreen);
            InvalidateVisual();
            return;
        }

        UpdateCursor(screen);
    }

    private void UpdateCursor(Point screen)
    {
        (VSNode Node, Pin Pin)? hit = HitPin(ToWorld(screen));
        if (hit is { } h)
        {
            Cursor = new Cursor(h.Pin.Direction == PinDirection.Output ? StandardCursorType.Hand : StandardCursorType.Cross);
        }
        else if (HitWire(screen) is not null)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            Cursor = Cursor.Default;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        if (_linkSource is { } link)
        {
            Point screen = e.GetPosition(this);
            if (HitPin(ToWorld(screen)) is { } target &&
                target.Pin.Direction == PinDirection.Input &&
                TryConnect(link.Node, link.Pin, target.Node, target.Pin.Name))
            {
                // connected
            }
            _linkSource = null;
            _linkCursor = null;
            InvalidateVisual();
            return;
        }

        if (_dragNode is not null)
        {
            _dragNode = null;
            GraphEdited?.Invoke();
            return;
        }

        _panning = false;
    }

    /// <summary>Validates and adds a wire; reports failures via no-op (validation list).</summary>
    public bool TryConnect(VSNode from, string fromPin, VSNode to, string toPin)
    {
        if (_graph is null) return false;
        Pin? outPin = from.GetPin(fromPin);
        Pin? inPin = to.GetPin(toPin);
        if (!GraphValidator.CanConnect(outPin, inPin))
            return false;
        BeforeGraphEdit?.Invoke();
        _graph.AddConnection(from, fromPin, to, toPin);
        _selectedWire = null;
        GraphEdited?.Invoke();
        InvalidateVisual();
        return true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point cursor = e.GetPosition(this);
        double factor = Math.Pow(1.12, e.Delta.Y);
        SetZoomAt(_zoom * factor, cursor);
        e.Handled = true;
    }

    /// <summary>Zoom keeping the given screen point (world position under it) fixed.</summary>
    public void SetZoomAt(double zoom, Point anchor)
    {
        Point world = ToWorld(anchor);
        double newZoom = Math.Clamp(zoom, 0.25, 4.0);
        if (Math.Abs(newZoom - _zoom) < 1e-9) return;
        _pan = new Point(anchor.X - world.X * newZoom, anchor.Y - world.Y * newZoom);
        _zoom = newZoom;
        InvalidateVisual();
        ZoomChanged?.Invoke(_zoom);
    }

    /// <summary>Zoom in/out around the canvas center (toolbar buttons / Ctrl+keys).</summary>
    public void ZoomBy(double factor)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        SetZoomAt(_zoom * factor, center);
    }

    public void ResetZoom() => SetZoomAt(1.0, new Point(Bounds.Width / 2, Bounds.Height / 2));

    /// <summary>Frame all nodes: fit + center the whole graph in the viewport.</summary>
    public void FitToView()
    {
        if (_graph == null || _graph.Nodes.Count == 0 || Bounds.Width < 32 || Bounds.Height < 32)
        {
            ResetZoom();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var node in _graph.Nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + NodeWidth);
            maxY = Math.Max(maxY, node.Y + NodeHeight(node));
        }

        const double pad = 70;
        double z = Math.Clamp(
            Math.Min(Bounds.Width / (maxX - minX + pad * 2), Bounds.Height / (maxY - minY + pad * 2)),
            0.25, 4.0);
        _zoom = z;
        _pan = new Point(
            Bounds.Width / 2 - (minX + maxX) / 2 * z,
            Bounds.Height / 2 - (minY + maxY) / 2 * z);
        InvalidateVisual();
        ZoomChanged?.Invoke(_zoom);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && (e.Key is Key.OemPlus or Key.Add))
        {
            ZoomBy(1.2);
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key is Key.OemMinus or Key.Subtract))
        {
            ZoomBy(1 / 1.2);
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key is Key.D0 or Key.NumPad0))
        {
            ResetZoom();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key is Key.D9 or Key.NumPad9)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _linkSource is not null)
        {
            _linkSource = null;
            _linkCursor = null;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ render

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(UiTheme.B(UiTheme.Bg), new Rect(Bounds.Size));
        DrawGrid(context);
        if (_graph is null) return;

        foreach (Connection conn in _graph.Connections)
            DrawWire(context, conn, conn == _selectedWire);

        if (_linkSource is { } link && _linkCursor is not null &&
            _graph.FindNode(link.Node.Id) is VSNode from &&
            from.GetPin(link.Pin) is Pin pin)
        {
            Point p0 = ToScreen(PinPosition(from, pin));
            DrawBezier(context, p0, _linkCursor.Value, PinColor(pin.DataType), 2, true);
        }

        foreach (VSNode node in _graph.Nodes)
            DrawNode(context, node);
    }

    private void DrawGrid(DrawingContext context)
    {
        double step = 24 * _zoom;
        while (step < 16) step *= 2;
        var pen = new Pen(UiTheme.B(Color.Parse("#151c30")), 1);

        double startX = _pan.X % step;
        if (startX < 0) startX += step;
        for (double x = startX; x < Bounds.Width; x += step)
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));

        double startY = _pan.Y % step;
        if (startY < 0) startY += step;
        for (double y = startY; y < Bounds.Height; y += step)
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawWire(DrawingContext context, Connection conn, bool selected)
    {
        VSNode? from = _graph!.FindNode(conn.FromNode);
        VSNode? to = _graph.FindNode(conn.ToNode);
        Pin? outPin = from?.GetPin(conn.FromPin);
        Pin? inPin = to?.GetPin(conn.ToPin);
        if (from is null || to is null || outPin is null || inPin is null)
            return;

        Point p0 = ToScreen(PinPosition(from, outPin));
        Point p1 = ToScreen(PinPosition(to, inPin));
        Color color = selected ? UiTheme.Cyan : PinColor(outPin.DataType);
        double width = outPin.Kind == PinKind.Exec ? (selected ? 3.5 : 2.5) : (selected ? 3 : 2);
        DrawBezier(context, p0, p1, color, width, false);
    }

    private static void DrawBezier(DrawingContext context, Point p0, Point p1, Color color, double width, bool dashed)
    {
        double dx = Math.Max(40, Math.Abs(p1.X - p0.X) * 0.5);
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(p0, false);
            gc.CubicBezierTo(new Point(p0.X + dx, p0.Y), new Point(p1.X - dx, p1.Y), p1);
            gc.EndFigure(false);
        }
        var pen = new Pen(new SolidColorBrush(color), width);
        if (dashed)
            pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        context.DrawGeometry(null, pen, geo);
    }

    private void DrawNode(DrawingContext context, VSNode node)
    {
        var rect = new Rect(ToScreen(new Point(node.X, node.Y)),
            new Size(NodeWidth * _zoom, NodeHeight(node) * _zoom));
        bool active = _activeNodes.Contains(node.Id);
        bool selected = node == _selectedNode;

        double scale = _zoom;
        var body = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        var header = new Rect(body.X, body.Y, body.Width, TitleHeight * scale);

        context.DrawRectangle(UiTheme.B(UiTheme.Card), null, body, 7 * scale, 7 * scale);

        Color cat = CategoryColor(node);
        context.DrawRectangle(new SolidColorBrush(cat), null, header, 7 * scale, 7 * scale);
        context.DrawRectangle(new SolidColorBrush(cat), null,
            new Rect(header.X, header.Y + TitleHeight * scale - 6 * scale, header.Width, 6 * scale));

        // title
        double titleMax = body.Width - 16 * scale;
        var title = MakeText(TrimTo(TitleFor(node), titleMax, 11 * scale), 11 * scale, Color.Parse("#0a0e17"), FontWeight.SemiBold);
        context.DrawText(title, new Point(header.X + 9 * scale, header.Y + (TitleHeight * scale - title.Height) / 2));

        // pins
        double rowY = body.Y + TitleHeight * scale + 11 * scale;
        int inputIndex = 0;
        foreach (Pin pin in node.InputPins)
        {
            double y = rowY + inputIndex * PinRow * scale;
            Point world = PinPosition(node, pin);
            Point sp = ToScreen(world);
            DrawPin(context, sp, pin, node);

            double labelX = sp.X + (PinRadius + 5) * scale;
            var label = MakeText(pin.Name, 10 * scale, UiTheme.Dim, FontWeight.Normal);
            bool showValue = false;
            string raw = "";
            if (pin.Kind == PinKind.Data &&
                node.Values.TryGetValue(pin.Name, out string? rv) &&
                !string.IsNullOrWhiteSpace(rv) &&
                _graph!.FindInputConnection(node.Id, pin.Name) is null)
            {
                showValue = true;
                raw = rv;
            }
            if (showValue)
            {
                double valueWidth = 46 * scale;
                var value = MakeText(TrimTo(raw, valueWidth, 10 * scale), 10 * scale, UiTheme.Orange, FontWeight.Medium);
                double vx = body.Right - 8 * scale - Math.Min(value.Width, valueWidth);
                if (vx > labelX + label.Width + 4 * scale)
                    context.DrawText(value, new Point(vx, y - value.Height / 2));
            }
            double labelMax = Math.Max(20, body.Width * 0.6);
            label = MakeText(TrimTo(pin.Name, labelMax, 10 * scale), 10 * scale, UiTheme.Dim, FontWeight.Normal);
            context.DrawText(label, new Point(labelX, y - label.Height / 2));
            inputIndex++;
        }

        int outputIndex = 0;
        foreach (Pin pin in node.OutputPins)
        {
            double y = rowY + outputIndex * PinRow * scale;
            Point sp = ToScreen(PinPosition(node, pin));
            DrawPin(context, sp, pin, node);

            var label = MakeText(TrimTo(pin.Name, 60 * scale, 10 * scale), 10 * scale,
                pin.Kind == PinKind.Exec ? UiTheme.Text : UiTheme.Dim, FontWeight.Normal);
            context.DrawText(label, new Point(sp.X - (PinRadius + 5) * scale - label.Width, y - label.Height / 2));
            outputIndex++;
        }

        // borders
        IPen borderPen = selected
            ? new Pen(UiTheme.B(UiTheme.Accent), 2)
            : active
                ? new Pen(UiTheme.B(UiTheme.Green), 2)
                : new Pen(UiTheme.B(UiTheme.Border), 1);
        context.DrawRectangle(null, borderPen, body, 7 * scale, 7 * scale);

        if (active && !selected)
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#3ddc8422")), null, body, 7 * scale, 7 * scale);
    }

    private void DrawPin(DrawingContext context, Point screen, Pin pin, VSNode node)
    {
        double r = PinRadius * _zoom;
        Color color = PinColor(pin.DataType);
        bool connected = pin.Kind == PinKind.Data
            ? _graph!.Connections.Any(c => (c.ToNode == node.Id && c.ToPin == pin.Name) ||
                                           (c.FromNode == node.Id && c.FromPin == pin.Name))
            : _graph!.Connections.Any(c => (c.ToNode == node.Id && c.ToPin == pin.Name) ||
                                           (c.FromNode == node.Id && c.FromPin == pin.Name));

        var fill = new SolidColorBrush(color);
        if (connected)
        {
            context.DrawEllipse(fill, null, screen, r, r);
        }
        else
        {
            context.DrawEllipse(UiTheme.B(UiTheme.Bg), new Pen(fill, 1.6), screen, r, r);
        }
    }

    private static string TitleFor(VSNode node)
    {
        if (node.TypeId.Length > 0 && NodeRegistry.TryGet(node.TypeId, out var def))
            return def!.Title;
        return node.GetType().Name;
    }

    private static Color CategoryColor(VSNode node) =>
        CategoryColor(node.TypeId.Length > 0 && NodeRegistry.TryGet(node.TypeId, out var def) ? def!.Category : "General");

    public static Color CategoryColor(string category) =>
        category switch
        {
            "Events" => Color.Parse("#e05555"),
            "Flow" => Color.Parse("#7c5cf0"),
            "Actions" => Color.Parse("#35c6d4"),
            "Math" => Color.Parse("#4f9ded"),
            "Logic" => Color.Parse("#3ddc84"),
            "Values" => Color.Parse("#e8a33d"),
            "Entity" => Color.Parse("#5a9bd5"),
            "Variables" => Color.Parse("#c06ad9"),
            "Input" => Color.Parse("#f07f5a"),
            "Physics" => Color.Parse("#8ad44f"),
            "HUD" => Color.Parse("#ffd75e"),
            "Graphics" => Color.Parse("#d35ad0"),
            _ => Color.Parse("#5c6580")
        };

    private static Color PinColor(PinDataType type) => type switch
    {
        PinDataType.Bool => UiTheme.Red,
        PinDataType.Int => UiTheme.Orange,
        PinDataType.Float => Color.Parse("#4f9ded"),
        PinDataType.String => Color.Parse("#ffd75e"),
        PinDataType.Vector3 => UiTheme.Green,
        PinDataType.Entity => UiTheme.Cyan,
        PinDataType.Any => Color.Parse("#9aa8ff"),
        _ => UiTheme.Text
    };

    private static FormattedText MakeText(string text, double size, Color color, FontWeight weight) =>
        new(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, UiTheme.B(color));

    /// <summary>Estimate-and-truncate with ellipsis (FormattedText has no trimming API).</summary>
    private static string TrimTo(string text, double maxWidth, double fontSize)
    {
        if (string.IsNullOrEmpty(text) || fontSize <= 0)
            return text;
        int maxChars = Math.Max(2, (int)(maxWidth / (fontSize * 0.58)));
        if (text.Length <= maxChars)
            return text;
        return text[..(maxChars - 1)] + "…";
    }
}
