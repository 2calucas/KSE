using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// A first-fit, coalescing free-list allocator over one descriptor heap. Program.cs disposes and recreates
/// resource sets on every quality-tier change and window resize, so a simple bump allocator would exhaust a
/// shared heap over a long interactive session — this reclaims freed ranges instead (mirrors the Vulkan
/// backend's descriptor pool, which genuinely frees space via vkFreeDescriptorSets).
/// </summary>
internal sealed class D3D12DescriptorAllocator : IDisposable
{
    private readonly ID3D12DescriptorHeap _heap;
    private readonly int _descriptorSize;
    private readonly List<(int Offset, int Count)> _freeRanges;
    private readonly object _lock = new();

    public ID3D12DescriptorHeap Heap => _heap;
    public bool ShaderVisible { get; }

    public D3D12DescriptorAllocator(ID3D12Device device, DescriptorHeapType type, int capacity, bool shaderVisible)
    {
        ShaderVisible = shaderVisible;
        var desc = new DescriptorHeapDescription(type, capacity, shaderVisible ? DescriptorHeapFlags.ShaderVisible : DescriptorHeapFlags.None);
        _heap = device.CreateDescriptorHeap(desc);
        _descriptorSize = device.GetDescriptorHandleIncrementSize(type);
        _freeRanges = [(0, capacity)];
    }

    public int Allocate(int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < _freeRanges.Count; i++)
            {
                var (offset, freeCount) = _freeRanges[i];
                if (freeCount < count) continue;

                _freeRanges[i] = (offset + count, freeCount - count);
                if (_freeRanges[i].Count == 0) _freeRanges.RemoveAt(i);
                return offset;
            }
            throw new InvalidOperationException("Descriptor heap exhausted — increase its capacity.");
        }
    }

    public void Free(int offset, int count)
    {
        if (count <= 0) return;
        lock (_lock)
        {
            int insertIndex = 0;
            while (insertIndex < _freeRanges.Count && _freeRanges[insertIndex].Offset < offset) insertIndex++;
            _freeRanges.Insert(insertIndex, (offset, count));

            if (insertIndex + 1 < _freeRanges.Count && _freeRanges[insertIndex].Offset + _freeRanges[insertIndex].Count == _freeRanges[insertIndex + 1].Offset)
            {
                var cur = _freeRanges[insertIndex];
                var next = _freeRanges[insertIndex + 1];
                _freeRanges[insertIndex] = (cur.Offset, cur.Count + next.Count);
                _freeRanges.RemoveAt(insertIndex + 1);
            }
            if (insertIndex > 0 && _freeRanges[insertIndex - 1].Offset + _freeRanges[insertIndex - 1].Count == _freeRanges[insertIndex].Offset)
            {
                var prev = _freeRanges[insertIndex - 1];
                var cur = _freeRanges[insertIndex];
                _freeRanges[insertIndex - 1] = (prev.Offset, prev.Count + cur.Count);
                _freeRanges.RemoveAt(insertIndex);
            }
        }
    }

    public CpuDescriptorHandle GetCpuHandle(int offset)
    {
        var start = _heap.GetCPUDescriptorHandleForHeapStart();
        return new CpuDescriptorHandle(start, offset, _descriptorSize);
    }

    public GpuDescriptorHandle GetGpuHandle(int offset)
    {
        var start = _heap.GetGPUDescriptorHandleForHeapStart();
        return new GpuDescriptorHandle(start, offset, _descriptorSize);
    }

    public void Dispose() => _heap.Dispose();
}
