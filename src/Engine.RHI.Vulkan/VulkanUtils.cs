using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Engine.RHI;

namespace Engine.RHI.Vulkan;

/// <summary>Null-terminated UTF-8 string backed by unmanaged memory, for Vulkan `byte*` string parameters.</summary>
internal readonly unsafe struct Utf8Z : IDisposable
{
    private readonly nint _pointer;
    public Utf8Z(string value) => _pointer = Marshal.StringToCoTaskMemUTF8(value);
    public static implicit operator byte*(Utf8Z z) => (byte*)z._pointer;
    public void Dispose() => Marshal.FreeCoTaskMem(_pointer);
}

internal static class VulkanUtils
{
    public static void CheckResult(VkResult result, string message)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException($"{message}: {result}");
    }

    public static VkFormat ToVkFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => VkFormat.R8Unorm,
        TextureFormat.RG8Unorm => VkFormat.R8G8Unorm,
        TextureFormat.RGBA8Unorm => VkFormat.R8G8B8A8Unorm,
        TextureFormat.RGBA8UnormSrgb => VkFormat.R8G8B8A8Srgb,
        TextureFormat.BGRA8Unorm => VkFormat.B8G8R8A8Unorm,
        TextureFormat.BGRA8UnormSrgb => VkFormat.B8G8R8A8Srgb,
        TextureFormat.R16Float => VkFormat.R16Sfloat,
        TextureFormat.RG16Float => VkFormat.R16G16Sfloat,
        TextureFormat.RGBA16Float => VkFormat.R16G16B16A16Sfloat,
        TextureFormat.R32Float => VkFormat.R32Sfloat,
        TextureFormat.RG32Float => VkFormat.R32G32Sfloat,
        TextureFormat.RGBA32Float => VkFormat.R32G32B32A32Sfloat,
        TextureFormat.RG16Snorm => VkFormat.R16G16Snorm,
        TextureFormat.D32Float => VkFormat.D32Sfloat,
        TextureFormat.D24UnormS8Uint => VkFormat.D24UnormS8Uint,
        TextureFormat.D32FloatS8Uint => VkFormat.D32SfloatS8Uint,
        TextureFormat.BC1RGBAUnorm => VkFormat.Bc1RgbaUnormBlock,
        TextureFormat.BC3Unorm => VkFormat.Bc3UnormBlock,
        TextureFormat.BC4Unorm => VkFormat.Bc4UnormBlock,
        TextureFormat.BC5Unorm => VkFormat.Bc5UnormBlock,
        TextureFormat.BC7Unorm => VkFormat.Bc7UnormBlock,
        TextureFormat.BC7UnormSrgb => VkFormat.Bc7SrgbBlock,
        _ => throw new NotSupportedException($"Unsupported texture format: {format}"),
    };

    public static TextureFormat FromVkFormat(VkFormat format) => format switch
    {
        VkFormat.R8Unorm => TextureFormat.R8Unorm,
        VkFormat.R8G8Unorm => TextureFormat.RG8Unorm,
        VkFormat.R8G8B8A8Unorm => TextureFormat.RGBA8Unorm,
        VkFormat.R8G8B8A8Srgb => TextureFormat.RGBA8UnormSrgb,
        VkFormat.B8G8R8A8Unorm => TextureFormat.BGRA8Unorm,
        VkFormat.B8G8R8A8Srgb => TextureFormat.BGRA8UnormSrgb,
        VkFormat.R16Sfloat => TextureFormat.R16Float,
        VkFormat.R16G16Sfloat => TextureFormat.RG16Float,
        VkFormat.R16G16B16A16Sfloat => TextureFormat.RGBA16Float,
        VkFormat.R32Sfloat => TextureFormat.R32Float,
        VkFormat.R32G32Sfloat => TextureFormat.RG32Float,
        VkFormat.R32G32B32A32Sfloat => TextureFormat.RGBA32Float,
        VkFormat.D32Sfloat => TextureFormat.D32Float,
        VkFormat.D24UnormS8Uint => TextureFormat.D24UnormS8Uint,
        VkFormat.D32SfloatS8Uint => TextureFormat.D32FloatS8Uint,
        _ => TextureFormat.Unknown,
    };

    public static VkImageUsageFlags ToVkImageUsage(TextureUsage usage)
    {
        var result = VkImageUsageFlags.None;
        if ((usage & TextureUsage.Sampled) != 0) result |= VkImageUsageFlags.Sampled;
        if ((usage & TextureUsage.Storage) != 0) result |= VkImageUsageFlags.Storage;
        if ((usage & TextureUsage.RenderTarget) != 0) result |= VkImageUsageFlags.ColorAttachment;
        if ((usage & TextureUsage.DepthStencil) != 0) result |= VkImageUsageFlags.DepthStencilAttachment;
        if ((usage & TextureUsage.CopySource) != 0) result |= VkImageUsageFlags.TransferSrc;
        if ((usage & TextureUsage.CopyDestination) != 0) result |= VkImageUsageFlags.TransferDst;
        return result;
    }

    public static VkBufferUsageFlags ToVkBufferUsage(BufferUsage usage)
    {
        var result = VkBufferUsageFlags.None;
        if ((usage & BufferUsage.VertexBuffer) != 0) result |= VkBufferUsageFlags.VertexBuffer;
        if ((usage & BufferUsage.IndexBuffer) != 0) result |= VkBufferUsageFlags.IndexBuffer;
        if ((usage & BufferUsage.UniformBuffer) != 0) result |= VkBufferUsageFlags.UniformBuffer;
        if ((usage & BufferUsage.StorageBuffer) != 0) result |= VkBufferUsageFlags.StorageBuffer;
        if ((usage & BufferUsage.IndirectBuffer) != 0) result |= VkBufferUsageFlags.IndirectBuffer;
        if ((usage & BufferUsage.CopySource) != 0) result |= VkBufferUsageFlags.TransferSrc;
        if ((usage & BufferUsage.CopyDestination) != 0) result |= VkBufferUsageFlags.TransferDst;
        if ((usage & BufferUsage.AccelerationStructureInput) != 0)
            result |= VkBufferUsageFlags.AccelerationStructureBuildInputReadOnlyKHR | VkBufferUsageFlags.ShaderDeviceAddress;
        return result;
    }

    public readonly record struct BarrierPoint(VkImageLayout Layout, VkAccessFlags2 Access, VkPipelineStageFlags2 Stage);

    public static BarrierPoint ToBarrierPoint(ResourceState state) => state switch
    {
        ResourceState.Undefined => new(VkImageLayout.Undefined, VkAccessFlags2.None, VkPipelineStageFlags2.TopOfPipe),
        ResourceState.General => new(VkImageLayout.General, VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite, VkPipelineStageFlags2.ComputeShader),
        ResourceState.RenderTarget => new(VkImageLayout.ColorAttachmentOptimal, VkAccessFlags2.ColorAttachmentWrite, VkPipelineStageFlags2.ColorAttachmentOutput),
        ResourceState.DepthStencilWrite => new(VkImageLayout.DepthStencilAttachmentOptimal, VkAccessFlags2.DepthStencilAttachmentWrite | VkAccessFlags2.DepthStencilAttachmentRead, VkPipelineStageFlags2.EarlyFragmentTests | VkPipelineStageFlags2.LateFragmentTests),
        ResourceState.DepthStencilRead => new(VkImageLayout.DepthStencilReadOnlyOptimal, VkAccessFlags2.DepthStencilAttachmentRead, VkPipelineStageFlags2.EarlyFragmentTests | VkPipelineStageFlags2.LateFragmentTests),
        ResourceState.ShaderResource => new(VkImageLayout.ShaderReadOnlyOptimal, VkAccessFlags2.ShaderRead, VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader | VkPipelineStageFlags2.VertexShader),
        ResourceState.UnorderedAccess => new(VkImageLayout.General, VkAccessFlags2.ShaderStorageRead | VkAccessFlags2.ShaderStorageWrite, VkPipelineStageFlags2.ComputeShader),
        ResourceState.CopySource => new(VkImageLayout.TransferSrcOptimal, VkAccessFlags2.TransferRead, VkPipelineStageFlags2.Transfer),
        ResourceState.CopyDestination => new(VkImageLayout.TransferDstOptimal, VkAccessFlags2.TransferWrite, VkPipelineStageFlags2.Transfer),
        ResourceState.Present => new(VkImageLayout.PresentSrcKHR, VkAccessFlags2.None, VkPipelineStageFlags2.BottomOfPipe),
        _ => throw new NotSupportedException($"Unsupported resource state: {state}"),
    };
}
