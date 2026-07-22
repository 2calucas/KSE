using Vortice.Vulkan;
using Vortice.SPIRV.Reflect;
using Engine.RHI;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanShaderModule : IShaderModule
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkShaderModule _module;

    public ShaderStage Stage { get; }
    public string EntryPoint { get; }
    public ShaderReflectionInfo Reflection { get; }
    internal VkShaderModule Handle => _module;

    public VulkanShaderModule(VulkanGraphicsDevice device, in ShaderModuleDescriptor descriptor)
    {
        _device = device;
        Stage = descriptor.Stage;
        EntryPoint = descriptor.EntryPoint;

        ReadOnlySpan<byte> spirv = descriptor.SpirvBytecode.Span;

        fixed (byte* pCode = spirv)
        {
            VkShaderModuleCreateInfo createInfo = new()
            {
                codeSize = (nuint)spirv.Length,
                pCode = (uint*)pCode,
            };
            VulkanUtils.CheckResult(device.Api.vkCreateShaderModule(&createInfo, out _module), "vkCreateShaderModule failed");
        }

        Reflection = ReflectSpirv(spirv, descriptor.Stage);
    }

    private static ShaderReflectionInfo ReflectSpirv(ReadOnlySpan<byte> spirv, ShaderStage stage)
    {
        SpvReflectShaderModule module;
        SPIRVReflectApi.ThrowIfFailed(SPIRVReflectApi.spvReflectCreateShaderModule(spirv, &module), "spvReflectCreateShaderModule");

        try
        {
            uint bindingCount;
            SPIRVReflectApi.spvReflectEnumerateDescriptorBindings(&module, &bindingCount, null);
            var bindingPtrs = new SpvReflectDescriptorBinding*[bindingCount];
            fixed (SpvReflectDescriptorBinding** pBindings = bindingPtrs)
            {
                SPIRVReflectApi.spvReflectEnumerateDescriptorBindings(&module, &bindingCount, pBindings);
            }

            var stageFlags = stage switch
            {
                ShaderStage.Vertex => ShaderStageFlags.Vertex,
                ShaderStage.Fragment => ShaderStageFlags.Fragment,
                ShaderStage.Compute => ShaderStageFlags.Compute,
                ShaderStage.Geometry => ShaderStageFlags.Geometry,
                ShaderStage.TessellationControl => ShaderStageFlags.TessellationControl,
                ShaderStage.TessellationEvaluation => ShaderStageFlags.TessellationEvaluation,
                _ => ShaderStageFlags.None,
            };

            var resources = new List<ResourceBinding>(bindingPtrs.Length);
            foreach (var b in bindingPtrs)
            {
                var kind = b->descriptor_type switch
                {
                    SpvReflectDescriptorType.UniformBuffer => ResourceKind.UniformBuffer,
                    SpvReflectDescriptorType.StorageBuffer => ResourceKind.StorageBuffer,
                    SpvReflectDescriptorType.SampledImage => ResourceKind.SampledTexture,
                    SpvReflectDescriptorType.StorageImage => ResourceKind.StorageTexture,
                    SpvReflectDescriptorType.Sampler => ResourceKind.Sampler,
                    SpvReflectDescriptorType.CombinedImageSampler => ResourceKind.CombinedTextureSampler,
                    SpvReflectDescriptorType.AccelerationStructureKHR => ResourceKind.AccelerationStructure,
                    _ => ResourceKind.UniformBuffer,
                };
                resources.Add(new ResourceBinding(b->set, b->binding, kind, Math.Max(1, b->count), stageFlags, b->Name));
            }

            uint pushConstantCount;
            SPIRVReflectApi.spvReflectEnumeratePushConstantBlocks(&module, &pushConstantCount, null);
            uint pushConstantSize = 0;
            if (pushConstantCount > 0)
            {
                var blocks = new SpvReflectBlockVariable*[pushConstantCount];
                fixed (SpvReflectBlockVariable** pBlocks = blocks)
                {
                    SPIRVReflectApi.spvReflectEnumeratePushConstantBlocks(&module, &pushConstantCount, pBlocks);
                }
                foreach (var block in blocks)
                    pushConstantSize = Math.Max(pushConstantSize, block->offset + block->size);
            }

            List<VertexInputAttribute> vertexInputs = [];
            if (stage == ShaderStage.Vertex)
            {
                uint inputCount;
                SPIRVReflectApi.spvReflectEnumerateInputVariables(&module, &inputCount, null);
                var inputs = new SpvReflectInterfaceVariable*[inputCount];
                fixed (SpvReflectInterfaceVariable** pInputs = inputs)
                {
                    SPIRVReflectApi.spvReflectEnumerateInputVariables(&module, &inputCount, pInputs);
                }
                foreach (var input in inputs)
                {
                    if (input->location == uint.MaxValue) continue; // built-ins report location -1
                    vertexInputs.Add(new VertexInputAttribute(input->location, VertexFormat.Float32x4, 0, input->name != null ? new string((sbyte*)input->name) : null));
                }
            }

            return new ShaderReflectionInfo
            {
                Resources = resources,
                VertexInputs = vertexInputs,
                PushConstantSizeBytes = pushConstantSize,
            };
        }
        finally
        {
            SPIRVReflectApi.spvReflectDestroyShaderModule(&module);
        }
    }

    internal static VkShaderStageFlags ToVkShaderStageFlags(ShaderStageFlags stages)
    {
        VkShaderStageFlags result = VkShaderStageFlags.None;
        if ((stages & ShaderStageFlags.Vertex) != 0) result |= VkShaderStageFlags.Vertex;
        if ((stages & ShaderStageFlags.Fragment) != 0) result |= VkShaderStageFlags.Fragment;
        if ((stages & ShaderStageFlags.Compute) != 0) result |= VkShaderStageFlags.Compute;
        if ((stages & ShaderStageFlags.Geometry) != 0) result |= VkShaderStageFlags.Geometry;
        if ((stages & ShaderStageFlags.TessellationControl) != 0) result |= VkShaderStageFlags.TessellationControl;
        if ((stages & ShaderStageFlags.TessellationEvaluation) != 0) result |= VkShaderStageFlags.TessellationEvaluation;
        return result;
    }

    public void Dispose() => _device.Api.vkDestroyShaderModule(_module);
}
