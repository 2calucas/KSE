using Engine.RHI;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// Pure C# bookkeeping — D3D12 has no GPU object equivalent to VkDescriptorSetLayout (the root signature bakes
/// table layouts directly). Precomputes, per binding, its offset within whichever shared heap range a
/// <see cref="D3D12ResourceSet"/> built from this layout will allocate (CBV/SRV/UAV bindings share one range,
/// Sampler bindings a separate one, since D3D12 can't mix the two kinds of range in one descriptor table).
/// </summary>
internal sealed class D3D12ResourceSetLayout : IResourceSetLayout
{
    private readonly Dictionary<uint, int> _resourceOffsets = [];
    private readonly Dictionary<uint, int> _samplerOffsets = [];

    public ResourceSetLayoutDescriptor Descriptor { get; }
    internal int ResourceCount { get; }
    internal int SamplerCount { get; }

    public D3D12ResourceSetLayout(in ResourceSetLayoutDescriptor descriptor)
    {
        Descriptor = descriptor;

        int resourceCursor = 0, samplerCursor = 0;
        foreach (var b in descriptor.Bindings)
        {
            int count = (int)Math.Max(1, b.ArrayCount);
            if (b.Kind == ResourceKind.Sampler)
            {
                _samplerOffsets[b.Binding] = samplerCursor;
                samplerCursor += count;
            }
            else
            {
                _resourceOffsets[b.Binding] = resourceCursor;
                resourceCursor += count;
            }
        }
        ResourceCount = resourceCursor;
        SamplerCount = samplerCursor;
    }

    internal int GetResourceOffset(uint binding) => _resourceOffsets[binding];
    internal int GetSamplerOffset(uint binding) => _samplerOffsets[binding];

    public void Dispose() { }
}
