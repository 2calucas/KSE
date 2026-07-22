using Engine.RHI;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// Same Milestone-1 simplification the Vulkan backend documents for itself: no semaphore-based frame pacing,
/// <see cref="Present"/> waits for the whole device to go idle before presenting. Trades cross-frame CPU/GPU
/// overlap for correctness; revisit once multiple-frames-in-flight matters.
/// </summary>
internal sealed class D3D12SwapChain : ISwapChain
{
    private readonly D3D12GraphicsDevice _device;
    private IDXGISwapChain3 _swapChain;
    private D3D12Texture[] _textures = [];
    private Format _format;

    public SwapChainDescriptor Descriptor { get; private set; }

    public D3D12SwapChain(D3D12GraphicsDevice device, in SwapChainDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        _swapChain = CreateSwapChain(descriptor.Width, descriptor.Height);
    }

    private IDXGISwapChain3 CreateSwapChain(uint width, uint height)
    {
        _format = D3D12Utils.ToDxgiFormat(Descriptor.Format);

        var swapChainDesc = new SwapChainDescription1
        {
            Width = (int)width,
            Height = (int)height,
            Format = _format,
            BufferCount = (int)Descriptor.BufferCount,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
        };

        using var swapChain1 = _device.Factory.CreateSwapChainForHwnd(_device.GraphicsQueue.Handle, Descriptor.WindowHandle, swapChainDesc);
        var swapChain3 = swapChain1.QueryInterface<IDXGISwapChain3>();
        WrapBackBuffers(swapChain3, width, height);
        return swapChain3;
    }

    private void WrapBackBuffers(IDXGISwapChain3 swapChain, uint width, uint height)
    {
        var textureDescriptor = new TextureDescriptor(width, height, Descriptor.Format, TextureUsage.RenderTarget | TextureUsage.CopyDestination, Name: "SwapchainImage");
        _textures = new D3D12Texture[Descriptor.BufferCount];
        for (int i = 0; i < _textures.Length; i++)
        {
            var resource = swapChain.GetBuffer<ID3D12Resource>(i);
            _textures[i] = new D3D12Texture(_device, resource, textureDescriptor);
        }
    }

    public ITexture AcquireNextTexture() => _textures[_swapChain.CurrentBackBufferIndex];

    public void Present()
    {
        _device.WaitIdle();
        _swapChain.Present(Descriptor.PresentMode == PresentMode.Immediate ? 0 : 1);
    }

    public void Resize(uint width, uint height)
    {
        _device.WaitIdle();
        foreach (var texture in _textures) texture.Dispose();
        _textures = [];

        _swapChain.ResizeBuffers((int)Descriptor.BufferCount, (int)width, (int)height, _format, SwapChainFlags.None);
        WrapBackBuffers(_swapChain, width, height);
        Descriptor = Descriptor with { Width = width, Height = height };
    }

    public void Dispose()
    {
        _device.WaitIdle();
        foreach (var texture in _textures) texture.Dispose();
        _swapChain.Dispose();
    }
}
