using Engine.RHI;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;

namespace Engine.RHI.Direct3D12;

public sealed class D3D12GraphicsDevice : IGraphicsDevice
{
    private const int CbvSrvUavHeapCapacity = 4096;
    private const int SamplerHeapCapacity = 1024;
    private const int RtvHeapCapacity = 256;
    private const int DsvHeapCapacity = 256;

    private readonly IDXGIFactory4 _factory;
    private readonly IDXGIAdapter1 _adapter;
    private readonly ID3D12Device5 _device;
    private readonly D3D12CommandQueue _graphicsQueue;

    internal ID3D12Device5 Device => _device;
    internal IDXGIFactory4 Factory => _factory;
    internal D3D12CommandQueue GraphicsQueue => _graphicsQueue;
    internal D3D12DescriptorAllocator ResourceHeap { get; }
    internal D3D12DescriptorAllocator SamplerHeap { get; }
    internal D3D12DescriptorAllocator RtvHeap { get; }
    internal D3D12DescriptorAllocator DsvHeap { get; }
    internal bool RootSignatureVersion11 { get; }

    public RhiBackend Backend => RhiBackend.Direct3D12;
    public RhiCapabilities Capabilities { get; }
    public RhiDeviceLimits Limits { get; }
    public string AdapterName { get; }

    public D3D12GraphicsDevice(bool enableValidation = true)
    {
        if (enableValidation && D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
        {
            debug!.EnableDebugLayer();
            debug.Dispose();
        }

        DXGI.CreateDXGIFactory2(enableValidation, out _factory!).CheckError();

        _adapter = SelectAdapter(_factory, out string adapterName);
        AdapterName = adapterName;

        D3D12.D3D12CreateDevice<ID3D12Device5>(_adapter, FeatureLevel.Level_12_0, out _device!).CheckError();

        var options5 = _device.CheckFeatureSupport<FeatureDataD3D12Options5>(Feature.Options5);
        bool rayTracingSupported = options5.RaytracingTier >= RaytracingTier.Tier1_1;

        var rootSigFeature = new FeatureDataRootSignature(RootSignatureVersion.Version11);
        RootSignatureVersion11 = _device.CheckFeatureSupport(Feature.RootSignature, ref rootSigFeature)
            && rootSigFeature.HighestVersion >= RootSignatureVersion.Version11;

        Capabilities = RhiCapabilities.ComputeShaders | RhiCapabilities.SamplerAnisotropy | RhiCapabilities.MultiDrawIndirect;
        if (rayTracingSupported)
            Capabilities |= RhiCapabilities.RayQuery;

        Limits = new RhiDeviceLimits(
            MaxTexture2DSize: 16384,
            MaxColorAttachments: 8,
            MaxBoundDescriptorSets: 8,
            MinUniformBufferOffsetAlignment: 256,
            MinStorageBufferOffsetAlignment: 16,
            MaxPushConstantSize: 256);

        ResourceHeap = new D3D12DescriptorAllocator(_device, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, CbvSrvUavHeapCapacity, shaderVisible: true);
        SamplerHeap = new D3D12DescriptorAllocator(_device, DescriptorHeapType.Sampler, SamplerHeapCapacity, shaderVisible: true);
        RtvHeap = new D3D12DescriptorAllocator(_device, DescriptorHeapType.RenderTargetView, RtvHeapCapacity, shaderVisible: false);
        DsvHeap = new D3D12DescriptorAllocator(_device, DescriptorHeapType.DepthStencilView, DsvHeapCapacity, shaderVisible: false);

        _graphicsQueue = new D3D12CommandQueue(this, CommandQueueKind.Graphics, CommandListType.Direct);
    }

    private static IDXGIAdapter1 SelectAdapter(IDXGIFactory4 factory, out string adapterName)
    {
        using var factory6 = factory.QueryInterface<IDXGIFactory6>();
        if (factory6 is not null)
        {
            for (uint i = 0; factory6.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? candidate).Success; i++)
            {
                var desc = candidate!.Description1;
                if ((desc.Flags & AdapterFlags.Software) != 0)
                {
                    candidate.Dispose();
                    continue;
                }
                adapterName = desc.Description;
                return candidate;
            }
        }

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? fallback).Success; i++)
        {
            var desc = fallback!.Description1;
            if ((desc.Flags & AdapterFlags.Software) != 0)
            {
                fallback.Dispose();
                continue;
            }
            adapterName = desc.Description;
            return fallback;
        }

        throw new InvalidOperationException("No Direct3D 12-capable hardware adapter was found.");
    }

    public ISwapChain CreateSwapChain(in SwapChainDescriptor descriptor) => new D3D12SwapChain(this, descriptor);
    public IBuffer CreateBuffer(in BufferDescriptor descriptor) => new D3D12Buffer(this, descriptor);
    public ITexture CreateTexture(in TextureDescriptor descriptor) => new D3D12Texture(this, descriptor);
    public ISampler CreateSampler(in SamplerDescriptor descriptor) => new D3D12Sampler(descriptor);
    public IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor) => new D3D12ShaderModule(descriptor);
    public IPipeline CreateGraphicsPipeline(in GraphicsPipelineDescriptor descriptor) => new D3D12Pipeline(this, descriptor);
    public IPipeline CreateComputePipeline(in ComputePipelineDescriptor descriptor) => new D3D12Pipeline(this, descriptor);
    public IResourceSetLayout CreateResourceSetLayout(in ResourceSetLayoutDescriptor descriptor) => new D3D12ResourceSetLayout(descriptor);
    public IResourceSet CreateResourceSet(in ResourceSetDescriptor descriptor) => new D3D12ResourceSet(this, descriptor);
    public IAccelerationStructure CreateBottomLevelAccelerationStructure(in BottomLevelAccelerationStructureDescriptor descriptor) => new D3D12AccelerationStructure(this, descriptor);
    public IAccelerationStructure CreateTopLevelAccelerationStructure(in TopLevelAccelerationStructureDescriptor descriptor) => new D3D12AccelerationStructure(this, descriptor);

    public ICommandQueue GetQueue(CommandQueueKind kind)
    {
        if (kind != CommandQueueKind.Graphics)
            throw new NotSupportedException($"Only the graphics queue is available in this milestone (requested {kind}).");
        return _graphicsQueue;
    }

    public IFence CreateFence(bool signaled = false) => new D3D12Fence(this, signaled);

    public MemoryUsageInfo QueryMemoryUsage()
    {
        using var adapter3 = _adapter.QueryInterface<IDXGIAdapter3>();
        if (adapter3 is null)
            return new MemoryUsageInfo(0, 0);

        var info = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        return new MemoryUsageInfo(info.CurrentUsage, info.Budget);
    }

    public void WaitIdle() => _graphicsQueue.WaitIdle();

    public void Dispose()
    {
        _graphicsQueue.WaitIdle();
        _graphicsQueue.Dispose();
        ResourceHeap.Dispose();
        SamplerHeap.Dispose();
        RtvHeap.Dispose();
        DsvHeap.Dispose();
        _device.Dispose();
        _adapter.Dispose();
        _factory.Dispose();
    }
}
