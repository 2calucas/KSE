using Vortice.Vulkan;
using Engine.RHI;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanResourceSetLayout : IResourceSetLayout
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkDescriptorSetLayout _layout;

    public ResourceSetLayoutDescriptor Descriptor { get; }
    internal VkDescriptorSetLayout Handle => _layout;

    public VulkanResourceSetLayout(VulkanGraphicsDevice device, in ResourceSetLayoutDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;

        var bindings = new VkDescriptorSetLayoutBinding[descriptor.Bindings.Count];
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = descriptor.Bindings[i];
            bindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = b.Binding,
                descriptorType = ToVkDescriptorType(b.Kind),
                descriptorCount = b.ArrayCount,
                stageFlags = VulkanShaderModule.ToVkShaderStageFlags(b.Stages),
            };
        }

        fixed (VkDescriptorSetLayoutBinding* pBindings = bindings)
        {
            VkDescriptorSetLayoutCreateInfo createInfo = new()
            {
                bindingCount = (uint)bindings.Length,
                pBindings = pBindings,
            };
            VulkanUtils.CheckResult(device.Api.vkCreateDescriptorSetLayout(&createInfo, out _layout), "vkCreateDescriptorSetLayout failed");
        }
    }

    internal static VkDescriptorType ToVkDescriptorType(ResourceKind kind) => kind switch
    {
        ResourceKind.UniformBuffer => VkDescriptorType.UniformBuffer,
        ResourceKind.StorageBuffer => VkDescriptorType.StorageBuffer,
        ResourceKind.SampledTexture => VkDescriptorType.SampledImage,
        ResourceKind.StorageTexture => VkDescriptorType.StorageImage,
        ResourceKind.Sampler => VkDescriptorType.Sampler,
        ResourceKind.CombinedTextureSampler => VkDescriptorType.CombinedImageSampler,
        ResourceKind.AccelerationStructure => VkDescriptorType.AccelerationStructureKHR,
        _ => throw new NotSupportedException(),
    };

    public void Dispose() => _device.Api.vkDestroyDescriptorSetLayout(_layout);
}
