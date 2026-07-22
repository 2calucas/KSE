namespace Engine.RHI;

public enum ResourceState
{
    Undefined,
    General,
    RenderTarget,
    DepthStencilWrite,
    DepthStencilRead,
    ShaderResource,
    UnorderedAccess,
    CopySource,
    CopyDestination,
    Present,
}

public enum TextureDimension
{
    Texture1D,
    Texture2D,
    Texture3D,
    TextureCube,
}

[Flags]
public enum TextureUsage
{
    None = 0,
    Sampled = 1 << 0,
    Storage = 1 << 1,
    RenderTarget = 1 << 2,
    DepthStencil = 1 << 3,
    CopySource = 1 << 4,
    CopyDestination = 1 << 5,
}

public readonly record struct TextureDescriptor(
    uint Width,
    uint Height,
    TextureFormat Format,
    TextureUsage Usage,
    TextureDimension Dimension = TextureDimension.Texture2D,
    uint DepthOrArrayLayers = 1,
    uint MipLevels = 1,
    uint SampleCount = 1,
    string? Name = null);

public readonly record struct TextureViewDescriptor(
    uint BaseMipLevel = 0,
    uint MipLevelCount = 1,
    uint BaseArrayLayer = 0,
    uint ArrayLayerCount = 1,
    TextureFormat? FormatOverride = null);

public readonly record struct TextureCopyRegion(
    uint SrcX, uint SrcY, uint SrcZ,
    uint DstX, uint DstY, uint DstZ,
    uint Width, uint Height, uint Depth,
    uint MipLevel = 0,
    uint ArrayLayer = 0);

public interface ITextureView : IDisposable
{
    ITexture Texture { get; }
    TextureViewDescriptor Descriptor { get; }
}

public interface ITexture : IDisposable
{
    TextureDescriptor Descriptor { get; }

    /// <summary>
    /// The resource's last-known barrier state, as tracked by whichever <see cref="ICommandBuffer"/>
    /// most recently transitioned it. FidelityFX and other backends that require the "current state"
    /// of a resource read this instead of re-deriving it.
    /// </summary>
    ResourceState CurrentState { get; }

    ITextureView CreateView(in TextureViewDescriptor descriptor);
}
