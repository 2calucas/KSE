using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Engine.RHI.Vulkan;

public sealed unsafe class VulkanGraphicsDevice : IGraphicsDevice
{
    private static bool s_loaderInitialized;

    private readonly VkInstance _instance;
    private readonly VkInstanceApi _instanceApi;
    private readonly VkPhysicalDevice _physicalDevice;
    private readonly VkDevice _device;
    private readonly VkDeviceApi _api;
    private readonly VmaAllocator _allocator;
    private readonly VkDescriptorPool _descriptorPool;
    private readonly uint _graphicsQueueFamily;
    private readonly VkQueue _graphicsQueue;
    private readonly VulkanCommandQueue _graphicsCommandQueue;
    private readonly bool _validationEnabled;

    internal VkInstance Instance => _instance;
    internal VkInstanceApi InstanceApi => _instanceApi;
    internal VkPhysicalDevice PhysicalDevice => _physicalDevice;
    internal VkDevice Device => _device;
    internal VkDeviceApi Api => _api;
    internal VmaAllocator Allocator => _allocator;
    internal VkDescriptorPool DescriptorPool => _descriptorPool;
    internal uint GraphicsQueueFamily => _graphicsQueueFamily;
    internal VkQueue GraphicsQueue => _graphicsQueue;

    public RhiBackend Backend => RhiBackend.Vulkan;
    public RhiCapabilities Capabilities { get; }
    public RhiDeviceLimits Limits { get; }
    public string AdapterName { get; }
    internal uint MinAccelerationStructureScratchOffsetAlignment { get; }

    public VulkanGraphicsDevice(bool enableValidation = true)
    {
        _validationEnabled = enableValidation;

        if (!s_loaderInitialized)
        {
            VulkanUtils.CheckResult(vkInitialize(), "Failed to load the Vulkan loader (vulkan-1.dll)");
            s_loaderInitialized = true;
        }

        _instance = CreateInstance(enableValidation);
        _instanceApi = GetApi(_instance);

        _physicalDevice = SelectPhysicalDevice(_instanceApi, out string adapterName);
        AdapterName = adapterName;
        _graphicsQueueFamily = FindGraphicsQueueFamily(_instanceApi, _physicalDevice);

        _device = CreateDevice(_instanceApi, _physicalDevice, _graphicsQueueFamily, out RhiCapabilities capabilities);
        Capabilities = capabilities;
        MinAccelerationStructureScratchOffsetAlignment = capabilities.HasFlag(RhiCapabilities.RayQuery)
            ? QueryMinAccelerationStructureScratchOffsetAlignment(_instanceApi, _physicalDevice)
            : 0;
        _api = GetApi(_instance, _device);

        _api.vkGetDeviceQueue(_graphicsQueueFamily, 0, out _graphicsQueue);

        _allocator = CreateAllocator(_instanceApi, _instance, _physicalDevice, _device, capabilities.HasFlag(RhiCapabilities.RayQuery));

        Limits = QueryLimits(_instanceApi, _physicalDevice);

        _descriptorPool = CreateSharedDescriptorPool(_api);

        _graphicsCommandQueue = new VulkanCommandQueue(this, CommandQueueKind.Graphics, _graphicsQueueFamily, _graphicsQueue);
    }

    private static VkInstance CreateInstance(bool enableValidation)
    {
        using var appName = new Utf8Z("Engine");

        VkApplicationInfo appInfo = new()
        {
            pApplicationName = appName,
            applicationVersion = new VkVersion(1, 0, 0),
            pEngineName = appName,
            engineVersion = new VkVersion(1, 0, 0),
            apiVersion = VkVersion.Version_1_3,
        };

        List<string> extensions = ["VK_KHR_surface", "VK_KHR_win32_surface"];
        List<string> layers = [];
        if (enableValidation && IsLayerAvailable("VK_LAYER_KHRONOS_validation"))
        {
            layers.Add("VK_LAYER_KHRONOS_validation");
        }

        using var vkExtensions = new VkStringArray(extensions);
        using var vkLayers = new VkStringArray(layers);

        VkInstanceCreateInfo createInfo = new()
        {
            pApplicationInfo = &appInfo,
            enabledExtensionCount = vkExtensions.Length,
            ppEnabledExtensionNames = vkExtensions,
            enabledLayerCount = vkLayers.Length,
            ppEnabledLayerNames = vkLayers.Length > 0 ? vkLayers : null,
        };

        VulkanUtils.CheckResult(vkCreateInstance(&createInfo, out VkInstance instance), "vkCreateInstance failed");
        return instance;
    }

