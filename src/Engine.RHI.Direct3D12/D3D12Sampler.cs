using Engine.RHI;
using Vortice.Direct3D12;

namespace Engine.RHI.Direct3D12;

/// <summary>D3D12 has no persistent sampler object — this just holds the description value until a
/// <see cref="D3D12ResourceSet"/> materializes it into a heap slot at bind time.</summary>
internal sealed class D3D12Sampler : ISampler
{
    public SamplerDescriptor Descriptor { get; }
    internal SamplerDescription Description { get; }

    public D3D12Sampler(in SamplerDescriptor descriptor)
    {
        Descriptor = descriptor;
        bool comparison = descriptor.CompareFunction.HasValue;
        Description = new SamplerDescription
        {
            Filter = D3D12Utils.ToFilter(descriptor.MinFilter, descriptor.MagFilter, descriptor.MipmapFilter, descriptor.MaxAnisotropy > 1.0f, comparison),
            AddressU = D3D12Utils.ToAddressMode(descriptor.AddressModeU),
            AddressV = D3D12Utils.ToAddressMode(descriptor.AddressModeV),
            AddressW = D3D12Utils.ToAddressMode(descriptor.AddressModeW),
            MaxAnisotropy = (uint)descriptor.MaxAnisotropy,
            ComparisonFunc = comparison ? D3D12Utils.ToComparisonFunction(descriptor.CompareFunction!.Value) : ComparisonFunction.Never,
            MinLod = descriptor.MinLod,
            MaxLod = descriptor.MaxLod,
        };
    }

    public void Dispose() { }
}
