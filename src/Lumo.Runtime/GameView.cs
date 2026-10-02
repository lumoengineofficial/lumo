using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumo.Engine.Assets;
using Lumo.Engine.Input;
using Lumo.Engine.Rendering;
using Lumo.Engine.Rendering.Software;
using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Mesh = Lumo.Engine.Rendering.Abstractions.Mesh;
using Key = Avalonia.Input.Key;
using LumoKey = Lumo.Engine.Input.Key;
using MouseButton = Lumo.Engine.Input.MouseButton;

namespace Lumo.Runtime;

/// <summary>
/// Game view: shows a loading screen, then renders the active scene every frame
/// (game camera when a primary camera exists, otherwise auto-framed 2D / default
/// 3D view), feeds keyboard and mouse input to the runtime and drives the tick.
/// </summary>
public sealed class GameView : Control
{
    private readonly GameRuntime _runtime;
    private System.Timers.Timer? _loop;
    private static readonly Color Bg = Color.Parse("#101425");
    private static readonly Color LoadingBg = Color.Parse("#0B0E1A");

    private readonly Stopwatch _loadingWatch = new();
    private bool _started;
    private bool _captured;
    private static Bitmap? _logo;

    /// <summary>Seconds the loading screen stays up before the game starts.</summary>
    public double LoadingSeconds { get; set; } = 2.2;

    public GameView(GameRuntime runtime)
    {
        _runtime = runtime;
        Focusable = true;
        ClipToBounds = true;
    }

    public void BeginLoop()
    {
        Focus();
        _loadingWatch.Restart();
        _loop = new System.Timers.Timer(16);
        _loop.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(Frame, Avalonia.Threading.DispatcherPriority.Render);
        _loop.Start();
    }

    private void Frame()
    {
        if (!_started)
        {
            if (_runtime.LoadError == null && _loadingWatch.Elapsed.TotalSeconds >= LoadingSeconds)
            {
                try
                {
                    _runtime.Start();
                    _started = true;
                    Focus();
                }
                catch (Exception ex)
                {
                    _runtime.LoadError = $"Play start failed: {ex.Message}";
                }
            }
            InvalidateVisual();
            return;
        }

        _runtime.Tick();
        InvalidateVisual();
    }

    public void Shutdown()
    {
        _loop?.Dispose();
        _loop = null;
        _runtime.Dispose();
    }

