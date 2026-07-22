namespace Engine.RHI;

public enum TextureFormat
{
    Unknown,
    R8Unorm,
    RG8Unorm,
    RGBA8Unorm,
    RGBA8UnormSrgb,
    BGRA8Unorm,
    BGRA8UnormSrgb,
    R16Float,
    RG16Float,
    RGBA16Float,
    R32Float,
    RG32Float,
    RGBA32Float,
    RG16Snorm,
    D32Float,
    D24UnormS8Uint,
    D32FloatS8Uint,
    BC1RGBAUnorm,
    BC3Unorm,
    BC4Unorm,
    BC5Unorm,
    BC7Unorm,
    BC7UnormSrgb,
}

public enum IndexFormat
{
    UInt16,
    UInt32,
}

public static class TextureFormatExtensions
{
    public static bool IsDepthFormat(this TextureFormat format) => format switch
    {
        TextureFormat.D32Float or TextureFormat.D24UnormS8Uint or TextureFormat.D32FloatS8Uint => true,
        _ => false,
    };

    public static bool HasStencil(this TextureFormat format) => format switch
    {
        TextureFormat.D24UnormS8Uint or TextureFormat.D32FloatS8Uint => true,
        _ => false,
    };
}
