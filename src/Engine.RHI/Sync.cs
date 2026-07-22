namespace Engine.RHI;

public interface IFence : IDisposable
{
    bool IsSignaled { get; }
    void Wait(ulong timeoutNanoseconds = ulong.MaxValue);
    void Reset();
}
