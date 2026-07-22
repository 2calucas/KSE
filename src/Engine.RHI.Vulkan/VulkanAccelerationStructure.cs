using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Engine.RHI;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Engine.RHI.Vulkan;

/// <summary>
/// Bottom-level acceleration structures are built once from static local-space geometry. Top-level acceleration
/// structures additionally keep a persistent scratch buffer and a host-visible instance buffer so they can be
/// rebuilt every frame (via <see cref="RecordRebuild"/>) to track animated instance transforms without
/// reallocating anything.
/// </summary>
internal sealed unsafe class VulkanAccelerationStructure : IAccelerationStructure
{
    private const ulong InstanceDataAlignment = 16;

    private readonly VulkanGraphicsDevice _device;

    private VkBuffer _storageBuffer;
    private VmaAllocation _storageAllocation;
    private VkAccelerationStructureKHR _handle;

    // Top-level only: persistent across rebuilds.
    private VkBuffer _scratchBuffer;
    private VmaAllocation _scratchAllocation;
    private ulong _scratchDeviceAddress;
    private VulkanBuffer? _instancesBuffer;
    private int _instanceCapacity;

    public AccelerationStructureKind Kind { get; }
    internal VkAccelerationStructureKHR Handle => _handle;
    internal ulong DeviceAddress { get; private set; }

    // ---- Bottom-level: static geometry, built once ----
    public VulkanAccelerationStructure(VulkanGraphicsDevice device, in BottomLevelAccelerationStructureDescriptor descriptor)
    {
        _device = device;
        Kind = AccelerationStructureKind.BottomLevel;

        int geometryCount = descriptor.Geometries.Count;
        var geometries = new VkAccelerationStructureGeometryKHR[geometryCount];
        var rangeInfos = new VkAccelerationStructureBuildRangeInfoKHR[geometryCount];
        var maxPrimitiveCounts = new uint[geometryCount];

        for (int i = 0; i < geometryCount; i++)
        {
            var geo = descriptor.Geometries[i];
            uint primitiveCount = geo.IndexCount / 3;
            maxPrimitiveCounts[i] = primitiveCount;

            geometries[i] = new VkAccelerationStructureGeometryKHR
            {
                geometryType = VkGeometryTypeKHR.Triangles,
                flags = geo.Opaque ? VkGeometryFlagsKHR.Opaque : VkGeometryFlagsKHR.None,
                geometry = new VkAccelerationStructureGeometryDataKHR
                {
                    triangles = new VkAccelerationStructureGeometryTrianglesDataKHR
                    {
                        vertexFormat = VkFormat.R32G32B32Sfloat,
                        vertexData = new VkDeviceOrHostAddressConstKHR { deviceAddress = GetBufferDeviceAddress(device, ((VulkanBuffer)geo.VertexBuffer).Handle) },
                        vertexStride = geo.VertexStrideBytes,
                        maxVertex = geo.VertexCount - 1,
                        indexType = geo.IndexFormat == IndexFormat.UInt16 ? VkIndexType.Uint16 : VkIndexType.Uint32,
                        indexData = new VkDeviceOrHostAddressConstKHR { deviceAddress = GetBufferDeviceAddress(device, ((VulkanBuffer)geo.IndexBuffer).Handle) },
                    },
                },
            };

            rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR { primitiveCount = primitiveCount };
        }

        fixed (VkAccelerationStructureGeometryKHR* pGeometries = geometries)
        fixed (uint* pMaxPrimitiveCounts = maxPrimitiveCounts)
        fixed (VkAccelerationStructureBuildRangeInfoKHR* pRangeInfos = rangeInfos)
        {
            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new()
            {
                type = VkAccelerationStructureTypeKHR.BottomLevel,
                flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace,
                mode = VkBuildAccelerationStructureModeKHR.Build,
                geometryCount = (uint)geometryCount,
                pGeometries = pGeometries,
            };

            VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new();
            device.Api.vkGetAccelerationStructureBuildSizesKHR(VkAccelerationStructureBuildTypeKHR.Device, &buildInfo, pMaxPrimitiveCounts, &sizeInfo);

            CreateStorage(device, sizeInfo.accelerationStructureSize, VkAccelerationStructureTypeKHR.BottomLevel);
            (VkBuffer scratchBuffer, VmaAllocation scratchAllocation, ulong scratchAddress) = CreateScratchBuffer(device, sizeInfo.buildScratchSize);

            buildInfo.dstAccelerationStructure = _handle;
            buildInfo.scratchData = new VkDeviceOrHostAddressKHR { deviceAddress = scratchAddress };

            VkAccelerationStructureBuildRangeInfoKHR* pRangeInfosPtr = pRangeInfos;

            ICommandQueue queue = device.GetQueue(CommandQueueKind.Graphics);
            ICommandBuffer cmdWrapper = queue.CreateCommandBuffer();
            cmdWrapper.Begin();
            device.Api.vkCmdBuildAccelerationStructuresKHR(((VulkanCommandBuffer)cmdWrapper).Handle, 1, &buildInfo, &pRangeInfosPtr);
            cmdWrapper.End();
            queue.Submit([cmdWrapper]);
            queue.WaitIdle();
            cmdWrapper.Dispose();

            vmaDestroyBuffer(device.Allocator, scratchBuffer, scratchAllocation);
        }

        VkAccelerationStructureDeviceAddressInfoKHR addressInfo = new() { accelerationStructure = _handle };
        DeviceAddress = device.Api.vkGetAccelerationStructureDeviceAddressKHR(&addressInfo);
    }

