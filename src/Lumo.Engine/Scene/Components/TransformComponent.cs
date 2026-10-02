using System.Numerics;
using Lumo.Engine.Core;

namespace Lumo.Engine.Scene;

/// <summary>
/// Core transform component for all entities.
/// </summary>
public sealed class TransformComponent
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    public Vector3 Forward
    {
        get
        {
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            return -rotationMatrix.GetColumn(2).XYZ();
        }
    }

    public Vector3 Right
    {
        get
        {
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            return rotationMatrix.GetColumn(0).XYZ();
        }
    }

    public Vector3 Up
    {
        get
        {
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            return rotationMatrix.GetColumn(1).XYZ();
        }
    }

    public Matrix4x4 LocalToWorldMatrix
    {
        get
        {
            Matrix4x4 scale = Matrix4x4.CreateScale(Scale);
            Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(Rotation);
            Matrix4x4 translation = Matrix4x4.CreateTranslation(Position);
            return scale * rotation * translation;
        }
    }

    public void SetRotationFromEuler(float pitch, float yaw, float roll)
    {
        Quaternion pitchQ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * MathHelper.Deg2Rad);
        Quaternion yawQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathHelper.Deg2Rad);
        Quaternion rollQ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * MathHelper.Deg2Rad);
        Rotation = Quaternion.Normalize(yawQ * pitchQ * rollQ);
    }

    /// <summary>
    /// Euler angles (degrees) that reproduce this rotation through
    /// <see cref="SetRotationFromEuler"/> (yaw * pitch * roll order).
    /// </summary>
    public Vector3 GetEulerAngles()
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(Rotation);
        float cx = MathF.Sqrt(m.M12 * m.M12 + m.M22 * m.M22);
        bool singular = cx < 1e-6f;

        float x, y, z;
        if (!singular)
        {
            x = MathF.Atan2(-m.M32, cx);
            y = MathF.Atan2(m.M31, m.M33);
            z = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            // Pitch at +/-90: yaw and roll merge; report the combined yaw.
            x = MathF.Atan2(-m.M32, cx);
            y = MathF.Atan2(m.M21, m.M11);
            z = 0f;
        }

        return new Vector3(x, y, z) * MathHelper.Rad2Deg;
    }

    public void LookAt(Vector3 target, Vector3 up)
    {
        Vector3 direction = Vector3.Normalize(target - Position);
        if (direction.LengthSquared() < 0.0001f)
            return;

        Vector3 right = Vector3.Normalize(Vector3.Cross(up, direction));
        Vector3 adjustedUp = Vector3.Cross(direction, right);

        Matrix4x4 lookMatrix = new Matrix4x4(
            right.X, adjustedUp.X, direction.X, 0,
            right.Y, adjustedUp.Y, direction.Y, 0,
            right.Z, adjustedUp.Z, direction.Z, 0,
            0, 0, 0, 1
        );
        Rotation = Quaternion.CreateFromRotationMatrix(lookMatrix);
    }

    public void ResetPosition() => Position = Vector3.Zero;
    public void ResetRotation() => Rotation = Quaternion.Identity;
    public void ResetScale() => Scale = Vector3.One;

    public void ResetAll()
    {
        ResetPosition();
        ResetRotation();
        ResetScale();
    }

    public void CopyFrom(TransformComponent source)
    {
        if (source == null) return;
        Position = source.Position;
        Rotation = source.Rotation;
        Scale = source.Scale;
    }
}

/// <summary>
/// Helper for Vector4 column extraction.
/// </summary>
internal static class MatrixExtensions
{
    public static Vector4 GetColumn(this Matrix4x4 m, int index) => index switch
    {
        0 => new Vector4(m.M11, m.M21, m.M31, m.M41),
        1 => new Vector4(m.M12, m.M22, m.M32, m.M42),
        2 => new Vector4(m.M13, m.M23, m.M33, m.M43),
        3 => new Vector4(m.M14, m.M24, m.M34, m.M44),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public static Vector3 XYZ(this Vector4 v) => new(v.X, v.Y, v.Z);
}
