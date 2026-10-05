using System.Numerics;

namespace Lumo.Plugins;

public enum ViewportPointerKind
{
    Down,
    Move,
    Up,
}

/// <summary>Pointer event forwarded from the editor's 3D viewport. Handlers can
/// consume the event (return true) to take over picking/dragging.</summary>
public sealed record ViewportPointerArgs
{
    public ViewportPointerKind Kind { get; init; }

    /// <summary>Viewport-local pixel position.</summary>
    public double X { get; init; }

    public double Y { get; init; }

    /// <summary>True while the left mouse button is held.</summary>
    public bool LeftPressed { get; init; }

    /// <summary>World-space ray through the pointer (false outside the 3D scene view).</summary>
    public bool HasRay { get; init; }

    public Vector3 RayOrigin { get; init; }

    public Vector3 RayDir { get; init; }
}

/// <summary>
/// Lets plugins take over viewport pointer input (sculpt brushes, placement
/// tools, ...). Handlers run before the default pick/drag logic; the first
/// handler returning true consumes the event. The host disables dispatch while
/// the game is playing.
/// </summary>
public static class ViewportHooks
{
    private static event Func<ViewportPointerArgs, bool>? _pointer;

    /// <summary>Host-managed master switch (false while playing).</summary>
    public static bool Enabled { get; set; } = true;

    public static event Func<ViewportPointerArgs, bool>? Pointer
    {
        add => _pointer += value;
        remove => _pointer -= value;
    }

    public static bool HasSubscribers => _pointer != null;

    public static bool Dispatch(ViewportPointerArgs args)
    {
        if (!Enabled || _pointer == null) return false;
        foreach (var handler in _pointer.GetInvocationList())
        {
            try
            {
                if (((Func<ViewportPointerArgs, bool>)handler)(args)) return true;
            }
            catch { }
        }
        return false;
    }
}
