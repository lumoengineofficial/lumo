using System.Numerics;

namespace Lumo.Engine.Rendering;

/// <summary>Post-process overlay kinds a plugin can register.</summary>
public enum FxKind
{
    Vignette,
    ColorGrade
}

/// <summary>A registered frame effect. Intensity is 0..1; Tint is used by ColorGrade.</summary>
public sealed record FxEffect(string Id, FxKind Kind, float Intensity, Vector3 Tint);

/// <summary>
/// Render-side extension point for plugins: frame overlays (vignette, color
/// grade) and the shared mesh-shading toggle. Renderers poll <see cref="Snapshot"/>
/// each frame; effects are plain data so plugin assemblies stay Avalonia-free.
/// </summary>
public static class FxRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, FxEffect> Effects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, renderers lambert-shade mesh triangles with the shared key light.</summary>
    public static bool MeshShading { get; set; }

    /// <summary>Registers or replaces an effect by id.</summary>
    public static void Register(FxEffect effect)
    {
        lock (Sync)
            Effects[effect.Id] = effect;
    }

    public static bool Remove(string id)
    {
        lock (Sync)
            return Effects.Remove(id);
    }

    public static void Clear()
    {
        lock (Sync)
            Effects.Clear();
    }

    /// <summary>Point-in-time copy of the registered effects for frame iteration.</summary>
    public static IReadOnlyList<FxEffect> Snapshot()
    {
        lock (Sync)
            return Effects.Values.ToList();
    }
}

/// <summary>Fixed key-light lambert helper shared by the game and editor renderers.</summary>
public static class FxLighting
{
    public static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.35f, -1f, -0.45f));
    public const float Ambient = 0.40f;

    /// <summary>
    /// Brightness 0.40..1.00 for a world-space normal. Two-sided: the normal is
    /// flipped when it faces away from the camera so interior walls stay lit.
    /// </summary>
    public static float Brightness(Vector3 worldNormal, Vector3 toCamera)
    {
        Vector3 n = worldNormal;
        if (Vector3.Dot(n, toCamera) < 0f)
            n = -n;
        float diffuse = MathF.Max(Vector3.Dot(n, -LightDirection), 0f);
        return Ambient + (1f - Ambient) * diffuse;
    }

    /// <summary>
    /// Material-aware shade: lambert diffuse + Blinn specular tinted by
    /// metallic, with roughness controlling highlight sharpness, plus an
    /// emissive term that ignores lighting. Returns the final 0..1+ RGB.
    /// </summary>
    public static Vector3 Shade(Vector3 worldNormal, Vector3 toCamera, Vector3 albedo,
        float metallic, float roughness, float emissive, Vector3 emissiveColor)
    {
        Vector3 n = worldNormal;
        Vector3 v = Vector3.Normalize(toCamera);
        if (Vector3.Dot(n, v) < 0f) n = -n;

        Vector3 l = -LightDirection;
        float ndl = MathF.Max(Vector3.Dot(n, l), 0f);
        float diffuse = Ambient + (1f - Ambient) * ndl;

        // Specular: roughness 0 -> tight hot highlight, roughness 1 -> none.
        float shininess = 2f + 128f * (1f - roughness) * (1f - roughness);
        Vector3 h = Vector3.Normalize(l + v);
        float spec = MathF.Pow(MathF.Max(Vector3.Dot(n, h), 0f), shininess) * (1f - roughness) * (0.25f + 0.75f * metallic);

        Vector3 result = albedo * diffuse + new Vector3(spec);
        if (emissive > 0f)
            result += emissiveColor * emissive;
        return result;
    }
}
