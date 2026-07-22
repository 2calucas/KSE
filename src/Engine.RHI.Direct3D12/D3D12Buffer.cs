using Engine.RHI;
using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

internal sealed class D3D12Buffer : IBuffer
{
    private readonly ID3D12Resource _resource;
    private readonly bool _hostVisible;

    public BufferDescriptor Descriptor { get; }
    public ResourceState CurrentState { get; private set; }
    internal ResourceState CurrentStateInternal { set => CurrentState = value; }
    internal ID3D12Resource Handle => _resource;

    public D3D12Buffer(D3D12GraphicsDevice device, in BufferDescriptor descriptor)
    {
        Descriptor = descriptor;
        _hostVisible = descriptor.Location != MemoryLocation.DeviceLocal;

        var heapProperties = new HeapProperties(D3D12Utils.ToHeapType(descriptor.Location));
        var resourceDesc = ResourceDescription.Buffer(descriptor.SizeBytes, D3D12Utils.ToResourceFlags(descriptor.Usage));

        ResourceStates initialState = D3D12Utils.InitialBufferState(descriptor.Location);
        _resource = device.Device.CreateCommittedResource(heapProperties, HeapFlags.None, resourceDesc, initialState);
        CurrentState = _hostVisible ? (descriptor.Location == MemoryLocation.HostUpload ? ResourceState.General : ResourceState.CopyDestination) : ResourceState.Undefined;
    }

    public Span<byte> Map()
    {
        if (!_hostVisible)
            throw new InvalidOperationException("Only HostUpload/HostReadback buffers can be mapped.");

        unsafe
        {
            void* data = _resource.Map(0).ToPointer();
            return new Span<byte>(data, (int)Descriptor.SizeBytes);
        }
    }

    public void Unmap() => _resource.Unmap(0);

    public void Dispose() => _resource.Dispose();
}
