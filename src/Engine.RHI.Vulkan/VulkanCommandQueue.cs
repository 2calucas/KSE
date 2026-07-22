using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanCommandQueue : ICommandQueue, IDisposable
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkQueue _queue;
    private readonly VkCommandPool _commandPool;

    public CommandQueueKind Kind { get; }
    internal VkQueue Queue => _queue;

    public VulkanCommandQueue(VulkanGraphicsDevice device, CommandQueueKind kind, uint queueFamily, VkQueue queue)
    {
        _device = device;
        Kind = kind;
        _queue = queue;

        VkCommandPoolCreateInfo createInfo = new()
        {
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
            queueFamilyIndex = queueFamily,
        };
        VulkanUtils.CheckResult(device.Api.vkCreateCommandPool(&createInfo, out _commandPool), "vkCreateCommandPool failed");
    }

    public ICommandBuffer CreateCommandBuffer()
    {
        VkCommandBufferAllocateInfo allocateInfo = new()
        {
            commandPool = _commandPool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        VkCommandBuffer commandBuffer;
        VulkanUtils.CheckResult(_device.Api.vkAllocateCommandBuffers(&allocateInfo, &commandBuffer), "vkAllocateCommandBuffers failed");
        return new VulkanCommandBuffer(_device, this, _commandPool, commandBuffer);
    }

    public void Submit(IReadOnlyList<ICommandBuffer> commandBuffers, IFence? signalFence = null)
    {
        Span<VkCommandBufferSubmitInfo> cmdInfos = new VkCommandBufferSubmitInfo[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            cmdInfos[i] = new VkCommandBufferSubmitInfo { commandBuffer = ((VulkanCommandBuffer)commandBuffers[i]).Handle };
        }

        fixed (VkCommandBufferSubmitInfo* pCmdInfos = cmdInfos)
        {
            VkSubmitInfo2 submitInfo = new()
            {
                commandBufferInfoCount = (uint)cmdInfos.Length,
                pCommandBufferInfos = pCmdInfos,
            };

            VkFence fence = signalFence is VulkanFence vf ? vf.Handle : VkFence.Null;
            VulkanUtils.CheckResult(_device.Api.vkQueueSubmit2(_queue, 1, &submitInfo, fence), "vkQueueSubmit2 failed");
        }
    }

    public void WaitIdle() => _device.Api.vkQueueWaitIdle(_queue);

    public void Dispose() => _device.Api.vkDestroyCommandPool(_commandPool);
}
