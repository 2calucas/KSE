using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanPipeline : IPipeline
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkPipeline _pipeline;
    private readonly VkPipelineLayout _layout;

    public bool IsCompute { get; }
    internal VkPipeline Handle => _pipeline;
    internal VkPipelineLayout Layout => _layout;
    internal VkPipelineBindPoint BindPoint => IsCompute ? VkPipelineBindPoint.Compute : VkPipelineBindPoint.Graphics;

    public VulkanPipeline(VulkanGraphicsDevice device, in GraphicsPipelineDescriptor descriptor)
    {
        _device = device;
        IsCompute = false;

        uint pushConstantSize = Math.Max(descriptor.VertexShader.Reflection.PushConstantSizeBytes, descriptor.FragmentShader.Reflection.PushConstantSizeBytes);
        ShaderStageFlags pushConstantStages = ShaderStageFlags.None;
        if (descriptor.VertexShader.Reflection.PushConstantSizeBytes > 0) pushConstantStages |= ShaderStageFlags.Vertex;
        if (descriptor.FragmentShader.Reflection.PushConstantSizeBytes > 0) pushConstantStages |= ShaderStageFlags.Fragment;
        _layout = CreatePipelineLayout(device, descriptor.ResourceSetLayouts, pushConstantSize, pushConstantStages);

        var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        using var vertEntry = new Utf8Z(descriptor.VertexShader.EntryPoint);
        using var fragEntry = new Utf8Z(descriptor.FragmentShader.EntryPoint);
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = ((VulkanShaderModule)descriptor.VertexShader).Handle, pName = vertEntry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = ((VulkanShaderModule)descriptor.FragmentShader).Handle, pName = fragEntry };

        var bindings = new VkVertexInputBindingDescription[descriptor.VertexBuffers.Count];
        List<VkVertexInputAttributeDescription> attributes = [];
        for (int i = 0; i < descriptor.VertexBuffers.Count; i++)
        {
            var vb = descriptor.VertexBuffers[i];
            bindings[i] = new VkVertexInputBindingDescription { binding = (uint)i, stride = vb.Stride, inputRate = VkVertexInputRate.Vertex };
            foreach (var attr in vb.Attributes)
                attributes.Add(new VkVertexInputAttributeDescription { location = attr.Location, binding = (uint)i, format = ToVkFormat(attr.Format), offset = attr.OffsetBytes });
        }
        var attributeArray = attributes.ToArray();

        fixed (VkVertexInputBindingDescription* pBindings = bindings)
        fixed (VkVertexInputAttributeDescription* pAttributes = attributeArray)
        {
            VkPipelineVertexInputStateCreateInfo vertexInputState = new()
            {
                vertexBindingDescriptionCount = (uint)bindings.Length,
                pVertexBindingDescriptions = pBindings,
                vertexAttributeDescriptionCount = (uint)attributeArray.Length,
                pVertexAttributeDescriptions = pAttributes,
            };

            VkPipelineInputAssemblyStateCreateInfo inputAssemblyState = new()
            {
                topology = ToVkPrimitiveTopology(descriptor.Topology),
            };

            VkPipelineViewportStateCreateInfo viewportState = new() { viewportCount = 1, scissorCount = 1 };

            VkPipelineRasterizationStateCreateInfo rasterizationState = new()
            {
                polygonMode = descriptor.Rasterizer.Wireframe ? VkPolygonMode.Line : VkPolygonMode.Fill,
                cullMode = descriptor.Rasterizer.CullMode switch
                {
                    CullMode.None => VkCullModeFlags.None,
                    CullMode.Front => VkCullModeFlags.Front,
                    _ => VkCullModeFlags.Back,
                },
                frontFace = descriptor.Rasterizer.FrontFace == FrontFace.Clockwise ? VkFrontFace.Clockwise : VkFrontFace.CounterClockwise,
                lineWidth = 1.0f,
            };

            VkPipelineMultisampleStateCreateInfo multisampleState = new() { rasterizationSamples = VkSampleCountFlags.Count1 };

            VkPipelineDepthStencilStateCreateInfo depthStencilState = new()
            {
                depthTestEnable = descriptor.DepthStencil.DepthTestEnable,
                depthWriteEnable = descriptor.DepthStencil.DepthWriteEnable,
                depthCompareOp = ToVkCompareOp(descriptor.DepthStencil.DepthCompare),
            };

            var colorBlendAttachments = new VkPipelineColorBlendAttachmentState[descriptor.ColorTargetFormats.Count];
            for (int i = 0; i < colorBlendAttachments.Length; i++)
            {
                colorBlendAttachments[i] = new VkPipelineColorBlendAttachmentState
                {
                    blendEnable = descriptor.Blend.Enabled,
                    srcColorBlendFactor = ToVkBlendFactor(descriptor.Blend.SrcColorFactor),
                    dstColorBlendFactor = ToVkBlendFactor(descriptor.Blend.DstColorFactor),
                    colorBlendOp = ToVkBlendOp(descriptor.Blend.ColorOp),
                    srcAlphaBlendFactor = ToVkBlendFactor(descriptor.Blend.SrcAlphaFactor),
                    dstAlphaBlendFactor = ToVkBlendFactor(descriptor.Blend.DstAlphaFactor),
                    alphaBlendOp = ToVkBlendOp(descriptor.Blend.AlphaOp),
                    colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A,
                };
            }

            fixed (VkPipelineColorBlendAttachmentState* pColorBlendAttachments = colorBlendAttachments)
            {
            VkPipelineColorBlendStateCreateInfo colorBlendState = new()
            {
                attachmentCount = (uint)colorBlendAttachments.Length,
                pAttachments = colorBlendAttachments.Length > 0 ? pColorBlendAttachments : null,
            };

            var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            VkPipelineDynamicStateCreateInfo dynamicState = new() { dynamicStateCount = 2, pDynamicStates = dynamicStates };

            var colorFormats = new VkFormat[descriptor.ColorTargetFormats.Count];
            for (int i = 0; i < colorFormats.Length; i++)
                colorFormats[i] = VulkanUtils.ToVkFormat(descriptor.ColorTargetFormats[i]);

            fixed (VkFormat* pColorFormats = colorFormats)
            {
                VkPipelineRenderingCreateInfo renderingCreateInfo = new()
                {
                    colorAttachmentCount = (uint)colorFormats.Length,
                    pColorAttachmentFormats = pColorFormats,
                    depthAttachmentFormat = descriptor.DepthStencilFormat.HasValue ? VulkanUtils.ToVkFormat(descriptor.DepthStencilFormat.Value) : VkFormat.Undefined,
                };

                VkGraphicsPipelineCreateInfo createInfo = new()
                {
                    pNext = &renderingCreateInfo,
                    stageCount = 2,
                    pStages = stages,
                    pVertexInputState = &vertexInputState,
                    pInputAssemblyState = &inputAssemblyState,
                    pViewportState = &viewportState,
                    pRasterizationState = &rasterizationState,
                    pMultisampleState = &multisampleState,
                    pDepthStencilState = &depthStencilState,
                    pColorBlendState = &colorBlendState,
                    pDynamicState = &dynamicState,
                    layout = _layout,
                };

                VkPipeline pipeline;
                VulkanUtils.CheckResult(device.Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &createInfo, &pipeline), "vkCreateGraphicsPipelines failed");
                _pipeline = pipeline;
            }
            }
        }
    }

    public VulkanPipeline(VulkanGraphicsDevice device, in ComputePipelineDescriptor descriptor)
    {
        _device = device;
        IsCompute = true;

        _layout = CreatePipelineLayout(device, descriptor.ResourceSetLayouts, descriptor.ComputeShader.Reflection.PushConstantSizeBytes, ShaderStageFlags.Compute);

        using var entry = new Utf8Z(descriptor.ComputeShader.EntryPoint);
        VkComputePipelineCreateInfo createInfo = new()
        {
            stage = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Compute, module = ((VulkanShaderModule)descriptor.ComputeShader).Handle, pName = entry },
            layout = _layout,
        };

        VkPipeline pipeline;
        VulkanUtils.CheckResult(device.Api.vkCreateComputePipelines(VkPipelineCache.Null, 1, &createInfo, &pipeline), "vkCreateComputePipelines failed");
        _pipeline = pipeline;
    }

    private static VkPipelineLayout CreatePipelineLayout(VulkanGraphicsDevice device, IReadOnlyList<IResourceSetLayout> setLayouts, uint pushConstantSize, ShaderStageFlags pushConstantStages)
    {
        var layouts = new VkDescriptorSetLayout[setLayouts.Count];
        for (int i = 0; i < layouts.Length; i++)
            layouts[i] = ((VulkanResourceSetLayout)setLayouts[i]).Handle;

        VkPushConstantRange range = new() { stageFlags = VulkanShaderModule.ToVkShaderStageFlags(pushConstantStages), offset = 0, size = pushConstantSize };

        fixed (VkDescriptorSetLayout* pLayouts = layouts)
        {
            VkPipelineLayoutCreateInfo createInfo = new()
            {
                setLayoutCount = (uint)layouts.Length,
                pSetLayouts = pLayouts,
                pushConstantRangeCount = pushConstantSize > 0 ? 1u : 0u,
                pPushConstantRanges = pushConstantSize > 0 ? &range : null,
            };
            VulkanUtils.CheckResult(device.Api.vkCreatePipelineLayout(&createInfo, out VkPipelineLayout layout), "vkCreatePipelineLayout failed");
            return layout;
        }
    }

    private static VkFormat ToVkFormat(VertexFormat format) => format switch
    {
        VertexFormat.Float32 => VkFormat.R32Sfloat,
        VertexFormat.Float32x2 => VkFormat.R32G32Sfloat,
        VertexFormat.Float32x3 => VkFormat.R32G32B32Sfloat,
        VertexFormat.Float32x4 => VkFormat.R32G32B32A32Sfloat,
        VertexFormat.UInt8x4Norm => VkFormat.R8G8B8A8Unorm,
        VertexFormat.UInt32 => VkFormat.R32Uint,
        _ => throw new NotSupportedException(),
    };

    private static VkPrimitiveTopology ToVkPrimitiveTopology(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList => VkPrimitiveTopology.TriangleList,
        PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
        PrimitiveTopology.LineList => VkPrimitiveTopology.LineList,
        PrimitiveTopology.LineStrip => VkPrimitiveTopology.LineStrip,
        PrimitiveTopology.PointList => VkPrimitiveTopology.PointList,
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

    private static VkBlendFactor ToVkBlendFactor(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => VkBlendFactor.Zero,
        BlendFactor.One => VkBlendFactor.One,
        BlendFactor.SrcAlpha => VkBlendFactor.SrcAlpha,
        BlendFactor.OneMinusSrcAlpha => VkBlendFactor.OneMinusSrcAlpha,
        BlendFactor.DstAlpha => VkBlendFactor.DstAlpha,
        BlendFactor.OneMinusDstAlpha => VkBlendFactor.OneMinusDstAlpha,
        BlendFactor.SrcColor => VkBlendFactor.SrcColor,
        BlendFactor.OneMinusSrcColor => VkBlendFactor.OneMinusSrcColor,
        BlendFactor.DstColor => VkBlendFactor.DstColor,
        BlendFactor.OneMinusDstColor => VkBlendFactor.OneMinusDstColor,
        _ => throw new NotSupportedException(),
    };

    private static VkBlendOp ToVkBlendOp(BlendOperation op) => op switch
    {
        BlendOperation.Add => VkBlendOp.Add,
        BlendOperation.Subtract => VkBlendOp.Subtract,
        BlendOperation.ReverseSubtract => VkBlendOp.ReverseSubtract,
        BlendOperation.Min => VkBlendOp.Min,
        BlendOperation.Max => VkBlendOp.Max,
        _ => throw new NotSupportedException(),
    };

    public void Dispose()
    {
        _device.Api.vkDestroyPipeline(_pipeline);
        _device.Api.vkDestroyPipelineLayout(_layout);
    }
}