    // ---- Top-level: allocates persistent scratch/instance buffers, then performs the first build ----
    public VulkanAccelerationStructure(VulkanGraphicsDevice device, in TopLevelAccelerationStructureDescriptor descriptor)
    {
        _device = device;
        Kind = AccelerationStructureKind.TopLevel;
        _instanceCapacity = descriptor.Instances.Count;

        // +InstanceDataAlignment bytes of padding: the spec requires the instance array's device address to be
        // 16-byte aligned, which a plain VMA allocation doesn't guarantee, so RecordRebuild rounds the address
        // up itself and writes starting at the corresponding byte offset into this buffer.
        _instancesBuffer = (VulkanBuffer)device.CreateBuffer(new BufferDescriptor(
            (ulong)(_instanceCapacity * sizeof(VkAccelerationStructureInstanceKHR)) + InstanceDataAlignment,
            BufferUsage.AccelerationStructureInput,
            MemoryLocation.HostUpload,
            "TLAS Instances"));

        VkAccelerationStructureGeometryKHR sizingGeometry = new()
        {
            geometryType = VkGeometryTypeKHR.Instances,
            geometry = new VkAccelerationStructureGeometryDataKHR { instances = new VkAccelerationStructureGeometryInstancesDataKHR() },
        };
        VkAccelerationStructureBuildGeometryInfoKHR sizingInfo = new()
        {
            type = VkAccelerationStructureTypeKHR.TopLevel,
            flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace,
            mode = VkBuildAccelerationStructureModeKHR.Build,
            geometryCount = 1,
            pGeometries = &sizingGeometry,
        };
        uint primitiveCount = (uint)_instanceCapacity;
        VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new();
        device.Api.vkGetAccelerationStructureBuildSizesKHR(VkAccelerationStructureBuildTypeKHR.Device, &sizingInfo, &primitiveCount, &sizeInfo);

        CreateStorage(device, sizeInfo.accelerationStructureSize, VkAccelerationStructureTypeKHR.TopLevel);
        (_scratchBuffer, _scratchAllocation, _scratchDeviceAddress) = CreateScratchBuffer(device, sizeInfo.buildScratchSize);

        ICommandQueue queue = device.GetQueue(CommandQueueKind.Graphics);
        ICommandBuffer cmdWrapper = queue.CreateCommandBuffer();
        cmdWrapper.Begin();
        RecordRebuild(((VulkanCommandBuffer)cmdWrapper).Handle, device.Api, descriptor);
        cmdWrapper.End();
        queue.Submit([cmdWrapper]);
        queue.WaitIdle();
        cmdWrapper.Dispose();
    }

    /// <summary>Rewrites the instance buffer with the given transforms and re-records a full rebuild (not an
    /// incremental refit — at the instance counts this engine deals with, a full rebuild is simpler and the
    /// cost difference is negligible) plus the barrier needed before a shader reads the result.</summary>
    internal void RecordRebuild(VkCommandBuffer cmd, VkDeviceApi api, in TopLevelAccelerationStructureDescriptor descriptor)
    {
        if (descriptor.Instances.Count != _instanceCapacity)
            throw new InvalidOperationException($"Top-level acceleration structure was created for {_instanceCapacity} instances but rebuild was given {descriptor.Instances.Count}.");

        ulong baseAddress = GetBufferDeviceAddress(_device, _instancesBuffer!.Handle);
        ulong instancesAddress = AlignUp(baseAddress, InstanceDataAlignment);
        int byteOffset = (int)(instancesAddress - baseAddress);

        Span<byte> mapped = _instancesBuffer.Map();
        var instances = MemoryMarshal.Cast<byte, VkAccelerationStructureInstanceKHR>(mapped[byteOffset..]);
        for (int i = 0; i < descriptor.Instances.Count; i++)
        {
            var inst = descriptor.Instances[i];
            var blas = (VulkanAccelerationStructure)inst.BottomLevel;
            var t = inst.Transform;
            instances[i] = new VkAccelerationStructureInstanceKHR
            {
                transform = new VkTransformMatrixKHR(t.M00, t.M01, t.M02, t.M03, t.M10, t.M11, t.M12, t.M13, t.M20, t.M21, t.M22, t.M23),
                instanceCustomIndex = inst.InstanceCustomIndex,
                mask = inst.Mask,
                instanceShaderBindingTableRecordOffset = 0,
                flags = VkGeometryInstanceFlagsKHR.None,
                accelerationStructureReference = blas.DeviceAddress,
            };
        }
        _instancesBuffer.Unmap();

        VkAccelerationStructureGeometryKHR geometry = new()
        {
            geometryType = VkGeometryTypeKHR.Instances,
            geometry = new VkAccelerationStructureGeometryDataKHR
            {
                instances = new VkAccelerationStructureGeometryInstancesDataKHR
                {
                    arrayOfPointers = false,
                    data = new VkDeviceOrHostAddressConstKHR { deviceAddress = instancesAddress },
                },
            },
        };

        VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new()
        {
            type = VkAccelerationStructureTypeKHR.TopLevel,
            flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace,
            mode = VkBuildAccelerationStructureModeKHR.Build,
            dstAccelerationStructure = _handle,
            geometryCount = 1,
            pGeometries = &geometry,
            scratchData = new VkDeviceOrHostAddressKHR { deviceAddress = _scratchDeviceAddress },
        };

        VkAccelerationStructureBuildRangeInfoKHR rangeInfo = new() { primitiveCount = (uint)_instanceCapacity };
        VkAccelerationStructureBuildRangeInfoKHR* pRangeInfo = &rangeInfo;
        api.vkCmdBuildAccelerationStructuresKHR(cmd, 1, &buildInfo, &pRangeInfo);

        VkMemoryBarrier2 barrier = new()
        {
            srcStageMask = VkPipelineStageFlags2.AccelerationStructureBuildKHR,
            srcAccessMask = VkAccessFlags2.AccelerationStructureWriteKHR,
            dstStageMask = VkPipelineStageFlags2.FragmentShader,
            dstAccessMask = VkAccessFlags2.AccelerationStructureReadKHR,
        };
        VkDependencyInfo dependencyInfo = new() { memoryBarrierCount = 1, pMemoryBarriers = &barrier };
        api.vkCmdPipelineBarrier2(cmd, &dependencyInfo);
    }

