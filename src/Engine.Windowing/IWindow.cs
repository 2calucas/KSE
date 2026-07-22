namespace Engine.Windowing;

public interface IWindow : IDisposable
{
    /// <summary>Native window handle (HWND on Win32) — what a graphics backend needs to create a surface/swapchain.</summary>
    nint Handle { get; }

    uint Width { get; }
    uint Height { get; }

    /// <summary>True once the user has requested the window close (e.g. clicked the close button).</summary>
    bool ShouldClose { get; }

    /// <summary>Raised when the window's client area changes size, with the new (width, height).</summary>
    event Action<uint, uint>? Resized;

    /// <summary>Raised on key-down, with the platform virtual-key code (Win32 VK_* on this backend).</summary>
    event Action<int>? KeyDown;

    /// <summary>Raised once per typed character (Win32 WM_CHAR), already resolved for shift/caps-lock/keyboard
    /// layout — use this instead of <see cref="KeyDown"/> for text-entry fields (e.g. a login form).</summary>
    event Action<char>? CharInput;

    /// <summary>True while this window is the foreground (focused) window. Gate continuous input polling on this
    /// so held keys don't keep affecting the app while some other window has focus.</summary>
    bool HasFocus { get; }

    /// <summary>Polls whether a virtual-key (or mouse button — VK_LBUTTON/VK_RBUTTON/VK_MBUTTON work too) is
    /// currently held, independent of the KeyDown event. Safe to call every frame for continuous movement.</summary>
    bool IsKeyDown(int virtualKeyCode);

    /// <summary>When set true, hides the cursor and re-centers it every <see cref="PollMouseDelta"/> call so
    /// mouse-look gets unbounded relative motion (FPS/editor-camera style) instead of clamping at screen edges.</summary>
    bool IsMouseCaptured { get; set; }

    /// <summary>Mouse movement in pixels since the last call. Returns (0,0) when not captured. Call once per frame.</summary>
    (int Dx, int Dy) PollMouseDelta();

    /// <summary>Pumps the platform's message queue. Call once per frame from the render loop.</summary>
    void PumpMessages();
}
