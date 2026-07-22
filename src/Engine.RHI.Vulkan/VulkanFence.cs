using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanFence : IFence
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkFence _fence;

    public VkFence Handle => _fence;

    public VulkanFence(VulkanGraphicsDevice device, bool signaled)
    {
        _device = device;
        VkFenceCreateInfo createInfo = new()
        {
            flags = signaled ? VkFenceCreateFlags.Signaled : VkFenceCreateFlags.None,
        };
        VulkanUtils.CheckResult(device.Api.vkCreateFence(&createInfo, out _fence), "vkCreateFence failed");
    }

    public bool IsSignaled => _device.Api.vkGetFenceStatus(_fence) == VkResult.Success;

    public void Wait(ulong timeoutNanoseconds = ulong.MaxValue)
    {
        VkFence fence = _fence;
        _device.Api.vkWaitForFences(1, &fence, true, timeoutNanoseconds);
    }

    public void Reset()
    {
        VkFence fence = _fence;
        _device.Api.vkResetFences(1, &fence);
    }

    public void Dispose() => _device.Api.vkDestroyFence(_fence);
}
