namespace Engine.RHI;

public enum RhiBackend
{
    Vulkan,
    Direct3D12,
    OpenGL,
}

[Flags]
public enum RhiCapabilities : ulong
{
    None = 0,
    ComputeShaders = 1UL << 0,
    GeometryShaders = 1UL << 1,
    TessellationShaders = 1UL << 2,
    MeshShaders = 1UL << 3,
    RayTracingPipeline = 1UL << 4,
    RayQuery = 1UL << 5,
    BindlessResources = 1UL << 6,
    ConservativeRaster = 1UL << 7,
    VariableRateShading = 1UL << 8,
    Timestamps = 1UL << 9,
    MultiDrawIndirect = 1UL << 10,
    DrawIndirectCount = 1UL << 11,
    SamplerAnisotropy = 1UL << 12,
}

public readonly record struct RhiDeviceLimits(
    uint MaxTexture2DSize,
    uint MaxColorAttachments,
    uint MaxBoundDescriptorSets,
    uint MinUniformBufferOffsetAlignment,
    uint MinStorageBufferOffsetAlignment,
    uint MaxPushConstantSize);
