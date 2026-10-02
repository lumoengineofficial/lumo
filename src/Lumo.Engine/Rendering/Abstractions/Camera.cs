using System.Numerics;
using Lumo.Engine.Core;

namespace Lumo.Engine.Rendering.Abstractions;

/// <summary>
/// Camera types supported by the engine.
/// </summary>
public enum CameraType
{
    Perspective,
    Orthographic
}

/// <summary>
/// Camera abstraction for viewing the scene.
/// </summary>
public class Camera
{
    public CameraType Type { get; set; } = CameraType.Perspective;
    public Vector3 Position { get; set; } = new(0, 0, 3);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float FieldOfView { get; set; } = 60.0f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1000.0f;
    public float AspectRatio { get; set; } = 16.0f / 9.0f;

    public float OrthoWidth { get; set; } = 10.0f;
    public float OrthoHeight { get; set; } = 6.0f;

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        return Type switch
        {
            CameraType.Perspective => Matrix4x4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(FieldOfView), AspectRatio, NearPlane, FarPlane),
            CameraType.Orthographic => Matrix4x4.CreateOrthographic(
                OrthoWidth, OrthoHeight, NearPlane, FarPlane),
            _ => Matrix4x4.Identity
        };
    }

    public void Orbit(float yawDelta, float pitchDelta)
    {
        Vector3 direction = Vector3.Normalize(Position - Target);
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector3(0, 0, 1);

        float yaw = MathF.Atan2(direction.X, direction.Z) + yawDelta;
        float pitch = MathF.Asin(Math.Clamp(direction.Y, -0.999f, 0.999f)) + pitchDelta;
        pitch = Math.Clamp(pitch, -MathF.PI / 2.0f + 0.01f, MathF.PI / 2.0f - 0.01f);

        float distance = Vector3.Distance(Position, Target);
        Position = Target + new Vector3(
            MathF.Sin(yaw) * MathF.Cos(pitch) * distance,
            MathF.Sin(pitch) * distance,
            MathF.Cos(yaw) * MathF.Cos(pitch) * distance
        );
    }

    public void Focus(Vector3 targetPosition, float focusDistance = 5.0f)
    {
        Vector3 direction = Position - Target;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector3(0, 0, 1);
        else
            direction = Vector3.Normalize(direction);

        Target = targetPosition;
        Position = Target + direction * Math.Max(0.5f, focusDistance);
    }

    public void Reset()
    {
        Target = Vector3.Zero;
        Position = new Vector3(0, 0, 3);
        Up = Vector3.UnitY;
        FieldOfView = 60.0f;
    }

    public void Pan(float rightDelta, float upDelta)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Up));
        Vector3 up = Vector3.Cross(right, forward);

        Vector3 offset = right * rightDelta + up * upDelta;
        Position += offset;
        Target += offset;
    }

    public void Zoom(float delta)
    {
        Vector3 direction = Vector3.Normalize(Target - Position);
        float distance = Vector3.Distance(Position, Target);
        distance = Math.Max(0.5f, distance - delta);
        Position = Target - direction * distance;
    }
}


