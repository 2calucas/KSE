using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanTexture : ITexture
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkImage _image;
    private readonly VmaAllocation _allocation;
    private readonly bool _ownsImage;

    public TextureDescriptor Descriptor { get; }
    public ResourceState CurrentState { get; private set; } = ResourceState.Undefined;
    internal ResourceState CurrentStateInternal { set => CurrentState = value; }
    internal VkImage Handle => _image;
    internal VkFormat Format { get; }
    internal VkImageAspectFlags AspectFlags { get; }

    /// <summary>Wraps an image the backend does not own (e.g. a swapchain image) — Dispose is a no-op.</summary>
    internal VulkanTexture(VulkanGraphicsDevice device, VkImage image, in TextureDescriptor descriptor)
    {
        _device = device;
        _image = image;
        Descriptor = descriptor;
        Format = VulkanUtils.ToVkFormat(descriptor.Format);
        AspectFlags = ComputeAspectFlags(descriptor.Format);
        _ownsImage = false;
    }

    public VulkanTexture(VulkanGraphicsDevice device, in TextureDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        Format = VulkanUtils.ToVkFormat(descriptor.Format);
        AspectFlags = ComputeAspectFlags(descriptor.Format);
        _ownsImage = true;

        VkImageCreateInfo createInfo = new()
        {
            imageType = descriptor.Dimension switch
            {
                TextureDimension.Texture1D => VkImageType.Image1D,
                TextureDimension.Texture3D => VkImageType.Image3D,
                _ => VkImageType.Image2D,
            },
            format = Format,
            extent = new VkExtent3D(descriptor.Width, descriptor.Height, descriptor.Dimension == TextureDimension.Texture3D ? descriptor.DepthOrArrayLayers : 1u),
            mipLevels = descriptor.MipLevels,
            arrayLayers = descriptor.Dimension == TextureDimension.Texture3D ? 1u : descriptor.DepthOrArrayLayers,
            samples = descriptor.SampleCount switch { 4 => VkSampleCountFlags.Count4, 8 => VkSampleCountFlags.Count8, _ => VkSampleCountFlags.Count1 },
            tiling = VkImageTiling.Optimal,
            usage = VulkanUtils.ToVkImageUsage(descriptor.Usage),
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        if (descriptor.Dimension == TextureDimension.TextureCube)
            createInfo.flags |= VkImageCreateFlags.CubeCompatible;

        VmaAllocationCreateInfo allocationCreateInfo = new() { usage = VmaMemoryUsage.AutoPreferDevice };

        VulkanUtils.CheckResult(
            vmaCreateImage(device.Allocator, &createInfo, &allocationCreateInfo, out _image, out _allocation, out _),
            "vmaCreateImage failed");
    }

    private static VkImageAspectFlags ComputeAspectFlags(TextureFormat format)
    {
        if (!format.IsDepthFormat())
            return VkImageAspectFlags.Color;
        return format.HasStencil() ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil : VkImageAspectFlags.Depth;
    }

    public ITextureView CreateView(in TextureViewDescriptor descriptor) => new VulkanTextureView(_device, this, descriptor);

    public void Dispose()
    {
        if (_ownsImage)
            vmaDestroyImage(_device.Allocator, _image, _allocation);
    }
}

internal sealed unsafe class VulkanTextureView : ITextureView
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkImageView _view;

    public ITexture Texture { get; }
    public TextureViewDescriptor Descriptor { get; }
    internal VkImageView Handle => _view;

    public VulkanTextureView(VulkanGraphicsDevice device, VulkanTexture texture, in TextureViewDescriptor descriptor)
    {
        _device = device;
        Texture = texture;
        Descriptor = descriptor;

        var format = descriptor.FormatOverride.HasValue ? VulkanUtils.ToVkFormat(descriptor.FormatOverride.Value) : texture.Format;

        // A default-initialized TextureViewDescriptor (e.g. `new TextureViewDescriptor()`) zero-inits every
        // field rather than applying the record's positional-parameter defaults, so treat 0 the same way
        // Vulkan's own VK_REMAINING_MIP_LEVELS/VK_REMAINING_ARRAY_LAYERS do: "everything from the base".
        uint mipLevelCount = descriptor.MipLevelCount == 0 ? texture.Descriptor.MipLevels - descriptor.BaseMipLevel : descriptor.MipLevelCount;
        uint arrayLayerCount = descriptor.ArrayLayerCount == 0 ? texture.Descriptor.DepthOrArrayLayers - descriptor.BaseArrayLayer : descriptor.ArrayLayerCount;

        VkImageViewCreateInfo createInfo = new()
        {
            image = texture.Handle,
            viewType = texture.Descriptor.Dimension switch
            {
                TextureDimension.Texture1D => VkImageViewType.Image1D,
                TextureDimension.Texture3D => VkImageViewType.Image3D,
                TextureDimension.TextureCube => VkImageViewType.ImageCube,
                _ => VkImageViewType.Image2D,
            },
            format = format,
            components = VkComponentMapping.Identity,
            subresourceRange = new VkImageSubresourceRange(texture.AspectFlags, descriptor.BaseMipLevel, mipLevelCount, descriptor.BaseArrayLayer, arrayLayerCount),
        };

        VulkanUtils.CheckResult(device.Api.vkCreateImageView(&createInfo, out _view), "vkCreateImageView failed");
    }

    public void Dispose() => _device.Api.vkDestroyImageView(_view);
}
