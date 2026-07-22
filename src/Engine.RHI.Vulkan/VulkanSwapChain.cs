using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;

namespace Engine.RHI.Vulkan;

/// <summary>
/// Milestone-1 simplification: acquire uses a fence and blocks the CPU until the image is
/// actually available, and Present waits for the device to go idle before presenting. This
/// trades cross-frame CPU/GPU overlap for correctness with zero additions to the public RHI
/// surface; revisit with real semaphore-based pacing once multiple-frames-in-flight matters.
/// </summary>
internal sealed unsafe class VulkanSwapChain : ISwapChain
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkSurfaceKHR _surface;
    private readonly VkFence _acquireFence;

    private VkSwapchainKHR _swapchain;
    private VulkanTexture[] _textures = [];
    private VkFormat _format;
    private uint _currentImageIndex;

    public SwapChainDescriptor Descriptor { get; private set; }

    public VulkanSwapChain(VulkanGraphicsDevice device, in SwapChainDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;

        VkWin32SurfaceCreateInfoKHR surfaceCreateInfo = new()
        {
            hinstance = Marshal.GetHINSTANCE(typeof(VulkanSwapChain).Module),
            hwnd = descriptor.WindowHandle,
        };
        VulkanUtils.CheckResult(device.InstanceApi.vkCreateWin32SurfaceKHR(&surfaceCreateInfo, out _surface), "vkCreateWin32SurfaceKHR failed");

        VkFenceCreateInfo fenceCreateInfo = new();
        VulkanUtils.CheckResult(device.Api.vkCreateFence(&fenceCreateInfo, out _acquireFence), "vkCreateFence failed");

        CreateSwapchain(descriptor.Width, descriptor.Height);
    }

    private void CreateSwapchain(uint width, uint height)
    {
        var instanceApi = _device.InstanceApi;
        var physicalDevice = _device.PhysicalDevice;

        VulkanUtils.CheckResult(instanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, _device.GraphicsQueueFamily, _surface, out VkBool32 supported), "vkGetPhysicalDeviceSurfaceSupportKHR failed");
        if (!supported)
            throw new InvalidOperationException("The selected graphics queue family does not support presenting to this surface.");

        VulkanUtils.CheckResult(instanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, _surface, out VkSurfaceCapabilitiesKHR caps), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR failed");

        instanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, _surface, out uint formatCount);
        Span<VkSurfaceFormatKHR> formats = new VkSurfaceFormatKHR[formatCount];
        fixed (VkSurfaceFormatKHR* pFormats = formats)
        {
            instanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, _surface, &formatCount, pFormats);
        }

        VkFormat desiredFormat = VulkanUtils.ToVkFormat(Descriptor.Format);
        VkSurfaceFormatKHR chosen = formats[0];
        foreach (var f in formats)
        {
            if (f.format == desiredFormat)
            {
                chosen = f;
                break;
            }
        }
        _format = chosen.format;

        uint imageCount = Math.Max(Descriptor.BufferCount, caps.minImageCount);
        if (caps.maxImageCount > 0)
            imageCount = Math.Min(imageCount, caps.maxImageCount);

        VkExtent2D extent = caps.currentExtent.width != uint.MaxValue
            ? caps.currentExtent
            : new VkExtent2D(Math.Clamp(width, caps.minImageExtent.width, caps.maxImageExtent.width), Math.Clamp(height, caps.minImageExtent.height, caps.maxImageExtent.height));

        VkPresentModeKHR presentMode = Descriptor.PresentMode switch
        {
            PresentMode.Immediate => VkPresentModeKHR.Immediate,
            PresentMode.Mailbox => VkPresentModeKHR.Mailbox,
            PresentMode.FifoRelaxed => VkPresentModeKHR.FifoRelaxed,
            _ => VkPresentModeKHR.Fifo,
        };

        VkSwapchainCreateInfoKHR createInfo = new()
        {
            surface = _surface,
            minImageCount = imageCount,
            imageFormat = _format,
            imageColorSpace = chosen.colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferDst,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = caps.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = presentMode,
            clipped = true,
            oldSwapchain = _swapchain,
        };

        VkSwapchainKHR newSwapchain;
        VulkanUtils.CheckResult(_device.Api.vkCreateSwapchainKHR(&createInfo, out newSwapchain), "vkCreateSwapchainKHR failed");

        DestroySwapchainObjects();
        _swapchain = newSwapchain;

        _device.Api.vkGetSwapchainImagesKHR(_swapchain, out uint actualImageCount);
        Span<VkImage> images = new VkImage[actualImageCount];
        fixed (VkImage* pImages = images)
        {
            _device.Api.vkGetSwapchainImagesKHR(_swapchain, &actualImageCount, pImages);
        }

        var textureDescriptor = new TextureDescriptor(extent.width, extent.height, VulkanUtils.FromVkFormat(_format), TextureUsage.RenderTarget | TextureUsage.CopyDestination, Name: "SwapchainImage");
        _textures = new VulkanTexture[images.Length];
        for (int i = 0; i < images.Length; i++)
            _textures[i] = new VulkanTexture(_device, images[i], textureDescriptor);

        Descriptor = Descriptor with { Width = extent.width, Height = extent.height };
    }

    private void DestroySwapchainObjects()
    {
        foreach (var texture in _textures)
            texture.Dispose();
        _textures = [];

        if (!_swapchain.IsNull)
            _device.Api.vkDestroySwapchainKHR(_swapchain);
    }

    public ITexture AcquireNextTexture()
    {
        uint imageIndex;
        VkResult result = _device.Api.vkAcquireNextImageKHR(_swapchain, ulong.MaxValue, VkSemaphore.Null, _acquireFence, &imageIndex);
        if (result is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
        {
            Resize(Descriptor.Width, Descriptor.Height);
            result = _device.Api.vkAcquireNextImageKHR(_swapchain, ulong.MaxValue, VkSemaphore.Null, _acquireFence, &imageIndex);
        }
        VulkanUtils.CheckResult(result, "vkAcquireNextImageKHR failed");

        VkFence fence = _acquireFence;
        _device.Api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
        _device.Api.vkResetFences(1, &fence);

        _currentImageIndex = imageIndex;
        return _textures[imageIndex];
    }

    public void Present()
    {
        _device.WaitIdle();

        VkSwapchainKHR swapchain = _swapchain;
        uint imageIndex = _currentImageIndex;
        VkPresentInfoKHR presentInfo = new()
        {
            swapchainCount = 1,
            pSwapchains = &swapchain,
            pImageIndices = &imageIndex,
        };
        VkResult result = _device.Api.vkQueuePresentKHR(_device.GraphicsQueue, &presentInfo);
        if (result is not (VkResult.Success or VkResult.SuboptimalKHR or VkResult.ErrorOutOfDateKHR))
            VulkanUtils.CheckResult(result, "vkQueuePresentKHR failed");
    }

    public void Resize(uint width, uint height)
    {
        _device.WaitIdle();
        CreateSwapchain(width, height);
    }

    public void Dispose()
    {
        _device.WaitIdle();
        DestroySwapchainObjects();
        _device.Api.vkDestroyFence(_acquireFence);
        _device.InstanceApi.vkDestroySurfaceKHR(_surface);
    }
}
