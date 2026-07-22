using System.Numerics;

namespace Sandbox;

/// <summary>Free-fly camera (Unreal-editor-viewport style): W/S move along the full look direction (including
/// pitch, so looking up and moving forward climbs), A/D strafe on the horizontal plane, Space/Ctrl move purely
/// vertically, and mouse deltas drive yaw/pitch while look is active.</summary>
internal sealed class Camera
{
    private const float MoveSpeed = 4f;
    private const float SprintMultiplier = 3f;
    private const float LookSensitivity = 0.0025f;
    private const float MaxPitch = MathF.PI / 2f * 0.98f;

    public Vector3 Position;
    public float Yaw;
    public float Pitch;

    public Camera(Vector3 position, Vector3 target)
    {
        Position = position;
        Vector3 dir = Vector3.Normalize(target - position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
    }

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public void Look(int mouseDx, int mouseDy)
    {
        Yaw += mouseDx * LookSensitivity;
        Pitch = Math.Clamp(Pitch - mouseDy * LookSensitivity, -MaxPitch, MaxPitch);
    }

    /// <summary>input.Z = forward/back, input.X = strafe right/left, input.Y = world up/down; expected pre-normalized.</summary>
    public void Move(Vector3 input, float deltaSeconds, bool sprint)
    {
        float speed = MoveSpeed * (sprint ? SprintMultiplier : 1f) * deltaSeconds;
        Vector3 forward = Forward;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Position += (forward * input.Z + right * input.X + Vector3.UnitY * input.Y) * speed;
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);
}
