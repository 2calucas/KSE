using Engine.RHI;
using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// Owns one shared <see cref="ID3D12CommandAllocator"/>, reset only right after <see cref="WaitIdle"/> — safe
/// because every call site in this milestone (the swapchain's forced per-Present WaitIdle, and every one-shot
/// setup helper) already guarantees the GPU is idle before the next Begin()/Reset() happens. No per-frame
/// allocator ring is needed under that constraint (mirrors the same simplification the Vulkan backend's
/// swapchain documents for itself).
/// </summary>
internal sealed class D3D12CommandQueue : ICommandQueue, IDisposable
{
    private readonly D3D12GraphicsDevice _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12Fence _idleFence;
    private readonly System.Threading.EventWaitHandle _idleEvent;
    private ulong _idleFenceValue;

    public CommandQueueKind Kind { get; }
    internal ID3D12CommandQueue Handle => _queue;
    internal ID3D12CommandAllocator Allocator => _allocator;

    public D3D12CommandQueue(D3D12GraphicsDevice device, CommandQueueKind kind, CommandListType listType)
    {
        _device = device;
        Kind = kind;

        var queueDesc = new CommandQueueDescription(listType, CommandQueuePriority.Normal, CommandQueueFlags.None);
        _queue = device.Device.CreateCommandQueue(queueDesc);
        _allocator = device.Device.CreateCommandAllocator(listType);
        _idleFence = device.Device.CreateFence(0, FenceFlags.None);
        _idleEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset);
    }

    public ICommandBuffer CreateCommandBuffer() => new D3D12CommandBuffer(_device, _allocator);

    public void Submit(IReadOnlyList<ICommandBuffer> commandBuffers, IFence? signalFence = null)
    {
        var lists = new ID3D12CommandList[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
            lists[i] = ((D3D12CommandBuffer)commandBuffers[i]).List;

        _queue.ExecuteCommandLists(lists);

        if (signalFence is D3D12Fence fence)
            fence.SignalFrom(_queue);
    }

    internal void Signal(ID3D12Fence fence, ulong value) => _queue.Signal(fence, value);

    public void WaitIdle()
    {
        ulong value = ++_idleFenceValue;
        _queue.Signal(_idleFence, value);
        if (_idleFence.CompletedValue < value)
        {
            _idleFence.SetEventOnCompletion(value, _idleEvent);
            _idleEvent.WaitOne();
        }

        // Safe to reuse now: the GPU has finished every command list recorded against this allocator so far.
        _allocator.Reset();
    }

    public void Dispose()
    {
        _idleEvent.Dispose();
        _idleFence.Dispose();
        _allocator.Dispose();
        _queue.Dispose();
    }
}
