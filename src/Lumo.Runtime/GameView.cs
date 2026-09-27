using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lumo.Engine.Assets;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using System.Globalization;
using System.Numerics;
using Mesh = Lumo.Engine.Rendering.Abstractions.Mesh;
using Key = Avalonia.Input.Key;
using LumoKey = Lumo.Engine.Input.Key;

namespace Lumo.Runtime;

/// <summary>
/// Game view: renders the active scene every frame (game camera when a primary
/// camera exists, otherwise auto-framed 2D / default 3D view), feeds keyboard
/// input to the runtime and drives the engine tick.
/// </summary>
public sealed class GameView : Control
{
    private readonly GameRuntime _runtime;
    private System.Timers.Timer? _loop;
    private static readonly Color Bg = Color.Parse("#101425");

    public GameView(GameRuntime runtime)
    {
        _runtime = runtime;
        Focusable = true;
        ClipToBounds = true;
    }

    public void BeginLoop()
    {
        Focus();
        _loop = new System.Timers.Timer(16);
        _loop.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _runtime.Tick();
                InvalidateVisual();
            }, Avalonia.Threading.DispatcherPriority.Render);
        _loop.Start();
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

    // ------------------------------------------------------------ render

    public override void Render(DrawingContext ctx)
    {
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);

        ctx.FillRectangle(new SolidColorBrush(Bg), new Rect(0, 0, w, h));

        var scene = _runtime.Scene;
        var (view, proj) = GetMatrices(w, h);

        foreach (var entity in scene.AllEntities)
        {
            if (entity.Transform == null || !entity.IsActive) continue;

            if (entity.MeshRenderer != null)
            {
                if (!entity.MeshRenderer.IsVisible) continue;
                var mesh = MeshLibrary.Get(entity.MeshRenderer.MeshName);
                if (mesh != null && mesh.Vertices.Length >= 9 && mesh.Indices.Length >= 3)
                    DrawMesh(ctx, mesh, entity, view, proj, w, h);
                else
                    DrawCube(ctx, entity.Transform.Position, MathF.Max(0.1f, entity.Transform.Scale.Y), view, proj, w, h);
            }
            else if (entity.SpriteRenderer is { IsVisible: true } sp)
            {
                DrawSprite(ctx, entity, sp, view, proj, w, h);
            }
        }

        if (_runtime.LoadError != null)
            DrawOverlayText(ctx, _runtime.LoadError, w, h, Color.Parse("#ff8080"));
        else if (scene.AllEntities.Count == 0)
            DrawOverlayText(ctx, "No scene loaded.", w, h, Color.Parse("#666677"));
    }

    private static void DrawOverlayText(DrawingContext ctx, string text, int w, int h, Color color)
    {
        var ft = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 14, new SolidColorBrush(color));
        ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }

    private static void DrawSprite(DrawingContext ctx, Entity entity, SpriteRendererComponent sp, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        float hw = sp.Width * 0.5f, hh = sp.Height * 0.5f;
        var c = entity.Transform.Position;
        var a = Project(c + new Vector3(-hw, -hh, 0), view, proj, w, h);
        var b = Project(c + new Vector3(hw, -hh, 0), view, proj, w, h);
        var d = Project(c + new Vector3(hw, hh, 0), view, proj, w, h);
        var e2 = Project(c + new Vector3(-hw, hh, 0), view, proj, w, h);
        if (a.X < -9000) return;

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

    private static void DrawCube(DrawingContext ctx, Vector3 center, float size, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
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
        var fill = new SolidColorBrush(Color.FromRgb(140, 160, 200));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(180, 200, 230)), 1.2);

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
            ctx.DrawGeometry(fill, edge, geo);
        }
    }

    private static void DrawMesh(DrawingContext ctx, Mesh mesh, Entity entity, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var m = entity.Transform.LocalToWorldMatrix;
        var verts = mesh.Vertices;
        var idx = mesh.Indices;

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

        var fill = new SolidColorBrush(Color.FromRgb(140, 160, 200));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(180, 200, 230)), 1.2);
        foreach (var (_, ai, bi, ci) in tris)
        {
            var p0 = Project(world[ai], view, proj, w, h);
            var p1 = Project(world[bi], view, proj, w, h);
            var p2 = Project(world[ci], view, proj, w, h);
            if (p0.X < -9000 || p1.X < -9000 || p2.X < -9000) continue;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(p0.X, p0.Y), true);
                gc.LineTo(new Point(p1.X, p1.Y));
                gc.LineTo(new Point(p2.X, p2.Y));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(fill, edge, geo);
        }
    }
}
