using System.Numerics;

namespace Lumo.Engine.Input;

/// <summary>
/// Keyboard key codes.
/// </summary>
public enum Key
{
    Unknown = 0,
    Space = 32,
    Escape = 256,
    Enter = 257,
    Tab = 258,
    Backspace = 259,
    Right = 262,
    Left = 263,
    Down = 264,
    Up = 265,
    LeftShift = 340,
    LeftControl = 341,
    LeftAlt = 342,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71,
    H = 72, I = 73, J = 74, K = 75, L = 76, M = 77, N = 78,
    O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85,
    V = 86, W = 87, X = 88, Y = 89, Z = 90,
    Num0 = 48, Num1 = 49, Num2 = 50, Num3 = 51, Num4 = 52,
    Num5 = 53, Num6 = 54, Num7 = 55, Num8 = 56, Num9 = 57
}

/// <summary>
/// Mouse button codes.
/// </summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

/// <summary>
/// Input state tracker for keyboard and mouse.
/// </summary>
public sealed class InputState
{
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly HashSet<Key> _justPressedKeys = [];
    private readonly HashSet<Key> _justReleasedKeys = [];
    private readonly HashSet<MouseButton> _pressedButtons = [];
    private readonly HashSet<MouseButton> _justPressedButtons = [];
    private readonly HashSet<MouseButton> _justReleasedButtons = [];

    public Vector2 MousePosition { get; set; }
    public Vector2 MouseDelta { get; set; }
    public float ScrollDelta { get; set; }

    public void BeginFrame()
    {
        _justPressedKeys.Clear();
        _justReleasedKeys.Clear();
        _justPressedButtons.Clear();
        _justReleasedButtons.Clear();
        ScrollDelta = 0;
        MouseDelta = Vector2.Zero;
    }

    /// <summary>Accumulates pointer movement since the last frame (pixels).</summary>
    public void AddMouseDelta(float dx, float dy) => MouseDelta += new Vector2(dx, dy);

    public void KeyPressed(Key key)
    {
        if (_pressedKeys.Add(key))
            _justPressedKeys.Add(key);
    }

    public void KeyReleased(Key key)
    {
        _pressedKeys.Remove(key);
        _justReleasedKeys.Add(key);
    }

    public void MousePressed(MouseButton button)
    {
        if (_pressedButtons.Add(button))
            _justPressedButtons.Add(button);
    }

    public void MouseReleased(MouseButton button)
    {
        _pressedButtons.Remove(button);
        _justReleasedButtons.Add(button);
    }

    public void Scroll(float delta)
    {
        ScrollDelta += delta;
    }

    public bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    public bool IsKeyJustPressed(Key key) => _justPressedKeys.Contains(key);
    public bool IsKeyJustReleased(Key key) => _justReleasedKeys.Contains(key);
    public bool IsMouseDown(MouseButton button) => _pressedButtons.Contains(button);
    public bool IsMouseJustPressed(MouseButton button) => _justPressedButtons.Contains(button);
}
