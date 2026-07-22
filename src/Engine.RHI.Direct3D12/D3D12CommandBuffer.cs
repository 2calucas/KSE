using Engine.RHI;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Engine.RHI.Direct3D12;

internal sealed class D3D12CommandBuffer : ICommandBuffer
{
    private readonly D3D12GraphicsDevice _device;
    private readonly ID3D12GraphicsCommandList4 _list;
    private D3D12Pipeline? _currentPipeline;

    internal ID3D12GraphicsCommandList4 List => _list;

    public D3D12CommandBuffer(D3D12GraphicsDevice device, ID3D12CommandAllocator allocator)
    {
        _device = device;
        _list = device.Device.CreateCommandList<ID3D12GraphicsCommandList4>(0, CommandListType.Direct, allocator);
        _list.Close();
    }

    public void Begin()
    {
        // The allocator was already reset by D3D12CommandQueue.WaitIdle() before this call — Reset() here just
        // reopens this specific list against it, mirroring the Vulkan backend's one-shot-buffer-per-recording model.
    }

    public void End() => _list.Close();

    public void BeginRenderPass(in RenderPassDescriptor descriptor)
    {
        var rtvHandles = new CpuDescriptorHandle[descriptor.ColorAttachments.Count];
        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            var attachment = descriptor.ColorAttachments[i];
            var view = (D3D12TextureView)attachment.Target;
            rtvHandles[i] = view.Rtv!.Value;
            if (attachment.Clear)
                _list.ClearRenderTargetView(rtvHandles[i], new Color4(attachment.ClearR, attachment.ClearG, attachment.ClearB, attachment.ClearA));
        }

        CpuDescriptorHandle? dsvHandle = null;
        if (descriptor.DepthStencilAttachment is { } depth)
        {
            var view = (D3D12TextureView)depth.Target;
            dsvHandle = view.Dsv!.Value;
            if (depth.ClearDepth)
                _list.ClearDepthStencilView(dsvHandle.Value, ClearFlags.Depth, depth.ClearDepthValue, depth.ClearStencilValue);
        }

