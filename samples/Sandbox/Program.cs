using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine.RHI;
using Engine.RHI.Vulkan;
using Engine.Windowing;
using Vortice.Dxc;

namespace Sandbox;

[StructLayout(LayoutKind.Sequential)]
internal struct FrameConstants
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 LightViewProjection;
    public Vector4 LightDirection;
    public Vector4 CameraPosition;
    public Vector4 Flags; // x = useRayTracedShadows, y = per-frame GI noise seed, z = bounce sphere's current reflectivity
    public Vector4 RoomLightPosition; // xyz = the indoor room's moving point light position
}

/// <summary>Per-draw material: Albedo.rgb is the base/matte color (ignored for the ground, which computes its
/// own procedural color), Albedo.a is reflectivity (0 = matte, 1 = mirror; only traced when ray tracing is
/// available), ObjectFlags.x selects the procedural ground-grid pattern instead of Albedo.rgb, ObjectFlags.y
/// selects the indoor room's moving point light instead of the outdoor directional sun.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectPushConstants
{
    public Matrix4x4 Model;
    public Vector4 Albedo;
    public Vector4 ObjectFlags;
}

internal static class Program
{
    private const int VK_UP = 0x26;
    private const int VK_DOWN = 0x28;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_SPACE = 0x20;
    private const int VK_RBUTTON = 0x02;
    private const int VK_ESCAPE = 0x1B;

    // TLAS InstanceCustomIndex values — must match the ShadeHit()/ShadeHitTerminal() mapping in Shaders.FragmentRT.
    private const uint InstanceCube = 0;
    private const uint InstancePlane = 1;
    private const uint InstanceSphereMirror = 2;
    private const uint InstanceSphereBounce = 3;
    private const uint InstanceRoomFloor = 4;
    private const uint InstanceRoomCeiling = 5;
    private const uint InstanceRoomBackWall = 6;
    private const uint InstanceRoomRightWall = 7;
    private const uint InstanceRoomLeftWallMirror = 8;
    private const uint InstanceRoomCone = 9;
    private const uint InstanceRoomSphere = 10;
    private const uint InstanceRoomCube = 11;

    private static IShaderModule CompileShader(IGraphicsDevice device, DxcShaderStage stage, string source, ShaderStage engineStage, string name,
        DxcShaderModel? shaderModel = null, int? spirvTargetEnvMajor = null, int? spirvTargetEnvMinor = null)
    {
        var options = new DxcCompilerOptions { GenerateSpirv = true, PackMatrixRowMajor = true };
        if (shaderModel.HasValue) options.ShaderModel = shaderModel.Value;
        if (spirvTargetEnvMajor.HasValue) options.SpvTargetEnvMajor = spirvTargetEnvMajor.Value;
        if (spirvTargetEnvMinor.HasValue) options.SpirvTargetEnvMinor = spirvTargetEnvMinor.Value;
        using var result = DxcCompiler.Compile(stage, source, "main", options, name, [], null!, []);

        string errors = result.GetErrors();
        if (!string.IsNullOrWhiteSpace(errors))
            throw new InvalidOperationException($"Shader compilation failed ({name}):\n{errors}");

        byte[] spirv = result.GetObjectBytecode().ToArray();
        return device.CreateShaderModule(new ShaderModuleDescriptor(spirv, engineStage));
    }

