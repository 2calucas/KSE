namespace Engine.RHI;

public enum FilterMode
{
    Nearest,
    Linear,
}

public enum MipmapFilterMode
{
    Nearest,
    Linear,
}

public enum AddressMode
{
    Repeat,
    MirrorRepeat,
    ClampToEdge,
    ClampToBorder,
}

public enum CompareFunction
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always,
}

public readonly record struct SamplerDescriptor(
    FilterMode MinFilter = FilterMode.Linear,
    FilterMode MagFilter = FilterMode.Linear,
    MipmapFilterMode MipmapFilter = MipmapFilterMode.Linear,
    AddressMode AddressModeU = AddressMode.Repeat,
    AddressMode AddressModeV = AddressMode.Repeat,
    AddressMode AddressModeW = AddressMode.Repeat,
    float MaxAnisotropy = 1.0f,
    CompareFunction? CompareFunction = null,
    float MinLod = 0.0f,
    float MaxLod = 1000.0f,
    string? Name = null);

public interface ISampler : IDisposable
{
    SamplerDescriptor Descriptor { get; }
}
