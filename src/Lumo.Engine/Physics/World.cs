namespace Lumo.Engine.Physics;

/// <summary>
/// Static world queries available to gameplay. The terrain plugin registers
/// its height sampler here when a project has a heightmap, so scripts and
/// graphs can ask for ground height without taking a dependency on the plugin.
/// </summary>
public static class World
{
    /// <summary>Ground height sampler (world X/Z -&gt; Y); null when no terrain is loaded.</summary>
    public static Func<float, float, float>? TerrainHeight { get; set; }

    /// <summary>Ground height at (x, z); 0 when no terrain is loaded.</summary>
    public static float HeightAt(float x, float z) => TerrainHeight?.Invoke(x, z) ?? 0f;
}
