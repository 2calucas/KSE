using Engine.RHI;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Engine.RHI.Direct3D12;

internal sealed class D3D12Texture : ITexture
{
    private readonly D3D12GraphicsDevice _device;
    private readonly ID3D12Resource _resource;
    private readonly bool _ownsResource;

    public TextureDescriptor Descriptor { get; }
    public ResourceState CurrentState { get; private set; } = ResourceState.Undefined;
    internal ResourceState CurrentStateInternal { set => CurrentState = value; }
    internal ID3D12Resource Handle => _resource;
    internal Format Format { get; }

    /// <summary>Wraps a resource this backend does not own (a swapchain buffer) — Dispose is a no-op.</summary>
    internal D3D12Texture(D3D12GraphicsDevice device, ID3D12Resource resource, in TextureDescriptor descriptor)
    {
        _device = device;
        _resource = resource;
        Descriptor = descriptor;
        Format = D3D12Utils.ToDxgiFormat(descriptor.Format);
        _ownsResource = false;
        CurrentState = ResourceState.Present;
    }

    public D3D12Texture(D3D12GraphicsDevice device, in TextureDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        Format = D3D12Utils.ToDxgiFormat(descriptor.Format);
        _ownsResource = true;

        var dimension = descriptor.Dimension switch
        {
            TextureDimension.Texture1D => ResourceDimension.Texture1D,
            TextureDimension.Texture3D => ResourceDimension.Texture3D,
            _ => ResourceDimension.Texture2D,
        };

        var resourceDesc = new ResourceDescription
        {
            Dimension = dimension,
            Width = descriptor.Width,
            Height = descriptor.Height,
            DepthOrArraySize = (ushort)(descriptor.Dimension == TextureDimension.Texture3D ? descriptor.DepthOrArrayLayers : descriptor.DepthOrArrayLayers),
            MipLevels = (ushort)descriptor.MipLevels,
            Format = Format,
            SampleDescription = new SampleDescription(descriptor.SampleCount, 0),
            Layout = TextureLayout.Unknown,
            Flags = D3D12Utils.ToResourceFlags(descriptor.Usage),
        };

        var heapProperties = new HeapProperties(HeapType.Default);

        ResourceStates initialState = ResourceStates.Common;
        ClearValue? clearValue = null;
        if ((descriptor.Usage & TextureUsage.DepthStencil) != 0)
        {
            initialState = ResourceStates.DepthWrite;
            clearValue = new ClearValue(Format, 1.0f, 0);
        }
        else if ((descriptor.Usage & TextureUsage.RenderTarget) != 0)
        {
            initialState = ResourceStates.RenderTarget;
            clearValue = new ClearValue(Format, new Vortice.Mathematics.Color4(0, 0, 0, 1));
        }

        _resource = device.Device.CreateCommittedResource(heapProperties, HeapFlags.None, resourceDesc, initialState, clearValue);
        CurrentState = initialState switch
        {
            ResourceStates.DepthWrite => ResourceState.DepthStencilWrite,
            ResourceStates.RenderTarget => ResourceState.RenderTarget,
            _ => ResourceState.Undefined,
        };
    }

    public ITextureView CreateView(in TextureViewDescriptor descriptor) => new D3D12TextureView(_device, this, descriptor);

    public void Dispose()
    {
        if (_ownsResource)
            _resource.Dispose();
    }
}

internal sealed class D3D12TextureView : ITextureView
{
    private readonly D3D12GraphicsDevice _device;
    private int _rtvOffset = -1;
    private int _dsvOffset = -1;

    public ITexture Texture { get; }
    public TextureViewDescriptor Descriptor { get; }
    internal Format Format { get; }
    internal CpuDescriptorHandle? Rtv { get; private set; }
    internal CpuDescriptorHandle? Dsv { get; private set; }

    public D3D12TextureView(D3D12GraphicsDevice device, D3D12Texture texture, in TextureViewDescriptor descriptor)
    {
        _device = device;
        Texture = texture;
        Descriptor = descriptor;
        Format = descriptor.FormatOverride.HasValue ? D3D12Utils.ToDxgiFormat(descriptor.FormatOverride.Value) : texture.Format;

        var usage = texture.Descriptor.Usage;
        if ((usage & TextureUsage.RenderTarget) != 0)
        {
            _rtvOffset = device.RtvHeap.Allocate(1);
            var handle = device.RtvHeap.GetCpuHandle(_rtvOffset);
            var rtvDesc = new RenderTargetViewDescription
            {
                Format = Format,
                ViewDimension = RenderTargetViewDimension.Texture2D,
                Texture2D = new Texture2DRenderTargetView { MipSlice = descriptor.BaseMipLevel },
            };
            device.Device.CreateRenderTargetView(texture.Handle, rtvDesc, handle);
            Rtv = handle;
        }
        if ((usage & TextureUsage.DepthStencil) != 0)
        {
            _dsvOffset = device.DsvHeap.Allocate(1);
            var handle = device.DsvHeap.GetCpuHandle(_dsvOffset);
            var dsvDesc = new DepthStencilViewDescription
            {
                Format = Format,
                ViewDimension = DepthStencilViewDimension.Texture2D,
                Texture2D = new Texture2DDepthStencilView { MipSlice = descriptor.BaseMipLevel },
            };
            device.Device.CreateDepthStencilView(texture.Handle, dsvDesc, handle);
            Dsv = handle;
        }
    }

    public void Dispose()
    {
        if (_rtvOffset >= 0) _device.RtvHeap.Free(_rtvOffset, 1);
        if (_dsvOffset >= 0) _device.DsvHeap.Free(_dsvOffset, 1);
    }
}
