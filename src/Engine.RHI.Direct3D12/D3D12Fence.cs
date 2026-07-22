using Engine.RHI;
using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

internal sealed class D3D12Fence : IFence
{
    private readonly ID3D12Fence _fence;
    private readonly System.Threading.EventWaitHandle _event;
    private ulong _nextValue = 1;

    internal ID3D12Fence Handle => _fence;

    public D3D12Fence(D3D12GraphicsDevice device, bool signaled)
    {
        ulong initial = signaled ? _nextValue : 0;
        _fence = device.Device.CreateFence(initial, FenceFlags.None);
        _event = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset);
        if (signaled)
            _nextValue++;
    }

    public bool IsSignaled => _fence.CompletedValue >= _nextValue - 1;

    /// <summary>Signals the fence to its next value from the given queue — called by the owning
    /// <see cref="D3D12CommandQueue"/> on submit, not by user code directly.</summary>
    internal ulong SignalFrom(ID3D12CommandQueue queue)
    {
        ulong value = _nextValue++;
        queue.Signal(_fence, value);
        return value;
    }

    public void Wait(ulong timeoutNanoseconds = ulong.MaxValue)
    {
        ulong target = _nextValue - 1;
        if (_fence.CompletedValue >= target)
            return;

        _fence.SetEventOnCompletion(target, _event);
        uint timeoutMs = timeoutNanoseconds == ulong.MaxValue ? uint.MaxValue : (uint)Math.Min(timeoutNanoseconds / 1_000_000, uint.MaxValue);
        _event.WaitOne((int)timeoutMs);
    }

    public void Reset()
    {
        // D3D12 fences are monotonically increasing counters, not resettable booleans like VkFence — the next
        // Wait()/IsSignaled check simply targets the next SignalFrom() value, so there is nothing to do here.
    }

    public void Dispose()
    {
        _event.Dispose();
        _fence.Dispose();
    }
}
