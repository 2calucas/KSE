using Engine.RHI;
using Vortice.DXGI;
using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

internal static class D3D12Utils
{
    public static void CheckResult(Vortice.Direct3D12.ResultCode code, string message)
    {
        if (code.Failure)
            throw new InvalidOperationException($"{message} (HRESULT {code.Code:X8})");
    }

    public static Format ToDxgiFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => Format.R8_UNorm,
        TextureFormat.RG8Unorm => Format.R8G8_UNorm,
        TextureFormat.RGBA8Unorm => Format.R8G8B8A8_UNorm,
        TextureFormat.RGBA8UnormSrgb => Format.R8G8B8A8_UNorm_SRgb,
        TextureFormat.BGRA8Unorm => Format.B8G8R8A8_UNorm,
        TextureFormat.BGRA8UnormSrgb => Format.B8G8R8A8_UNorm_SRgb,
        TextureFormat.R16Float => Format.R16_Float,
        TextureFormat.RG16Float => Format.R16G16_Float,
        TextureFormat.RGBA16Float => Format.R16G16B16A16_Float,
        TextureFormat.R32Float => Format.R32_Float,
        TextureFormat.RG32Float => Format.R32G32_Float,
        TextureFormat.RGBA32Float => Format.R32G32B32A32_Float,
        TextureFormat.RG16Snorm => Format.R16G16_SNorm,
        TextureFormat.D32Float => Format.D32_Float,
        TextureFormat.D24UnormS8Uint => Format.D24_UNorm_S8_UInt,
        TextureFormat.D32FloatS8Uint => Format.D32_Float_S8X24_UInt,
        TextureFormat.BC1RGBAUnorm => Format.BC1_UNorm,
        TextureFormat.BC3Unorm => Format.BC3_UNorm,
        TextureFormat.BC4Unorm => Format.BC4_UNorm,
        TextureFormat.BC5Unorm => Format.BC5_UNorm,
        TextureFormat.BC7Unorm => Format.BC7_UNorm,
        TextureFormat.BC7UnormSrgb => Format.BC7_UNorm_SRgb,
        _ => Format.Unknown,
    };

    public static TextureFormat FromDxgiFormat(Format format) => format switch
    {
        Format.R8_UNorm => TextureFormat.R8Unorm,
        Format.R8G8_UNorm => TextureFormat.RG8Unorm,
        Format.R8G8B8A8_UNorm => TextureFormat.RGBA8Unorm,
        Format.R8G8B8A8_UNorm_SRgb => TextureFormat.RGBA8UnormSrgb,
        Format.B8G8R8A8_UNorm => TextureFormat.BGRA8Unorm,
        Format.B8G8R8A8_UNorm_SRgb => TextureFormat.BGRA8UnormSrgb,
        Format.R16_Float => TextureFormat.R16Float,
        Format.R16G16_Float => TextureFormat.RG16Float,
        Format.R16G16B16A16_Float => TextureFormat.RGBA16Float,
        Format.R32_Float => TextureFormat.R32Float,
        Format.R32G32_Float => TextureFormat.RG32Float,
        Format.R32G32B32A32_Float => TextureFormat.RGBA32Float,
        Format.R16G16_SNorm => TextureFormat.RG16Snorm,
        Format.D32_Float => TextureFormat.D32Float,
        Format.D24_UNorm_S8_UInt => TextureFormat.D24UnormS8Uint,
        Format.D32_Float_S8X24_UInt => TextureFormat.D32FloatS8Uint,
        Format.BC1_UNorm => TextureFormat.BC1RGBAUnorm,
        Format.BC3_UNorm => TextureFormat.BC3Unorm,
        Format.BC4_UNorm => TextureFormat.BC4Unorm,
        Format.BC5_UNorm => TextureFormat.BC5Unorm,
        Format.BC7_UNorm => TextureFormat.BC7Unorm,
        Format.BC7_UNorm_SRgb => TextureFormat.BC7UnormSrgb,
        _ => TextureFormat.Unknown,
    };

    /// <summary>Acceleration-structure storage resources use their own dedicated state which is never produced
    /// by this translation (see the comment on <see cref="ResourceState"/> usage in D3D12AccelerationStructure).</summary>
    public static ResourceStates ToResourceStates(ResourceState state) => state switch
    {
        ResourceState.Undefined => ResourceStates.Common,
        ResourceState.General => ResourceStates.UnorderedAccess,
        ResourceState.RenderTarget => ResourceStates.RenderTarget,
        ResourceState.DepthStencilWrite => ResourceStates.DepthWrite,
        ResourceState.DepthStencilRead => ResourceStates.DepthRead,
        ResourceState.ShaderResource => ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource,
        ResourceState.UnorderedAccess => ResourceStates.UnorderedAccess,
        ResourceState.CopySource => ResourceStates.CopySource,
        ResourceState.CopyDestination => ResourceStates.CopyDest,
        ResourceState.Present => ResourceStates.Present,
        _ => ResourceStates.Common,
    };

    public static HeapType ToHeapType(MemoryLocation location) => location switch
    {
        MemoryLocation.HostUpload => HeapType.Upload,
        MemoryLocation.HostReadback => HeapType.Readback,
        _ => HeapType.Default,
    };

    /// <summary>The permanent resource state a buffer created in the given heap type must stay in — D3D12
    /// requires Upload heaps to stay GenericRead and Readback heaps to stay CopyDest for their entire lifetime.</summary>
    public static ResourceStates InitialBufferState(MemoryLocation location) => location switch
    {
        MemoryLocation.HostUpload => ResourceStates.GenericRead,
        MemoryLocation.HostReadback => ResourceStates.CopyDest,
        _ => ResourceStates.Common,
    };

    public static ResourceFlags ToResourceFlags(BufferUsage usage)
    {
        var flags = ResourceFlags.None;
        if ((usage & BufferUsage.StorageBuffer) != 0) flags |= ResourceFlags.AllowUnorderedAccess;
        return flags;
    }

    public static ResourceFlags ToResourceFlags(TextureUsage usage)
    {
        var flags = ResourceFlags.None;
        if ((usage & TextureUsage.RenderTarget) != 0) flags |= ResourceFlags.AllowRenderTarget;
        if ((usage & TextureUsage.DepthStencil) != 0) flags |= ResourceFlags.AllowDepthStencil;
        if ((usage & TextureUsage.Storage) != 0) flags |= ResourceFlags.AllowUnorderedAccess;
        if ((usage & TextureUsage.DepthStencil) != 0 && (usage & TextureUsage.Sampled) == 0)
            flags |= ResourceFlags.DenyShaderResource;
        return flags;
    }

    public static Filter ToFilter(FilterMode min, FilterMode mag, MipmapFilterMode mip, bool anisotropic, bool comparison)
    {
        if (anisotropic)
            return comparison ? Filter.ComparisonAnisotropic : Filter.Anisotropic;

        int minBit = min == FilterMode.Linear ? 1 : 0;
        int magBit = mag == FilterMode.Linear ? 1 : 0;
        int mipBit = mip == MipmapFilterMode.Linear ? 1 : 0;
        int encoded = (minBit << 4) | (magBit << 2) | mipBit;
        Filter filter = encoded switch
        {
            0b000000 => Filter.MinMagMipPoint,
            0b000001 => Filter.MinMagPointMipLinear,
            0b000100 => Filter.MinPointMagLinearMipPoint,
            0b000101 => Filter.MinPointMagMipLinear,
            0b010000 => Filter.MinLinearMagMipPoint,
            0b010001 => Filter.MinLinearMagPointMipLinear,
            0b010100 => Filter.MinMagLinearMipPoint,
            0b010101 => Filter.MinMagMipLinear,
            _ => Filter.MinMagMipLinear,
        };
        if (comparison)
            filter = (Filter)((int)filter | 0x80);
        return filter;
    }

    public static TextureAddressMode ToAddressMode(AddressMode mode) => mode switch
    {
        AddressMode.Repeat => TextureAddressMode.Wrap,
        AddressMode.MirrorRepeat => TextureAddressMode.Mirror,
        AddressMode.ClampToEdge => TextureAddressMode.Clamp,
        AddressMode.ClampToBorder => TextureAddressMode.Border,
        _ => TextureAddressMode.Wrap,
    };

    public static ComparisonFunction ToComparisonFunction(CompareFunction fn) => fn switch
    {
        CompareFunction.Never => ComparisonFunction.Never,
        CompareFunction.Less => ComparisonFunction.Less,
        CompareFunction.Equal => ComparisonFunction.Equal,
        CompareFunction.LessEqual => ComparisonFunction.LessEqual,
        CompareFunction.Greater => ComparisonFunction.Greater,
        CompareFunction.NotEqual => ComparisonFunction.NotEqual,
        CompareFunction.GreaterEqual => ComparisonFunction.GreaterEqual,
        CompareFunction.Always => ComparisonFunction.Always,
        _ => ComparisonFunction.Never,
    };
}