        _list.OMSetRenderTargets(rtvHandles, dsvHandle);
    }

    public void EndRenderPass() { }

    public void SetPipeline(IPipeline pipeline)
    {
        _currentPipeline = (D3D12Pipeline)pipeline;
        _list.SetPipelineState(_currentPipeline.Handle);
        if (_currentPipeline.IsCompute)
            _list.SetComputeRootSignature(_currentPipeline.RootSignature);
        else
        {
            _list.SetGraphicsRootSignature(_currentPipeline.RootSignature);
            _list.IASetPrimitiveTopology(_currentPipeline.Topology);
        }
        _list.SetDescriptorHeaps([_device.ResourceHeap.Heap, _device.SamplerHeap.Heap]);
    }

    public void SetResourceSet(uint setIndex, IResourceSet set)
    {
        if (_currentPipeline is null)
            throw new InvalidOperationException("SetResourceSet requires a pipeline to be bound first.");

        var d3dSet = (D3D12ResourceSet)set;
        int resourceParam = _currentPipeline.SetResourceRootParam[setIndex];
        int samplerParam = _currentPipeline.SetSamplerRootParam[setIndex];

        if (resourceParam >= 0)
        {
            var handle = _device.ResourceHeap.GetGpuHandle(d3dSet.ResourceBase);
            if (_currentPipeline.IsCompute) _list.SetComputeRootDescriptorTable(resourceParam, handle);
            else _list.SetGraphicsRootDescriptorTable(resourceParam, handle);
        }
        if (samplerParam >= 0)
        {
            var handle = _device.SamplerHeap.GetGpuHandle(d3dSet.SamplerBase);
            if (_currentPipeline.IsCompute) _list.SetComputeRootDescriptorTable(samplerParam, handle);
            else _list.SetGraphicsRootDescriptorTable(samplerParam, handle);
        }
    }

    public void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
    {
        var d3dBuffer = (D3D12Buffer)buffer;
        var view = new VertexBufferView(d3dBuffer.Handle.GPUVirtualAddress + offset, (uint)(d3dBuffer.Descriptor.SizeBytes - offset), 0);
        _list.IASetVertexBuffers((int)slot, view);
    }

    public void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0)
    {
        var d3dBuffer = (D3D12Buffer)buffer;
        var dxgiFormat = format == IndexFormat.UInt16 ? Vortice.DXGI.Format.R16_UInt : Vortice.DXGI.Format.R32_UInt;
        var view = new IndexBufferView(d3dBuffer.Handle.GPUVirtualAddress + offset, (uint)(d3dBuffer.Descriptor.SizeBytes - offset), dxgiFormat);
        _list.IASetIndexBuffer(view);
    }

    public void SetViewport(in Viewport viewport)
        => _list.RSSetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);

    public void SetScissor(in ScissorRect rect)
        => _list.RSSetScissorRect((int)rect.X, (int)rect.Y, (int)(rect.X + rect.Width), (int)(rect.Y + rect.Height));

    public unsafe void PushConstants<T>(ShaderStageFlags stages, in T data, uint offset = 0) where T : unmanaged
    {
        if (_currentPipeline is null)
            throw new InvalidOperationException("PushConstants requires a pipeline to be bound first.");
        if (_currentPipeline.RootConstantsParamIndex < 0)
            return;

        fixed (T* pData = &data)
        {
            if (_currentPipeline.IsCompute)
                _list.SetComputeRoot32BitConstants(_currentPipeline.RootConstantsParamIndex, (int)_currentPipeline.RootConstants32BitCount, (nint)pData, (int)(offset / 4));
            else
                _list.SetGraphicsRoot32BitConstants(_currentPipeline.RootConstantsParamIndex, (int)_currentPipeline.RootConstants32BitCount, (nint)pData, (int)(offset / 4));
        }
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        => _list.DrawInstanced((int)vertexCount, (int)instanceCount, (int)firstVertex, (int)firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        => _list.DrawIndexedInstanced((int)indexCount, (int)instanceCount, (int)firstIndex, vertexOffset, (int)firstInstance);

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ = 1)
        => _list.Dispatch((int)groupCountX, (int)groupCountY, (int)groupCountZ);

    public void CopyBuffer(IBuffer src, IBuffer dst, ulong size, ulong srcOffset = 0, ulong dstOffset = 0)
        => _list.CopyBufferRegion(((D3D12Buffer)dst).Handle, dstOffset, ((D3D12Buffer)src).Handle, srcOffset, size);

    public void CopyBufferToTexture(IBuffer src, ITexture dst, in TextureCopyRegion region)
    {
        var texture = (D3D12Texture)dst;
        var srcBuffer = (D3D12Buffer)src;

        uint bytesPerPixel = 4; // every format the sandbox uploads via this path is 4 bytes/texel today
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(texture.Format, (int)region.Width, (int)region.Height, 1, (int)(region.Width * bytesPerPixel)),
        };

        var srcLocation = new TextureCopyLocation(srcBuffer.Handle, footprint);
        var dstLocation = new TextureCopyLocation(texture.Handle, (int)region.MipLevel + (int)region.ArrayLayer * (int)texture.Descriptor.MipLevels);
        _list.CopyTextureRegion(dstLocation, (int)region.DstX, (int)region.DstY, (int)region.DstZ, srcLocation, null);
    }

    public void TransitionTexture(ITexture texture, ResourceState from, ResourceState to)
    {
        var d3dTexture = (D3D12Texture)texture;
        var fromState = D3D12Utils.ToResourceStates(from);
        var toState = D3D12Utils.ToResourceStates(to);
        if (fromState != toState)
            _list.ResourceBarrierTransition(d3dTexture.Handle, fromState, toState);
        d3dTexture.CurrentStateInternal = to;
    }

    public void TransitionBuffer(IBuffer buffer, ResourceState from, ResourceState to)
    {
        var d3dBuffer = (D3D12Buffer)buffer;
        if (d3dBuffer.Descriptor.Location != MemoryLocation.DeviceLocal)
        {
            // Host-visible buffers stay permanently in GenericRead/CopyDest - see D3D12Buffer/D3D12Utils.
            d3dBuffer.CurrentStateInternal = to;
            return;
        }

        var fromState = D3D12Utils.ToResourceStates(from);
        var toState = D3D12Utils.ToResourceStates(to);
        if (fromState != toState)
            _list.ResourceBarrierTransition(d3dBuffer.Handle, fromState, toState);
        d3dBuffer.CurrentStateInternal = to;
    }

    public void RebuildTopLevelAccelerationStructure(IAccelerationStructure accelerationStructure, in TopLevelAccelerationStructureDescriptor descriptor)
        => ((D3D12AccelerationStructure)accelerationStructure).RecordRebuild(_list, descriptor);

    public void Dispose() => _list.Dispose();
}
