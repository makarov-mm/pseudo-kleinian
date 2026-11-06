// The slice of Win32, GDI and WGL the program needs, as source-generated P/Invoke.
using System.Runtime.InteropServices;

// System DLLs only: no lookup in the application directory (DLL hijacking).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Kleinian;

internal static unsafe partial class Win32
{
    public const uint CS_VREDRAW = 0x0001, CS_HREDRAW = 0x0002, CS_OWNDC = 0x0020;
    public const uint WS_VISIBLE = 0x10000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int IDC_ARROW = 32512;
    public const uint PM_REMOVE = 0x0001;
    public const uint MB_ICONERROR = 0x00000010;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const uint USER_DEFAULT_SCREEN_DPI = 96;

    public const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_QUIT = 0x0012, WM_KEYDOWN = 0x0100;
    public const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEWHEEL = 0x020A;
    public const uint WM_DPICHANGED = 0x02E0;
    public const int SIZE_MINIMIZED = 1;

    public const int VK_BACK = 0x08, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_1 = 0x31, VK_E = 0x45, VK_M = 0x4D;

    public const uint PFD_DOUBLEBUFFER = 0x00000001, PFD_DRAW_TO_WINDOW = 0x00000004, PFD_SUPPORT_OPENGL = 0x00000020;
    public const byte PFD_TYPE_RGBA = 0, PFD_MAIN_PLANE = 0;

    public const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // WGL_ARB_create_context
    public const int WGL_CONTEXT_MAJOR_VERSION_ARB = 0x2091, WGL_CONTEXT_MINOR_VERSION_ARB = 0x2092;
    public const int WGL_CONTEXT_PROFILE_MASK_ARB = 0x9126, WGL_CONTEXT_CORE_PROFILE_BIT_ARB = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers;
        public byte iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(nint lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
                                               int x, int y, int w, int h, nint parent, nint menu, nint hInstance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hWnd, string text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, RECT* rc);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessageW(MSG* msg, nint hWnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WaitMessage();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* msg);

    // Windows 10 1703+; callers catch EntryPointNotFoundException on older systems.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectExForDpi(RECT* rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRect(RECT* rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [LibraryImport("gdi32.dll")]
    public static partial int ChoosePixelFormat(nint hdc, PIXELFORMATDESCRIPTOR* pfd);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPixelFormat(nint hdc, int format, PIXELFORMATDESCRIPTOR* pfd);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SwapBuffers(nint hdc);

    [LibraryImport("opengl32.dll")]
    public static partial nint wglCreateContext(nint hdc);

    [LibraryImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglMakeCurrent(nint hdc, nint ctx);

    [LibraryImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglDeleteContext(nint ctx);

    [LibraryImport("opengl32.dll", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint wglGetProcAddress(string name);

    public static int LoWord(nint v) => (short)((long)v & 0xFFFF);
    public static int HiWord(nint v) => (short)(((long)v >> 16) & 0xFFFF);
}
