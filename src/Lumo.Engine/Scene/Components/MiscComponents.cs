namespace Lumo.Engine.Scene;

/// <summary>
/// Component types for rendering meshes.
/// </summary>
public sealed class MeshRendererComponent
{
    public string? MeshName { get; set; }
    public string? MaterialName { get; set; }
    public bool IsVisible { get; set; } = true;
    public System.Numerics.Vector3 Color { get; set; } = new(0.55f, 0.62f, 0.75f);
}

/// <summary>
/// Component for 2D sprite rendering.
/// </summary>
public sealed class SpriteRendererComponent
{
    public string? SpritePath { get; set; }
    public float Width { get; set; } = 1.0f;
    public float Height { get; set; } = 1.0f;
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public System.Numerics.Vector3 Color { get; set; } = new(0.47f, 0.67f, 0.94f);
}

/// <summary>
/// Camera component for entities.
/// </summary>
public sealed class CameraComponent
{
    public bool IsPrimary { get; set; }
    public float FieldOfView { get; set; } = 60.0f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1000.0f;
    public float AspectRatio { get; set; } = 16.0f / 9.0f;
    public Rendering.Abstractions.CameraType CameraType { get; set; } = Rendering.Abstractions.CameraType.Perspective;
}

/// <summary>
/// Light component for entities.
/// </summary>
public sealed class LightComponent
{
    public Rendering.Abstractions.LightType LightType { get; set; } = Rendering.Abstractions.LightType.Directional;
    public float Intensity { get; set; } = 1.0f;
    public System.Numerics.Vector3 Color { get; set; } = System.Numerics.Vector3.One;
    public float Range { get; set; } = 50.0f;
}
