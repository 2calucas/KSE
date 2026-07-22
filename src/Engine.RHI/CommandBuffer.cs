namespace Engine.RHI;

public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth = 0.0f, float MaxDepth = 1.0f);

public readonly record struct ScissorRect(int X, int Y, uint Width, uint Height);

public readonly record struct ColorAttachment(
    ITextureView Target,
    bool Clear = true,
    float ClearR = 0, float ClearG = 0, float ClearB = 0, float ClearA = 1);

public readonly record struct DepthStencilAttachment(
    ITextureView Target,
    bool ClearDepth = true,
    float ClearDepthValue = 1.0f,
    bool ClearStencil = false,
    byte ClearStencilValue = 0);

public readonly record struct RenderPassDescriptor(
    IReadOnlyList<ColorAttachment> ColorAttachments,
    DepthStencilAttachment? DepthStencilAttachment = null);

public interface ICommandBuffer : IDisposable
{
    void Begin();
    void End();

    void BeginRenderPass(in RenderPassDescriptor descriptor);
    void EndRenderPass();

    void SetPipeline(IPipeline pipeline);
    void SetResourceSet(uint setIndex, IResourceSet set);
    void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0);
    void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0);
    void SetViewport(in Viewport viewport);
    void SetScissor(in ScissorRect rect);
    void PushConstants<T>(ShaderStageFlags stages, in T data, uint offset = 0) where T : unmanaged;

    void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);
    void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ = 1);

    void CopyBuffer(IBuffer src, IBuffer dst, ulong size, ulong srcOffset = 0, ulong dstOffset = 0);
    void CopyBufferToTexture(IBuffer src, ITexture dst, in TextureCopyRegion region);

    void TransitionTexture(ITexture texture, ResourceState from, ResourceState to);
    void TransitionBuffer(IBuffer buffer, ResourceState from, ResourceState to);

    /// <summary>Rebuilds a top-level acceleration structure in place (e.g. every frame, to track animated
    /// instance transforms) and inserts the barrier needed before it's read by a shader later in this command
    /// buffer. The instance count in <paramref name="descriptor"/> must match the one it was created with.</summary>
    void RebuildTopLevelAccelerationStructure(IAccelerationStructure accelerationStructure, in TopLevelAccelerationStructureDescriptor descriptor);
}

public enum CommandQueueKind
{
    Graphics,
    Compute,
    Copy,
}

public interface ICommandQueue
{
    CommandQueueKind Kind { get; }
    ICommandBuffer CreateCommandBuffer();

    /// <summary>Submits command buffers for execution, optionally signaling a fence on completion.</summary>
    void Submit(IReadOnlyList<ICommandBuffer> commandBuffers, IFence? signalFence = null);
    void WaitIdle();
}
