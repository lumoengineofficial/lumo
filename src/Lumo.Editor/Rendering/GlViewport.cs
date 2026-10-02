using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumo.Engine.Core;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using System;

namespace Lumo.Editor.Rendering;

public class GlViewport : Control
{
    private IWindow? _window;
    private GL? _gl;
    private uint _shaderProgram;
    private uint _gridShaderProgram;
    private uint _cubeVao, _cubeVbo, _cubeEbo;
    private uint _gridVao, _gridVbo;
    private bool _initialized;
    private WriteableBitmap? _bitmap;
    private byte[]? _pixelBuffer;
    private float _cameraDistance = 5f;
    private float _cameraYaw = 45f;
    private float _cameraPitch = 30f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private bool _orbiting;
    private bool _panning;
    private Point _lastMouse;

    private int _modelLoc, _viewLoc, _projLoc, _colorLoc;
    private int _gridViewLoc, _gridProjLoc, _gridColorLoc, _gridCamPosLoc;
    private readonly System.Collections.Generic.List<(Matrix4x4 transform, Vector4 color)> _objects = [];
    private int _gridLineCount;

    public bool IsReady => _initialized && _gl != null && _window != null;

    public GlViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerMoved += OnPointerMoved;
        PointerWheelChanged += OnWheelChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) { }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeGl();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _initialized = false;
        _window?.Dispose();
        _window = null;
    }

    private void InitializeGl()
    {
        if (_initialized) return;
        var bounds = Bounds;
        int w = Math.Max(1, (int)bounds.Width);
        int h = Math.Max(1, (int)bounds.Height);

try
        {
            var opts = WindowOptions.Default with
            {
                Size = new Vector2D<int>(w, h),
                API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
                IsVisible = false,
            };

            _window = Silk.NET.Windowing.Window.Create(opts);
            _window.Initialize();
            OnWindowLoad();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GL init failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OnWindowLoad()
    {
        _gl = GL.GetApi(_window);
        CompileShaders();
        SetupCube();
        SetupGrid();
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.03f, 0.03f, 0.06f, 1.0f);
        _initialized = true;
    }

    public void RenderFrame()
    {
        if (!_initialized || _gl == null || _window == null) return;

        try
        {
            _window.MakeCurrent();
            _window.DoEvents();

            var bounds = Bounds;
            int w = Math.Max(1, (int)bounds.Width);
            int h = Math.Max(1, (int)bounds.Height);

            if (w != _window.Size.X || h != _window.Size.Y)
            {
                _window.Size = new Vector2D<int>(w, h);
            }

            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            float aspect = (float)w / Math.Max(1, h);
            var view = GetViewMatrix();
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), aspect, 0.1f, 200f);

            DrawGrid(view, proj);
            DrawObjects(view, proj);

            _window.SwapBuffers();
            ReadPixelsToBitmap(w, h);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
        }
    }

    private Matrix4x4 GetViewMatrix()
    {
        float yawRad = MathHelper.DegreesToRadians(_cameraYaw);
        float pitchRad = MathHelper.DegreesToRadians(_cameraPitch);
        Vector3 pos = _cameraTarget + new Vector3(
            _cameraDistance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            _cameraDistance * MathF.Sin(pitchRad),
            _cameraDistance * MathF.Cos(pitchRad) * MathF.Cos(yawRad));
        return Matrix4x4.CreateLookAt(pos, _cameraTarget, Vector3.UnitY);
    }

    private Vector3 GetCameraPosition()
    {
        float yawRad = MathHelper.DegreesToRadians(_cameraYaw);
        float pitchRad = MathHelper.DegreesToRadians(_cameraPitch);
        return _cameraTarget + new Vector3(
            _cameraDistance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            _cameraDistance * MathF.Sin(pitchRad),
            _cameraDistance * MathF.Cos(pitchRad) * MathF.Cos(yawRad));
    }

    private unsafe void DrawGrid(Matrix4x4 view, Matrix4x4 proj)
    {
        if (_gl == null || _gridShaderProgram == 0) return;
        _gl.UseProgram(_gridShaderProgram);
        _gl.BindVertexArray(_gridVao);
        _gl.UniformMatrix4(_gridViewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
        _gl.UniformMatrix4(_gridProjLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));
        var camPos = GetCameraPosition();
        _gl.Uniform4(_gridColorLoc, 0.25f, 0.25f, 0.35f, 0.5f);
        _gl.Uniform3(_gridCamPosLoc, camPos.X, camPos.Y, camPos.Z);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridLineCount);
        _gl.BindVertexArray(0);
    }

    private unsafe void DrawObjects(Matrix4x4 view, Matrix4x4 proj)
    {
        if (_gl == null) return;
        _gl.UseProgram(_shaderProgram);
        var camPos = GetCameraPosition();
        int lightDirLoc = _gl.GetUniformLocation(_shaderProgram, "lightDir");
        int viewPosLoc = _gl.GetUniformLocation(_shaderProgram, "viewPos");
        _gl.Uniform3(lightDirLoc, -0.5f, -1.0f, -0.3f);
        _gl.Uniform3(viewPosLoc, camPos.X, camPos.Y, camPos.Z);
        _gl.BindVertexArray(_cubeVao);

        for (int i = 0; i < _objects.Count; i++)
        {
            var transform = _objects[i].transform;
            var color = _objects[i].color;
            _gl.UniformMatrix4(_modelLoc, 1, false, (float*)Unsafe.AsPointer(ref transform));
            _gl.UniformMatrix4(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            _gl.UniformMatrix4(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));
            _gl.Uniform4(_colorLoc, color.X, color.Y, color.Z, color.W);
            _gl.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, (void*)0);
        }
        _gl.BindVertexArray(0);
    }

    private unsafe void ReadPixelsToBitmap(int w, int h)
    {
        if (_gl == null) return;
        int stride = w * 4;
        int bufferSize = stride * h;
        if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize)
            _pixelBuffer = new byte[bufferSize];

        fixed (byte* ptr = _pixelBuffer)
            _gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);

        for (int y = 0; y < h / 2; y++)
        {
            int top = y * stride;
            int bot = (h - 1 - y) * stride;
            for (int x = 0; x < stride; x++)
                (_pixelBuffer[top + x], _pixelBuffer[bot + x]) = (_pixelBuffer[bot + x], _pixelBuffer[top + x]);
        }

        for (int i = 0; i < _pixelBuffer.Length; i += 4)
            (_pixelBuffer[i], _pixelBuffer[i + 2]) = (_pixelBuffer[i + 2], _pixelBuffer[i]);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (w <= 0 || h <= 0) return;
                var wb = new WriteableBitmap(
                    new PixelSize(w, h),
                    new Avalonia.Vector(96, 96));
                using (var fb = wb.Lock())
                {
                    for (int row = 0; row < h; row++)
                    {
                        var srcSpan = new Span<byte>((void*)(fb.Address + row * fb.RowBytes), stride);
                        var dstSpan = _pixelBuffer.AsSpan(row * stride, stride);
                        dstSpan.CopyTo(srcSpan);
                    }
                }
                _bitmap?.Dispose();
                _bitmap = wb;
                InvalidateVisual();
            }
            catch { }
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap != null)
            context.DrawImage(_bitmap, new Rect(Bounds.Size));
    }

    private uint CompilePair(string v, string f)
    {
        uint vs = Compile(ShaderType.VertexShader, v);
        uint fs = Compile(ShaderType.FragmentShader, f);
        uint prog = _gl!.CreateProgram();
        _gl.AttachShader(prog, vs);
        _gl.AttachShader(prog, fs);
        _gl.LinkProgram(prog);
        _gl.GetProgram(prog, GLEnum.LinkStatus, out int ok);
        if (ok == 0) throw new Exception("Link: " + _gl.GetProgramInfoLog(prog));
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return prog;
    }

    private uint Compile(ShaderType type, string src)
    {
        uint s = _gl!.CreateShader(type);
        _gl.ShaderSource(s, src);
        _gl.CompileShader(s);
        _gl.GetShader(s, GLEnum.CompileStatus, out int ok);
        if (ok == 0) throw new Exception("Compile: " + _gl.GetShaderInfoLog(s));
        return s;
    }

    private void CompileShaders()
    {
        _shaderProgram = CompilePair(@"
#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
uniform mat4 model, view, projection;
out vec3 FragPos, Normal;
void main() {
    FragPos = vec3(model * vec4(aPos, 1.0));
    Normal = mat3(transpose(inverse(model))) * aNormal;
    gl_Position = projection * view * vec4(FragPos, 1.0);
}", @"
#version 330 core
out vec4 FragColor;
in vec3 FragPos, Normal;
uniform vec3 lightDir, viewPos;
uniform vec4 objectColor;
void main() {
    vec3 ambient = 0.15 * vec3(1.0);
    vec3 norm = normalize(Normal);
    float diff = max(dot(norm, normalize(lightDir)), 0.0);
    vec3 diffuse = diff * vec3(1.0);
    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-normalize(lightDir), norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0);
    vec3 specular = 0.4 * spec * vec3(1.0);
    FragColor = vec4((ambient + diffuse + specular) * objectColor.rgb, objectColor.a);
}");

        _modelLoc = _gl!.GetUniformLocation(_shaderProgram, "model");
        _viewLoc = _gl.GetUniformLocation(_shaderProgram, "view");
        _projLoc = _gl.GetUniformLocation(_shaderProgram, "projection");
        _colorLoc = _gl.GetUniformLocation(_shaderProgram, "objectColor");

        _gridShaderProgram = CompilePair(@"
#version 330 core
layout (location = 0) in vec3 aPos;
uniform mat4 view, projection;
out vec3 WorldPos;
void main() {
    WorldPos = aPos;
    gl_Position = projection * view * vec4(aPos, 1.0);
}", @"
#version 330 core
out vec4 FragColor;
in vec3 WorldPos;
uniform vec4 gridColor;
uniform vec3 camPos;
void main() {
    float dist = length(WorldPos.xz - camPos.xz);
    float fade = 1.0 - smoothstep(20.0, 60.0, dist);
    FragColor = vec4(gridColor.rgb, gridColor.a * fade);
}");

        _gridViewLoc = _gl!.GetUniformLocation(_gridShaderProgram, "view");
        _gridProjLoc = _gl.GetUniformLocation(_gridShaderProgram, "projection");
        _gridColorLoc = _gl.GetUniformLocation(_gridShaderProgram, "gridColor");
        _gridCamPosLoc = _gl.GetUniformLocation(_gridShaderProgram, "camPos");
    }

    private unsafe void SetupCube()
    {
        float[] verts = [
            -0.5f,-0.5f, 0.5f,  0, 0, 1,   0.5f,-0.5f, 0.5f,  0, 0, 1,   0.5f, 0.5f, 0.5f,  0, 0, 1,  -0.5f, 0.5f, 0.5f,  0, 0, 1,
            -0.5f,-0.5f,-0.5f,  0, 0,-1,   0.5f,-0.5f,-0.5f,  0, 0,-1,   0.5f, 0.5f,-0.5f,  0, 0,-1,  -0.5f, 0.5f,-0.5f,  0, 0,-1,
            -0.5f, 0.5f,-0.5f,  0, 1, 0,   0.5f, 0.5f,-0.5f,  0, 1, 0,   0.5f, 0.5f, 0.5f,  0, 1, 0,  -0.5f, 0.5f, 0.5f,  0, 1, 0,
            -0.5f,-0.5f,-0.5f,  0,-1, 0,   0.5f,-0.5f,-0.5f,  0,-1, 0,   0.5f,-0.5f, 0.5f,  0,-1, 0,  -0.5f,-0.5f, 0.5f,  0,-1, 0,
             0.5f,-0.5f,-0.5f,  1, 0, 0,   0.5f,-0.5f, 0.5f,  1, 0, 0,   0.5f, 0.5f, 0.5f,  1, 0, 0,   0.5f, 0.5f,-0.5f,  1, 0, 0,
            -0.5f,-0.5f,-0.5f, -1, 0, 0,  -0.5f,-0.5f, 0.5f, -1, 0, 0,  -0.5f, 0.5f, 0.5f, -1, 0, 0,  -0.5f, 0.5f,-0.5f, -1, 0, 0,
        ];
        uint[] idx = [0,1,2,2,3,0,4,5,6,6,7,4,8,9,10,10,11,8,12,13,14,14,15,12,16,17,18,18,19,16,20,21,22,22,23,20];

        _cubeVao = _gl!.GenVertexArray();
        _gl.BindVertexArray(_cubeVao);
        _cubeVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _cubeVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);
        _cubeEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _cubeEbo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, idx, BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.BindVertexArray(0);
    }

    private unsafe void SetupGrid()
    {
        var verts = new System.Collections.Generic.List<float>();
        for (float i = -50f; i <= 50f; i += 1f)
        {
            verts.AddRange([i, 0, -50f, i, 0, 50f]);
            verts.AddRange([-50f, 0, i, 50f, 0, i]);
        }
        _gridLineCount = verts.Count / 3;

        _gridVao = _gl!.GenVertexArray();
        _gl.BindVertexArray(_gridVao);
        _gridVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, verts.ToArray(), BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.BindVertexArray(0);
    }

    public void SetSceneObjects(Lumo.Engine.Scene.Scene scene)
    {
        _objects.Clear();
        foreach (var entity in scene.AllEntities)
        {
            if (entity.Transform == null) continue;
            if (entity.MeshRenderer != null)
                _objects.Add((entity.Transform.LocalToWorldMatrix, new Vector4(0.6f, 0.7f, 0.9f, 1.0f)));
            if (entity.Light != null)
                _objects.Add((entity.Transform.LocalToWorldMatrix, new Vector4(1f, 1f, 0.3f, 1.0f)));
        }
    }

    public void FocusOnEntity(Lumo.Engine.Scene.Entity? entity)
    {
        if (entity?.Transform != null)
        {
            _cameraTarget = entity.Transform.Position;
            float bounds = Math.Max(0.5f, MathF.Max(entity.Transform.Scale.X, MathF.Max(entity.Transform.Scale.Y, entity.Transform.Scale.Z)));
            _cameraDistance = Math.Max(2f, bounds * 3.5f);
        }
    }

    public void ResetCamera()
    {
        _cameraTarget = Vector3.Zero;
        _cameraDistance = 5f;
        _cameraYaw = 45f;
        _cameraPitch = 30f;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        _lastMouse = pos;
        Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _orbiting = true;
        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed || e.GetCurrentPoint(this).Properties.IsRightButtonPressed) _panning = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _orbiting = false;
        _panning = false;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastMouse.X);
        float dy = (float)(pos.Y - _lastMouse.Y);
        _lastMouse = pos;

        if (_orbiting)
        {
            _cameraYaw -= dx * 0.4f;
            _cameraPitch += dy * 0.4f;
            _cameraPitch = Math.Clamp(_cameraPitch, -89f, 89f);
        }
        else if (_panning)
        {
            float panSpeed = _cameraDistance * 0.0015f;
            float yawRad = MathHelper.DegreesToRadians(_cameraYaw);
            float pitchRad = MathHelper.DegreesToRadians(_cameraPitch);
            Vector3 camPos = _cameraTarget + new Vector3(
                _cameraDistance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                _cameraDistance * MathF.Sin(pitchRad),
                _cameraDistance * MathF.Cos(pitchRad) * MathF.Cos(yawRad));
            Vector3 fwd = Vector3.Normalize(_cameraTarget - camPos);
            Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, fwd));
            _cameraTarget -= right * dx * panSpeed - up * dy * panSpeed;
        }
    }

    private void OnWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _cameraDistance -= (float)e.Delta.Y * 1.5f;
        _cameraDistance = Math.Clamp(_cameraDistance, 1f, 100f);
        e.Handled = true;
    }
}
