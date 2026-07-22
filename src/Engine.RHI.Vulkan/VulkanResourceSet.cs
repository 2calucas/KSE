using Vortice.Vulkan;
using Engine.RHI;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanResourceSet : IResourceSet
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkDescriptorSet _set;

    public ResourceSetDescriptor Descriptor { get; }
    internal VkDescriptorSet Handle => _set;

    public VulkanResourceSet(VulkanGraphicsDevice device, in ResourceSetDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;

        VkDescriptorSetLayout layout = ((VulkanResourceSetLayout)descriptor.Layout).Handle;
        VkDescriptorSetAllocateInfo allocateInfo = new()
        {
            descriptorPool = device.DescriptorPool,
            descriptorSetCount = 1,
            pSetLayouts = &layout,
        };
        VkDescriptorSet set;
        VulkanUtils.CheckResult(device.Api.vkAllocateDescriptorSets(&allocateInfo, &set), "vkAllocateDescriptorSets failed");
        _set = set;

        var layoutBindingKinds = new Dictionary<uint, ResourceKind>();
        foreach (var b in ((VulkanResourceSetLayout)descriptor.Layout).Descriptor.Bindings)
            layoutBindingKinds[b.Binding] = b.Kind;

        var writes = new VkWriteDescriptorSet[descriptor.Entries.Count];
        var bufferInfos = new VkDescriptorBufferInfo[descriptor.Entries.Count];
        var imageInfos = new VkDescriptorImageInfo[descriptor.Entries.Count];
        var accelStructureHandles = new VkAccelerationStructureKHR[descriptor.Entries.Count];
        var accelStructureInfos = new VkWriteDescriptorSetAccelerationStructureKHR[descriptor.Entries.Count];

        for (int i = 0; i < descriptor.Entries.Count; i++)
        {
            var entry = descriptor.Entries[i];
            var kind = layoutBindingKinds[entry.Binding];
            var descriptorType = VulkanResourceSetLayout.ToVkDescriptorType(kind);

            writes[i] = new VkWriteDescriptorSet
            {
                dstSet = _set,
                dstBinding = entry.Binding,
                dstArrayElement = 0,
                descriptorCount = 1,
                descriptorType = descriptorType,
            };

            if (kind is ResourceKind.UniformBuffer or ResourceKind.StorageBuffer)
            {
                var buffer = (VulkanBuffer)entry.Buffer!;
                bufferInfos[i] = new VkDescriptorBufferInfo
                {
                    buffer = buffer.Handle,
                    offset = entry.BufferOffset,
                    range = entry.BufferRange == 0 ? buffer.Descriptor.SizeBytes - entry.BufferOffset : entry.BufferRange,
                };
            }
            else if (kind is ResourceKind.SampledTexture or ResourceKind.StorageTexture or ResourceKind.CombinedTextureSampler)
            {
                var view = (VulkanTextureView)entry.TextureView!;
                imageInfos[i] = new VkDescriptorImageInfo
                {
                    imageView = view.Handle,
                    imageLayout = kind == ResourceKind.StorageTexture ? VkImageLayout.General : VkImageLayout.ShaderReadOnlyOptimal,
                    sampler = entry.Sampler is VulkanSampler s ? s.Handle : VkSampler.Null,
                };
            }
            else if (kind == ResourceKind.Sampler)
            {
                imageInfos[i] = new VkDescriptorImageInfo { sampler = ((VulkanSampler)entry.Sampler!).Handle };
            }
            else if (kind == ResourceKind.AccelerationStructure)
            {
                accelStructureHandles[i] = ((VulkanAccelerationStructure)entry.AccelerationStructure!).Handle;
            }
        }

        fixed (VkWriteDescriptorSet* pWrites = writes)
        fixed (VkDescriptorBufferInfo* pBufferInfos = bufferInfos)
        fixed (VkDescriptorImageInfo* pImageInfos = imageInfos)
        fixed (VkAccelerationStructureKHR* pAccelStructureHandles = accelStructureHandles)
        fixed (VkWriteDescriptorSetAccelerationStructureKHR* pAccelStructureInfos = accelStructureInfos)
        {
            for (int i = 0; i < writes.Length; i++)
            {
                var kind = layoutBindingKinds[descriptor.Entries[i].Binding];
                if (kind is ResourceKind.UniformBuffer or ResourceKind.StorageBuffer)
                {
                    writes[i].pBufferInfo = &pBufferInfos[i];
                }
                else if (kind == ResourceKind.AccelerationStructure)
                {
                    pAccelStructureInfos[i] = new VkWriteDescriptorSetAccelerationStructureKHR
                    {
                        accelerationStructureCount = 1,
                        pAccelerationStructures = &pAccelStructureHandles[i],
                    };
                    writes[i].pNext = &pAccelStructureInfos[i];
                }
                else
                {
                    writes[i].pImageInfo = &pImageInfos[i];
                }
            }
            device.Api.vkUpdateDescriptorSets((uint)writes.Length, pWrites, 0, null);
        }
    }

    public void Dispose()
    {
        VkDescriptorSet set = _set;
        _device.Api.vkFreeDescriptorSets(_device.DescriptorPool, 1, &set);
    }
}
