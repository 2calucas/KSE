using Engine.RHI;
using Vortice.Dxc;
using Vortice.Direct3D12.Shader;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// DX12 reflection has a real gap Vulkan's SPIRV-Reflect doesn't: reflecting straight off the final DXIL
/// bytecode bytes (all this class ever receives — see <see cref="ShaderModuleDescriptor"/>) returns correct
/// register/space/size data but every resource/cbuffer *name* comes back empty. So unlike the Vulkan backend,
/// nothing here is looked up by name — <see cref="GetPushConstantSize"/> identifies the implicit push-constant
/// cbuffer purely by which register number the pipeline expects it at (see D3D12Pipeline), and resource
/// register assignment for root-signature construction doesn't consult reflection at all (also see
/// D3D12Pipeline) — reflection is used only for push-constant sizing and vertex input semantics.
/// </summary>
internal sealed class D3D12ShaderModule : IShaderModule
{
    public ShaderStage Stage { get; }
    public string EntryPoint { get; }
    public ShaderReflectionInfo Reflection { get; } = new();

    internal byte[] Bytecode { get; }
    internal ID3D12ShaderReflection DxilReflection { get; }

    public D3D12ShaderModule(in ShaderModuleDescriptor descriptor)
    {
        Stage = descriptor.Stage;
        EntryPoint = descriptor.EntryPoint;
        Bytecode = descriptor.SpirvBytecode.ToArray();

        // DxcCompiler.Utils is a process-wide shared singleton - never dispose it, doing so corrupts every
        // subsequent DXC operation (compiles included) elsewhere in the process.
        var utils = DxcCompiler.Utils;
        unsafe
        {
            fixed (byte* pData = Bytecode)
            {
                using var blob = utils.CreateBlob((nint)pData, (uint)Bytecode.Length, 0u);
                DxilReflection = utils.CreateReflection<ID3D12ShaderReflection>(blob);
            }
        }
    }

    /// <summary>Byte size of this stage's implicit push-constant cbuffer (from a loose
    /// <c>[[vk::push_constant]]</c> global — the attribute itself is ignored by DXIL codegen, but the
    /// variable still needs a constant-buffer home), or 0 if this stage has none. <paramref name="explicitCbvCount"/>
    /// is how many CBV registers this pipeline's own resource-set layouts already explicitly claim for this
    /// stage — the implicit cbuffer always lands at exactly that next register, by construction (see the
    /// class-level remarks).</summary>
    internal uint GetPushConstantSize(uint explicitCbvCount)
    {
        var desc = DxilReflection.Description;
        uint cbvOrdinal = 0;
        for (uint i = 0; i < desc.BoundResources; i++)
        {
            var res = DxilReflection.GetResourceBindingDescription(i);
            if (res.Type != ShaderInputType.ConstantBuffer) continue;

            if (res.BindPoint == explicitCbvCount)
                return DxilReflection.GetConstantBufferByIndex(cbvOrdinal).Description.Size;
            cbvOrdinal++;
        }
        return 0;
    }

    /// <summary>Vertex input semantics in declaration order — HLSL semantic strings aren't part of the
    /// engine's numeric <see cref="VertexInputAttribute.Location"/>, so a vertex shader's own reflected input
    /// signature is the only source for them, paired positionally against <c>VertexBuffers[*].Attributes[*]</c>
    /// sorted by ascending <see cref="VertexInputAttribute.Location"/>.</summary>
    internal (string SemanticName, uint SemanticIndex)[] GetInputSemantics()
    {
        var desc = DxilReflection.Description;
        var result = new (string, uint)[desc.InputParameters];
        for (uint i = 0; i < desc.InputParameters; i++)
        {
            var p = DxilReflection.GetInputParameterDescription(i);
            result[i] = (p.SemanticName, p.SemanticIndex);
        }
        return result;
    }

    public void Dispose() => DxilReflection.Dispose();
}
