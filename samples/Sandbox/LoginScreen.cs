using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Engine.RHI;

namespace Sandbox;

internal enum AuthMode
{
    Login,
    SignUp,
}

internal enum AuthField
{
    Username,
    Password,
}

/// <summary>Login / sign-up screen rendered entirely through the engine's own pipeline — same GDI+
/// texture-blit technique as <see cref="UiOverlay"/>/<see cref="StatsOverlay"/>, just centered and interactive
/// instead of corner-anchored and read-only. Gates <see cref="Program"/>'s main scene: nothing else in the
/// Sandbox loads until <see cref="IsAuthenticated"/> is true. Text entry comes from <see cref="IWindow.CharInput"/>
/// (WM_CHAR — already shift/caps-lock resolved); Tab/Backspace/Enter/F2 come from <see cref="IWindow.KeyDown"/>.</summary>
internal sealed class LoginScreen : IDisposable
{
    private const int PanelWidth = 420;
    private const int PanelHeight = 280;
    private const int MaxFieldLength = 32;

    private const int VK_TAB = 0x09;
    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_F2 = 0x71;

    private readonly AccountStore _accountStore;
    private readonly IGraphicsDevice _device;
    private readonly ICommandQueue _queue;
    private readonly IResourceSetLayout _setLayout;
    private readonly ISampler _sampler;
    private readonly IPipeline _pipeline;
    private readonly IBuffer _vertexBuffer;

    private ITexture? _texture;
    private ITextureView? _textureView;
    private IResourceSet? _resourceSet;
    private bool _textureDirty = true;

    private readonly System.Text.StringBuilder _username = new();
    private readonly System.Text.StringBuilder _password = new();
    private AuthMode _mode = AuthMode.Login;
    private AuthField _focus = AuthField.Username;
    private string _statusMessage = "";
    private bool _statusIsError;

    public bool IsAuthenticated { get; private set; }
    public string? LoggedInUsername { get; private set; }

