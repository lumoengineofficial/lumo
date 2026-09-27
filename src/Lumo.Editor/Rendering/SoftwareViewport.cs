using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lumo.Engine.Assets;
using Lumo.Engine.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using Mesh = Lumo.Engine.Rendering.Abstractions.Mesh;

namespace Lumo.Editor.Rendering;

public enum ViewportMode
{
    Scene,
    Game,
    Mode2D
}

public class SoftwareViewport : Control
{
    private float _camDist = 6f;
    private float _camYaw = 45f;
    private float _camPitch = 25f;
    private Vector3 _camTarget = Vector3.Zero;
    private bool _orbiting, _panning;
    private Point _lastMouse;
    private Scene? _scene;
    private readonly List<(Vector3 pos, Vector4 color)> _markers = [];

    public string ModeLabel { get; set; } = "Software (CPU)";

    public ViewportMode Mode { get; set; } = ViewportMode.Scene;

    public bool ShowGrid { get; set; } = true;

    public bool ShowGizmos { get; set; } = true;

    /// <summary>Raised on viewport click: picked entity, or null on empty space.</summary>
    public event Action<Entity?>? EntityPicked;

    /// <summary>Raised while a picked entity is being dragged.</summary>
    public event Action<Entity>? EntityMoved;
    public event Action? TransformDragStarted;

    /// <summary>When this returns false, clicks still select but do not move entities.</summary>
    public Func<bool>? CanEditTransform;

    private Entity? _selected;
    public Entity? SelectedEntity
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    private Entity? _dragEntity;
    private Vector3 _dragOffset;

