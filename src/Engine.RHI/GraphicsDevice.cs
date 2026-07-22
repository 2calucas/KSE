namespace Engine.RHI;

/// <summary>GPU memory usage as reported by the backend's allocator (e.g. VMA budgets on Vulkan).</summary>
public readonly record struct MemoryUsageInfo(ulong UsedBytes, ulong BudgetBytes);

public interface IGraphicsDevice : IDisposable
{
    RhiBackend Backend { get; }
    RhiCapabilities Capabilities { get; }
    RhiDeviceLimits Limits { get; }
    string AdapterName { get; }

    ISwapChain CreateSwapChain(in SwapChainDescriptor descriptor);
    IBuffer CreateBuffer(in BufferDescriptor descriptor);
    ITexture CreateTexture(in TextureDescriptor descriptor);
    ISampler CreateSampler(in SamplerDescriptor descriptor);
    IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor);
    IPipeline CreateGraphicsPipeline(in GraphicsPipelineDescriptor descriptor);
    IPipeline CreateComputePipeline(in ComputePipelineDescriptor descriptor);
    IResourceSetLayout CreateResourceSetLayout(in ResourceSetLayoutDescriptor descriptor);
    IResourceSet CreateResourceSet(in ResourceSetDescriptor descriptor);

    /// <summary>Builds a bottom-level acceleration structure synchronously (submits and waits on the graphics
    /// queue internally) — geometry is expected to be static, built once at load time.</summary>
    IAccelerationStructure CreateBottomLevelAccelerationStructure(in BottomLevelAccelerationStructureDescriptor descriptor);

    /// <summary>Builds a top-level acceleration structure synchronously for its first frame. Instance transforms
    /// that change every frame (e.g. an animated object) should be refreshed via
    /// <see cref="ICommandBuffer.RebuildTopLevelAccelerationStructure"/> instead of recreating this.</summary>
    IAccelerationStructure CreateTopLevelAccelerationStructure(in TopLevelAccelerationStructureDescriptor descriptor);

    ICommandQueue GetQueue(CommandQueueKind kind);
    IFence CreateFence(bool signaled = false);

    MemoryUsageInfo QueryMemoryUsage();

    void WaitIdle();
}
