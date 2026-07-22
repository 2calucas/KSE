using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine.RHI;

namespace Sandbox;

internal readonly record struct InputState(
    bool Forward, bool Back, bool Left, bool Right, bool Up, bool Down, bool Sprint, bool LookActive,
    int MouseDx, int MouseDy, Vector3 Position, float YawDegrees, float PitchDegrees);

/// <summary>Side panel (left edge, directly below the stats HUD) that prints live input state: which movement
/// keys are held, whether mouse-look is active and its current delta, and the resulting camera pose. Same GDI+
/// texture-blit technique as the other overlays, but rebuilds only when the reported state actually changes.</summary>
internal sealed class InputOverlay : IDisposable
{
    private const int TextureWidth = 280;
    private const int TextureHeight = 210;
    private const float Margin = 16f;
    private const float TopOffset = 164f; // sits directly below StatsOverlay (132 tall + 16 margin + 16 gap)

    private readonly IGraphicsDevice _device;
    private readonly ICommandQueue _queue;
    private readonly IResourceSetLayout _setLayout;
    private readonly ISampler _sampler;
    private readonly IPipeline _pipeline;
    private readonly IBuffer _vertexBuffer;

    private ITexture? _texture;
    private ITextureView? _textureView;
    private IResourceSet? _resourceSet;
    private InputState? _lastState;

    public InputOverlay(IGraphicsDevice device, ICommandQueue queue, IShaderModule vertexShader, IShaderModule fragmentShader, TextureFormat colorFormat)
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

    public void Render(ICommandBuffer cmd, uint screenWidth, uint screenHeight, InputState input)
    {
        if (_texture is null || _lastState != input)
        {
            RebuildTexture(input);
            _lastState = input;
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
        float top = TopOffset;
        float bottom = TopOffset + TextureHeight;

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

    private void RebuildTexture(InputState input)
    {
        byte[] pixels = new byte[TextureWidth * TextureHeight * 4];

        using (var bmp = new Bitmap(TextureWidth, TextureHeight, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(190, 18, 18, 22));
                using var font = new Font("Consolas", 13f, FontStyle.Bold);
                using var hintFont = new Font("Consolas", 11f, FontStyle.Regular);
                using var headerBrush = new SolidBrush(Color.Gainsboro);
                using var activeBrush = new SolidBrush(Color.FromArgb(255, 120, 220, 120));
                using var idleBrush = new SolidBrush(Color.FromArgb(255, 110, 110, 116));
                using var valueBrush = new SolidBrush(Color.WhiteSmoke);

                g.DrawString("Input", font, headerBrush, 8, 6);

                void DrawKey(string label, bool held, float x, float y)
                    => g.DrawString(label, font, held ? activeBrush : idleBrush, x, y);

                const float row1 = 30f;
                DrawKey("W", input.Forward, 8, row1);
                DrawKey("A", input.Left, 32, row1);
                DrawKey("S", input.Back, 56, row1);
                DrawKey("D", input.Right, 80, row1);
                DrawKey("Shift", input.Sprint, 120, row1);
                DrawKey("Space", input.Up, 185, row1);

                const float row2 = row1 + 22f;
                DrawKey("Ctrl(down)", input.Down, 8, row2);
                DrawKey("RMB(look)", input.LookActive, 130, row2);

                float y = row2 + 30f;
                g.DrawString($"Mouse dX/dY: {input.MouseDx,4} / {input.MouseDy,4}", font, valueBrush, 8, y);
                y += 20f;
                g.DrawString($"Pos: {input.Position.X,6:0.0} {input.Position.Y,6:0.0} {input.Position.Z,6:0.0}", font, valueBrush, 8, y);
                y += 20f;
                g.DrawString($"Yaw/Pitch: {input.YawDegrees,6:0} / {input.PitchDegrees,5:0}", font, valueBrush, 8, y);
                y += 24f;
                g.DrawString("Hold Right Mouse to look, WASD to move", hintFont, headerBrush, 8, y);
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

        _texture = _device.CreateTexture(new TextureDescriptor(TextureWidth, TextureHeight, TextureFormat.BGRA8Unorm, TextureUsage.Sampled | TextureUsage.CopyDestination, Name: "InputOverlay"));
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