    private static bool IsLayerAvailable(string layerName)
    {
        VulkanUtils.CheckResult(vkEnumerateInstanceLayerProperties(out uint count), "vkEnumerateInstanceLayerProperties (count) failed");
        Span<VkLayerProperties> layers = stackalloc VkLayerProperties[(int)count];
        VulkanUtils.CheckResult(vkEnumerateInstanceLayerProperties(layers), "vkEnumerateInstanceLayerProperties failed");
        foreach (var layer in layers)
        {
            var current = layer;
            string name = Marshal.PtrToStringUTF8((nint)current.layerName) ?? string.Empty;
            if (name == layerName)
                return true;
        }
        return false;
    }

    private static VkPhysicalDevice SelectPhysicalDevice(VkInstanceApi instanceApi, out string adapterName)
    {
        VulkanUtils.CheckResult(instanceApi.vkEnumeratePhysicalDevices(out uint count), "vkEnumeratePhysicalDevices (count) failed");
        if (count == 0)
            throw new InvalidOperationException("No Vulkan-capable physical devices were found.");

        Span<VkPhysicalDevice> devices = new VkPhysicalDevice[count];
        fixed (VkPhysicalDevice* pDevices = devices)
        {
            VulkanUtils.CheckResult(instanceApi.vkEnumeratePhysicalDevices(&count, pDevices), "vkEnumeratePhysicalDevices failed");
        }

        VkPhysicalDevice best = default;
        VkPhysicalDeviceProperties bestProps = default;
        bool found = false;

        foreach (var candidate in devices)
        {
            instanceApi.vkGetPhysicalDeviceProperties(candidate, out VkPhysicalDeviceProperties props);
            if (!found || props.deviceType == VkPhysicalDeviceType.DiscreteGpu)
            {
                best = candidate;
                bestProps = props;
                found = true;
                if (props.deviceType == VkPhysicalDeviceType.DiscreteGpu)
                    break;
            }
        }

        adapterName = Marshal.PtrToStringUTF8((nint)bestProps.deviceName) ?? "Unknown";
        return best;
    }

