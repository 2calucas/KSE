using Vortice.Vulkan;
using Engine.RHI;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanSampler : ISampler
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkSampler _sampler;

    public SamplerDescriptor Descriptor { get; }
    internal VkSampler Handle => _sampler;

    public VulkanSampler(VulkanGraphicsDevice device, in SamplerDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;

        VkSamplerCreateInfo createInfo = new()
        {
            magFilter = descriptor.MagFilter == FilterMode.Linear ? VkFilter.Linear : VkFilter.Nearest,
            minFilter = descriptor.MinFilter == FilterMode.Linear ? VkFilter.Linear : VkFilter.Nearest,
            mipmapMode = descriptor.MipmapFilter == MipmapFilterMode.Linear ? VkSamplerMipmapMode.Linear : VkSamplerMipmapMode.Nearest,
            addressModeU = ToVkAddressMode(descriptor.AddressModeU),
            addressModeV = ToVkAddressMode(descriptor.AddressModeV),
            addressModeW = ToVkAddressMode(descriptor.AddressModeW),
            anisotropyEnable = descriptor.MaxAnisotropy > 1.0f,
            maxAnisotropy = descriptor.MaxAnisotropy,
            compareEnable = descriptor.CompareFunction.HasValue,
            compareOp = descriptor.CompareFunction.HasValue ? ToVkCompareOp(descriptor.CompareFunction.Value) : VkCompareOp.Never,
            minLod = descriptor.MinLod,
            maxLod = descriptor.MaxLod,
            borderColor = VkBorderColor.FloatTransparentBlack,
        };

        VulkanUtils.CheckResult(device.Api.vkCreateSampler(&createInfo, out _sampler), "vkCreateSampler failed");
    }

    private static VkSamplerAddressMode ToVkAddressMode(AddressMode mode) => mode switch
    {
        AddressMode.Repeat => VkSamplerAddressMode.Repeat,
        AddressMode.MirrorRepeat => VkSamplerAddressMode.MirroredRepeat,
        AddressMode.ClampToEdge => VkSamplerAddressMode.ClampToEdge,
        AddressMode.ClampToBorder => VkSamplerAddressMode.ClampToBorder,
        _ => throw new NotSupportedException(),
    };

    private static VkCompareOp ToVkCompareOp(CompareFunction fn) => fn switch
    {
        CompareFunction.Never => VkCompareOp.Never,
        CompareFunction.Less => VkCompareOp.Less,
        CompareFunction.Equal => VkCompareOp.Equal,
        CompareFunction.LessEqual => VkCompareOp.LessOrEqual,
        CompareFunction.Greater => VkCompareOp.Greater,
        CompareFunction.NotEqual => VkCompareOp.NotEqual,
        CompareFunction.GreaterEqual => VkCompareOp.GreaterOrEqual,
        CompareFunction.Always => VkCompareOp.Always,
        _ => throw new NotSupportedException(),
    };

    public void Dispose() => _device.Api.vkDestroySampler(_sampler);
}
