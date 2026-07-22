using System.Runtime.InteropServices;

namespace Engine.Windowing;

public sealed partial class Win32Window : IWindow
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int SW_SHOW = 5;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_CHAR = 0x0102;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public nint Handle { get; }
    public bool ShouldClose { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public event Action<uint, uint>? Resized;
    public event Action<int>? KeyDown;
    public event Action<char>? CharInput;

    public bool HasFocus => GetForegroundWindow() == Handle;

    public bool IsKeyDown(int virtualKeyCode) => (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;

    private bool _isMouseCaptured;
    public bool IsMouseCaptured
    {
        get => _isMouseCaptured;
        set
        {
            if (value == _isMouseCaptured) return;
            _isMouseCaptured = value;
            ShowCursor(!value);
            if (value)
            {
                // Re-center immediately so the first PollMouseDelta call doesn't report a huge jump from
                // wherever the cursor happened to be when capture was enabled.
                SetCursorPos(ClientCenterInScreenCoords());
            }
        }
    }

    public (int Dx, int Dy) PollMouseDelta()
    {
        if (!_isMouseCaptured) return (0, 0);

        POINT center = ClientCenterInScreenCoords();
        GetCursorPos(out POINT current);
        int dx = current.x - center.x;
        int dy = current.y - center.y;

        if (dx != 0 || dy != 0)
            SetCursorPos(center);

        return (dx, dy);
    }

    private POINT ClientCenterInScreenCoords()
    {
        POINT center = new() { x = (int)Width / 2, y = (int)Height / 2 };
        ClientToScreen(Handle, ref center);
        return center;
    }

    private static void SetCursorPos(POINT point) => SetCursorPos(point.x, point.y);

    private readonly WndProcDelegate _wndProcDelegate;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    public Win32Window(string title, uint width, uint height)
    {
        Width = width;
        Height = height;
        _wndProcDelegate = WndProc;

        nint hInstance = GetModuleHandleW(null);
        string className = "EngineWindow";

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className,
        };
        RegisterClassExW(ref wc);

        // CreateWindowEx's width/height are the OUTER window size (including title bar/borders); grow the
        // requested rect so the resulting client area is exactly `width` x `height`.
        RECT rect = new() { left = 0, top = 0, right = (int)width, bottom = (int)height };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);
        int outerWidth = rect.right - rect.left;
        int outerHeight = rect.bottom - rect.top;

        Handle = CreateWindowExW(0, className, title, WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT, CW_USEDEFAULT, outerWidth, outerHeight, nint.Zero, nint.Zero, hInstance, nint.Zero);

        ShowWindow(Handle, SW_SHOW);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
                ShouldClose = true;
                DestroyWindow(hWnd);
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
            case WM_SIZE:
                uint newWidth = (uint)(lParam.ToInt64() & 0xFFFF);
                uint newHeight = (uint)((lParam.ToInt64() >> 16) & 0xFFFF);
                if (newWidth > 0 && newHeight > 0 && (newWidth != Width || newHeight != Height))
                {
                    Width = newWidth;
                    Height = newHeight;
                    Resized?.Invoke(Width, Height);
                }
                return 0;
            case WM_KEYDOWN:
                KeyDown?.Invoke((int)wParam.ToInt64());
                return 0;
            case WM_CHAR:
                CharInput?.Invoke((char)wParam.ToInt64());
                return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void PumpMessages()
    {
        while (PeekMessageW(out MSG msg, nint.Zero, 0, 0, 1))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public void Dispose() => DestroyWindow(Handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "AdjustWindowRectEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "SetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "ShowCursor")]
    private static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);
}