    private static (ITexture Texture, ITextureView View) CreateDepthTarget(IGraphicsDevice device, ICommandQueue queue, uint width, uint height, string name)
    {
        ITexture texture = device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.D32Float, TextureUsage.DepthStencil, Name: name));
        ITextureView view = texture.CreateView(new TextureViewDescriptor(0, 1, 0, 1));

        ICommandBuffer cmd = queue.CreateCommandBuffer();
        cmd.Begin();
        cmd.TransitionTexture(texture, ResourceState.Undefined, ResourceState.DepthStencilWrite);
        cmd.End();
        queue.Submit([cmd]);
        queue.WaitIdle();
        cmd.Dispose();

        return (texture, view);
    }

    private static (ITexture Texture, ITextureView View) CreateShadowMap(IGraphicsDevice device, ICommandQueue queue, uint resolution)
    {
        ITexture texture = device.CreateTexture(new TextureDescriptor(resolution, resolution, TextureFormat.D32Float, TextureUsage.DepthStencil | TextureUsage.Sampled, Name: "ShadowMap"));
        ITextureView view = texture.CreateView(new TextureViewDescriptor(0, 1, 0, 1));

        ICommandBuffer cmd = queue.CreateCommandBuffer();
        cmd.Begin();
        cmd.TransitionTexture(texture, ResourceState.Undefined, ResourceState.ShaderResource);
        cmd.End();
        queue.Submit([cmd]);
        queue.WaitIdle();
        cmd.Dispose();

        return (texture, view);
    }

    private static (ITexture Texture, ITextureView View) CreateSceneColorTarget(IGraphicsDevice device, ICommandQueue queue, uint width, uint height)
    {
        ITexture texture = device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.BGRA8Unorm, TextureUsage.RenderTarget | TextureUsage.Sampled, Name: "SceneColor"));
        ITextureView view = texture.CreateView(new TextureViewDescriptor(0, 1, 0, 1));

        ICommandBuffer cmd = queue.CreateCommandBuffer();
        cmd.Begin();
        cmd.TransitionTexture(texture, ResourceState.Undefined, ResourceState.ShaderResource);
        cmd.End();
        queue.Submit([cmd]);
        queue.WaitIdle();
        cmd.Dispose();

        return (texture, view);
    }

    private static uint ShadowResolutionFor(QualityTier tier) => tier switch
    {
        QualityTier.Low => 512,
        QualityTier.Medium => 1024,
        QualityTier.High => 2048,
        _ => 1024,
    };

    // The actual "performance" lever for Low/Medium/High: render the scene at a fraction of the display
    // resolution, then upscale it onto the swapchain — same knob (and same reason it's fast) as a real
    // engine's render-scale setting, and a preview of what FSR will replace later.
    private static float RenderScaleFor(QualityTier tier) => tier switch
    {
        QualityTier.Low => 0.60f,
        QualityTier.Medium => 0.80f,
        QualityTier.High => 1.00f,
        _ => 1.00f,
    };

    private static void Main()
    {
        const uint requestedWidth = 1024, requestedHeight = 768;
        using IWindow window = new Win32Window("Engine Sandbox", requestedWidth, requestedHeight);
        using IGraphicsDevice device = new VulkanGraphicsDevice(enableValidation: true);
        bool rtSupported = device.Capabilities.HasFlag(RhiCapabilities.RayQuery);
        Console.WriteLine($"Vulkan device: {device.AdapterName} (ray tracing: {(rtSupported ? "supported" : "unavailable")})");

        using ISwapChain swapChain = device.CreateSwapChain(new SwapChainDescriptor(window.Handle, requestedWidth, requestedHeight));
        ICommandQueue queue = device.GetQueue(CommandQueueKind.Graphics);

        uint width = swapChain.Descriptor.Width;
        uint height = swapChain.Descriptor.Height;

        using IShaderModule vertexShader = CompileShader(device, DxcShaderStage.Vertex, Shaders.Vertex, ShaderStage.Vertex, "main.vs.hlsl");
        // Only the fragment-shader variant this GPU can actually use gets compiled: the RT-capable one needs
        // HLSL SM 6.5+ and a SPIR-V target env new enough for SPV_KHR_ray_query (vulkan1.2+).
        using IShaderModule mainFragmentShader = rtSupported
            ? CompileShader(device, DxcShaderStage.Pixel, Shaders.FragmentRT, ShaderStage.Fragment, "main.ps.hlsl", DxcShaderModel.Model6_5, 1, 2)
            : CompileShader(device, DxcShaderStage.Pixel, Shaders.Fragment, ShaderStage.Fragment, "main.ps.hlsl");
        using IShaderModule shadowVertexShader = CompileShader(device, DxcShaderStage.Vertex, Shaders.ShadowVertex, ShaderStage.Vertex, "shadow.vs.hlsl");
        using IShaderModule shadowFragmentShader = CompileShader(device, DxcShaderStage.Pixel, Shaders.ShadowFragment, ShaderStage.Fragment, "shadow.ps.hlsl");
        using IShaderModule uiVertexShader = CompileShader(device, DxcShaderStage.Vertex, Shaders.UiVertex, ShaderStage.Vertex, "ui.vs.hlsl");
        using IShaderModule uiFragmentShader = CompileShader(device, DxcShaderStage.Pixel, Shaders.UiFragment, ShaderStage.Fragment, "ui.ps.hlsl");

        // ---- Login / sign-up gate: nothing below this point (scene geometry, ray tracing, the main render
        // loop) runs until a user has logged in or created an account. Rendered with the engine's own
        // pipeline via LoginScreen, not a separate GUI toolkit, so it's genuinely built into this .exe.
        // Pressing Escape while in the scene logs out and returns here (RunScene returns false) instead of
        // closing the window; closing the window instead (RunScene returns true) ends the loop entirely. ----
        var accountStore = new AccountStore();
        while (true)
        {
            using var loginScreen = new LoginScreen(accountStore, device, queue, uiVertexShader, uiFragmentShader, TextureFormat.BGRA8Unorm);
            string? loggedInUsername = RunLoginGate(window, swapChain, queue, loginScreen, width, height);
            if (loggedInUsername is null)
            {
                return; // window closed before authenticating
            }
            Console.WriteLine($"Logged in as \"{loggedInUsername}\".");
            width = swapChain.Descriptor.Width;
            height = swapChain.Descriptor.Height;

            bool windowClosed = RunScene(window, device, queue, swapChain, rtSupported,
                vertexShader, mainFragmentShader, shadowVertexShader, shadowFragmentShader, uiVertexShader, uiFragmentShader,
                width, height);
            if (windowClosed)
            {
                return;
            }
            Console.WriteLine("Logged out.");
        }
    }

    /// <summary>Builds every scene resource and runs the main render loop for one logged-in session, until
    /// either the window is closed (returns true) or the user presses Escape to log out (returns false, and
    /// the caller in <see cref="Main"/> loops back to <see cref="RunLoginGate"/>).</summary>
    private static bool RunScene(IWindow window, IGraphicsDevice device, ICommandQueue queue, ISwapChain swapChain, bool rtSupported,
        IShaderModule vertexShader, IShaderModule mainFragmentShader, IShaderModule shadowVertexShader, IShaderModule shadowFragmentShader,
        IShaderModule uiVertexShader, IShaderModule uiFragmentShader, uint width, uint height)
    {
        using ISampler shadowSampler = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, MipmapFilterMode.Nearest,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            CompareFunction: CompareFunction.LessEqual));
        using ISampler blitSampler = device.CreateSampler(new SamplerDescriptor(FilterMode.Linear, FilterMode.Linear, MipmapFilterMode.Nearest,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge));

        using IBuffer frameConstantsBuffer = device.CreateBuffer(new BufferDescriptor((ulong)Marshal.SizeOf<FrameConstants>(), BufferUsage.UniformBuffer, MemoryLocation.HostUpload));

        QualityTier activeRasterQuality = QualityTier.Medium;

        // ---- Scene render targets (render-scale-dependent; recreated on resize or Low/Medium/High change) ----
        uint renderWidth = Math.Max(1u, (uint)(width * RenderScaleFor(activeRasterQuality)));
        uint renderHeight = Math.Max(1u, (uint)(height * RenderScaleFor(activeRasterQuality)));
        (ITexture Texture, ITextureView View) sceneColor = CreateSceneColorTarget(device, queue, renderWidth, renderHeight);
        (ITexture Texture, ITextureView View) sceneDepth = CreateDepthTarget(device, queue, renderWidth, renderHeight, "SceneDepth");

        // ---- Shadow map (quality-tier-dependent; recreated when Low/Medium/High changes) ----
        (ITexture Texture, ITextureView View) shadowMap = CreateShadowMap(device, queue, ShadowResolutionFor(activeRasterQuality));

        // ---- Main pass resources: one layout/pipeline regardless of tier — reflection is a material property
        // that's always traced when the GPU supports it, while the shadow *technique* (map vs. ray) is a
        // per-frame flag in FrameConstants tied to the selected quality tier. The acceleration-structure binding
        // only exists when rtSupported, since without it neither shadows nor reflections can be ray traced. ----
        List<ResourceSetLayoutBinding> mainBindings =
        [
            new ResourceSetLayoutBinding(0, ResourceKind.UniformBuffer, ShaderStageFlags.Vertex | ShaderStageFlags.Fragment),
            new ResourceSetLayoutBinding(1, ResourceKind.SampledTexture, ShaderStageFlags.Fragment),
            new ResourceSetLayoutBinding(2, ResourceKind.Sampler, ShaderStageFlags.Fragment),
        ];
        if (rtSupported) mainBindings.Add(new ResourceSetLayoutBinding(3, ResourceKind.AccelerationStructure, ShaderStageFlags.Fragment));
        using IResourceSetLayout mainSetLayout = device.CreateResourceSetLayout(new ResourceSetLayoutDescriptor(mainBindings));

        using IPipeline mainPipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            vertexShader, mainFragmentShader, [mainSetLayout],
            [new VertexBufferLayout(8 * sizeof(float),
            [
                new VertexInputAttribute(0, VertexFormat.Float32x3, 0),
                new VertexInputAttribute(1, VertexFormat.Float32x3, 3 * sizeof(float)),
                new VertexInputAttribute(2, VertexFormat.Float32x2, 6 * sizeof(float)),
            ])],
            [TextureFormat.BGRA8Unorm],
            TextureFormat.D32Float,
            Rasterizer: new RasterizerState(CullMode.None, FrontFace.CounterClockwise),
            DepthStencil: new DepthStencilState(DepthTestEnable: true, DepthWriteEnable: true, DepthCompare: CompareFunction.Less)));

        using IPipeline shadowPipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            shadowVertexShader, shadowFragmentShader, [],
            [new VertexBufferLayout(8 * sizeof(float),
            [
                new VertexInputAttribute(0, VertexFormat.Float32x3, 0),
                new VertexInputAttribute(1, VertexFormat.Float32x3, 3 * sizeof(float)),
                new VertexInputAttribute(2, VertexFormat.Float32x2, 6 * sizeof(float)),
            ])],
            [],
            TextureFormat.D32Float,
            Rasterizer: new RasterizerState(CullMode.None, FrontFace.CounterClockwise),
            DepthStencil: new DepthStencilState(DepthTestEnable: true, DepthWriteEnable: true, DepthCompare: CompareFunction.Less)));

        // ---- Blit ("present") resources: upscale/downscale sceneColor onto the swapchain ----
        using IResourceSetLayout blitSetLayout = device.CreateResourceSetLayout(new ResourceSetLayoutDescriptor(
        [
            new ResourceSetLayoutBinding(0, ResourceKind.SampledTexture, ShaderStageFlags.Fragment),
            new ResourceSetLayoutBinding(1, ResourceKind.Sampler, ShaderStageFlags.Fragment),
        ]));
        IResourceSet BuildBlitResourceSet() => device.CreateResourceSet(new ResourceSetDescriptor(blitSetLayout,
        [
            new ResourceSetEntry(0, TextureView: sceneColor.View),
            new ResourceSetEntry(1, Sampler: blitSampler),
        ]));
        IResourceSet blitResourceSet = BuildBlitResourceSet();

        using IPipeline blitPipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            uiVertexShader, uiFragmentShader, [blitSetLayout],
            [new VertexBufferLayout(4 * sizeof(float),
            [
                new VertexInputAttribute(0, VertexFormat.Float32x2, 0),
                new VertexInputAttribute(1, VertexFormat.Float32x2, 2 * sizeof(float)),
            ])],
            [TextureFormat.BGRA8Unorm],
            Rasterizer: new RasterizerState(CullMode.None, FrontFace.CounterClockwise),
            DepthStencil: new DepthStencilState(DepthTestEnable: false, DepthWriteEnable: false)));

        // Full-screen NDC quad — always covers the whole swapchain image regardless of window size.
        using IBuffer fullscreenQuadBuffer = device.CreateBuffer(new BufferDescriptor(6 * 4 * sizeof(float), BufferUsage.VertexBuffer, MemoryLocation.HostUpload));
        MemoryMarshalCopy<float>(
        [
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f,
            -1f, -1f, 0f, 0f,
        ], fullscreenQuadBuffer);

        // Vertex/index buffers double as acceleration-structure build input when ray tracing is available, so the
        // same buffers back both the rasterized draw calls and the BLAS geometry below — no duplicate copies.
        BufferUsage asInput = rtSupported ? BufferUsage.AccelerationStructureInput : BufferUsage.None;

        var (cubeVertices, cubeIndices) = Cube.Generate();
        using IBuffer cubeVertexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(cubeVertices.Length * sizeof(float)), BufferUsage.VertexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(cubeVertices, cubeVertexBuffer);
        using IBuffer cubeIndexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(cubeIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(cubeIndices, cubeIndexBuffer);

        var (planeVertices, planeIndices) = Plane.Generate();
        using IBuffer planeVertexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(planeVertices.Length * sizeof(float)), BufferUsage.VertexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(planeVertices, planeVertexBuffer);
        using IBuffer planeIndexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(planeIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(planeIndices, planeIndexBuffer);

        // Both outdoor spheres share one mesh/BLAS — only their TLAS instance transform and push-constant
        // material differ. The room's sphere and cube reuse these same buffers/BLAS too (see below).
        var (sphereVertices, sphereIndices) = Sphere.Generate();
        using IBuffer sphereVertexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(sphereVertices.Length * sizeof(float)), BufferUsage.VertexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(sphereVertices, sphereVertexBuffer);
        using IBuffer sphereIndexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(sphereIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(sphereIndices, sphereIndexBuffer);

        // ---- Indoor room geometry: a cube-shaped room (floor/ceiling/back/right/left walls all the same size),
        // so one quad mesh/BLAS is reused for all five surfaces via different TLAS instance transforms — plus a
        // cone for the object inside it. ----
        const float roomHalfSize = 4f;
        const float roomHeight = roomHalfSize * 2f;
        const float roomCenterX = 25f;

        var (quadVertices, quadIndices) = Quad.Generate(roomHalfSize);
        using IBuffer quadVertexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(quadVertices.Length * sizeof(float)), BufferUsage.VertexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(quadVertices, quadVertexBuffer);
        using IBuffer quadIndexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(quadIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(quadIndices, quadIndexBuffer);

        var (coneVertices, coneIndices) = Cone.Generate();
        using IBuffer coneVertexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(coneVertices.Length * sizeof(float)), BufferUsage.VertexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(coneVertices, coneVertexBuffer);
        using IBuffer coneIndexBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(coneIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer | asInput, MemoryLocation.HostUpload));
        MemoryMarshalCopy(coneIndices, coneIndexBuffer);

        // Room surfaces never move — computed once. Each wall's local +Z (the quad's default normal) is
        // rotated to face into the room's interior so N.L lighting works correctly.
        Matrix4x4 roomFloorModel = Matrix4x4.CreateRotationX(-MathF.PI / 2f) * Matrix4x4.CreateTranslation(roomCenterX, 0f, 0f);
        Matrix4x4 roomCeilingModel = Matrix4x4.CreateRotationX(MathF.PI / 2f) * Matrix4x4.CreateTranslation(roomCenterX, roomHeight, 0f);
        Matrix4x4 roomBackWallModel = Matrix4x4.CreateRotationY(-MathF.PI / 2f) * Matrix4x4.CreateTranslation(roomCenterX + roomHalfSize, roomHalfSize, 0f);
        Matrix4x4 roomRightWallModel = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(roomCenterX, roomHalfSize, roomHalfSize);
        Matrix4x4 roomLeftWallModel = Matrix4x4.CreateTranslation(roomCenterX, roomHalfSize, -roomHalfSize);

        Vector3 roomCubePos = new(roomCenterX - 1.8f, 0.5f, -1.8f);
        Vector3 roomSpherePos = new(roomCenterX + 1.8f, 0.6f, 1.4f);
        Vector3 roomConePos = new(roomCenterX + 0.2f, 0f, -0.2f);
        Vector3 roomLightCenter = new(roomCenterX, 2.2f, 0f);

        // ---- Ray tracing: BLAS per static mesh (built once), TLAS holding all instances (rebuilt every frame
        // to track the cube's rotation and the room light's motion — see the per-frame
        // RebuildTopLevelAccelerationStructure call below). ----
        using IAccelerationStructure? cubeBlas = rtSupported
            ? device.CreateBottomLevelAccelerationStructure(new BottomLevelAccelerationStructureDescriptor(
                [new AccelerationStructureGeometryDescriptor(cubeVertexBuffer, 8 * sizeof(float), (uint)(cubeVertices.Length / 8), cubeIndexBuffer, IndexFormat.UInt16, (uint)cubeIndices.Length)]))
            : null;
        using IAccelerationStructure? planeBlas = rtSupported
            ? device.CreateBottomLevelAccelerationStructure(new BottomLevelAccelerationStructureDescriptor(
                [new AccelerationStructureGeometryDescriptor(planeVertexBuffer, 8 * sizeof(float), (uint)(planeVertices.Length / 8), planeIndexBuffer, IndexFormat.UInt16, (uint)planeIndices.Length)]))
            : null;
        using IAccelerationStructure? sphereBlas = rtSupported
            ? device.CreateBottomLevelAccelerationStructure(new BottomLevelAccelerationStructureDescriptor(
                [new AccelerationStructureGeometryDescriptor(sphereVertexBuffer, 8 * sizeof(float), (uint)(sphereVertices.Length / 8), sphereIndexBuffer, IndexFormat.UInt16, (uint)sphereIndices.Length)]))
            : null;
        using IAccelerationStructure? quadBlas = rtSupported
            ? device.CreateBottomLevelAccelerationStructure(new BottomLevelAccelerationStructureDescriptor(
                [new AccelerationStructureGeometryDescriptor(quadVertexBuffer, 8 * sizeof(float), (uint)(quadVertices.Length / 8), quadIndexBuffer, IndexFormat.UInt16, (uint)quadIndices.Length)]))
            : null;
        using IAccelerationStructure? coneBlas = rtSupported
            ? device.CreateBottomLevelAccelerationStructure(new BottomLevelAccelerationStructureDescriptor(
                [new AccelerationStructureGeometryDescriptor(coneVertexBuffer, 8 * sizeof(float), (uint)(coneVertices.Length / 8), coneIndexBuffer, IndexFormat.UInt16, (uint)coneIndices.Length)]))
            : null;

        // Initial transforms are irrelevant — the per-frame loop rebuilds this with real data before it's ever read.
        using IAccelerationStructure? tlas = rtSupported
            ? device.CreateTopLevelAccelerationStructure(new TopLevelAccelerationStructureDescriptor(
                [
                    new AccelerationStructureInstanceDescriptor(cubeBlas!, AccelerationStructureTransform.Identity, InstanceCube),
                    new AccelerationStructureInstanceDescriptor(planeBlas!, AccelerationStructureTransform.Identity, InstancePlane),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.Identity, InstanceSphereMirror),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.Identity, InstanceSphereBounce),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.Identity, InstanceRoomFloor),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.Identity, InstanceRoomCeiling),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.Identity, InstanceRoomBackWall),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.Identity, InstanceRoomRightWall),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.Identity, InstanceRoomLeftWallMirror),
                    new AccelerationStructureInstanceDescriptor(coneBlas!, AccelerationStructureTransform.Identity, InstanceRoomCone),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.Identity, InstanceRoomSphere),
                    new AccelerationStructureInstanceDescriptor(cubeBlas!, AccelerationStructureTransform.Identity, InstanceRoomCube),
                ]))
            : null;

        // Rebuilt alongside the shadow map (Low/Medium/High/RT tier change), since binding 1 references its view.
        IResourceSet BuildMainResourceSet()
        {
            List<ResourceSetEntry> entries =
            [
                new ResourceSetEntry(0, Buffer: frameConstantsBuffer),
                new ResourceSetEntry(1, TextureView: shadowMap.View),
                new ResourceSetEntry(2, Sampler: shadowSampler),
            ];
            if (rtSupported) entries.Add(new ResourceSetEntry(3, AccelerationStructure: tlas));
            return device.CreateResourceSet(new ResourceSetDescriptor(mainSetLayout, entries));
        }
        IResourceSet mainResourceSet = BuildMainResourceSet();

        using var qualityOverlay = new UiOverlay(device, queue, uiVertexShader, uiFragmentShader, TextureFormat.BGRA8Unorm, rtSupported);
        using var statsOverlay = new StatsOverlay(device, queue, uiVertexShader, uiFragmentShader, TextureFormat.BGRA8Unorm);
        using var inputOverlay = new InputOverlay(device, queue, uiVertexShader, uiFragmentShader, TextureFormat.BGRA8Unorm);
        using var gpuUsageMonitor = new GpuUsageMonitor();
        var frameStats = new FrameStats();
        var camera = new Camera(new Vector3(0f, 2.8f, -5.8f), new Vector3(0f, 1.1f, 0f));

        bool loggedOut = false;
        void OnKeyDown(int vk)
        {
            if (vk == VK_DOWN) qualityOverlay.CycleNext();
            else if (vk == VK_UP) qualityOverlay.CyclePrevious();
            else if (vk == VK_ESCAPE) loggedOut = true;
        }
        window.KeyDown += OnKeyDown;

        (uint Width, uint Height)? pendingResize = null;
        void OnResized(uint w, uint h) => pendingResize = (w, h);
        window.Resized += OnResized;

        Dictionary<ITexture, ITextureView> viewCache = [];
        var stopwatch = Stopwatch.StartNew();
        var frameTimer = Stopwatch.StartNew();
        double lastFrameSeconds = 1.0 / 60.0;
        Vector3 lightDirToSource = Vector3.Normalize(new Vector3(0.4f, 0.7f, 0.6f));
        const float cubeHeight = 1.6f;
        const float sphereRadius = 0.6f;
        Vector3 sphereMirrorPos = new(-1.7f, sphereRadius, 0f);
        Vector3 sphereBouncePos = new(1.7f, sphereRadius, 0f);

        void RecreateSceneTargets()
        {
            renderWidth = Math.Max(1u, (uint)(width * RenderScaleFor(activeRasterQuality)));
            renderHeight = Math.Max(1u, (uint)(height * RenderScaleFor(activeRasterQuality)));

            blitResourceSet.Dispose();
            sceneColor.View.Dispose();
            sceneColor.Texture.Dispose();
            sceneDepth.View.Dispose();
            sceneDepth.Texture.Dispose();

            sceneColor = CreateSceneColorTarget(device, queue, renderWidth, renderHeight);
            sceneDepth = CreateDepthTarget(device, queue, renderWidth, renderHeight, "SceneDepth");
            blitResourceSet = BuildBlitResourceSet();
        }

        try
        {
        Console.WriteLine("Rendering. Up/Down arrows cycle quality. Hold Right Mouse to look, WASD to move, Space/Ctrl for up/down, Shift to sprint. Esc to log out. Fly to x=25 to find the indoor room. Close the window to exit.");
        while (!window.ShouldClose && !loggedOut)
        {
            window.PumpMessages();
            if (window.ShouldClose || loggedOut) break;

            if (pendingResize is { } resize)
            {
                foreach (var v in viewCache.Values) v.Dispose();
                viewCache.Clear();

                swapChain.Resize(resize.Width, resize.Height);
                width = swapChain.Descriptor.Width;
                height = swapChain.Descriptor.Height;

                RecreateSceneTargets();
                pendingResize = null;
            }

            if (qualityOverlay.IsSelectedImplemented && qualityOverlay.SelectedTier != activeRasterQuality)
            {
                activeRasterQuality = qualityOverlay.SelectedTier;

                mainResourceSet.Dispose();
                shadowMap.View.Dispose();
                shadowMap.Texture.Dispose();
                shadowMap = CreateShadowMap(device, queue, ShadowResolutionFor(activeRasterQuality));
                mainResourceSet = BuildMainResourceSet();

                RecreateSceneTargets();
            }

            // ---- Camera input: hold RMB to mouse-look, WASD to move, Space/Ctrl for up/down, Shift to sprint.
            // Gated on window focus so held keys don't keep steering the camera while another window is active. ----
            bool hasFocus = window.HasFocus;
            bool lookActive = hasFocus && window.IsKeyDown(VK_RBUTTON);
            window.IsMouseCaptured = lookActive;
            (int mouseDx, int mouseDy) = window.PollMouseDelta();
            if (lookActive) camera.Look(mouseDx, mouseDy);
            else { mouseDx = 0; mouseDy = 0; }

            bool moveForward = hasFocus && window.IsKeyDown('W');
            bool moveBack = hasFocus && window.IsKeyDown('S');
            bool moveLeft = hasFocus && window.IsKeyDown('A');
            bool moveRight = hasFocus && window.IsKeyDown('D');
            bool moveUp = hasFocus && window.IsKeyDown(VK_SPACE);
            bool moveDown = hasFocus && window.IsKeyDown(VK_CONTROL);
            bool sprint = hasFocus && window.IsKeyDown(VK_SHIFT);

            var moveInput = new Vector3(
                (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f),
                (moveUp ? 1f : 0f) - (moveDown ? 1f : 0f),
                (moveForward ? 1f : 0f) - (moveBack ? 1f : 0f));
            if (moveInput != Vector3.Zero)
                camera.Move(Vector3.Normalize(moveInput), (float)lastFrameSeconds, sprint);

            float time = (float)stopwatch.Elapsed.TotalSeconds;
            Matrix4x4 cubeModel = Matrix4x4.CreateRotationY(time) * Matrix4x4.CreateRotationX(time * 0.6f) * Matrix4x4.CreateTranslation(0f, cubeHeight, 0f);
            Matrix4x4 planeModel = Matrix4x4.Identity;
            Matrix4x4 sphereMirrorModel = Matrix4x4.CreateTranslation(sphereMirrorPos);
            Matrix4x4 sphereBounceModel = Matrix4x4.CreateTranslation(sphereBouncePos);
            // Material oscillates between a full mirror and a flat matte orange over a few seconds.
            float bounceReflectivity = 0.5f + 0.5f * MathF.Sin(time * 1.3f);

            // Room objects: the cube gets a slow spin for visual interest, sphere/cone stay put.
            Matrix4x4 roomCubeModel = Matrix4x4.CreateRotationY(time * 0.5f) * Matrix4x4.CreateTranslation(roomCubePos);
            Matrix4x4 roomSphereModel = Matrix4x4.CreateTranslation(roomSpherePos);
            Matrix4x4 roomConeModel = Matrix4x4.CreateTranslation(roomConePos);

            // The room's point light weaves a Lissajous-like path around/between the three objects.
            Vector3 roomLightPos = roomLightCenter + new Vector3(
                MathF.Sin(time * 0.6f) * 2.2f,
                MathF.Sin(time * 0.9f) * 1.0f,
                MathF.Cos(time * 0.45f) * 2.0f);

            bool useRayTracedShadows = rtSupported && activeRasterQuality == QualityTier.RayTracing;

            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, width / (float)height, 0.1f, 100f);
            projection.M22 *= -1f;
            Matrix4x4 viewProjection = view * projection;

            Vector3 lightCameraPos = lightDirToSource * 6f + new Vector3(0f, cubeHeight, 0f);
            Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightCameraPos, new Vector3(0f, cubeHeight, 0f), Vector3.UnitY);
            Matrix4x4 lightProjection = Matrix4x4.CreateOrthographic(9f, 9f, 0.1f, 20f);
            lightProjection.M22 *= -1f;
            Matrix4x4 lightViewProjection = lightView * lightProjection;

            var frameConstants = new FrameConstants
            {
                ViewProjection = viewProjection,
                LightViewProjection = lightViewProjection,
                LightDirection = new Vector4(lightDirToSource, 0f),
                CameraPosition = new Vector4(camera.Position, 1f),
                // y = a per-frame seed for the GI hemisphere-sample hash, so its dithering animates as grain
                // instead of freezing into a static pattern when the camera and scene are still.
                // z = the bounce sphere's live reflectivity, so another surface's reflection ray that hits it
                // shows its *current* appearance (mirror or matte) instead of a fixed placeholder.
                Flags = new Vector4(useRayTracedShadows ? 1f : 0f, time * 1000f, bounceReflectivity, 0f),
                RoomLightPosition = new Vector4(roomLightPos, 1f),
            };
            MemoryMarshalCopy([frameConstants], frameConstantsBuffer);

            ITexture backBuffer = swapChain.AcquireNextTexture();
            if (!viewCache.TryGetValue(backBuffer, out ITextureView? backBufferView))
            {
                backBufferView = backBuffer.CreateView(new TextureViewDescriptor(0, 1, 0, 1));
                viewCache[backBuffer] = backBufferView;
            }

            ICommandBuffer cmd = queue.CreateCommandBuffer();
            cmd.Begin();

            // ---- Ray tracing: refresh the TLAS with this frame's transforms (the outdoor cube spins, the room
            // cube spins slower, and the room light moves — everything else here is static but still needs to
            // be in every rebuild call since it rebuilds the whole TLAS, not just what changed). ----
            if (rtSupported)
            {
                cmd.RebuildTopLevelAccelerationStructure(tlas!, new TopLevelAccelerationStructureDescriptor(
                [
                    new AccelerationStructureInstanceDescriptor(cubeBlas!, AccelerationStructureTransform.FromEngineMatrix(cubeModel), InstanceCube),
                    new AccelerationStructureInstanceDescriptor(planeBlas!, AccelerationStructureTransform.FromEngineMatrix(planeModel), InstancePlane),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.FromEngineMatrix(sphereMirrorModel), InstanceSphereMirror),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.FromEngineMatrix(sphereBounceModel), InstanceSphereBounce),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.FromEngineMatrix(roomFloorModel), InstanceRoomFloor),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.FromEngineMatrix(roomCeilingModel), InstanceRoomCeiling),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.FromEngineMatrix(roomBackWallModel), InstanceRoomBackWall),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.FromEngineMatrix(roomRightWallModel), InstanceRoomRightWall),
                    new AccelerationStructureInstanceDescriptor(quadBlas!, AccelerationStructureTransform.FromEngineMatrix(roomLeftWallModel), InstanceRoomLeftWallMirror),
                    new AccelerationStructureInstanceDescriptor(coneBlas!, AccelerationStructureTransform.FromEngineMatrix(roomConeModel), InstanceRoomCone),
                    new AccelerationStructureInstanceDescriptor(sphereBlas!, AccelerationStructureTransform.FromEngineMatrix(roomSphereModel), InstanceRoomSphere),
                    new AccelerationStructureInstanceDescriptor(cubeBlas!, AccelerationStructureTransform.FromEngineMatrix(roomCubeModel), InstanceRoomCube),
                ]));
            }

            // ---- Shadow pass (outdoor sun only — the room's point light has no shadow map, it's always ray
            // traced; the room's own geometry doesn't need to appear in this pass at all). ----
            cmd.TransitionTexture(shadowMap.Texture, ResourceState.ShaderResource, ResourceState.DepthStencilWrite);
            cmd.BeginRenderPass(new RenderPassDescriptor([], new DepthStencilAttachment(shadowMap.View, ClearDepth: true, ClearDepthValue: 1f)));
            cmd.SetPipeline(shadowPipeline);
            cmd.SetViewport(new Viewport(0, 0, shadowMap.Texture.Descriptor.Width, shadowMap.Texture.Descriptor.Height));
            cmd.SetScissor(new ScissorRect(0, 0, shadowMap.Texture.Descriptor.Width, shadowMap.Texture.Descriptor.Height));

            cmd.SetVertexBuffer(0, cubeVertexBuffer);
            cmd.SetIndexBuffer(cubeIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(ShaderStageFlags.Vertex, cubeModel * lightViewProjection);
            cmd.DrawIndexed((uint)cubeIndices.Length);

            cmd.SetVertexBuffer(0, planeVertexBuffer);
            cmd.SetIndexBuffer(planeIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(ShaderStageFlags.Vertex, planeModel * lightViewProjection);
            cmd.DrawIndexed((uint)planeIndices.Length);

            cmd.SetVertexBuffer(0, sphereVertexBuffer);
            cmd.SetIndexBuffer(sphereIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(ShaderStageFlags.Vertex, sphereMirrorModel * lightViewProjection);
            cmd.DrawIndexed((uint)sphereIndices.Length);
            cmd.PushConstants(ShaderStageFlags.Vertex, sphereBounceModel * lightViewProjection);
            cmd.DrawIndexed((uint)sphereIndices.Length);

            cmd.EndRenderPass();
            cmd.TransitionTexture(shadowMap.Texture, ResourceState.DepthStencilWrite, ResourceState.ShaderResource);

            // ---- Main pass: renders into sceneColor/sceneDepth at the current render scale, not natively ----
            cmd.TransitionTexture(sceneColor.Texture, ResourceState.ShaderResource, ResourceState.RenderTarget);
            cmd.BeginRenderPass(new RenderPassDescriptor(
                [new ColorAttachment(sceneColor.View, true, 0.08f, 0.08f, 0.12f, 1f)],
                new DepthStencilAttachment(sceneDepth.View, ClearDepth: true, ClearDepthValue: 1f)));
            cmd.SetPipeline(mainPipeline);
            cmd.SetViewport(new Viewport(0, 0, renderWidth, renderHeight));
            cmd.SetScissor(new ScissorRect(0, 0, renderWidth, renderHeight));
            cmd.SetResourceSet(0, mainResourceSet);

            const ShaderStageFlags objectStages = ShaderStageFlags.Vertex | ShaderStageFlags.Fragment;
            var useGroundPattern = new Vector4(1f, 0f, 0f, 0f);
            var useRoomLight = new Vector4(0f, 1f, 0f, 0f);

            cmd.SetVertexBuffer(0, cubeVertexBuffer);
            cmd.SetIndexBuffer(cubeIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = cubeModel, Albedo = new Vector4(1f, 1f, 1f, 0f), ObjectFlags = Vector4.Zero });
            cmd.DrawIndexed((uint)cubeIndices.Length);

            cmd.SetVertexBuffer(0, planeVertexBuffer);
            cmd.SetIndexBuffer(planeIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = planeModel, Albedo = Vector4.Zero, ObjectFlags = useGroundPattern });
            cmd.DrawIndexed((uint)planeIndices.Length);

            cmd.SetVertexBuffer(0, sphereVertexBuffer);
            cmd.SetIndexBuffer(sphereIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = sphereMirrorModel, Albedo = new Vector4(0.8f, 0.8f, 0.85f, 1.0f), ObjectFlags = Vector4.Zero });
            cmd.DrawIndexed((uint)sphereIndices.Length);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = sphereBounceModel, Albedo = new Vector4(1.0f, 0.45f, 0.1f, bounceReflectivity), ObjectFlags = Vector4.Zero });
            cmd.DrawIndexed((uint)sphereIndices.Length);

            // ---- Indoor room: floor/ceiling/back/right walls are flat colors, left wall is a mirror, all lit
            // by the moving point light (ObjectFlags.y = useRoomLight) instead of the outdoor sun. ----
            cmd.SetVertexBuffer(0, quadVertexBuffer);
            cmd.SetIndexBuffer(quadIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomFloorModel, Albedo = new Vector4(0.15f, 0.75f, 0.25f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)quadIndices.Length);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomCeilingModel, Albedo = new Vector4(0.92f, 0.92f, 0.92f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)quadIndices.Length);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomBackWallModel, Albedo = new Vector4(0.80f, 0.15f, 0.15f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)quadIndices.Length);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomRightWallModel, Albedo = new Vector4(0.20f, 0.35f, 0.90f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)quadIndices.Length);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomLeftWallModel, Albedo = new Vector4(0.8f, 0.8f, 0.85f, 1.0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)quadIndices.Length);

            cmd.SetVertexBuffer(0, coneVertexBuffer);
            cmd.SetIndexBuffer(coneIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomConeModel, Albedo = new Vector4(0.65f, 0.30f, 0.85f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)coneIndices.Length);

            cmd.SetVertexBuffer(0, sphereVertexBuffer);
            cmd.SetIndexBuffer(sphereIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomSphereModel, Albedo = new Vector4(0.95f, 0.80f, 0.15f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)sphereIndices.Length);

            cmd.SetVertexBuffer(0, cubeVertexBuffer);
            cmd.SetIndexBuffer(cubeIndexBuffer, IndexFormat.UInt16);
            cmd.PushConstants(objectStages, new ObjectPushConstants { Model = roomCubeModel, Albedo = new Vector4(0.20f, 0.85f, 0.80f, 0f), ObjectFlags = useRoomLight });
            cmd.DrawIndexed((uint)cubeIndices.Length);

            cmd.EndRenderPass();
            cmd.TransitionTexture(sceneColor.Texture, ResourceState.RenderTarget, ResourceState.ShaderResource);

            // ---- Blit pass: upscale/downscale sceneColor onto the swapchain at native resolution ----
            cmd.TransitionTexture(backBuffer, backBuffer.CurrentState, ResourceState.RenderTarget);
            cmd.BeginRenderPass(new RenderPassDescriptor([new ColorAttachment(backBufferView, true, 0f, 0f, 0f, 1f)]));
            cmd.SetPipeline(blitPipeline);
            cmd.SetViewport(new Viewport(0, 0, width, height));
            cmd.SetScissor(new ScissorRect(0, 0, width, height));
            cmd.SetResourceSet(0, blitResourceSet);
            cmd.SetVertexBuffer(0, fullscreenQuadBuffer);
            cmd.Draw(6);
            cmd.EndRenderPass();

            // ---- UI overlay pass (native resolution, drawn on top) ----
            cmd.BeginRenderPass(new RenderPassDescriptor([new ColorAttachment(backBufferView, Clear: false)]));
            qualityOverlay.Render(cmd, width, height);
            statsOverlay.Render(cmd, width, height, frameStats, device.QueryMemoryUsage(), gpuUsageMonitor.GetUsagePercent());
            inputOverlay.Render(cmd, width, height, new InputState(
                moveForward, moveBack, moveLeft, moveRight, moveUp, moveDown, sprint, lookActive,
                mouseDx, mouseDy, camera.Position, camera.Yaw * (180f / MathF.PI), camera.Pitch * (180f / MathF.PI)));
            cmd.EndRenderPass();

            cmd.TransitionTexture(backBuffer, ResourceState.RenderTarget, ResourceState.Present);
            cmd.End();

            queue.Submit([cmd]);
            swapChain.Present();
            cmd.Dispose();

            frameStats.AddFrame(frameTimer.Elapsed.TotalMilliseconds);
            lastFrameSeconds = frameTimer.Elapsed.TotalSeconds;
            frameTimer.Restart();
        }

        device.WaitIdle();
        foreach (var view in viewCache.Values)
            view.Dispose();
        sceneColor.View.Dispose();
        sceneColor.Texture.Dispose();
        sceneDepth.View.Dispose();
        sceneDepth.Texture.Dispose();
        shadowMap.View.Dispose();
        shadowMap.Texture.Dispose();
        mainResourceSet.Dispose();
        blitResourceSet.Dispose();
        }
        finally
        {
            // Unsubscribe before returning so a subsequent RunScene call (after logging back in) doesn't
            // stack a second handler referencing this session's now-disposed qualityOverlay/pendingResize.
            window.KeyDown -= OnKeyDown;
            window.Resized -= OnResized;
        }

        return window.ShouldClose;
    }

    /// <summary>Pumps messages and renders <paramref name="loginScreen"/> exclusively until it reports a
    /// successful login/sign-up or the window is closed. Returns the logged-in username, or null if the
    /// window was closed first (the caller should exit immediately in that case, without building the scene).</summary>
    private static string? RunLoginGate(IWindow window, ISwapChain swapChain, ICommandQueue queue, LoginScreen loginScreen, uint width, uint height)
    {
        void OnChar(char c) => loginScreen.HandleChar(c);
        void OnKeyDown(int vk) => loginScreen.HandleKeyDown(vk);
        (uint Width, uint Height)? pendingResize = null;
        void OnResized(uint w, uint h) => pendingResize = (w, h);

        window.CharInput += OnChar;
        window.KeyDown += OnKeyDown;
        window.Resized += OnResized;

        Dictionary<ITexture, ITextureView> viewCache = [];
        try
        {
            while (!window.ShouldClose && !loginScreen.IsAuthenticated)
            {
                window.PumpMessages();
                if (window.ShouldClose) break;

                if (pendingResize is { } resize)
                {
                    foreach (var v in viewCache.Values) v.Dispose();
                    viewCache.Clear();

                    swapChain.Resize(resize.Width, resize.Height);
                    width = swapChain.Descriptor.Width;
                    height = swapChain.Descriptor.Height;
                    pendingResize = null;
                }

                ITexture backBuffer = swapChain.AcquireNextTexture();
                if (!viewCache.TryGetValue(backBuffer, out ITextureView? backBufferView))
                {
                    backBufferView = backBuffer.CreateView(new TextureViewDescriptor(0, 1, 0, 1));
                    viewCache[backBuffer] = backBufferView;
                }

                ICommandBuffer cmd = queue.CreateCommandBuffer();
                cmd.Begin();
                cmd.TransitionTexture(backBuffer, backBuffer.CurrentState, ResourceState.RenderTarget);
                cmd.BeginRenderPass(new RenderPassDescriptor([new ColorAttachment(backBufferView, true, 0.05f, 0.05f, 0.08f, 1f)]));
                cmd.SetViewport(new Viewport(0, 0, width, height));
                cmd.SetScissor(new ScissorRect(0, 0, width, height));
                loginScreen.Render(cmd, width, height);
                cmd.EndRenderPass();
                cmd.TransitionTexture(backBuffer, ResourceState.RenderTarget, ResourceState.Present);
                cmd.End();

                queue.Submit([cmd]);
                swapChain.Present();
                cmd.Dispose();
            }
        }
        finally
        {
            window.CharInput -= OnChar;
            window.KeyDown -= OnKeyDown;
            window.Resized -= OnResized;
            queue.WaitIdle();
            foreach (var view in viewCache.Values)
                view.Dispose();
        }

        return window.ShouldClose ? null : loginScreen.LoggedInUsername;
    }

    private static void MemoryMarshalCopy<T>(T[] source, IBuffer destination) where T : unmanaged
    {
        Span<byte> dest = destination.Map();
        MemoryMarshal.AsBytes<T>(source).CopyTo(dest);
        destination.Unmap();
    }
}