    private static uint FindGraphicsQueueFamily(VkInstanceApi instanceApi, VkPhysicalDevice physicalDevice)
    {
        instanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, out uint count);
        Span<VkQueueFamilyProperties> families = new VkQueueFamilyProperties[count];
        fixed (VkQueueFamilyProperties* pFamilies = families)
        {
            instanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, pFamilies);
        }

        for (int i = 0; i < families.Length; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) != 0 && (families[i].queueFlags & VkQueueFlags.Compute) != 0)
                return (uint)i;
        }

        throw new InvalidOperationException("No graphics+compute capable queue family was found.");
    }

    private static VkDevice CreateDevice(VkInstanceApi instanceApi, VkPhysicalDevice physicalDevice, uint queueFamily, out RhiCapabilities capabilities)
    {
        float priority = 1.0f;
        VkDeviceQueueCreateInfo queueCreateInfo = new()
        {
            queueFamilyIndex = queueFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        // Inline ray tracing (RayQuery) needs all three: the accel-structure extension to build/hold BLAS/TLAS,
        // ray_query to trace against them from an ordinary fragment shader, and deferred_host_operations as a
        // hard dependency of accel-structure (even though we only ever build synchronously, never deferred).
        bool rayTracingExtensionsAvailable =
            IsDeviceExtensionAvailable(instanceApi, physicalDevice, "VK_KHR_acceleration_structure") &&
            IsDeviceExtensionAvailable(instanceApi, physicalDevice, "VK_KHR_ray_query") &&
            IsDeviceExtensionAvailable(instanceApi, physicalDevice, "VK_KHR_deferred_host_operations");

        VkPhysicalDeviceRayQueryFeaturesKHR rayQueryFeatures = new();
        VkPhysicalDeviceAccelerationStructureFeaturesKHR accelStructureFeatures = new() { pNext = &rayQueryFeatures };

        VkPhysicalDeviceVulkan13Features features13 = new()
        {
            pNext = rayTracingExtensionsAvailable ? &accelStructureFeatures : null,
            dynamicRendering = true,
            synchronization2 = true,
        };

        VkPhysicalDeviceVulkan12Features features12 = new()
        {
            pNext = &features13,
            descriptorIndexing = true,
            bufferDeviceAddress = false,
        };

        VkPhysicalDeviceFeatures2 features2 = new()
        {
            pNext = &features12,
        };
        instanceApi.vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);

        bool rayTracingSupported = rayTracingExtensionsAvailable && accelStructureFeatures.accelerationStructure && rayQueryFeatures.rayQuery;

        List<string> extensions = ["VK_KHR_swapchain"];
        if (rayTracingSupported)
        {
            extensions.Add("VK_KHR_acceleration_structure");
            extensions.Add("VK_KHR_ray_query");
            extensions.Add("VK_KHR_deferred_host_operations");
            features12.bufferDeviceAddress = true;
        }
        else
        {
            features13.pNext = null;
        }

        using var vkExtensions = new VkStringArray(extensions);

        VkDeviceCreateInfo createInfo = new()
        {
            pNext = &features2,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueCreateInfo,
            enabledExtensionCount = vkExtensions.Length,
            ppEnabledExtensionNames = vkExtensions,
        };

        VulkanUtils.CheckResult(instanceApi.vkCreateDevice(physicalDevice, &createInfo, out VkDevice device), "vkCreateDevice failed");

        capabilities = RhiCapabilities.ComputeShaders | RhiCapabilities.SamplerAnisotropy | RhiCapabilities.MultiDrawIndirect;
        if (rayTracingSupported)
            capabilities |= RhiCapabilities.RayQuery;
        return device;
    }

    private static bool IsDeviceExtensionAvailable(VkInstanceApi instanceApi, VkPhysicalDevice physicalDevice, string name)
    {
        instanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, out uint count);
        Span<VkExtensionProperties> properties = new VkExtensionProperties[count];
        instanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, properties);
        foreach (var property in properties)
        {
            var current = property;
            string extensionName = Marshal.PtrToStringUTF8((nint)current.extensionName) ?? string.Empty;
            if (extensionName == name)
                return true;
        }
        return false;
    }

    private static VmaAllocator CreateAllocator(VkInstanceApi instanceApi, VkInstance instance, VkPhysicalDevice physicalDevice, VkDevice device, bool bufferDeviceAddressEnabled)
    {
        VmaVulkanFunctions functions = new()
        {
            vkGetInstanceProcAddr = vkGetInstanceProcAddr_ptr,
            vkGetDeviceProcAddr = (delegate* unmanaged<VkDevice, byte*, PFN_vkVoidFunction>)instanceApi.vkGetDeviceProcAddr_ptr.Value,
        };

        VmaAllocatorCreateInfo createInfo = new()
        {
            flags = bufferDeviceAddressEnabled ? VmaAllocatorCreateFlags.BufferDeviceAddress : VmaAllocatorCreateFlags.None,
            instance = instance,
            physicalDevice = physicalDevice,
            device = device,
            vulkanApiVersion = VkVersion.Version_1_3,
            pVulkanFunctions = &functions,
        };

        VulkanUtils.CheckResult(vmaCreateAllocator(&createInfo, out VmaAllocator allocator), "vmaCreateAllocator failed");
        return allocator;
    }

    private static RhiDeviceLimits QueryLimits(VkInstanceApi instanceApi, VkPhysicalDevice physicalDevice)
    {
        instanceApi.vkGetPhysicalDeviceProperties(physicalDevice, out VkPhysicalDeviceProperties props);
        return new RhiDeviceLimits(
            MaxTexture2DSize: props.limits.maxImageDimension2D,
            MaxColorAttachments: props.limits.maxColorAttachments,
            MaxBoundDescriptorSets: props.limits.maxBoundDescriptorSets,
            MinUniformBufferOffsetAlignment: (uint)props.limits.minUniformBufferOffsetAlignment,
            MinStorageBufferOffsetAlignment: (uint)props.limits.minStorageBufferOffsetAlignment,
            MaxPushConstantSize: props.limits.maxPushConstantsSize);
    }

    private static uint QueryMinAccelerationStructureScratchOffsetAlignment(VkInstanceApi instanceApi, VkPhysicalDevice physicalDevice)
    {
        VkPhysicalDeviceAccelerationStructurePropertiesKHR asProperties = new();
        VkPhysicalDeviceProperties2 properties2 = new() { pNext = &asProperties };
        instanceApi.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return asProperties.minAccelerationStructureScratchOffsetAlignment;
    }

    private static VkDescriptorPool CreateSharedDescriptorPool(VkDeviceApi api)
    {
        Span<VkDescriptorPoolSize> sizes =
        [
            new(VkDescriptorType.UniformBuffer, 4096),
            new(VkDescriptorType.StorageBuffer, 4096),
            new(VkDescriptorType.CombinedImageSampler, 4096),
            new(VkDescriptorType.SampledImage, 4096),
            new(VkDescriptorType.StorageImage, 4096),
            new(VkDescriptorType.Sampler, 1024),
        ];

        fixed (VkDescriptorPoolSize* pSizes = sizes)
        {
            VkDescriptorPoolCreateInfo createInfo = new()
            {
                flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet,
                maxSets = 4096,
                poolSizeCount = (uint)sizes.Length,
                pPoolSizes = pSizes,
            };
            VulkanUtils.CheckResult(api.vkCreateDescriptorPool(&createInfo, out VkDescriptorPool pool), "vkCreateDescriptorPool failed");
            return pool;
        }
    }

    public ISwapChain CreateSwapChain(in SwapChainDescriptor descriptor) => new VulkanSwapChain(this, descriptor);
    public IBuffer CreateBuffer(in BufferDescriptor descriptor) => new VulkanBuffer(this, descriptor);
    public ITexture CreateTexture(in TextureDescriptor descriptor) => new VulkanTexture(this, descriptor);
    public ISampler CreateSampler(in SamplerDescriptor descriptor) => new VulkanSampler(this, descriptor);
    public IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor) => new VulkanShaderModule(this, descriptor);
    public IPipeline CreateGraphicsPipeline(in GraphicsPipelineDescriptor descriptor) => new VulkanPipeline(this, descriptor);
    public IPipeline CreateComputePipeline(in ComputePipelineDescriptor descriptor) => new VulkanPipeline(this, descriptor);
    public IResourceSetLayout CreateResourceSetLayout(in ResourceSetLayoutDescriptor descriptor) => new VulkanResourceSetLayout(this, descriptor);
    public IResourceSet CreateResourceSet(in ResourceSetDescriptor descriptor) => new VulkanResourceSet(this, descriptor);
    public IAccelerationStructure CreateBottomLevelAccelerationStructure(in BottomLevelAccelerationStructureDescriptor descriptor) => new VulkanAccelerationStructure(this, descriptor);
    public IAccelerationStructure CreateTopLevelAccelerationStructure(in TopLevelAccelerationStructureDescriptor descriptor) => new VulkanAccelerationStructure(this, descriptor);

    public ICommandQueue GetQueue(CommandQueueKind kind)
    {
        if (kind != CommandQueueKind.Graphics)
            throw new NotSupportedException($"Only the graphics queue is available in this milestone (requested {kind}).");
        return _graphicsCommandQueue;
    }

    public IFence CreateFence(bool signaled = false) => new VulkanFence(this, signaled);

    public MemoryUsageInfo QueryMemoryUsage()
    {
        _instanceApi.vkGetPhysicalDeviceMemoryProperties(_physicalDevice, out VkPhysicalDeviceMemoryProperties memProps);
        Span<VmaBudget> budgets = stackalloc VmaBudget[(int)VK_MAX_MEMORY_HEAPS];
        fixed (VmaBudget* pBudgets = budgets)
        {
            vmaGetHeapBudgets(_allocator, pBudgets);
        }

        ulong used = 0, budget = 0;
        for (int i = 0; i < memProps.memoryHeapCount; i++)
        {
            used += budgets[i].usage;
            budget += budgets[i].budget;
        }
        return new MemoryUsageInfo(used, budget);
    }

    public void WaitIdle() => _api.vkDeviceWaitIdle();

    public void Dispose()
    {
        _api.vkDeviceWaitIdle();
        _graphicsCommandQueue.Dispose();
        _api.vkDestroyDescriptorPool(_descriptorPool);
        vmaDestroyAllocator(_allocator);
        _api.vkDestroyDevice();
        _instanceApi.vkDestroyInstance();
    }
}
