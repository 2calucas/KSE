using Engine.RHI;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Engine.RHI.Direct3D12;

internal sealed class D3D12ResourceSet : IResourceSet
{
    private readonly D3D12GraphicsDevice _device;
    private readonly int _resourceBase = -1;
    private readonly int _resourceCount;
    private readonly int _samplerBase = -1;
    private readonly int _samplerCount;

    public ResourceSetDescriptor Descriptor { get; }
    internal int ResourceBase => _resourceBase;
    internal int SamplerBase => _samplerBase;

    public D3D12ResourceSet(D3D12GraphicsDevice device, in ResourceSetDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        var layout = (D3D12ResourceSetLayout)descriptor.Layout;

        if (layout.ResourceCount > 0)
        {
            _resourceCount = layout.ResourceCount;
            _resourceBase = device.ResourceHeap.Allocate(_resourceCount);
        }
        if (layout.SamplerCount > 0)
        {
            _samplerCount = layout.SamplerCount;
            _samplerBase = device.SamplerHeap.Allocate(_samplerCount);
        }

        var bindingKinds = new Dictionary<uint, ResourceKind>();
        foreach (var b in layout.Descriptor.Bindings)
            bindingKinds[b.Binding] = b.Kind;

        foreach (var entry in descriptor.Entries)
        {
            var kind = bindingKinds[entry.Binding];
            switch (kind)
            {
                case ResourceKind.UniformBuffer:
                    WriteConstantBufferView(device, layout, entry);
                    break;
                case ResourceKind.SampledTexture:
                    WriteShaderResourceView(device, layout, entry);
                    break;
                case ResourceKind.StorageTexture:
                    WriteUnorderedAccessView(device, layout, entry);
                    break;
                case ResourceKind.Sampler:
                    WriteSampler(device, layout, entry);
                    break;
                case ResourceKind.AccelerationStructure:
                    WriteAccelerationStructureView(device, layout, entry);
                    break;
                default:
                    throw new NotSupportedException($"Resource kind {kind} is not supported by the D3D12 backend yet.");
            }
        }
    }

    private void WriteConstantBufferView(D3D12GraphicsDevice device, D3D12ResourceSetLayout layout, in ResourceSetEntry entry)
    {
        var buffer = (D3D12Buffer)entry.Buffer!;
        ulong offset = entry.BufferOffset;
        ulong range = entry.BufferRange == 0 ? buffer.Descriptor.SizeBytes - offset : entry.BufferRange;
        // D3D12 constant buffer views must be a multiple of 256 bytes.
        uint alignedSize = (uint)((range + 255) & ~255ul);

        var cbvDesc = new ConstantBufferViewDescription
        {
            BufferLocation = buffer.Handle.GPUVirtualAddress + offset,
            SizeInBytes = alignedSize,
        };
        var handle = device.ResourceHeap.GetCpuHandle(_resourceBase + layout.GetResourceOffset(entry.Binding));
        device.Device.CreateConstantBufferView(cbvDesc, handle);
    }

    private void WriteShaderResourceView(D3D12GraphicsDevice device, D3D12ResourceSetLayout layout, in ResourceSetEntry entry)
    {
        var view = (D3D12TextureView)entry.TextureView!;
        var texture = (D3D12Texture)view.Texture;
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = view.Format,
            ViewDimension = ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView
            {
                MostDetailedMip = view.Descriptor.BaseMipLevel,
                MipLevels = view.Descriptor.MipLevelCount == 0 ? uint.MaxValue : view.Descriptor.MipLevelCount,
            },
        };
        var handle = device.ResourceHeap.GetCpuHandle(_resourceBase + layout.GetResourceOffset(entry.Binding));
        device.Device.CreateShaderResourceView(texture.Handle, srvDesc, handle);
    }

    private void WriteUnorderedAccessView(D3D12GraphicsDevice device, D3D12ResourceSetLayout layout, in ResourceSetEntry entry)
    {
        var view = (D3D12TextureView)entry.TextureView!;
        var texture = (D3D12Texture)view.Texture;
        var uavDesc = new UnorderedAccessViewDescription
        {
            Format = view.Format,
            ViewDimension = UnorderedAccessViewDimension.Texture2D,
            Texture2D = new Texture2DUnorderedAccessView { MipSlice = view.Descriptor.BaseMipLevel },
        };
        var handle = device.ResourceHeap.GetCpuHandle(_resourceBase + layout.GetResourceOffset(entry.Binding));
        device.Device.CreateUnorderedAccessView(texture.Handle, null, uavDesc, handle);
    }

    private void WriteSampler(D3D12GraphicsDevice device, D3D12ResourceSetLayout layout, in ResourceSetEntry entry)
    {
        var sampler = (D3D12Sampler)entry.Sampler!;
        var handle = device.SamplerHeap.GetCpuHandle(_samplerBase + layout.GetSamplerOffset(entry.Binding));
        device.Device.CreateSampler(sampler.Description, handle);
    }

    private void WriteAccelerationStructureView(D3D12GraphicsDevice device, D3D12ResourceSetLayout layout, in ResourceSetEntry entry)
    {
        var accel = (D3D12AccelerationStructure)entry.AccelerationStructure!;
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = ShaderResourceViewDimension.RaytracingAccelerationStructure,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            RaytracingAccelerationStructure = new RaytracingAccelerationStructureShaderResourceView { Location = accel.DeviceAddress },
        };
        var handle = device.ResourceHeap.GetCpuHandle(_resourceBase + layout.GetResourceOffset(entry.Binding));
        // Acceleration-structure SRVs are addressed purely by GPU VA — the resource pointer must be null.
        device.Device.CreateShaderResourceView(null, srvDesc, handle);
    }

    public void Dispose()
    {
        if (_resourceBase >= 0) _device.ResourceHeap.Free(_resourceBase, _resourceCount);
        if (_samplerBase >= 0) _device.SamplerHeap.Free(_samplerBase, _samplerCount);
    }
}