    private void CreateStorage(VulkanGraphicsDevice device, ulong size, VkAccelerationStructureTypeKHR type)
    {
        VkBufferCreateInfo bufferCreateInfo = new()
        {
            size = size,
            // vkGetAccelerationStructureDeviceAddressKHR requires the backing buffer to also carry
            // ShaderDeviceAddress, even though it's the acceleration structure's address being queried.
            usage = VkBufferUsageFlags.AccelerationStructureStorageKHR | VkBufferUsageFlags.ShaderDeviceAddress,
            sharingMode = VkSharingMode.Exclusive,
        };
        VmaAllocationCreateInfo allocationCreateInfo = new() { usage = VmaMemoryUsage.AutoPreferDevice };
        VulkanUtils.CheckResult(
            vmaCreateBuffer(device.Allocator, &bufferCreateInfo, &allocationCreateInfo, out _storageBuffer, out _storageAllocation, out _),
            "vmaCreateBuffer (acceleration structure storage) failed");

        VkAccelerationStructureCreateInfoKHR createInfo = new()
        {
            buffer = _storageBuffer,
            offset = 0,
            size = size,
            type = type,
        };
        VkAccelerationStructureKHR handle;
        VulkanUtils.CheckResult(device.Api.vkCreateAccelerationStructureKHR(&createInfo, &handle), "vkCreateAccelerationStructureKHR failed");
        _handle = handle;
    }

    // Scratch buffers must start at a device address aligned to the GPU-reported
    // minAccelerationStructureScratchOffsetAlignment (256 on this RX 7900 XT) — VMA has no way to request that
    // directly, so over-allocate and round the returned address up ourselves.
    private static (VkBuffer Buffer, VmaAllocation Allocation, ulong Address) CreateScratchBuffer(VulkanGraphicsDevice device, ulong size)
    {
        ulong alignment = Math.Max(device.MinAccelerationStructureScratchOffsetAlignment, 1);
        VkBufferCreateInfo bufferCreateInfo = new()
        {
            size = size + alignment,
            usage = VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.ShaderDeviceAddress,
            sharingMode = VkSharingMode.Exclusive,
        };
        VmaAllocationCreateInfo allocationCreateInfo = new() { usage = VmaMemoryUsage.AutoPreferDevice };
        VulkanUtils.CheckResult(
            vmaCreateBuffer(device.Allocator, &bufferCreateInfo, &allocationCreateInfo, out VkBuffer buffer, out VmaAllocation allocation, out _),
            "vmaCreateBuffer (acceleration structure scratch) failed");
        return (buffer, allocation, AlignUp(GetBufferDeviceAddress(device, buffer), alignment));
    }

    private static ulong GetBufferDeviceAddress(VulkanGraphicsDevice device, VkBuffer buffer)
    {
        VkBufferDeviceAddressInfo info = new() { buffer = buffer };
        return device.Api.vkGetBufferDeviceAddress(&info);
    }

    private static ulong AlignUp(ulong value, ulong alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    public void Dispose()
    {
        _device.Api.vkDestroyAccelerationStructureKHR(_handle);
        vmaDestroyBuffer(_device.Allocator, _storageBuffer, _storageAllocation);
        if (Kind == AccelerationStructureKind.TopLevel)
        {
            vmaDestroyBuffer(_device.Allocator, _scratchBuffer, _scratchAllocation);
            _instancesBuffer?.Dispose();
        }
    }
}
