using System.Numerics;
using Lumo.Engine.Input;
using Entity = Lumo.Engine.Scene.Entity;
using GameScene = Lumo.Engine.Scene.Scene;

namespace Lumo.Engine.Scripting;

/// <summary>
/// Base class for all C# game scripts. User scripts inherit this
/// and override lifecycle callbacks.
/// </summary>
public abstract class LumoScript
{
    /// <summary>The entity this script is attached to.</summary>
    public Entity Entity { get; internal set; } = null!;

    /// <summary>The scene containing the attached entity.</summary>
    public GameScene? Scene => Entity?.ParentScene;

    /// <summary>Delta time of the current frame (seconds).</summary>
    public float DeltaTime { get; internal set; }

    /// <summary>Total elapsed time since play started (seconds).</summary>
    public float Time { get; internal set; }

    /// <summary>Input state (available during play mode).</summary>
    public InputState? Input { get; internal set; }

    /// <summary>Called once when play mode starts.</summary>
    public virtual void OnStart() { }

    /// <summary>Called every frame while play mode runs.</summary>
    public virtual void OnUpdate(float deltaTime) { }

    /// <summary>Called once when play mode stops.</summary>
    public virtual void OnDestroy() { }

    // ---- Convenience helpers ----

    protected Vector3 Position
    {
        get => Entity.Transform.Position;
        set => Entity.Transform.Position = value;
    }

    protected Vector3 Scale
    {
        get => Entity.Transform.Scale;
        set => Entity.Transform.Scale = value;
    }

    protected void Log(string message) => ScriptHost.Log(message);

    protected Entity? Find(string name) => Scene?.FindByName(name);

    protected void Translate(Vector3 delta) => Entity.Transform.Position += delta;

    protected void RotateEuler(float pitch, float yaw, float roll)
        => Entity.Transform.SetRotationFromEuler(
            Entity.Transform.GetEulerAngles().X + pitch,
            Entity.Transform.GetEulerAngles().Y + yaw,
            Entity.Transform.GetEulerAngles().Z + roll);

    /// <summary>Set a HUD text entry. X/Y are 0..1 screen fractions, size is font pixels.</summary>
    protected void SetHud(string id, string text, float x, float y, float size, Vector3? color = null)
        => ScriptHost.Hud?.Set(id, text, x, y, size, color ?? Vector3.One);

    /// <summary>Remove a HUD text entry by id.</summary>
    protected void RemoveHud(string id) => ScriptHost.Hud?.Remove(id);

    /// <summary>Ground height at (x, z) from the terrain heightmap; 0 without terrain.</summary>
    protected static float HeightAt(float x, float z) => Physics.World.HeightAt(x, z);

    /// <summary>
    /// Moves by <paramref name="delta"/> one axis at a time, sliding along
    /// visible mesh obstacles. Terrain chunks are excluded (their AABBs cover
    /// whole chunks); ground height comes from <see cref="HeightAt"/>.
    /// When <paramref name="collidePrefixes"/> is given, only entities whose
    /// name starts with one of the prefixes block the move (decor like apples
    /// or grass stays walk-through).
    /// </summary>
    protected Vector3 SlideMove(Vector3 delta, float radius, out bool blocked,
        string excludePrefix = "Terrain", params string[] collidePrefixes)
    {
        blocked = false;
        var obstacles = VisualScripting.PhysicsHelper.Collect(Scene, Entity, null, "", excludePrefix);
        if (collidePrefixes.Length > 0)
            obstacles = obstacles
                .Where(o => collidePrefixes.Any(p => o.Entity.Name != null &&
                    o.Entity.Name.StartsWith(p, StringComparison.Ordinal)))
                .ToList();
        Vector3 pos = Position;

        if (delta.X != 0f)
        {
            Vector3 cand = pos with { X = pos.X + delta.X };
            if (VisualScripting.PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }
        if (delta.Y != 0f)
        {
            Vector3 cand = pos with { Y = pos.Y + delta.Y };
            if (VisualScripting.PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }
        if (delta.Z != 0f)
        {
            Vector3 cand = pos with { Z = pos.Z + delta.Z };
            if (VisualScripting.PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }
        return pos;
    }
}
