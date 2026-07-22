namespace Engine.RHI;

public enum AccelerationStructureKind
{
    BottomLevel,
    TopLevel,
}

/// <summary>Row-major 3x4 affine transform for acceleration-structure instances — matches Vulkan's
/// VkTransformMatrixKHR / D3D12's instance transform layout (column-vector convention, translation in the
/// last column). This engine's System.Numerics matrices use row-vector convention with translation in the
/// last row, so use <see cref="FromEngineMatrix"/> rather than assuming the same memory layout.</summary>
public readonly struct AccelerationStructureTransform
{
    public readonly float M00, M01, M02, M03;
    public readonly float M10, M11, M12, M13;
    public readonly float M20, M21, M22, M23;

    public AccelerationStructureTransform(
        float m00, float m01, float m02, float m03,
        float m10, float m11, float m12, float m13,
        float m20, float m21, float m22, float m23)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
    }

    public static readonly AccelerationStructureTransform Identity = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0);

    public static AccelerationStructureTransform FromEngineMatrix(System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M21, m.M31, m.M41,
        m.M12, m.M22, m.M32, m.M42,
        m.M13, m.M23, m.M33, m.M43);
}

public readonly record struct AccelerationStructureGeometryDescriptor(
    IBuffer VertexBuffer,
    uint VertexStrideBytes,
    uint VertexCount,
    IBuffer IndexBuffer,
    IndexFormat IndexFormat,
    uint IndexCount,
    bool Opaque = true);

public readonly record struct BottomLevelAccelerationStructureDescriptor(
    IReadOnlyList<AccelerationStructureGeometryDescriptor> Geometries,
    string? Name = null);

public readonly record struct AccelerationStructureInstanceDescriptor(
    IAccelerationStructure BottomLevel,
    AccelerationStructureTransform Transform,
    uint InstanceCustomIndex = 0,
    byte Mask = 0xFF);

public readonly record struct TopLevelAccelerationStructureDescriptor(
    IReadOnlyList<AccelerationStructureInstanceDescriptor> Instances,
    string? Name = null);

public interface IAccelerationStructure : IDisposable
{
    AccelerationStructureKind Kind { get; }
}
