using Engine.RHI;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Shader;
using Vortice.DXGI;
using D3DPrimitiveTopology = Vortice.Direct3D.PrimitiveTopology;
using D3DBlendOperation = Vortice.Direct3D12.BlendOperation;

namespace Engine.RHI.Direct3D12;

/// <summary>
/// Root signature register numbers are assigned purely by counting per register class (CBV/SRV/Sampler),
/// per shader stage, in <see cref="GraphicsPipelineDescriptor.ResourceSetLayouts"/> declaration order — NOT
/// derived from shader reflection. This works because every HLSL `register(tN/bN/sN)` clause in Shaders.cs is
/// already vestigial for the existing Vulkan backend (SPIR-V codegen only obeys `[[vk::binding]]`) and, by
/// convention, already numbered sequentially in the same order the C# bindings list declares them. Future
/// HLSL added to this codebase must keep declaring same-register-class resources in that same relative order.
/// Reflection is used for exactly two things instead: push-constant size (<see cref="D3D12ShaderModule.GetPushConstantSize"/>)
/// and vertex input semantics.
/// </summary>
internal sealed class D3D12Pipeline : IPipeline
{
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipelineState;

    public bool IsCompute { get; }
    internal ID3D12RootSignature RootSignature => _rootSignature;
    internal ID3D12PipelineState Handle => _pipelineState;
    internal D3DPrimitiveTopology Topology { get; }

    /// <summary>Root parameter index holding this set's CBV/SRV/UAV descriptor table, or -1 if it has none.</summary>
    internal int[] SetResourceRootParam { get; }
    /// <summary>Root parameter index holding this set's Sampler descriptor table, or -1 if it has none.</summary>
    internal int[] SetSamplerRootParam { get; }
    internal int RootConstantsParamIndex { get; }
    internal uint RootConstants32BitCount { get; }

    public D3D12Pipeline(D3D12GraphicsDevice device, in GraphicsPipelineDescriptor descriptor)
    {
        IsCompute = false;
        Topology = ToPrimitiveTopology(descriptor.Topology);

        var vs = (D3D12ShaderModule)descriptor.VertexShader;
        var ps = (D3D12ShaderModule)descriptor.FragmentShader;

        var build = BuildRootSignature(device, descriptor.ResourceSetLayouts, [
            (vs, ShaderStageFlags.Vertex),
            (ps, ShaderStageFlags.Fragment),
        ]);
        _rootSignature = build.RootSignature;
        SetResourceRootParam = build.SetResourceRootParam;
        SetSamplerRootParam = build.SetSamplerRootParam;
        RootConstantsParamIndex = build.RootConstantsParamIndex;
        RootConstants32BitCount = build.RootConstants32BitCount;

        var inputElements = BuildInputLayout(vs, descriptor.VertexBuffers);

        var psoDesc = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vs.Bytecode,
            PixelShader = ps.Bytecode,
            InputLayout = new InputLayoutDescription(inputElements),
            PrimitiveTopologyType = ToTopologyType(descriptor.Topology),
            RasterizerState = ToRasterizerDescription(descriptor.Rasterizer),
            DepthStencilState = ToDepthStencilDescription(descriptor.DepthStencil, descriptor.DepthStencilFormat.HasValue),
            BlendState = ToBlendDescription(descriptor.Blend),
            SampleMask = uint.MaxValue,
            SampleDescription = new SampleDescription(1, 0),
            DepthStencilFormat = descriptor.DepthStencilFormat.HasValue ? D3D12Utils.ToDxgiFormat(descriptor.DepthStencilFormat.Value) : Format.Unknown,
        };
        for (int i = 0; i < descriptor.ColorTargetFormats.Count; i++)
            psoDesc.RenderTargetFormats[i] = D3D12Utils.ToDxgiFormat(descriptor.ColorTargetFormats[i]);
        psoDesc.RenderTargetFormats.RenderTargetCount = (uint)descriptor.ColorTargetFormats.Count;

