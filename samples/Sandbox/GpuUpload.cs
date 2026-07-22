using Engine.RHI;

namespace Sandbox;

internal static class GpuUpload
{
    public static void UploadTexture(IGraphicsDevice device, ICommandQueue queue, ITexture texture, ReadOnlySpan<byte> pixels, uint width, uint height)
    {
        IBuffer staging = device.CreateBuffer(new BufferDescriptor((ulong)pixels.Length, BufferUsage.CopySource, MemoryLocation.HostUpload));
        pixels.CopyTo(staging.Map());
        staging.Unmap();

        ICommandBuffer cmd = queue.CreateCommandBuffer();
        cmd.Begin();
        cmd.TransitionTexture(texture, ResourceState.Undefined, ResourceState.CopyDestination);
        cmd.CopyBufferToTexture(staging, texture, new TextureCopyRegion(0, 0, 0, 0, 0, 0, width, height, 1));
        cmd.TransitionTexture(texture, ResourceState.CopyDestination, ResourceState.ShaderResource);
        cmd.End();

        IFence fence = device.CreateFence();
        queue.Submit([cmd], fence);
        fence.Wait();

        cmd.Dispose();
        fence.Dispose();
        staging.Dispose();
    }
}
