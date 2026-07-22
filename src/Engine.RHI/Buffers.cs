namespace Engine.RHI;

[Flags]
public enum BufferUsage
{
    None = 0,
    VertexBuffer = 1 << 0,
    IndexBuffer = 1 << 1,
    UniformBuffer = 1 << 2,
    StorageBuffer = 1 << 3,
    IndirectBuffer = 1 << 4,
    CopySource = 1 << 5,
    CopyDestination = 1 << 6,
    /// <summary>Usable as vertex/index input to an acceleration-structure build. Implies GPU device-address
    /// support, which requires the backend's ray-tracing capability (see <see cref="RhiCapabilities.RayQuery"/>).</summary>
    AccelerationStructureInput = 1 << 7,
}

public enum MemoryLocation
{
    /// <summary>Fast device-local memory; not directly writable from the CPU.</summary>
    DeviceLocal,
    /// <summary>Host-visible memory the CPU can map and write every frame (staging/upload).</summary>
    HostUpload,
    /// <summary>Host-visible memory optimized for the GPU reading back CPU-written results.</summary>
    HostReadback,
}

public readonly record struct BufferDescriptor(
    ulong SizeBytes,
    BufferUsage Usage,
    MemoryLocation Location = MemoryLocation.DeviceLocal,
    string? Name = null);

public interface IBuffer : IDisposable
{
    BufferDescriptor Descriptor { get; }
    ResourceState CurrentState { get; }

    /// <summary>Maps the buffer for CPU access. Only valid for HostUpload/HostReadback locations.</summary>
    Span<byte> Map();
    void Unmap();
}
