namespace Engine.RHI;

public enum PresentMode
{
    /// <summary>Vsync'd, no tearing (FIFO).</summary>
    Fifo,
    /// <summary>Vsync'd, but a newer frame may replace a queued one (mailbox) — lower latency than Fifo.</summary>
    Mailbox,
    /// <summary>No vsync, may tear (immediate).</summary>
    Immediate,
    /// <summary>Adaptive vsync: tears only when the frame would otherwise miss the vblank.</summary>
    FifoRelaxed,
}

public readonly record struct SwapChainDescriptor(
    nint WindowHandle,
    uint Width,
    uint Height,
    TextureFormat Format = TextureFormat.BGRA8Unorm,
    PresentMode PresentMode = PresentMode.Fifo,
    uint BufferCount = 2,
    string? Name = null);

public interface ISwapChain : IDisposable
{
    SwapChainDescriptor Descriptor { get; }

    /// <summary>Acquires the next back buffer to render into; blocks/waits as required by the backend.</summary>
    ITexture AcquireNextTexture();
    void Present();
    void Resize(uint width, uint height);
}
