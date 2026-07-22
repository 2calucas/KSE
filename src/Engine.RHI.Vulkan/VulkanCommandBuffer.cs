using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanCommandBuffer : ICommandBuffer
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VulkanCommandQueue _owningQueue;
    private readonly VkCommandPool _pool;
    private readonly VkCommandBuffer _commandBuffer;
    private readonly VkDeviceApi _api;

    private VulkanPipeline? _currentPipeline;

    internal VkCommandBuffer Handle => _commandBuffer;

    public VulkanCommandBuffer(VulkanGraphicsDevice device, VulkanCommandQueue owningQueue, VkCommandPool pool, VkCommandBuffer commandBuffer)
    {
        _device = device;
        _owningQueue = owningQueue;
        _pool = pool;
        _commandBuffer = commandBuffer;
        _api = device.Api;
    }

    public void Begin()
    {
        VkCommandBufferBeginInfo beginInfo = new()
        {
            flags = VkCommandBufferUsageFlags.OneTimeSubmit,
        };
        VulkanUtils.CheckResult(_api.vkBeginCommandBuffer(_commandBuffer, &beginInfo), "vkBeginCommandBuffer failed");
    }

    public void End() => VulkanUtils.CheckResult(_api.vkEndCommandBuffer(_commandBuffer), "vkEndCommandBuffer failed");

    public void BeginRenderPass(in RenderPassDescriptor descriptor)
    {
        Span<VkRenderingAttachmentInfo> colorAttachments = new VkRenderingAttachmentInfo[descriptor.ColorAttachments.Count];
        uint width = 0, height = 0;

        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            var attachment = descriptor.ColorAttachments[i];
            var view = (VulkanTextureView)attachment.Target;
            width = view.Texture.Descriptor.Width;
            height = view.Texture.Descriptor.Height;

            colorAttachments[i] = new VkRenderingAttachmentInfo
            {
                imageView = view.Handle,
                imageLayout = VkImageLayout.ColorAttachmentOptimal,
                loadOp = attachment.Clear ? VkAttachmentLoadOp.Clear : VkAttachmentLoadOp.Load,
                storeOp = VkAttachmentStoreOp.Store,
                clearValue = new VkClearValue(attachment.ClearR, attachment.ClearG, attachment.ClearB, attachment.ClearA),
            };
        }

        VkRenderingAttachmentInfo depthAttachment = default;
        bool hasDepth = descriptor.DepthStencilAttachment.HasValue;
        if (hasDepth)
        {
            var d = descriptor.DepthStencilAttachment!.Value;
            var view = (VulkanTextureView)d.Target;
            width = view.Texture.Descriptor.Width;
            height = view.Texture.Descriptor.Height;

            depthAttachment = new VkRenderingAttachmentInfo
            {
                imageView = view.Handle,
                imageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                loadOp = d.ClearDepth ? VkAttachmentLoadOp.Clear : VkAttachmentLoadOp.Load,
                storeOp = VkAttachmentStoreOp.Store,
                clearValue = new VkClearValue(d.ClearDepthValue, d.ClearStencilValue),
            };
        }

        fixed (VkRenderingAttachmentInfo* pColorAttachments = colorAttachments)
        {
            VkRenderingInfo renderingInfo = new()
            {
                renderArea = new VkRect2D(0, 0, width, height),
                layerCount = 1,
                colorAttachmentCount = (uint)colorAttachments.Length,
                pColorAttachments = pColorAttachments,
                pDepthAttachment = hasDepth ? &depthAttachment : null,
            };
            _api.vkCmdBeginRendering(_commandBuffer, &renderingInfo);
        }
    }

    public void EndRenderPass() => _api.vkCmdEndRendering(_commandBuffer);

    public void SetPipeline(IPipeline pipeline)
    {
        _currentPipeline = (VulkanPipeline)pipeline;
        _api.vkCmdBindPipeline(_commandBuffer, _currentPipeline.BindPoint, _currentPipeline.Handle);
    }

    public void SetResourceSet(uint setIndex, IResourceSet set)
    {
        if (_currentPipeline is null)
            throw new InvalidOperationException("SetResourceSet requires a pipeline to be bound first.");

        VkDescriptorSet descriptorSet = ((VulkanResourceSet)set).Handle;
        _api.vkCmdBindDescriptorSets(_commandBuffer, _currentPipeline.BindPoint, _currentPipeline.Layout, setIndex, 1, &descriptorSet, 0, null);
    }

    public void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
    {
        VkBuffer handle = ((VulkanBuffer)buffer).Handle;
        ulong off = offset;
        _api.vkCmdBindVertexBuffers(_commandBuffer, slot, 1, &handle, &off);
    }

    public void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0)
    {
        var indexType = format == IndexFormat.UInt16 ? VkIndexType.Uint16 : VkIndexType.Uint32;
        _api.vkCmdBindIndexBuffer(_commandBuffer, ((VulkanBuffer)buffer).Handle, offset, indexType);
    }

    public void SetViewport(in Viewport viewport)
    {
        VkViewport vp = new(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _api.vkCmdSetViewport(_commandBuffer, 0, 1, &vp);
    }

    public void SetScissor(in ScissorRect rect)
    {
        VkRect2D scissor = new(rect.X, rect.Y, rect.Width, rect.Height);
        _api.vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);
    }

    public void PushConstants<T>(ShaderStageFlags stages, in T data, uint offset = 0) where T : unmanaged
    {
        if (_currentPipeline is null)
            throw new InvalidOperationException("PushConstants requires a pipeline to be bound first.");

        fixed (T* pData = &data)
        {
            _api.vkCmdPushConstants(_commandBuffer, _currentPipeline.Layout, VulkanShaderModule.ToVkShaderStageFlags(stages), offset, (uint)sizeof(T), pData);
        }
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        => _api.vkCmdDraw(_commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        => _api.vkCmdDrawIndexed(_commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ = 1)
        => _api.vkCmdDispatch(_commandBuffer, groupCountX, groupCountY, groupCountZ);

    public void CopyBuffer(IBuffer src, IBuffer dst, ulong size, ulong srcOffset = 0, ulong dstOffset = 0)
    {
        VkBufferCopy region = new() { srcOffset = srcOffset, dstOffset = dstOffset, size = size };
        _api.vkCmdCopyBuffer(_commandBuffer, ((VulkanBuffer)src).Handle, ((VulkanBuffer)dst).Handle, 1, &region);
    }

    public void CopyBufferToTexture(IBuffer src, ITexture dst, in TextureCopyRegion region)
    {
        var texture = (VulkanTexture)dst;
        VkBufferImageCopy copy = new()
        {
            bufferOffset = 0,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, region.MipLevel, region.ArrayLayer, 1),
            imageOffset = new VkOffset3D((int)region.DstX, (int)region.DstY, (int)region.DstZ),
            imageExtent = new VkExtent3D(region.Width, region.Height, region.Depth),
        };
        _api.vkCmdCopyBufferToImage(_commandBuffer, ((VulkanBuffer)src).Handle, texture.Handle, VkImageLayout.TransferDstOptimal, 1, &copy);
    }

    public void TransitionTexture(ITexture texture, ResourceState from, ResourceState to)
    {
        var vkTexture = (VulkanTexture)texture;
        var fromPoint = VulkanUtils.ToBarrierPoint(from);
        var toPoint = VulkanUtils.ToBarrierPoint(to);

        VkImageMemoryBarrier2 barrier = new()
        {
            srcStageMask = fromPoint.Stage,
            srcAccessMask = fromPoint.Access,
            dstStageMask = toPoint.Stage,
            dstAccessMask = toPoint.Access,
            oldLayout = fromPoint.Layout,
            newLayout = toPoint.Layout,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = vkTexture.Handle,
            subresourceRange = new VkImageSubresourceRange(vkTexture.AspectFlags, 0, vkTexture.Descriptor.MipLevels, 0, vkTexture.Descriptor.DepthOrArrayLayers),
        };

        VkDependencyInfo dependencyInfo = new()
        {
            imageMemoryBarrierCount = 1,
            pImageMemoryBarriers = &barrier,
        };
        _api.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
        vkTexture.CurrentStateInternal = to;
    }

    public void TransitionBuffer(IBuffer buffer, ResourceState from, ResourceState to)
    {
        var vkBuffer = (VulkanBuffer)buffer;
        var fromPoint = VulkanUtils.ToBarrierPoint(from);
        var toPoint = VulkanUtils.ToBarrierPoint(to);

        VkBufferMemoryBarrier2 barrier = new()
        {
            srcStageMask = fromPoint.Stage,
            srcAccessMask = fromPoint.Access,
            dstStageMask = toPoint.Stage,
            dstAccessMask = toPoint.Access,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            buffer = vkBuffer.Handle,
            offset = 0,
            size = VK_WHOLE_SIZE,
        };

        VkDependencyInfo dependencyInfo = new()
        {
            bufferMemoryBarrierCount = 1,
            pBufferMemoryBarriers = &barrier,
        };
        _api.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
        vkBuffer.CurrentStateInternal = to;
    }

    public void RebuildTopLevelAccelerationStructure(IAccelerationStructure accelerationStructure, in TopLevelAccelerationStructureDescriptor descriptor)
        => ((VulkanAccelerationStructure)accelerationStructure).RecordRebuild(_commandBuffer, _api, descriptor);

    public void Dispose()
    {
        VkCommandBuffer cmd = _commandBuffer;
        _api.vkFreeCommandBuffers(_pool, 1, &cmd);
    }
}
