using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Engine.RHI;

namespace Sandbox;

internal enum QualityTier
{
    Low,
    Medium,
    High,
    RayTracing,
    PerformanceRT,
    PathTracing,
}

/// <summary>
/// A corner-anchored quality-tier picker. Text is rasterized via GDI+ into a small texture (cheap: only
/// re-rasterized when the selection changes, not every frame) and drawn as an alpha-blended screen-space quad.
/// </summary>
internal sealed class UiOverlay : IDisposable
{
    private readonly (QualityTier Tier, string Label, bool Implemented)[] _options;

    private const int TextureWidth = 300;
    private const int TextureHeight = 168;
    private const float Margin = 16f;

    private readonly IGraphicsDevice _device;
    private readonly ICommandQueue _queue;
    private readonly IResourceSetLayout _setLayout;
    private readonly ISampler _sampler;
    private readonly IPipeline _pipeline;
    private readonly IBuffer _vertexBuffer;

    private ITexture? _texture;
    private ITextureView? _textureView;
    private IResourceSet? _resourceSet;
    private int _selectedIndex;
    private bool _textureDirty = true;

    public QualityTier SelectedTier => _options[_selectedIndex].Tier;
    public bool IsSelectedImplemented => _options[_selectedIndex].Implemented;

    public UiOverlay(IGraphicsDevice device, ICommandQueue queue, IShaderModule vertexShader, IShaderModule fragmentShader, TextureFormat colorFormat, bool rayTracingSupported)
    {
        _device = device;
        _queue = queue;

        _options =
        [
            (QualityTier.Low, "Low", true),
            (QualityTier.Medium, "Medium", true),
            (QualityTier.High, "High", true),
            (QualityTier.RayTracing, "Ray Tracing", rayTracingSupported),
            (QualityTier.PerformanceRT, "Performance RT", false),
            (QualityTier.PathTracing, "Path Tracing", false),
        ];

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

    public void CycleNext()
    {
        _selectedIndex = (_selectedIndex + 1) % _options.Length;
        _textureDirty = true;
    }

    public void CyclePrevious()
    {
        _selectedIndex = (_selectedIndex - 1 + _options.Length) % _options.Length;
        _textureDirty = true;
    }

    public void Render(ICommandBuffer cmd, uint screenWidth, uint screenHeight)
    {
        if (_textureDirty || _texture is null)
        {
            RebuildTexture();
            _textureDirty = false;
        }

        WriteQuad(screenWidth, screenHeight);

        cmd.SetPipeline(_pipeline);
        cmd.SetResourceSet(0, _resourceSet!);
        cmd.SetVertexBuffer(0, _vertexBuffer);
        cmd.Draw(6);
    }

    private void WriteQuad(uint screenWidth, uint screenHeight)
    {
        float left = screenWidth - Margin - TextureWidth;
        float right = screenWidth - Margin;
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

    private void RebuildTexture()
    {
        byte[] pixels = new byte[TextureWidth * TextureHeight * 4];

        using (var bmp = new Bitmap(TextureWidth, TextureHeight, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(190, 18, 18, 22));
                using var font = new Font("Consolas", 14f, FontStyle.Bold);
                using var headerBrush = new SolidBrush(Color.Gainsboro);
                g.DrawString("Quality", font, headerBrush, 8, 6);

                float y = 32;
                for (int i = 0; i < _options.Length; i++)
                {
                    var (_, label, implemented) = _options[i];
                    bool selected = i == _selectedIndex;
                    Color color = !implemented ? Color.OrangeRed : selected ? Color.LimeGreen : Color.WhiteSmoke;
                    string prefix = selected ? "> " : "  ";
                    using var brush = new SolidBrush(color);
                    g.DrawString(prefix + label, font, brush, 8, y);
                    y += 22;
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

        // GDI+'s Format32bppArgb is stored in memory as B,G,R,A — matches BGRA8Unorm directly, no swizzle needed.
        _texture = _device.CreateTexture(new TextureDescriptor(TextureWidth, TextureHeight, TextureFormat.BGRA8Unorm, TextureUsage.Sampled | TextureUsage.CopyDestination, Name: "UiOverlay"));
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
