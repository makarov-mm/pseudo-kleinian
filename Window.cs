// The one window of the program: class registration, pixel format, an
// OpenGL 3.3 core context, the message pump and mouse / keyboard input.
// Single-threaded; the input callbacks run inside PumpMessages().
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kleinian;

internal static unsafe class Window
{
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static bool Minimized { get; private set; }
    public static bool Dragging { get; private set; } // left button held

    public static Action<int>? KeyDown;        // virtual-key code
    public static Action<int, int>? MouseDrag; // pixels moved with the left button held
    public static Action<int>? MouseWheel;     // wheel delta, 120 per notch

    private const string ClassName = "KleinianWindow";

    private static nint s_hwnd, s_hdc, s_context;
    private static int s_lastX, s_lastY;
    private static Exception? s_callbackError;

    // `width` x `height` is the client area in 96-dpi units; on a HiDPI
    // display the window is scaled up so it opens at the same apparent size
    // and renders every physical pixel.
    public static void Create(string title, int width, int height)
    {
        uint dpi = EnableDpiAwareness();
        width = (int)((long)width * dpi / Win32.USER_DEFAULT_SCREEN_DPI);
        height = (int)((long)height * dpi / Win32.USER_DEFAULT_SCREEN_DPI);

        nint instance = Win32.GetModuleHandleW(0);
        RegisterClass(instance);

        Win32.RECT frame = new() { right = width, bottom = height };
        try { Win32.AdjustWindowRectExForDpi(&frame, Win32.WS_OVERLAPPEDWINDOW, false, 0, dpi); }
        catch (EntryPointNotFoundException) { Win32.AdjustWindowRect(&frame, Win32.WS_OVERLAPPEDWINDOW, false); }

        s_hwnd = Win32.CreateWindowExW(0, ClassName, title, Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE,
                                       Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
                                       frame.right - frame.left, frame.bottom - frame.top, 0, 0, instance, 0);
        if (s_hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        s_hdc = Win32.GetDC(s_hwnd);

        var pfd = new Win32.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)sizeof(Win32.PIXELFORMATDESCRIPTOR),
            nVersion = 1,
            dwFlags = Win32.PFD_DRAW_TO_WINDOW | Win32.PFD_SUPPORT_OPENGL | Win32.PFD_DOUBLEBUFFER,
            iPixelType = Win32.PFD_TYPE_RGBA,
            cColorBits = 32,
            iLayerType = Win32.PFD_MAIN_PLANE,
        };
        int format = Win32.ChoosePixelFormat(s_hdc, &pfd);
        if (format == 0) throw new InvalidOperationException("ChoosePixelFormat failed");
        if (!Win32.SetPixelFormat(s_hdc, format, &pfd)) throw new InvalidOperationException("SetPixelFormat failed");

        s_context = CreateContext(s_hdc);
        GL.Load();
        if (GL.wglSwapIntervalEXT is not null) GL.wglSwapIntervalEXT(1);