        _pipelineState = device.Device.CreateGraphicsPipelineState<ID3D12PipelineState>(psoDesc);
    }

    public D3D12Pipeline(D3D12GraphicsDevice device, in ComputePipelineDescriptor descriptor)
    {
        IsCompute = true;
        Topology = PrimitiveTopology.TriangleList;

        var cs = (D3D12ShaderModule)descriptor.ComputeShader;
        var build = BuildRootSignature(device, descriptor.ResourceSetLayouts, [(cs, ShaderStageFlags.Compute)]);
        _rootSignature = build.RootSignature;
        SetResourceRootParam = build.SetResourceRootParam;
        SetSamplerRootParam = build.SetSamplerRootParam;
        RootConstantsParamIndex = build.RootConstantsParamIndex;
        RootConstants32BitCount = build.RootConstants32BitCount;

        var psoDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = cs.Bytecode,
        };
        _pipelineState = device.Device.CreateComputePipelineState<ID3D12PipelineState>(psoDesc);
    }

    private readonly record struct RootSignatureBuild(
        ID3D12RootSignature RootSignature,
        int[] SetResourceRootParam,
        int[] SetSamplerRootParam,
        int RootConstantsParamIndex,
        uint RootConstants32BitCount);

    private static RootSignatureBuild BuildRootSignature(
        D3D12GraphicsDevice device,
        IReadOnlyList<IResourceSetLayout> setLayouts,
        (D3D12ShaderModule Module, ShaderStageFlags Stage)[] stages)
    {
        var rootParams = new List<RootParameter1>();
        var setResourceRootParam = new int[setLayouts.Count];
        var setSamplerRootParam = new int[setLayouts.Count];
        Array.Fill(setResourceRootParam, -1);
        Array.Fill(setSamplerRootParam, -1);

        // Running per-register-class counters, per stage, so push-constant register detection (below) and any
        // future reflective lookups stay consistent with how many registers of each class the layouts already
        // claimed by the time we reach the implicit push-constant cbuffer.
        var cbvCounters = stages.ToDictionary(s => s.Stage, _ => 0u);

        for (int setIndex = 0; setIndex < setLayouts.Count; setIndex++)
        {
            var layout = (D3D12ResourceSetLayout)setLayouts[setIndex];
            var bindings = layout.Descriptor.Bindings;

            var resourceRanges = new List<DescriptorRange1>();
            var samplerRanges = new List<DescriptorRange1>();

            int i = 0;
            while (i < bindings.Count)
            {
                var kind = bindings[i].Kind;
                int start = i;
                while (i < bindings.Count && bindings[i].Kind == kind) i++;
                int count = 0;
                for (int j = start; j < i; j++) count += (int)Math.Max(1, bindings[j].ArrayCount);

                int baseRegister = kind == ResourceKind.Sampler
                    ? CountPriorRegisters(setLayouts, setIndex, ResourceKind.Sampler)
                    : CountPriorRegisters(setLayouts, setIndex, kind, sameClassAs: true);

                var rangeType = ToRangeType(kind);
                var range = new DescriptorRange1(rangeType, count, baseRegister);
                if (kind == ResourceKind.Sampler) samplerRanges.Add(range);
                else resourceRanges.Add(range);

                // Advance each stage's CBV counter for every explicit CBV binding this set claims, so the
                // implicit push-constant cbuffer (computed after the loop) lands at the correct next register.
                if (kind == ResourceKind.UniformBuffer)
                {
                    foreach (var (module, stage) in stages)
                    {
                        for (int j = start; j < i; j++)
                        {
                            if ((bindings[j].Stages & stage) != 0)
                                cbvCounters[stage] += (uint)Math.Max(1, bindings[j].ArrayCount);
                        }
                    }
                }
            }

            if (resourceRanges.Count > 0)
            {
                setResourceRootParam[setIndex] = rootParams.Count;
                rootParams.Add(RootParameter1.FromDescriptorTable([.. resourceRanges], ShaderVisibility.All));
            }
            if (samplerRanges.Count > 0)
            {
                setSamplerRootParam[setIndex] = rootParams.Count;
                rootParams.Add(RootParameter1.FromDescriptorTable([.. samplerRanges], ShaderVisibility.All));
            }
        }

        // Push constants: the largest push-constant block across every stage sharing this pipeline (matches
        // VulkanPipeline's Math.Max over vertex/fragment reflection) becomes one root-constants parameter.
        uint pushConstantSize = 0;
        foreach (var (module, stage) in stages)
            pushConstantSize = Math.Max(pushConstantSize, module.GetPushConstantSize(cbvCounters[stage]));

        int rootConstantsParamIndex = -1;
        uint rootConstants32BitCount = (pushConstantSize + 3) / 4;
        if (rootConstants32BitCount > 0)
        {
            // The implicit push-constant cbuffer always lands at the same register in every stage that has
            // one (each stage's own cbvCounters value at the point it was queried above), so any stage's
            // counter works as the shared root-constants base register.
            uint register = cbvCounters.Values.Max();
            rootConstantsParamIndex = rootParams.Count;
            rootParams.Add(RootParameter1.FromConstants((int)register, 0, (int)rootConstants32BitCount, ShaderVisibility.All));
        }

        var rootSigDesc = new VersionedRootSignatureDescription(new RootSignatureDescription1(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [.. rootParams]));

        using var blob = device.RootSignatureVersion11
            ? rootSigDesc.Serialize()
            : new VersionedRootSignatureDescription(new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                rootParams.Select(p => (RootParameter)p).ToArray())).Serialize();

        var rootSignature = device.Device.CreateRootSignature<ID3D12RootSignature>(0, blob);
        return new RootSignatureBuild(rootSignature, setResourceRootParam, setSamplerRootParam, rootConstantsParamIndex, rootConstants32BitCount);
    }

    /// <summary>How many registers of <paramref name="kind"/>'s class earlier sets in <paramref name="setLayouts"/>
    /// already claimed, plus (when <paramref name="sameClassAs"/> is set) earlier same-class bindings within
    /// this same set — i.e. this range's base register number.</summary>
    private static int CountPriorRegisters(IReadOnlyList<IResourceSetLayout> setLayouts, int uptoSetIndex, ResourceKind kind, bool sameClassAs = false)
    {
        int count = 0;
        for (int s = 0; s < uptoSetIndex; s++)
        {
            var layout = (D3D12ResourceSetLayout)setLayouts[s];
            foreach (var b in layout.Descriptor.Bindings)
                if (IsSameRegisterClass(b.Kind, kind)) count += (int)Math.Max(1, b.ArrayCount);
        }
        return count;
    }

    private static bool IsSameRegisterClass(ResourceKind a, ResourceKind b)
    {
        if (a == b) return true;
        bool IsSrv(ResourceKind k) => k is ResourceKind.SampledTexture or ResourceKind.AccelerationStructure;
        return IsSrv(a) && IsSrv(b);
    }

    private static DescriptorRangeType ToRangeType(ResourceKind kind) => kind switch
    {
        ResourceKind.UniformBuffer => DescriptorRangeType.ConstantBufferView,
        ResourceKind.SampledTexture or ResourceKind.AccelerationStructure => DescriptorRangeType.ShaderResourceView,
        ResourceKind.StorageTexture or ResourceKind.StorageBuffer => DescriptorRangeType.UnorderedAccessView,
        ResourceKind.Sampler => DescriptorRangeType.Sampler,
        _ => throw new NotSupportedException($"Resource kind {kind} is not supported by the D3D12 backend yet."),
    };

    private static InputElementDescription[] BuildInputLayout(D3D12ShaderModule vs, IReadOnlyList<VertexBufferLayout> vertexBuffers)
    {
        var semantics = vs.GetInputSemantics();
        var elements = new List<InputElementDescription>();
        int semanticIndex = 0;
        for (int b = 0; b < vertexBuffers.Count; b++)
        {
            var sortedAttrs = vertexBuffers[b].Attributes.OrderBy(a => a.Location).ToList();
            foreach (var attr in sortedAttrs)
            {
                var (semanticName, semanticIdx) = semantics[semanticIndex++];
                elements.Add(new InputElementDescription(semanticName, semanticIdx, ToVertexFormat(attr.Format), (int)attr.OffsetBytes, b));
            }
        }
        return [.. elements];
    }

    private static Format ToVertexFormat(VertexFormat format) => format switch
    {
        VertexFormat.Float32 => Format.R32_Float,
        VertexFormat.Float32x2 => Format.R32G32_Float,
        VertexFormat.Float32x3 => Format.R32G32B32_Float,
        VertexFormat.Float32x4 => Format.R32G32B32A32_Float,
        VertexFormat.UInt8x4Norm => Format.R8G8B8A8_UNorm,
        VertexFormat.UInt32 => Format.R32_UInt,
        _ => throw new NotSupportedException(),
    };

    private static D3DPrimitiveTopology ToPrimitiveTopology(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList => D3DPrimitiveTopology.TriangleList,
        PrimitiveTopology.TriangleStrip => D3DPrimitiveTopology.TriangleStrip,
        PrimitiveTopology.LineList => D3DPrimitiveTopology.LineList,
        PrimitiveTopology.LineStrip => D3DPrimitiveTopology.LineStrip,
        PrimitiveTopology.PointList => D3DPrimitiveTopology.PointList,
        _ => throw new NotSupportedException(),
    };

    private static PrimitiveTopologyType ToTopologyType(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip => PrimitiveTopologyType.Triangle,
        PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => PrimitiveTopologyType.Line,
        PrimitiveTopology.PointList => PrimitiveTopologyType.Point,
        _ => throw new NotSupportedException(),
    };

    private static RasterizerDescription ToRasterizerDescription(RasterizerState state) => new()
    {
        FillMode = state.Wireframe ? FillMode.Wireframe : FillMode.Solid,
        CullMode = state.CullMode switch
        {
            CullMode.None => Vortice.Direct3D12.CullMode.None,
            CullMode.Front => Vortice.Direct3D12.CullMode.Front,
            _ => Vortice.Direct3D12.CullMode.Back,
        },
        FrontCounterClockwise = state.FrontFace == FrontFace.CounterClockwise,
        DepthClipEnable = !state.DepthClampEnable,
    };

    private static DepthStencilDescription ToDepthStencilDescription(DepthStencilState state, bool hasDepthTarget) => new()
    {
        DepthEnable = hasDepthTarget && state.DepthTestEnable,
        DepthWriteMask = state.DepthWriteEnable ? DepthWriteMask.All : DepthWriteMask.Zero,
        DepthFunc = ToComparisonFunction(state.DepthCompare),
        StencilEnable = false,
    };

    private static ComparisonFunction ToComparisonFunction(CompareFunction fn) => D3D12Utils.ToComparisonFunction(fn);

    private static BlendDescription ToBlendDescription(BlendState state)
    {
        var desc = new BlendDescription();
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = state.Enabled,
            SourceBlend = ToBlend(state.SrcColorFactor),
            DestinationBlend = ToBlend(state.DstColorFactor),
            BlendOperation = ToBlendOp(state.ColorOp),
            SourceBlendAlpha = ToBlend(state.SrcAlphaFactor),
            DestinationBlendAlpha = ToBlend(state.DstAlphaFactor),
            BlendOperationAlpha = ToBlendOp(state.AlphaOp),
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        for (int i = 1; i < 8; i++)
            desc.RenderTarget[i] = desc.RenderTarget[0];
        return desc;
    }

    private static Blend ToBlend(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => Blend.Zero,
        BlendFactor.One => Blend.One,
        BlendFactor.SrcAlpha => Blend.SourceAlpha,
        BlendFactor.OneMinusSrcAlpha => Blend.InverseSourceAlpha,
        BlendFactor.DstAlpha => Blend.DestinationAlpha,
        BlendFactor.OneMinusDstAlpha => Blend.InverseDestinationAlpha,
        BlendFactor.SrcColor => Blend.SourceColor,
        BlendFactor.OneMinusSrcColor => Blend.InverseSourceColor,
        BlendFactor.DstColor => Blend.DestinationColor,
        BlendFactor.OneMinusDstColor => Blend.InverseDestinationColor,
        _ => throw new NotSupportedException(),
    };

    private static D3DBlendOperation ToBlendOp(BlendOperation op) => op switch
    {
        BlendOperation.Add => D3DBlendOperation.Add,
        BlendOperation.Subtract => D3DBlendOperation.Subtract,
        BlendOperation.ReverseSubtract => D3DBlendOperation.ReverseSubtract,
        BlendOperation.Min => D3DBlendOperation.Min,
        BlendOperation.Max => D3DBlendOperation.Max,
        _ => throw new NotSupportedException(),
    };

    public void Dispose()
    {
        _pipelineState.Dispose();
        _rootSignature.Dispose();
    }
}
