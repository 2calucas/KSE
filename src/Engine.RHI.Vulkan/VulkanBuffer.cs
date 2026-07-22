using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Engine.RHI.Vulkan;

internal sealed unsafe class VulkanBuffer : IBuffer
{
    private readonly VulkanGraphicsDevice _device;
    private readonly VkBuffer _buffer;
    private readonly VmaAllocation _allocation;
    private readonly bool _hostVisible;

    public BufferDescriptor Descriptor { get; }
    public ResourceState CurrentState { get; private set; } = ResourceState.Undefined;
    internal ResourceState CurrentStateInternal { set => CurrentState = value; }
    internal VkBuffer Handle => _buffer;

    public VulkanBuffer(VulkanGraphicsDevice device, in BufferDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        _hostVisible = descriptor.Location != MemoryLocation.DeviceLocal;

        VkBufferCreateInfo createInfo = new()
        {
            size = descriptor.SizeBytes,
            usage = VulkanUtils.ToVkBufferUsage(descriptor.Usage),
            sharingMode = VkSharingMode.Exclusive,
        };

        VmaAllocationCreateInfo allocationCreateInfo = new()
        {
            usage = descriptor.Location switch
            {
                MemoryLocation.HostUpload => VmaMemoryUsage.AutoPreferHost,
                MemoryLocation.HostReadback => VmaMemoryUsage.AutoPreferHost,
                _ => VmaMemoryUsage.AutoPreferDevice,
            },
            flags = descriptor.Location switch
            {
                MemoryLocation.HostUpload => VmaAllocationCreateFlags.HostAccessSequentialWrite | VmaAllocationCreateFlags.Mapped,
                MemoryLocation.HostReadback => VmaAllocationCreateFlags.HostAccessRandom | VmaAllocationCreateFlags.Mapped,
                _ => VmaAllocationCreateFlags.None,
            },
        };

        VulkanUtils.CheckResult(
            vmaCreateBuffer(device.Allocator, &createInfo, &allocationCreateInfo, out _buffer, out _allocation, out _),
            "vmaCreateBuffer failed");
    }

    public Span<byte> Map()
    {
        if (!_hostVisible)
            throw new InvalidOperationException("Only HostUpload/HostReadback buffers can be mapped.");

        void* data;
        VulkanUtils.CheckResult(vmaMapMemory(_device.Allocator, _allocation, &data), "vmaMapMemory failed");
        return new Span<byte>(data, (int)Descriptor.SizeBytes);
    }

    public void Unmap() => vmaUnmapMemory(_device.Allocator, _allocation);

    public void Dispose() => vmaDestroyBuffer(_device.Allocator, _buffer, _allocation);
}
