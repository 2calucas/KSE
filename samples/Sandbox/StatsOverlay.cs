using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Engine.RHI;

namespace Sandbox;

/// <summary>Top-left performance HUD: FPS, frame time, 1%/10% lows, GPU usage, VRAM. Same GDI+ texture-blit
/// technique as UiOverlay, refreshed on a timer instead of on selection change.</summary>
internal sealed class StatsOverlay : IDisposable
{
    private const int TextureWidth = 300;
    private const int TextureHeight = 132;
    private const float Margin = 16f;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly IGraphicsDevice _device;
    private readonly ICommandQueue _queue;
    private readonly IResourceSetLayout _setLayout;
    private readonly ISampler _sampler;
    private readonly IPipeline _pipeline;
    private readonly IBuffer _vertexBuffer;

    private ITexture? _texture;
    private ITextureView? _textureView;
    private IResourceSet? _resourceSet;
    private DateTime _lastRefresh = DateTime.MinValue;

    public StatsOverlay(IGraphicsDevice device, ICommandQueue queue, IShaderModule vertexShader, IShaderModule fragmentShader, TextureFormat colorFormat)
    {
        _device = device;
        _queue = queue;

        _setLayout = device.CreateResourceSetLayout(new ResourceSetLayoutDescriptor(
        [
            new ResourceSetLayoutBinding(0, ResourceKind.SampledTexture, ShaderStageFlags.Fragment),
            new ResourceSetLayoutBinding(1, ResourceKind.Sampler, ShaderStageFlags.Fragment),
        ]));
        _sampler = device.CreateSampler(new SamplerDescriptor(FilterMode.Linear, FilterMode.Linear, MipmapFilterMode.Nearest));

        _pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            vertexShader,
            fragmentShader,
            [_setLayout],
            [new VertexBufferLayout(4 * sizeof(float),
            [
                new VertexInputAttribute(0, VertexFormat.Float32x2, 0),
                new VertexInputAttribute(1, VertexFormat.Float32x2, 2 * sizeof(float)),
            ])],
            [colorFormat],
            Rasterizer: new RasterizerState(CullMode.None, FrontFace.CounterClockwise),
            DepthStencil: new DepthStencilState(DepthTestEnable: false, DepthWriteEnable: false),
            Blend: new BlendState(true, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)));

        _vertexBuffer = device.CreateBuffer(new BufferDescriptor(6 * 4 * sizeof(float), BufferUsage.VertexBuffer, MemoryLocation.HostUpload));
    }

    public void Render(ICommandBuffer cmd, uint screenWidth, uint screenHeight, FrameStats stats, MemoryUsageInfo memory, float? gpuUsagePercent)
    {
        DateTime now = DateTime.UtcNow;
        if (_texture is null || now - _lastRefresh >= RefreshInterval)
        {
            RebuildTexture(stats, memory, gpuUsagePercent);
            _lastRefresh = now;
        }

        WriteQuad(screenWidth, screenHeight);

        cmd.SetPipeline(_pipeline);
        cmd.SetResourceSet(0, _resourceSet!);
        cmd.SetVertexBuffer(0, _vertexBuffer);
        cmd.Draw(6);
    }

    private void WriteQuad(uint screenWidth, uint screenHeight)
    {
        float left = Margin;
        float right = Margin + TextureWidth;
        float top = Margin;
        float bottom = Margin + TextureHeight;

        float ToNdcX(float px) => px / screenWidth * 2f - 1f;
        float ToNdcY(float py) => py / screenHeight * 2f - 1f;

        float l = ToNdcX(left), r = ToNdcX(right), t = ToNdcY(top), b = ToNdcY(bottom);

        float[] vertices =
        [
            l, t, 0f, 0f,
            r, t, 1f, 0f,
            r, b, 1f, 1f,
            r, b, 1f, 1f,
            l, b, 0f, 1f,
            l, t, 0f, 0f,
        ];

        Span<byte> dest = _vertexBuffer.Map();
        MemoryMarshal.AsBytes<float>(vertices).CopyTo(dest);
        _vertexBuffer.Unmap();
    }

    private void RebuildTexture(FrameStats stats, MemoryUsageInfo memory, float? gpuUsagePercent)
    {
        byte[] pixels = new byte[TextureWidth * TextureHeight * 4];

        using (var bmp = new Bitmap(TextureWidth, TextureHeight, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(190, 18, 18, 22));
                using var font = new Font("Consolas", 14f, FontStyle.Bold);
                using var headerBrush = new SolidBrush(Color.Gainsboro);
                using var valueBrush = new SolidBrush(Color.WhiteSmoke);

                g.DrawString("Performance", font, headerBrush, 8, 6);

                string gpuText = gpuUsagePercent.HasValue ? $"{gpuUsagePercent.Value:0}%" : "N/A";
                double usedMb = memory.UsedBytes / (1024.0 * 1024.0);
                double budgetMb = memory.BudgetBytes / (1024.0 * 1024.0);

                string[] lines =
                [
                    $"FPS: {stats.CurrentFps,6:0.0}  ({stats.LastFrameMs,5:0.00} ms)",
                    $"Avg: {stats.AverageFps,6:0.0}",
                    $"1% Low: {stats.Low1PercentFps,6:0.0}",
                    $"10% Low: {stats.Low10PercentFps,5:0.0}",
                    $"GPU: {gpuText}",
                    $"VRAM: {usedMb,6:0} / {budgetMb,0:0} MB",
                ];

                float y = 32;
                foreach (var line in lines)
                {
                    g.DrawString(line, font, valueBrush, 8, y);
                    y += 17;
                }
            }

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, TextureWidth, TextureHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < TextureHeight; row++)
                {
                    nint rowPtr = data.Scan0 + row * data.Stride;
                    Marshal.Copy(rowPtr, pixels, row * TextureWidth * 4, TextureWidth * 4);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        _resourceSet?.Dispose();
        _textureView?.Dispose();
        _texture?.Dispose();

        _texture = _device.CreateTexture(new TextureDescriptor(TextureWidth, TextureHeight, TextureFormat.BGRA8Unorm, TextureUsage.Sampled | TextureUsage.CopyDestination, Name: "StatsOverlay"));
        GpuUpload.UploadTexture(_device, _queue, _texture, pixels, TextureWidth, TextureHeight);
        _textureView = _texture.CreateView(new TextureViewDescriptor(0, 1, 0, 1));
        _resourceSet = _device.CreateResourceSet(new ResourceSetDescriptor(_setLayout,
        [
            new ResourceSetEntry(0, TextureView: _textureView),
            new ResourceSetEntry(1, Sampler: _sampler),
        ]));
    }

    public void Dispose()
    {
        _resourceSet?.Dispose();
        _textureView?.Dispose();
        _texture?.Dispose();
        _vertexBuffer.Dispose();
        _pipeline.Dispose();
        _sampler.Dispose();
        _setLayout.Dispose();
    }
}