    public LoginScreen(AccountStore accountStore, IGraphicsDevice device, ICommandQueue queue,
        IShaderModule vertexShader, IShaderModule fragmentShader, TextureFormat colorFormat)
    {
        _accountStore = accountStore;
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

    public void HandleChar(char c)
    {
        if (IsAuthenticated) return;
        if (c < 0x20 || c == 0x7F) return; // control characters (Tab/Backspace/Enter/Esc) — handled in HandleKeyDown instead

        System.Text.StringBuilder target = _focus == AuthField.Username ? _username : _password;
        if (target.Length < MaxFieldLength)
        {
            target.Append(c);
            _textureDirty = true;
        }
    }

    public void HandleKeyDown(int virtualKeyCode)
    {
        if (IsAuthenticated) return;

        switch (virtualKeyCode)
        {
            case VK_TAB:
                _focus = _focus == AuthField.Username ? AuthField.Password : AuthField.Username;
                _textureDirty = true;
                break;
            case VK_BACK:
                System.Text.StringBuilder target = _focus == AuthField.Username ? _username : _password;
                if (target.Length > 0) target.Length--;
                _textureDirty = true;
                break;
            case VK_RETURN:
                Submit();
                break;
            case VK_F2:
                _mode = _mode == AuthMode.Login ? AuthMode.SignUp : AuthMode.Login;
                _statusMessage = "";
                _textureDirty = true;
                break;
        }
    }

    private void Submit()
    {
        string username = _username.ToString();
        string password = _password.ToString();

        bool ok = _mode == AuthMode.Login
            ? _accountStore.TryLogin(username, password, out string error)
            : _accountStore.TryCreateAccount(username, password, out error);

        if (ok)
        {
            IsAuthenticated = true;
            LoggedInUsername = username.Trim();
            return;
        }

        _password.Clear();
        _statusMessage = error;
        _statusIsError = true;
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
        float left = (screenWidth - PanelWidth) / 2f;
        float top = (screenHeight - PanelHeight) / 2f;
        float right = left + PanelWidth;
        float bottom = top + PanelHeight;

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
        byte[] pixels = new byte[PanelWidth * PanelHeight * 4];

        using (var bmp = new Bitmap(PanelWidth, PanelHeight, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(235, 18, 18, 24));
                g.DrawRectangle(new Pen(Color.FromArgb(255, 70, 70, 78)), 0, 0, PanelWidth - 1, PanelHeight - 1);

                using var titleFont = new Font("Consolas", 16f, FontStyle.Bold);
                using var labelFont = new Font("Consolas", 12f, FontStyle.Regular);
                using var hintFont = new Font("Consolas", 10f, FontStyle.Regular);
                using var titleBrush = new SolidBrush(Color.Gainsboro);
                using var labelBrush = new SolidBrush(Color.WhiteSmoke);
                using var hintBrush = new SolidBrush(Color.FromArgb(255, 130, 130, 138));
                using var errorBrush = new SolidBrush(Color.FromArgb(255, 230, 90, 90));
                using var okBrush = new SolidBrush(Color.FromArgb(255, 120, 220, 120));
                using var focusedPen = new Pen(Color.FromArgb(255, 120, 190, 255), 2f);
                using var idlePen = new Pen(Color.FromArgb(255, 90, 90, 98), 1f);

                string title = _mode == AuthMode.Login ? "KSE — Log In" : "KSE — Create Account";
                g.DrawString(title, titleFont, titleBrush, 16, 14);

                DrawField(g, "Username", _username.ToString(), 16, 60, focused: _focus == AuthField.Username,
                    labelFont, labelBrush, focusedPen, idlePen, mask: false);
                DrawField(g, "Password", _password.ToString(), 16, 130, focused: _focus == AuthField.Password,
                    labelFont, labelBrush, focusedPen, idlePen, mask: true);

                if (_statusMessage.Length > 0)
                {
                    g.DrawString(_statusMessage, labelFont, _statusIsError ? errorBrush : okBrush, 16, 196);
                }

                g.DrawString("Tab: switch field    Enter: submit", hintFont, hintBrush, 16, 228);
                string modeHint = _mode == AuthMode.Login
                    ? "F2: switch to Create Account"
                    : "F2: switch to Log In";
                g.DrawString(modeHint, hintFont, hintBrush, 16, 246);
            }

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, PanelWidth, PanelHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < PanelHeight; row++)
                {
                    nint rowPtr = data.Scan0 + row * data.Stride;
                    Marshal.Copy(rowPtr, pixels, row * PanelWidth * 4, PanelWidth * 4);
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

        _texture = _device.CreateTexture(new TextureDescriptor(PanelWidth, PanelHeight, TextureFormat.BGRA8Unorm, TextureUsage.Sampled | TextureUsage.CopyDestination, Name: "LoginScreen"));
        GpuUpload.UploadTexture(_device, _queue, _texture, pixels, PanelWidth, PanelHeight);
        _textureView = _texture.CreateView(new TextureViewDescriptor(0, 1, 0, 1));
        _resourceSet = _device.CreateResourceSet(new ResourceSetDescriptor(_setLayout,
        [
            new ResourceSetEntry(0, TextureView: _textureView),
            new ResourceSetEntry(1, Sampler: _sampler),
        ]));
    }

    private static void DrawField(Graphics g, string label, string value, int x, int y, bool focused,
        Font labelFont, Brush labelBrush, Pen focusedPen, Pen idlePen, bool mask)
    {
        g.DrawString(label, labelFont, labelBrush, x, y);

        var boxRect = new Rectangle(x, y + 20, PanelWidth - x * 2, 28);
        g.DrawRectangle(focused ? focusedPen : idlePen, boxRect);

        string shown = mask ? new string('•', value.Length) : value;
        if (focused) shown += "|";
        g.DrawString(shown, labelFont, labelBrush, boxRect.X + 6, boxRect.Y + 5);
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