    // ------------------------------------------------------------ input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        LumoKey key = MapKey(e.Key);
        if (key != LumoKey.Unknown)
        {
            _runtime.Input.KeyPressed(key);
            e.Handled = true;
        }
        if (e.Key == Key.Escape && _captured)
            ReleaseLook();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        LumoKey key = MapKey(e.Key);
        if (key != LumoKey.Unknown)
        {
            _runtime.Input.KeyReleased(key);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_started)
            return;
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        MouseButton button =
            props.IsRightButtonPressed ? MouseButton.Right :
            props.IsMiddleButtonPressed ? MouseButton.Middle :
            MouseButton.Left;
        _runtime.Input.MousePressed(button);
        if (button == MouseButton.Left && !_captured)
            CaptureLook();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_started)
            return;
        _runtime.Input.MouseReleased(MapButton(e.InitialPressMouseButton));
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_started || !_captured)
            return;

        // Look deltas come from the OS cursor: the pointer is parked at the
        // window centre, so any movement is turned into mouse-look pixels.
        if (!GetCursorPos(out var cur) || !TryGetClientCenter(out var center))
            return;

        int dx = cur.X - center.X;
        int dy = cur.Y - center.Y;
        if (Math.Abs(dx) >= 1 || Math.Abs(dy) >= 1)
        {
            _runtime.Input.AddMouseDelta(dx, dy);
            SetCursorPos(center.X, center.Y);
        }
        e.Handled = true;
    }

    private void CaptureLook()
    {
        _captured = true;
        Cursor = new Cursor(StandardCursorType.None);
        if (TryGetClientCenter(out var center))
            SetCursorPos(center.X, center.Y);
    }

    private void ReleaseLook()
    {
        _captured = false;
        Cursor = Cursor.Default;
    }

    private static MouseButton MapButton(Avalonia.Input.MouseButton button) => button switch
    {
        Avalonia.Input.MouseButton.Right => MouseButton.Right,
        Avalonia.Input.MouseButton.Middle => MouseButton.Middle,
        _ => MouseButton.Left,
    };

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

    // ------------------------------------------------------------ win32 cursor

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WinPoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out WinRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref WinPoint point);

    private bool TryGetClientCenter(out WinPoint center)
    {
        center = default;
        var handle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !GetClientRect(handle, out var rect))
            return false;

        var point = new WinPoint { X = (rect.Right - rect.Left) / 2, Y = (rect.Bottom - rect.Top) / 2 };
        if (!ClientToScreen(handle, ref point))
            return false;
        center = point;
        return true;
    }

    // ------------------------------------------------------------ camera

    private Entity? FindPrimaryCamera()
    {
        Entity? first = null;
        foreach (var e in _runtime.Scene.AllEntities)
        {
            if (e.Camera == null) continue;
            if (e.Camera.IsPrimary) return e;
            first ??= e;
        }
        return first;
    }

    private (Matrix4x4 view, Matrix4x4 proj) GetMatrices(int w, int h)
    {
        float aspect = (float)w / Math.Max(1, h);
        Entity? cam = FindPrimaryCamera();

        if (cam != null)
        {
            var pos = cam.Transform.Position;
            var fwd = cam.Transform.Forward;
            var view = Matrix4x4.CreateLookAt(pos, pos + fwd, Vector3.UnitY);
            float fov = cam.Camera?.FieldOfView ?? 60f;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov * MathF.PI / 180f, aspect, 0.05f, 1000f);
            return (view, proj);
        }

        (Vector3 min, Vector3 max) bounds = SpriteBounds();

        if (OnlySprites())
        {
            // Auto-framed 2D top-down view.
            Vector3 center = (bounds.min + bounds.max) * 0.5f;
            float halfX = (bounds.max.X - bounds.min.X) * 0.5f + 1f;
            float halfY = (bounds.max.Y - bounds.min.Y) * 0.5f + 1f;
            float halfH = MathF.Max(MathF.Max(halfY, halfX / MathF.Max(0.2f, aspect)), 1.5f);
            float camDist = MathF.Max(3f, halfH * 2f);
            var eye = center + new Vector3(0, 0, camDist);
            var view2D = Matrix4x4.CreateLookAt(eye, center, Vector3.UnitY);
            var proj2D = Matrix4x4.CreateOrthographic(halfH * 2f * aspect, halfH * 2f, 0.01f, 500f);
            return (view2D, proj2D);
        }

        // Default orbiting view of the whole scene.
        Vector3 target = (bounds.min + bounds.max) * 0.5f;
        const float dist = 8f;
        float yaw = 45f * MathF.PI / 180f;
        float pitch = 25f * MathF.PI / 180f;
        var scenePos = target + new Vector3(
            dist * MathF.Cos(pitch) * MathF.Sin(yaw),
            dist * MathF.Sin(pitch),
            dist * MathF.Cos(pitch) * MathF.Cos(yaw));
        var view3D = Matrix4x4.CreateLookAt(scenePos, target, Vector3.UnitY);
        var proj3D = Matrix4x4.CreatePerspectiveFieldOfView(60f * MathF.PI / 180f, aspect, 0.1f, 200f);
        return (view3D, proj3D);
    }

    private bool OnlySprites()
    {
        foreach (var e in _runtime.Scene.AllEntities)
        {
            if (!e.IsActive || e.Transform == null) continue;
            if (e.SpriteRenderer is not { IsVisible: true }) return false;
        }
        return true;
    }

    private (Vector3 min, Vector3 max) SpriteBounds()
    {
        bool any = false;
        var min = Vector3.Zero;
        var max = Vector3.Zero;
        foreach (var e in _runtime.Scene.AllEntities)
        {
            if (!e.IsActive || e.SpriteRenderer is not { IsVisible: true } sp || e.Transform == null) continue;
            var c = e.Transform.Position;
            var lo = c - new Vector3(sp.Width * 0.5f, sp.Height * 0.5f, 0);
            var hi = c + new Vector3(sp.Width * 0.5f, sp.Height * 0.5f, 0);
            if (!any) { min = lo; max = hi; any = true; }
            else { min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); }
        }
        if (!any) { min = new Vector3(-2, -2, 0); max = new Vector3(2, 2, 0); }
        return (min, max);
    }

    private static Vector2 Project(Vector3 world, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), view * proj);
        if (clip.W <= 0.001f) return new Vector2(-9999, -9999);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2((ndcX + 1) * 0.5f * w, (1 - ndcY) * 0.5f * h);
    }

    private static Vector3 CameraPosition(Matrix4x4 view)
    {
        Matrix4x4.Invert(view, out var inv);
        return new Vector3(inv.M41, inv.M42, inv.M43);
    }

    // ------------------------------------------------------------ render

    public override void Render(DrawingContext ctx)
    {
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);

        if (_runtime.LoadError != null)
        {
            ctx.FillRectangle(new SolidColorBrush(Bg), new Rect(0, 0, w, h));
            DrawOverlayText(ctx, _runtime.LoadError, w, h, Color.Parse("#ff8080"));
            return;
        }

        if (!_started)
        {
            DrawLoading(ctx, w, h);
            return;
        }

        ctx.FillRectangle(new SolidColorBrush(Bg), new Rect(0, 0, w, h));

        var scene = _runtime.Scene;
        var (view, proj) = GetMatrices(w, h);
        Vector3 camPos = CameraPosition(view);
        bool shading = FxRegistry.MeshShading;

        foreach (var entity in scene.AllEntities)
        {
            if (entity.Transform == null || !entity.IsActive) continue;

            if (entity.MeshRenderer != null)
            {
                if (!entity.MeshRenderer.IsVisible) continue;
                var mesh = MeshLibrary.Get(entity.MeshRenderer.MeshName);
                if (mesh != null && mesh.Vertices.Length >= 9 && mesh.Indices.Length >= 3)
                    DrawMesh(ctx, mesh, entity, view, proj, camPos, shading, w, h);
                else
                    DrawCube(ctx, entity, view, proj, camPos, shading, w, h);
            }
            else if (entity.SpriteRenderer is { IsVisible: true } sp)
            {
                DrawSprite(ctx, entity, sp, _runtime.ProjectDir, view, proj, w, h);
            }
        }

        DrawFrameEffects(ctx, w, h);

        foreach (var entry in _runtime.Hud.Entries)
            DrawHudEntry(ctx, entry, w, h);

        if (scene.AllEntities.Count == 0)
            DrawOverlayText(ctx, "No scene loaded.", w, h, Color.Parse("#666677"));
    }

    private static void DrawFrameEffects(DrawingContext ctx, int w, int h)
    {
        foreach (var fx in FxRegistry.Snapshot())
        {
            float intensity = Math.Clamp(fx.Intensity, 0f, 1f);
            if (intensity <= 0f)
                continue;

            switch (fx.Kind)
            {
                case FxKind.Vignette:
                    var vignette = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
                            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
                            new GradientStop(Color.FromArgb((byte)(215 * intensity), 0, 0, 0), 1.0)
                        }
                    };
                    ctx.FillRectangle(vignette, new Rect(0, 0, w, h));
                    break;

                case FxKind.ColorGrade:
                    byte r = (byte)Math.Clamp(fx.Tint.X * 255f, 0, 255);
                    byte g = (byte)Math.Clamp(fx.Tint.Y * 255f, 0, 255);
                    byte b = (byte)Math.Clamp(fx.Tint.Z * 255f, 0, 255);
                    ctx.FillRectangle(
                        new SolidColorBrush(Color.FromArgb((byte)(46 * intensity), r, g, b)),
                        new Rect(0, 0, w, h));
                    break;
            }
        }
    }

    // ------------------------------------------------------------ loading

    private void DrawLoading(DrawingContext ctx, int w, int h)
    {
        ctx.FillRectangle(new SolidColorBrush(LoadingBg), new Rect(0, 0, w, h));

        double t = LoadingSeconds <= 0
            ? 1
            : Math.Clamp(_loadingWatch.Elapsed.TotalSeconds / LoadingSeconds, 0, 1);

        double centerX = w * 0.5;

        var logo = Logo;
        if (logo != null)
        {
            double logoH = 150;
            double aspect = (double)logo.PixelSize.Width / Math.Max(1, logo.PixelSize.Height);
            double logoW = logoH * aspect;
            double logoY = h * 0.30 - logoH * 0.5;
            ctx.DrawImage(logo, new Rect(centerX - logoW * 0.5, logoY, logoW, logoH));
        }

        var title = MakeText("LUMO ENGINE", 30, Color.Parse("#F2F5FF"), FontWeight.Bold);
        ctx.DrawText(title, new Point(centerX - title.Width * 0.5, h * 0.30 + 92));

        var sub = MakeText(_runtime.Title, 16, Color.Parse("#8E9AC4"));
        ctx.DrawText(sub, new Point(centerX - sub.Width * 0.5, h * 0.30 + 136));

        double barW = 440, barH = 8;
        double barX = centerX - barW * 0.5;
        double barY = h * 0.62;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1A2036")), new Rect(barX, barY, barW, barH), 4);
        double ease = 1 - Math.Pow(1 - t, 3);
        if (ease > 0.001)
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#4F9DED")), new Rect(barX, barY, barW * ease, barH), 4);

        var percent = MakeText($"{(int)(t * 100)}%", 14, Color.Parse("#8E9AC4"));
        ctx.DrawText(percent, new Point(centerX - percent.Width * 0.5, barY + 18));

        var loading = MakeText("Loading " + (_runtime.Title ?? "game") + "...", 15, Color.Parse("#C9D2F0"));
        ctx.DrawText(loading, new Point(centerX - loading.Width * 0.5, barY - 34));

        var tip = MakeText("Click to capture mouse  |  WASD move  |  Left click shoot  |  Esc release",
            13, Color.Parse("#5D6794"));
        ctx.DrawText(tip, new Point(centerX - tip.Width * 0.5, h * 0.84));
    }

    private static Bitmap? Logo => _logo ??= LoadLogo();

    private static Bitmap? LoadLogo()
    {
        try
        {
            var asm = typeof(GameView).Assembly;
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("lumo_logo_transparent.png", StringComparison.OrdinalIgnoreCase));
            if (name == null)
                return null;
            using var stream = asm.GetManifestResourceStream(name);
            return stream == null ? null : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static FormattedText MakeText(string text, double size, Color color, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, weight), size, new SolidColorBrush(color));

    // ------------------------------------------------------------ draw

    private static void DrawHudEntry(DrawingContext ctx, HudEntry entry, int w, int h)
    {
        if (string.IsNullOrEmpty(entry.Text)) return;
        var c = entry.Color;
        var color = Color.FromRgb(
            (byte)Math.Clamp((int)(c.X * 255f), 0, 255),
            (byte)Math.Clamp((int)(c.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)(c.Z * 255f), 0, 255));
        var ft = new FormattedText(
            entry.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold), entry.Size, new SolidColorBrush(color));
        ctx.DrawText(ft, new Point(entry.X * w, entry.Y * h));
    }

    private static void DrawOverlayText(DrawingContext ctx, string text, int w, int h, Color color)
    {
        var ft = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 14, new SolidColorBrush(color));
        ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }

    private static void DrawSprite(DrawingContext ctx, Entity entity, SpriteRendererComponent sp, string? projectRoot, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        float hw = sp.Width * 0.5f, hh = sp.Height * 0.5f;
        var c = entity.Transform.Position;
        var a = Project(c + new Vector3(-hw, -hh, 0), view, proj, w, h);
        var b = Project(c + new Vector3(hw, -hh, 0), view, proj, w, h);
        var d = Project(c + new Vector3(hw, hh, 0), view, proj, w, h);
        var e2 = Project(c + new Vector3(-hw, hh, 0), view, proj, w, h);
        if (a.X < -9000) return;

        if (!string.IsNullOrEmpty(sp.SpritePath) && projectRoot != null)
        {
            var tex = Texture2D.Load(Path.Combine(projectRoot, sp.SpritePath));
            if (tex != null)
            {
                var quad = TexturedQuad.Rasterize(tex,
                    new Vector2(a.X, a.Y), new Vector2(b.X, b.Y),
                    new Vector2(d.X, d.Y), new Vector2(e2.X, e2.Y), sp.Color);
                if (quad != null)
                {
                    var img = MakeQuadBitmap(quad);
                    ctx.DrawImage(img, new Rect(quad.X, quad.Y, quad.Width, quad.Height));
                    return;
                }
            }
        }

        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(a.X, a.Y), true);
            gc.LineTo(new Point(b.X, b.Y));
            gc.LineTo(new Point(d.X, d.Y));
            gc.LineTo(new Point(e2.X, e2.Y));
            gc.EndFigure(true);
        }
        var sc = sp.Color;
        var fill = Color.FromRgb(
            (byte)Math.Clamp((int)(sc.X * 255f), 0, 255),
            (byte)Math.Clamp((int)(sc.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)(sc.Z * 255f), 0, 255));
        ctx.DrawGeometry(new SolidColorBrush(fill), null, geo);
    }

    private static WriteableBitmap MakeQuadBitmap(QuadRaster quad)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(quad.Width, quad.Height), new Avalonia.Vector(96, 96));
        using var fb = bmp.Lock();
        int rowBytes = quad.Width * 4;
        for (int y = 0; y < quad.Height; y++)
        {
            Marshal.Copy(quad.Pixels, y * rowBytes,
                (IntPtr)(fb.Address + (long)y * fb.RowBytes), rowBytes);
        }
        return bmp;
    }

    private static Color ShadeColor(Vector3 baseColor, float brightness) => Color.FromRgb(
        (byte)Math.Clamp(baseColor.X * brightness * 255f, 0, 255),
        (byte)Math.Clamp(baseColor.Y * brightness * 255f, 0, 255),
        (byte)Math.Clamp(baseColor.Z * brightness * 255f, 0, 255));

    private static void DrawCube(DrawingContext ctx, Entity entity, Matrix4x4 view, Matrix4x4 proj, Vector3 camPos, bool shading, int w, int h)
    {
        Vector3 center = entity.Transform.Position;
        float size = MathF.Max(0.1f, entity.Transform.Scale.Y);
        var baseColor = entity.MeshRenderer?.Color ?? new Vector3(0.55f, 0.62f, 0.75f);
        float hs = size * 0.5f;
        var verts = new Vector3[]
        {
            center + new Vector3(-hs, -hs, -hs), center + new Vector3(hs, -hs, -hs),
            center + new Vector3(hs, hs, -hs), center + new Vector3(-hs, hs, -hs),
            center + new Vector3(-hs, -hs, hs), center + new Vector3(hs, -hs, hs),
            center + new Vector3(hs, hs, hs), center + new Vector3(-hs, hs, hs),
        };
        int[][] faces = [
            [0, 1, 2, 3], [5, 4, 7, 6], [4, 0, 3, 7],
            [1, 5, 6, 2], [3, 2, 6, 7], [4, 5, 1, 0]
        ];
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(180, 200, 230)), 1.0);

        var faceList = new List<(float depth, int[] face)>();
        foreach (var f in faces)
        {
            Vector3 avg = (verts[f[0]] + verts[f[1]] + verts[f[2]] + verts[f[3]]) / 4f;
            faceList.Add((Vector4.Transform(new Vector4(avg, 1), view).Z, f));
        }
        faceList.Sort((a, b) => b.depth.CompareTo(a.depth));

        foreach (var (_, f) in faceList)
        {
            var p0 = Project(verts[f[0]], view, proj, w, h);
            var p1 = Project(verts[f[1]], view, proj, w, h);
            var p2 = Project(verts[f[2]], view, proj, w, h);
            var p3 = Project(verts[f[3]], view, proj, w, h);
            if (p0.X < -9000 || p1.X < -9000 || p2.X < -9000 || p3.X < -9000) continue;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(p0.X, p0.Y), true);
                gc.LineTo(new Point(p1.X, p1.Y));
                gc.LineTo(new Point(p2.X, p2.Y));
                gc.LineTo(new Point(p3.X, p3.Y));
                gc.EndFigure(true);
            }

            Vector3 normal = Vector3.Normalize(Vector3.Cross(verts[f[1]] - verts[f[0]], verts[f[2]] - verts[f[0]]));
            Vector3 mid = (verts[f[0]] + verts[f[2]]) * 0.5f;
            float brightness = shading ? FxLighting.Brightness(normal, camPos - mid) : 1f;
            var fill = new SolidColorBrush(ShadeColor(baseColor, brightness));
            ctx.DrawGeometry(fill, edge, geo);
        }
    }

    private static void DrawMesh(DrawingContext ctx, Mesh mesh, Entity entity, Matrix4x4 view, Matrix4x4 proj, Vector3 camPos, bool shading, int w, int h)
    {
        var m = entity.Transform.LocalToWorldMatrix;
        var verts = mesh.Vertices;
        var idx = mesh.Indices;
        var baseColor = entity.MeshRenderer?.Color ?? new Vector3(0.55f, 0.62f, 0.75f);

        var world = new Vector3[verts.Length / 3];
        for (int i = 0; i < world.Length; i++)
        {
            var v = new Vector3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);
            world[i] = Vector3.Transform(v, m);
        }

        var tris = new List<(float depth, int a, int b, int c)>(idx.Length / 3);
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var mid = (world[idx[i]] + world[idx[i + 1]] + world[idx[i + 2]]) / 3f;
            float depth = Vector4.Transform(new Vector4(mid, 1), view).Z;
            if (depth > -0.001f) continue;
            tris.Add((depth, (int)idx[i], (int)idx[i + 1], (int)idx[i + 2]));
        }
        tris.Sort((x, y) => y.depth.CompareTo(x.depth));

        var edge = new Pen(new SolidColorBrush(Color.FromRgb(150, 168, 200)), 0.8);
        var vcols = mesh.VertexColors;
        bool hasVc = vcols.Length == world.Length * 4;
        foreach (var (_, ai, bi, ci) in tris)
        {
            var p0 = Project(world[ai], view, proj, w, h);
            var p1 = Project(world[bi], view, proj, w, h);
            var p2 = Project(world[ci], view, proj, w, h);
            if (p0.X < -9000 || p1.X < -9000 || p2.X < -9000) continue;

            Vector3 fill = baseColor;
            if (hasVc)
            {
                int c0 = ai * 4, c1 = bi * 4, c2 = ci * 4;
                float alpha = (vcols[c0 + 3] + vcols[c1 + 3] + vcols[c2 + 3]) / 3f;
                if (alpha < 0.5f) continue;
                fill = new Vector3(
                    (vcols[c0] + vcols[c1] + vcols[c2]) / 3f * baseColor.X,
                    (vcols[c0 + 1] + vcols[c1 + 1] + vcols[c2 + 1]) / 3f * baseColor.Y,
                    (vcols[c0 + 2] + vcols[c1 + 2] + vcols[c2 + 2]) / 3f * baseColor.Z);
            }

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(p0.X, p0.Y), true);
                gc.LineTo(new Point(p1.X, p1.Y));
                gc.LineTo(new Point(p2.X, p2.Y));
                gc.EndFigure(true);
            }

            Vector3 normal = Vector3.Normalize(Vector3.Cross(world[bi] - world[ai], world[ci] - world[ai]));
            Vector3 centroid = (world[ai] + world[bi] + world[ci]) / 3f;
            float brightness = shading ? FxLighting.Brightness(normal, camPos - centroid) : 1f;
            ctx.DrawGeometry(new SolidColorBrush(ShadeColor(fill, brightness)), edge, geo);
        }
    }
}