    public SoftwareViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += OnPress;
        PointerReleased += OnRelease;
        PointerMoved += OnMove;
        PointerWheelChanged += OnWheel;
    }

    public void SetScene(Scene scene) => _scene = scene;

    private void OnPress(object? s, PointerPressedEventArgs e)
    {
        _lastMouse = e.GetPosition(this);
        Focus();
        var p = e.GetCurrentPoint(this).Properties;
        if (p.IsLeftButtonPressed)
        {
            Entity? hit = Mode == ViewportMode.Game ? null : Pick(_lastMouse);
            if (hit != null)
            {
                EntityPicked?.Invoke(hit);
                if (CanEditTransform?.Invoke() != false)
                {
                    var world = ScreenToWorld(_lastMouse, hit.Transform.Position.Z);
                    if (world is Vector3 w)
                    {
                        _dragEntity = hit;
                        _dragOffset = hit.Transform.Position - w;
                        TransformDragStarted?.Invoke();
                    }
                }
            }
            else
            {
                if (Mode != ViewportMode.Game) EntityPicked?.Invoke(null);
                _orbiting = true;
            }
        }
        else if (p.IsMiddleButtonPressed || p.IsRightButtonPressed) _panning = true;
        e.Handled = true;
    }

    private void OnRelease(object? s, PointerReleasedEventArgs e)
    {
        _dragEntity = null;
        _orbiting = false;
        _panning = false;
        e.Handled = true;
    }

    private void OnMove(object? s, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastMouse.X);
        float dy = (float)(pos.Y - _lastMouse.Y);
        _lastMouse = pos;

        if (_dragEntity != null)
        {
            var world = ScreenToWorld(pos, _dragEntity.Transform.Position.Z);
            if (world is Vector3 w)
            {
                var np = w + _dragOffset;
                _dragEntity.Transform.Position = new Vector3(np.X, np.Y, _dragEntity.Transform.Position.Z);
                InvalidateVisual();
                EntityMoved?.Invoke(_dragEntity);
            }
            return;
        }

        if (Mode == ViewportMode.Mode2D)
        {
            if (_orbiting || _panning)
            {
                // 2D: drag pans the plane.
                float spd = _camDist * 0.002f;
                _camTarget -= new Vector3(dx * spd, -dy * spd, 0);
                InvalidateVisual();
            }
            return;
        }

        if (_orbiting)
        {
            _camYaw -= dx * 0.4f;
            _camPitch += dy * 0.4f;
            _camPitch = Math.Clamp(_camPitch, -89f, 89f);
            InvalidateVisual();
        }
        else if (_panning)
        {
            float spd = _camDist * 0.002f;
            float yawR = _camYaw * MathF.PI / 180f;
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, new Vector3(MathF.Sin(yawR), 0, MathF.Cos(yawR))));
            _camTarget += right * dx * spd - Vector3.UnitY * dy * spd;
            InvalidateVisual();
        }
    }

    private void OnWheel(object? s, PointerWheelEventArgs e)
    {
        _camDist -= (float)e.Delta.Y * 1.5f;
        _camDist = Math.Clamp(_camDist, 0.5f, 100f);
        InvalidateVisual();
        e.Handled = true;
    }

    // --------------------------------------------------------- picking

    /// <summary>Screen point → world point on the plane Z = planeZ (null in Game view).</summary>
    private Vector3? ScreenToWorld(Point p, float planeZ)
    {
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);
        float ndcX = (float)(p.X / w) * 2f - 1f;
        float ndcY = 1f - (float)(p.Y / h) * 2f;

        if (Mode == ViewportMode.Mode2D)
        {
            float halfH = _camDist * 0.5f;
            float halfW = halfH * ((float)w / h);
            return _camTarget + new Vector3(ndcX * halfW, ndcY * halfH, 0);
        }

        if (Mode != ViewportMode.Scene) return null;

        var (pos, fwd, right, up) = GetSceneCamera();
        float aspect = (float)w / h;
        float tanHalf = MathF.Tan(60f * MathF.PI / 180f / 2f);
        var dir = Vector3.Normalize(fwd + right * (ndcX * tanHalf * aspect) + up * (ndcY * tanHalf));
        if (MathF.Abs(dir.Z) < 1e-6f) return null;
        float t = (planeZ - pos.Z) / dir.Z;
        if (t <= 0f) return null;
        return pos + dir * t;
    }

    private (Vector3 pos, Vector3 fwd, Vector3 right, Vector3 up) GetSceneCamera()
    {
        float yawR = _camYaw * MathF.PI / 180f;
        float pitchR = _camPitch * MathF.PI / 180f;
        var pos = _camTarget + new Vector3(
            _camDist * MathF.Cos(pitchR) * MathF.Sin(yawR),
            _camDist * MathF.Sin(pitchR),
            _camDist * MathF.Cos(pitchR) * MathF.Cos(yawR));
        var fwd = Vector3.Normalize(_camTarget - pos);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, fwd));
        return (pos, fwd, right, up);
    }

    /// <summary>Entity under the cursor (sprites by quad, others by screen radius).</summary>
    private Entity? Pick(Point p)
    {
        if (_scene == null || Mode == ViewportMode.Game) return null;
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);
        var (view, proj) = GetMatrices(w, h);
        var sp = new Vector2((float)p.X, (float)p.Y);

        Entity? best = null;
        float bestDepth = float.MinValue;
        int bestIndex = -1;
        int index = 0;

        foreach (var entity in _scene.AllEntities)
        {
            index++;
            if (entity.Transform == null || !entity.IsActive) continue;
            bool hit = false;

            if (entity.MeshRenderer != null)
            {
                if (entity.MeshRenderer.IsVisible && InPickRadius(entity, sp, view, proj, w, h)) hit = true;
            }
            else if (entity.SpriteRenderer is { IsVisible: true } spr)
            {
                var c = entity.Transform.Position;
                float hw = spr.Width * 0.5f, hh = spr.Height * 0.5f;
                var a = Project(c + new Vector3(-hw, -hh, 0), view, proj, w, h);
                var b = Project(c + new Vector3(hw, -hh, 0), view, proj, w, h);
                var d = Project(c + new Vector3(hw, hh, 0), view, proj, w, h);
                var e2 = Project(c + new Vector3(-hw, hh, 0), view, proj, w, h);
                if (a.X > -9000 && PointInQuad(sp, a, b, d, e2)) hit = true;
            }
            else if (entity.Light != null || entity.Camera != null)
            {
                if (InPickRadius(entity, sp, view, proj, w, h)) hit = true;
            }
            else
            {
                // bare actor: editor dot
                var c = Project(entity.Transform.Position, view, proj, w, h);
                if (c.X > -9000 && Vector2.Distance(sp, c) <= 9f) hit = true;
            }

            if (!hit) continue;
            float depth = Vector4.Transform(new Vector4(entity.Transform.Position, 1), view).Z;
            if (depth > bestDepth || (MathF.Abs(depth - bestDepth) < 1e-4f && index > bestIndex))
            {
                best = entity;
                bestDepth = depth;
                bestIndex = index;
            }
        }
        return best;
    }

    private static bool InPickRadius(Entity entity, Vector2 sp, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var c = Project(entity.Transform.Position, view, proj, w, h);
        if (c.X < -9000) return false;
        return Vector2.Distance(sp, c) <= PickRadius(entity, view, proj, w, h);
    }

    private static float PickRadius(Entity entity, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var pos = entity.Transform.Position;
        float s = MathF.Max(0.1f, MathF.Max(entity.Transform.Scale.X, MathF.Max(entity.Transform.Scale.Y, entity.Transform.Scale.Z)));
        var c = Project(pos, view, proj, w, h);
        float r = 13f;
        var ex = Project(pos + new Vector3(s * 0.5f, 0, 0), view, proj, w, h);
        var ey = Project(pos + new Vector3(0, s * 0.5f, 0), view, proj, w, h);
        if (ex.X > -9000) r = MathF.Max(r, Vector2.Distance(c, ex) * 1.7f);
        if (ey.X > -9000) r = MathF.Max(r, Vector2.Distance(c, ey) * 1.7f);
        return MathF.Min(r, 90f);
    }

    private static bool PointInQuad(Vector2 p, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        bool? positive = null;
        Span<Vector2> vs = [v0, v1, v2, v3];
        for (int i = 0; i < 4; i++)
        {
            var a = vs[i];
            var b = vs[(i + 1) % 4];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (MathF.Abs(cross) < 1e-3f) continue;
            bool pos = cross > 0;
            if (positive is null) positive = pos;
            else if (positive != pos) return false;
        }
        return positive is not false;
    }

    private (Matrix4x4 view, Matrix4x4 proj) GetMatrices(int w, int h)
    {
        float aspect = (float)w / Math.Max(1, h);

        if (Mode == ViewportMode.Mode2D)
        {
            // Orthographic top-down onto the XY plane (looking along -Z).
            var eye = _camTarget + new Vector3(0, 0, _camDist);
            var view = Matrix4x4.CreateLookAt(eye, _camTarget, Vector3.UnitY);
            float halfH = _camDist * 0.5f;
            var proj = Matrix4x4.CreateOrthographic(halfH * 2f * aspect, halfH * 2f, 0.01f, 500f);
            return (view, proj);
        }

        if (Mode == ViewportMode.Game && _scene != null)
        {
            var camEntity = FindGameCamera();
            if (camEntity != null)
            {
                var pos = camEntity.Transform.Position;
                var fwd = camEntity.Transform.Forward;
                var view = Matrix4x4.CreateLookAt(pos, pos + fwd, Vector3.UnitY);
                float fov = camEntity.Camera?.FieldOfView ?? 60f;
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov * MathF.PI / 180f, aspect, 0.05f, 1000f);
                return (view, proj);
            }
        }

        float yawR = _camYaw * MathF.PI / 180f;
        float pitchR = _camPitch * MathF.PI / 180f;
        var scenePos = _camTarget + new Vector3(
            _camDist * MathF.Cos(pitchR) * MathF.Sin(yawR),
            _camDist * MathF.Sin(pitchR),
            _camDist * MathF.Cos(pitchR) * MathF.Cos(yawR));
        var sceneView = Matrix4x4.CreateLookAt(scenePos, _camTarget, Vector3.UnitY);
        var sceneProj = Matrix4x4.CreatePerspectiveFieldOfView(60f * MathF.PI / 180f, aspect, 0.1f, 200f);
        return (sceneView, sceneProj);
    }

    private Entity? FindGameCamera()
    {
        if (_scene == null) return null;
        Entity? first = null;
        foreach (var e in _scene.AllEntities)
        {
            if (e.Camera == null) continue;
            if (e.Camera.IsPrimary) return e;
            first ??= e;
        }
        return first;
    }

    private static Vector2 Project(Vector3 world, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), view * proj);
        if (clip.W <= 0.001f) return new Vector2(-9999, -9999);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2((ndcX + 1) * 0.5f * w, (1 - ndcY) * 0.5f * h);
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds.Size;
        int w = Math.Max(1, (int)bounds.Width);
        int h = Math.Max(1, (int)bounds.Height);

        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1a1a22")), new Rect(0, 0, w, h));

        var (view, proj) = GetMatrices(w, h);

        if (Mode == ViewportMode.Game)
        {
            // Game view: clear to sky color, no editor grid/axes.
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#101425")), new Rect(0, 0, w, h));
        }
        else
        {
            DrawSkyGradient(ctx, w, h);
            if (ShowGrid) DrawGrid(ctx, view, proj, w, h);
            DrawAxes(ctx, view, proj, w, h);
        }

        DrawSceneObjects(ctx, view, proj, w, h);
        DrawSelection(ctx, view, proj, w, h);

        string label = Mode switch
        {
            ViewportMode.Game => FindGameCamera() != null ? "Game (Camera)" : "Game (no camera — free view)",
            ViewportMode.Mode2D => "2D Orthographic",
            _ => ModeLabel
        };
        var modeText = new FormattedText(
            label,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            new SolidColorBrush(Color.Parse("#666677")));
        ctx.DrawText(modeText, new Point(8, h - 20));
    }

    private void DrawSkyGradient(DrawingContext ctx, int w, int h)
    {
        var n = 20;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            byte r = (byte)(20 + t * 8);
            byte g = (byte)(22 + t * 10);
            byte b = (byte)(35 + t * 18);
            var rect = new Rect(0, i * h / (double)n, w, h / (double)n + 1);
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), rect);
        }
    }

    private void DrawGrid(DrawingContext ctx, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var minorPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 60)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromRgb(65, 65, 85)), 1);
        float range = 25f;

        if (Mode == ViewportMode.Mode2D)
        {
            // Grid on the XY plane (Z = 0) for 2D editing.
            for (float i = -range; i <= range; i += 1f)
            {
                bool major = Math.Abs(i % 5f) < 0.01f;
                var pen = major ? majorPen : minorPen;

                var a1 = Project(new Vector3(i, -range, 0), view, proj, w, h);
                var b1 = Project(new Vector3(i, range, 0), view, proj, w, h);
                ctx.DrawLine(pen, new Point(a1.X, a1.Y), new Point(b1.X, b1.Y));

                var a2 = Project(new Vector3(-range, i, 0), view, proj, w, h);
                var b2 = Project(new Vector3(range, i, 0), view, proj, w, h);
                ctx.DrawLine(pen, new Point(a2.X, a2.Y), new Point(b2.X, b2.Y));
            }
            return;
        }

        for (float i = -range; i <= range; i += 1f)
        {
            bool major = Math.Abs(i % 5f) < 0.01f;
            var pen = major ? majorPen : minorPen;

            var a1 = Project(new Vector3(i, 0, -range), view, proj, w, h);
            var b1 = Project(new Vector3(i, 0, range), view, proj, w, h);
            if (a1.X > -9000 && b1.X > -9000)
                ctx.DrawLine(pen, new Point(a1.X, a1.Y), new Point(b1.X, b1.Y));

            var a2 = Project(new Vector3(-range, 0, i), view, proj, w, h);
            var b2 = Project(new Vector3(range, 0, i), view, proj, w, h);
            if (a2.X > -9000 && b2.X > -9000)
                ctx.DrawLine(pen, new Point(a2.X, a2.Y), new Point(b2.X, b2.Y));
        }
    }

    private void DrawAxes(DrawingContext ctx, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var origin = Project(Vector3.Zero, view, proj, w, h);
        if (origin.X < -9000) return;

        float len = 3f;
        var xEnd = Project(new Vector3(len, 0, 0), view, proj, w, h);
        var yEnd = Project(new Vector3(0, len, 0), view, proj, w, h);
        var zEnd = Project(new Vector3(0, 0, len), view, proj, w, h);

        if (xEnd.X > -9000)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(200, 70, 70)), 2), new Point(origin.X, origin.Y), new Point(xEnd.X, xEnd.Y));
        if (yEnd.X > -9000)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 180, 70)), 2), new Point(origin.X, origin.Y), new Point(yEnd.X, yEnd.Y));
        if (zEnd.X > -9000)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 100, 210)), 2), new Point(origin.X, origin.Y), new Point(zEnd.X, zEnd.Y));
    }

    private void DrawSceneObjects(DrawingContext ctx, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        if (_scene == null) return;

        var fillPen = new Pen(new SolidColorBrush(Color.FromRgb(140, 160, 200)), 1.2);
        var edgePen = new Pen(new SolidColorBrush(Color.FromRgb(180, 200, 230)), 1.4);
        var lightPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 220, 80)), 1.5);
        var camPen = new Pen(new SolidColorBrush(Color.FromRgb(80, 200, 200)), 1.5);

        foreach (var entity in _scene.AllEntities)
        {
            if (entity.Transform == null) continue;
            if (!entity.IsActive) continue;
            var pos = entity.Transform.Position;
            var scale = entity.Transform.Scale;
            float s = MathF.Max(0.1f, (scale.X + scale.Y + scale.Z) / 3f);

            if (entity.MeshRenderer != null)
            {
                if (!entity.MeshRenderer.IsVisible) continue;
                var mesh = MeshLibrary.Get(entity.MeshRenderer.MeshName);
                if (mesh != null && mesh.Vertices.Length >= 9 && mesh.Indices.Length >= 3)
                    DrawMesh(ctx, mesh, entity, view, proj, w, h, fillPen, edgePen);
                else
                    DrawCube(ctx, pos, s, view, proj, w, h, fillPen, edgePen);
            }
            else if (entity.SpriteRenderer != null && entity.SpriteRenderer.IsVisible)
            {
                DrawSprite(ctx, entity, view, proj, w, h);
            }
            else if (entity.Light != null)
            {
                if (ShowGizmos) DrawDiamond(ctx, pos, s * 0.4f, view, proj, w, h, lightPen);
            }
            else if (entity.Camera != null)
            {
                if (ShowGizmos) DrawCameraIcon(ctx, pos, view, proj, w, h, camPen);
            }
            else
            {
                var p = Project(pos, view, proj, w, h);
                if (p.X > -9000)
                    ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(120, 140, 170)), null, new Point(p.X, p.Y), 3, 3);
            }
        }
    }

    private void DrawMesh(DrawingContext ctx, Mesh mesh, Entity entity, Matrix4x4 view, Matrix4x4 proj, int w, int h, Pen fill, Pen edge)
    {
        var m = entity.Transform.LocalToWorldMatrix;
        var verts = mesh.Vertices;
        var idx = mesh.Indices;

        // Transform vertices once.
        var world = new Vector3[verts.Length / 3];
        for (int i = 0; i < world.Length; i++)
        {
            var v = new Vector3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);
            world[i] = Vector3.Transform(v, m);
        }

        var tris = new List<(float depth, int a, int b, int c)>(idx.Length / 3);
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var a = world[idx[i]];
            var b = world[idx[i + 1]];
            var c = world[idx[i + 2]];
            var mid = (a + b + c) / 3f;
            var viewPos = Vector4.Transform(new Vector4(mid, 1), view);
            if (viewPos.W <= 0.001f) continue;
            tris.Add((viewPos.Z, (int)idx[i], (int)idx[i + 1], (int)idx[i + 2]));
        }
        tris.Sort((x, y) => y.depth.CompareTo(x.depth));

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
            ctx.DrawGeometry(fill.Brush, edge, geo);
        }
    }

    private void DrawSprite(DrawingContext ctx, Entity entity, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        var sp = entity.SpriteRenderer!;
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
        var stroke = Color.FromRgb(
            (byte)Math.Min(255, (int)(fill.R + 70)),
            (byte)Math.Min(255, (int)(fill.G + 70)),
            (byte)Math.Min(255, (int)(fill.B + 70)));
        ctx.DrawGeometry(
            new SolidColorBrush(fill),
            new Pen(new SolidColorBrush(stroke), 1.4),
            geo);
    }

    private void DrawSelection(DrawingContext ctx, Matrix4x4 view, Matrix4x4 proj, int w, int h)
    {
        if (_selected is not { } ent || ent.Transform == null || !ent.IsActive) return;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#9fe8ff")), 2);
        var handle = new SolidColorBrush(Color.Parse("#e8fbff"));

        if (ent.SpriteRenderer is { IsVisible: true } spr)
        {
            var c = ent.Transform.Position;
            float hw = spr.Width * 0.5f, hh = spr.Height * 0.5f;
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
            ctx.DrawGeometry(null, pen, geo);
            foreach (var corner in new[] { a, b, d, e2 })
                ctx.DrawRectangle(handle, null, new Rect(corner.X - 3, corner.Y - 3, 6, 6));
        }
        else
        {
            var c = Project(ent.Transform.Position, view, proj, w, h);
            if (c.X < -9000) return;
            float r = PickRadius(ent, view, proj, w, h) + 4;
            ctx.DrawEllipse(null, pen, new Point(c.X, c.Y), r, r);
            ctx.DrawRectangle(handle, null, new Rect(c.X - 3, c.Y - 3, 6, 6));
        }
    }

    private void DrawCube(DrawingContext ctx, Vector3 center, float size, Matrix4x4 view, Matrix4x4 proj, int w, int h, Pen fill, Pen edge)
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

        // Draw faces sorted by depth (back to front)
        var faceList = new List<(float depth, int[] face)>();
        foreach (var f in faces)
        {
            Vector3 avg = (verts[f[0]] + verts[f[1]] + verts[f[2]] + verts[f[3]]) / 4f;
            var viewPos = Vector4.Transform(new Vector4(avg, 1), view);
            faceList.Add((viewPos.Z, f));
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
            ctx.DrawGeometry(fill.Brush, edge, geo);
        }
    }

    private void DrawDiamond(DrawingContext ctx, Vector3 center, float size, Matrix4x4 view, Matrix4x4 proj, int w, int h, Pen pen)
    {
        var top = Project(center + new Vector3(0, size, 0), view, proj, w, h);
        var bot = Project(center + new Vector3(0, -size, 0), view, proj, w, h);
        var l = Project(center + new Vector3(-size, 0, 0), view, proj, w, h);
        var r = Project(center + new Vector3(size, 0, 0), view, proj, w, h);
        var f = Project(center + new Vector3(0, 0, size), view, proj, w, h);
        var b = Project(center + new Vector3(0, 0, -size), view, proj, w, h);

        if (top.X < -9000) return;
        DrawLine(ctx, pen, top, l); DrawLine(ctx, pen, top, r);
        DrawLine(ctx, pen, top, f); DrawLine(ctx, pen, top, b);
        DrawLine(ctx, pen, bot, l); DrawLine(ctx, pen, bot, r);
        DrawLine(ctx, pen, bot, f); DrawLine(ctx, pen, bot, b);
        DrawLine(ctx, pen, l, f); DrawLine(ctx, pen, f, r);
        DrawLine(ctx, pen, r, b); DrawLine(ctx, pen, b, l);
    }

    private void DrawCameraIcon(DrawingContext ctx, Vector3 center, Matrix4x4 view, Matrix4x4 proj, int w, int h, Pen pen)
    {
        float s = 0.35f;
        var c = Project(center, view, proj, w, h);
        if (c.X < -9000) return;
        var pts = new[]
        {
            Project(center + new Vector3(-s, -s * 0.6f, 0), view, proj, w, h),
            Project(center + new Vector3(s, -s * 0.6f, 0), view, proj, w, h),
            Project(center + new Vector3(s, s * 0.6f, 0), view, proj, w, h),
            Project(center + new Vector3(-s, s * 0.6f, 0), view, proj, w, h),
        };
        if (pts[0].X < -9000) return;
        for (int i = 0; i < 4; i++)
            DrawLine(ctx, pen, pts[i], pts[(i + 1) % 4]);
        var lens = Project(center + new Vector3(s * 1.4f, 0, 0), view, proj, w, h);
        DrawLine(ctx, pen, pts[1], lens);
        DrawLine(ctx, pen, pts[2], lens);
    }

    private static void DrawLine(DrawingContext ctx, Pen pen, Vector2 a, Vector2 b)
    {
        if (a.X < -9000 || b.X < -9000) return;
        ctx.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
    }
}
