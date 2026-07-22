namespace Engine.RHI;

[Flags]
public enum ShaderStageFlags
{
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
    Compute = 1 << 2,
    Geometry = 1 << 3,
    TessellationControl = 1 << 4,
    TessellationEvaluation = 1 << 5,
    All = Vertex | Fragment | Compute | Geometry | TessellationControl | TessellationEvaluation,
}

public enum ShaderStage
{
    Vertex,
    Fragment,
    Compute,
    Geometry,
    TessellationControl,
    TessellationEvaluation,
}

public enum ResourceKind
{
    UniformBuffer,
    StorageBuffer,
    SampledTexture,
    StorageTexture,
    Sampler,
    CombinedTextureSampler,
    AccelerationStructure,
}

public enum VertexFormat
{
    Float32,
    Float32x2,
    Float32x3,
    Float32x4,
    UInt8x4Norm,
    UInt32,
}

/// <summary>A single binding point discovered via SPIR-V reflection, keyed by the Vulkan-shaped (set, binding) pair.</summary>
public readonly record struct ResourceBinding(
    uint Set,
    uint Binding,
    ResourceKind Kind,
    uint ArrayCount,
    ShaderStageFlags Stages,
    string? Name = null);

public readonly record struct VertexInputAttribute(
    uint Location,
    VertexFormat Format,
    uint OffsetBytes,
    string? Name = null);

public sealed class ShaderReflectionInfo
{
    public IReadOnlyList<ResourceBinding> Resources { get; init; } = [];
    public IReadOnlyList<VertexInputAttribute> VertexInputs { get; init; } = [];
    public uint PushConstantSizeBytes { get; init; }
}

public readonly record struct ShaderModuleDescriptor(
    ReadOnlyMemory<byte> SpirvBytecode,
    ShaderStage Stage,
    string EntryPoint = "main",
    string? Name = null);

public interface IShaderModule : IDisposable
{
    ShaderStage Stage { get; }
    string EntryPoint { get; }
    ShaderReflectionInfo Reflection { get; }
}
