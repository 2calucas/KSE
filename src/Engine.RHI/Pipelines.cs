namespace Engine.RHI;

public enum PrimitiveTopology
{
    TriangleList,
    TriangleStrip,
    LineList,
    LineStrip,
    PointList,
}

public enum CullMode
{
    None,
    Front,
    Back,
}

public enum FrontFace
{
    CounterClockwise,
    Clockwise,
}

public enum BlendFactor
{
    Zero,
    One,
    SrcAlpha,
    OneMinusSrcAlpha,
    DstAlpha,
    OneMinusDstAlpha,
    SrcColor,
    OneMinusSrcColor,
    DstColor,
    OneMinusDstColor,
}

public enum BlendOperation
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

public readonly record struct BlendState(
    bool Enabled = false,
    BlendFactor SrcColorFactor = BlendFactor.One,
    BlendFactor DstColorFactor = BlendFactor.Zero,
    BlendOperation ColorOp = BlendOperation.Add,
    BlendFactor SrcAlphaFactor = BlendFactor.One,
    BlendFactor DstAlphaFactor = BlendFactor.Zero,
    BlendOperation AlphaOp = BlendOperation.Add);

public readonly record struct RasterizerState(
    CullMode CullMode = CullMode.Back,
    FrontFace FrontFace = FrontFace.CounterClockwise,
    bool Wireframe = false,
    bool DepthClampEnable = false);

public readonly record struct DepthStencilState(
    bool DepthTestEnable = true,
    bool DepthWriteEnable = true,
    CompareFunction DepthCompare = CompareFunction.Less);

public readonly record struct VertexBufferLayout(
    uint Stride,
    IReadOnlyList<VertexInputAttribute> Attributes);

public readonly record struct GraphicsPipelineDescriptor(
    IShaderModule VertexShader,
    IShaderModule FragmentShader,
    IReadOnlyList<IResourceSetLayout> ResourceSetLayouts,
    IReadOnlyList<VertexBufferLayout> VertexBuffers,
    IReadOnlyList<TextureFormat> ColorTargetFormats,
    TextureFormat? DepthStencilFormat = null,
    PrimitiveTopology Topology = PrimitiveTopology.TriangleList,
    RasterizerState Rasterizer = default,
    DepthStencilState DepthStencil = default,
    BlendState Blend = default,
    string? Name = null);

public readonly record struct ComputePipelineDescriptor(
    IShaderModule ComputeShader,
    IReadOnlyList<IResourceSetLayout> ResourceSetLayouts,
    string? Name = null);

public interface IPipeline : IDisposable
{
    bool IsCompute { get; }
}

public readonly record struct ResourceSetLayoutBinding(
    uint Binding,
    ResourceKind Kind,
    ShaderStageFlags Stages,
    uint ArrayCount = 1);

public readonly record struct ResourceSetLayoutDescriptor(
    IReadOnlyList<ResourceSetLayoutBinding> Bindings,
    string? Name = null);

public interface IResourceSetLayout : IDisposable
{
    ResourceSetLayoutDescriptor Descriptor { get; }
}

public readonly record struct ResourceSetEntry(
    uint Binding,
    IBuffer? Buffer = null,
    ITextureView? TextureView = null,
    ISampler? Sampler = null,
    IAccelerationStructure? AccelerationStructure = null,
    ulong BufferOffset = 0,
    ulong BufferRange = 0);

public readonly record struct ResourceSetDescriptor(
    IResourceSetLayout Layout,
    IReadOnlyList<ResourceSetEntry> Entries,
    string? Name = null);

public interface IResourceSet : IDisposable
{
    ResourceSetDescriptor Descriptor { get; }
}