        Win32.RECT rc;
        if (Win32.GetClientRect(s_hwnd, &rc))
        {
            Width = Math.Max(1, rc.right - rc.left);
            Height = Math.Max(1, rc.bottom - rc.top);
        }
    }

    public static void SetTitle(string title) => Win32.SetWindowTextW(s_hwnd, title);

    public static void Swap() => Win32.SwapBuffers(s_hdc);

    // Dispatches pending messages; returns false once the window is closing.
    // While minimized it blocks until the next message instead of spinning.
    public static bool PumpMessages()
    {
        if (Minimized) Win32.WaitMessage();
        Win32.MSG msg;
        while (Win32.PeekMessageW(&msg, 0, 0, 0, Win32.PM_REMOVE))
        {
            if (msg.message == Win32.WM_QUIT) return false;
            Win32.TranslateMessage(&msg);
            Win32.DispatchMessageW(&msg);
            if (s_callbackError is { } error)
            {
                s_callbackError = null;
                throw error;
            }
        }
        return true;
    }

    public static void Destroy()
    {
        Win32.wglMakeCurrent(0, 0);
        if (s_context != 0) Win32.wglDeleteContext(s_context);
        if (s_hdc != 0) _ = Win32.ReleaseDC(s_hwnd, s_hdc);
        if (s_hwnd != 0) Win32.DestroyWindow(s_hwnd);
        s_context = s_hdc = s_hwnd = 0;
    }

    // Per-monitor DPI awareness: the client area gets real pixels instead of
    // a bitmap-scaled window on high-DPI screens. The API appeared in Windows
    // 10 1703; without it the window is simply DPI-unaware at 96 dpi.
    private static uint EnableDpiAwareness()
    {
        try
        {
            Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            return Win32.GetDpiForSystem();
        }
        catch (EntryPointNotFoundException)
        {
            return Win32.USER_DEFAULT_SCREEN_DPI;
        }
    }

    // ---- window procedure ---------------------------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
            case Win32.WM_SIZE:
                Minimized = (int)wParam == Win32.SIZE_MINIMIZED;
                Width = Math.Max(1, Win32.LoWord(lParam));
                Height = Math.Max(1, Win32.HiWord(lParam));
                return 0;

            case Win32.WM_DPICHANGED: // moved to a monitor with another scale: take the suggested rect
                Win32.RECT* suggested = (Win32.RECT*)lParam;
                Win32.SetWindowPos(hwnd, 0, suggested->left, suggested->top,
                                   suggested->right - suggested->left, suggested->bottom - suggested->top,
                                   Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return 0;

            case Win32.WM_LBUTTONDOWN:
                Dragging = true;
                s_lastX = Win32.LoWord(lParam);
                s_lastY = Win32.HiWord(lParam);
                Win32.SetCapture(hwnd); // keep receiving moves and the button-up outside the window
                return 0;

            case Win32.WM_LBUTTONUP:
                Dragging = false;
                Win32.ReleaseCapture();
                return 0;

            case Win32.WM_MOUSEMOVE:
                if (Dragging)
                {
                    int x = Win32.LoWord(lParam), y = Win32.HiWord(lParam);
                    MouseDrag?.Invoke(x - s_lastX, y - s_lastY);
                    s_lastX = x;
                    s_lastY = y;
                }
                return 0;

            case Win32.WM_MOUSEWHEEL:
                MouseWheel?.Invoke(Win32.HiWord(wParam));
                return 0;

            case Win32.WM_KEYDOWN:
                KeyDown?.Invoke((int)wParam);
                return 0;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
            }
        }
        catch (Exception e)
        {
            s_callbackError = e; // rethrown by PumpMessages: exceptions must not cross into Windows
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void RegisterClass(nint instance)
    {
        fixed (char* className = ClassName)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_OWNDC | Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = &WndProc,
                hInstance = instance,
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = className,
            };
            if (Win32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    // A legacy context first, which is the only way to reach
    // wglCreateContextAttribsARB, then the 3.3 core context the shaders are
    // written for. Without the extension the legacy context stays: current
    // drivers expose GLSL 3.30 there too.
    private static nint CreateContext(nint hdc)
    {
        nint legacy = Win32.wglCreateContext(hdc);
        if (legacy == 0 || !Win32.wglMakeCurrent(hdc, legacy))
            throw new InvalidOperationException("wglCreateContext failed");

        var create = (delegate* unmanaged[Stdcall]<nint, nint, int*, nint>)GL.TryProc("wglCreateContextAttribsARB");
        if (create is null) return legacy;

        int* attribs = stackalloc int[]
        {
            Win32.WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
            Win32.WGL_CONTEXT_MINOR_VERSION_ARB, 3,
            Win32.WGL_CONTEXT_PROFILE_MASK_ARB, Win32.WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
            0,
        };
        nint core = create(hdc, 0, attribs);
        if (core == 0) return legacy;
        if (!Win32.wglMakeCurrent(hdc, core))
        {
            Win32.wglDeleteContext(core);
            Win32.wglMakeCurrent(hdc, legacy);
            return legacy;
        }
        Win32.wglDeleteContext(legacy);
        return core;
    }
}
