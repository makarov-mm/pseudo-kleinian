// OpenGL 3.3 core: the entry points the renderer uses. The OpenGL 1.1
// functions come straight from opengl32.dll; everything newer is fetched with
// wglGetProcAddress once a context is current and called through unmanaged
// function pointers (no delegates, no marshalling per call).
using System.Runtime.InteropServices;
using System.Text;

namespace Kleinian;

internal static unsafe partial class GL
{
    public const uint COLOR_BUFFER_BIT = 0x4000;
    public const uint FLOAT = 0x1406, UNSIGNED_BYTE = 0x1401;
    public const uint ARRAY_BUFFER = 0x8892, STATIC_DRAW = 0x88E4;
    public const uint VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30;
    public const uint COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82;
    public const uint TRIANGLE_STRIP = 0x0005;
    public const uint TEXTURE_2D = 0x0DE1, TEXTURE0 = 0x84C0;
    public const uint RGB = 0x1907, RGBA = 0x1908, RGBA8 = 0x8058, RGBA16F = 0x881A;
    public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800;
    public const uint TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
    public const uint LINEAR = 0x2601, CLAMP_TO_EDGE = 0x812F;
    public const uint FRAMEBUFFER = 0x8D40, COLOR_ATTACHMENT0 = 0x8CE0, FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint PACK_ALIGNMENT = 0x0D05;

    [LibraryImport("opengl32.dll")] public static partial void glClear(uint mask);
    [LibraryImport("opengl32.dll")] public static partial void glClearColor(float r, float g, float b, float a);
    [LibraryImport("opengl32.dll")] public static partial void glViewport(int x, int y, int w, int h);
    [LibraryImport("opengl32.dll")] public static partial void glDrawArrays(uint mode, int first, int count);
    [LibraryImport("opengl32.dll")] public static partial void glGenTextures(int n, uint* textures);
    [LibraryImport("opengl32.dll")] public static partial void glDeleteTextures(int n, uint* textures);
    [LibraryImport("opengl32.dll")] public static partial void glBindTexture(uint target, uint texture);
    [LibraryImport("opengl32.dll")] public static partial void glTexParameteri(uint target, uint name, int value);
    [LibraryImport("opengl32.dll")] public static partial void glTexImage2D(uint target, int level, int internalFormat, int width, int height,
                                                                            int border, uint format, uint type, void* pixels);
    [LibraryImport("opengl32.dll")] public static partial void glReadPixels(int x, int y, int width, int height, uint format, uint type, void* data);
    [LibraryImport("opengl32.dll")] public static partial void glPixelStorei(uint name, int value);

    public static delegate* unmanaged[Stdcall]<uint, uint> glCreateShader;
    public static delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void> glShaderSource;
    public static delegate* unmanaged[Stdcall]<uint, void> glCompileShader;
    public static delegate* unmanaged[Stdcall]<uint, uint, int*, void> glGetShaderiv;
    public static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> glGetShaderInfoLog;
    public static delegate* unmanaged[Stdcall]<uint> glCreateProgram;
    public static delegate* unmanaged[Stdcall]<uint, uint, void> glAttachShader;
    public static delegate* unmanaged[Stdcall]<uint, void> glLinkProgram;
    public static delegate* unmanaged[Stdcall]<uint, uint, int*, void> glGetProgramiv;
    public static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> glGetProgramInfoLog;
    public static delegate* unmanaged[Stdcall]<uint, void> glUseProgram;
    public static delegate* unmanaged[Stdcall]<uint, void> glDeleteShader;
    public static delegate* unmanaged[Stdcall]<uint, byte*, int> glGetUniformLocation;
    public static delegate* unmanaged[Stdcall]<int, int, void> glUniform1i;
    public static delegate* unmanaged[Stdcall]<int, float, void> glUniform1f;
    public static delegate* unmanaged[Stdcall]<int, float, float, void> glUniform2f;
    public static delegate* unmanaged[Stdcall]<int, float, float, float, void> glUniform3f;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glGenVertexArrays;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glGenBuffers;
    public static delegate* unmanaged[Stdcall]<uint, void> glBindVertexArray;
    public static delegate* unmanaged[Stdcall]<uint, uint, void> glBindBuffer;
    public static delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void> glBufferData;
    public static delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, nint, void> glVertexAttribPointer;
    public static delegate* unmanaged[Stdcall]<uint, void> glEnableVertexAttribArray;
    public static delegate* unmanaged[Stdcall]<uint, void> glActiveTexture;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glGenFramebuffers;
    public static delegate* unmanaged[Stdcall]<int, uint*, void> glDeleteFramebuffers;
    public static delegate* unmanaged[Stdcall]<uint, uint, void> glBindFramebuffer;
    public static delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> glFramebufferTexture2D;
    public static delegate* unmanaged[Stdcall]<uint, uint> glCheckFramebufferStatus;
    public static delegate* unmanaged[Stdcall]<int, int> wglSwapIntervalEXT; // null if unsupported

    public static void Load()
    {
        glCreateShader = (delegate* unmanaged[Stdcall]<uint, uint>)Proc("glCreateShader");
        glShaderSource = (delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void>)Proc("glShaderSource");
        glCompileShader = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glCompileShader");
        glGetShaderiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Proc("glGetShaderiv");
        glGetShaderInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)Proc("glGetShaderInfoLog");
        glCreateProgram = (delegate* unmanaged[Stdcall]<uint>)Proc("glCreateProgram");
        glAttachShader = (delegate* unmanaged[Stdcall]<uint, uint, void>)Proc("glAttachShader");
        glLinkProgram = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glLinkProgram");
        glGetProgramiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Proc("glGetProgramiv");
        glGetProgramInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)Proc("glGetProgramInfoLog");
        glUseProgram = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glUseProgram");
        glDeleteShader = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glDeleteShader");
        glGetUniformLocation = (delegate* unmanaged[Stdcall]<uint, byte*, int>)Proc("glGetUniformLocation");
        glUniform1i = (delegate* unmanaged[Stdcall]<int, int, void>)Proc("glUniform1i");
        glUniform1f = (delegate* unmanaged[Stdcall]<int, float, void>)Proc("glUniform1f");
        glUniform2f = (delegate* unmanaged[Stdcall]<int, float, float, void>)Proc("glUniform2f");
        glUniform3f = (delegate* unmanaged[Stdcall]<int, float, float, float, void>)Proc("glUniform3f");
        glGenVertexArrays = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glGenVertexArrays");
        glGenBuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glGenBuffers");
        glBindVertexArray = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glBindVertexArray");
        glBindBuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Proc("glBindBuffer");
        glBufferData = (delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void>)Proc("glBufferData");
        glVertexAttribPointer = (delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, nint, void>)Proc("glVertexAttribPointer");
        glEnableVertexAttribArray = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glEnableVertexAttribArray");
        glActiveTexture = (delegate* unmanaged[Stdcall]<uint, void>)Proc("glActiveTexture");
        glGenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glGenFramebuffers");
        glDeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Proc("glDeleteFramebuffers");
        glBindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Proc("glBindFramebuffer");
        glFramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)Proc("glFramebufferTexture2D");
        glCheckFramebufferStatus = (delegate* unmanaged[Stdcall]<uint, uint>)Proc("glCheckFramebufferStatus");
        wglSwapIntervalEXT = (delegate* unmanaged[Stdcall]<int, int>)TryProc("wglSwapIntervalEXT");
    }

    // wglGetProcAddress reports failure as 0, 1, 2, 3 or -1 depending on the driver.
    public static nint TryProc(string name)
    {
        nint p = Win32.wglGetProcAddress(name);
        return p is 0 or 1 or 2 or 3 or -1 ? 0 : p;
    }

    private static nint Proc(string name)
    {
        nint p = TryProc(name);
        return p != 0 ? p : throw new InvalidOperationException($"OpenGL function not available: {name}");
    }

    // ---- helpers -----------------------------------------------------------

    // `name` is a null-terminated UTF-8 literal, e.g. "uRes"u8.
    public static int Uniform(uint program, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            return glGetUniformLocation(program, p);
    }

    public static void Uniform(int location, System.Numerics.Vector3 v) => glUniform3f(location, v.X, v.Y, v.Z);

    public static void BindTexture(uint unit, uint texture)
    {
        glActiveTexture(TEXTURE0 + unit);
        glBindTexture(TEXTURE_2D, texture);
    }

    // A fullscreen triangle strip on attribute 0 (vec2 clip-space position).
    public static uint CreateQuad()
    {
        ReadOnlySpan<float> corners = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];
        uint vao, vbo;
        glGenVertexArrays(1, &vao);
        glBindVertexArray(vao);
        glGenBuffers(1, &vbo);
        glBindBuffer(ARRAY_BUFFER, vbo);
        fixed (float* p = corners)
            glBufferData(ARRAY_BUFFER, corners.Length * sizeof(float), p, STATIC_DRAW);
        glVertexAttribPointer(0, 2, FLOAT, 0, 2 * sizeof(float), 0);
        glEnableVertexAttribArray(0);
        return vao;
    }

    // An empty 2D texture with linear filtering and edge clamping.
    public static uint CreateTexture(int width, int height, uint internalFormat, uint type)
    {
        uint texture;
        glGenTextures(1, &texture);
        glBindTexture(TEXTURE_2D, texture);
        glTexImage2D(TEXTURE_2D, 0, (int)internalFormat, width, height, 0, RGBA, type, null);
        glTexParameteri(TEXTURE_2D, TEXTURE_MIN_FILTER, (int)LINEAR);
        glTexParameteri(TEXTURE_2D, TEXTURE_MAG_FILTER, (int)LINEAR);
        glTexParameteri(TEXTURE_2D, TEXTURE_WRAP_S, (int)CLAMP_TO_EDGE);
        glTexParameteri(TEXTURE_2D, TEXTURE_WRAP_T, (int)CLAMP_TO_EDGE);
        return texture;
    }

    public static void DeleteTexture(uint texture) => glDeleteTextures(1, &texture);

    // A framebuffer rendering into `texture`; leaves the default framebuffer bound.
    public static uint CreateFramebuffer(uint texture)
    {
        uint framebuffer;
        glGenFramebuffers(1, &framebuffer);
        glBindFramebuffer(FRAMEBUFFER, framebuffer);
        glFramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, TEXTURE_2D, texture, 0);
        uint status = glCheckFramebufferStatus(FRAMEBUFFER);
        glBindFramebuffer(FRAMEBUFFER, 0);
        if (status != FRAMEBUFFER_COMPLETE) throw new InvalidOperationException($"Framebuffer incomplete: 0x{status:X}");
        return framebuffer;
    }

    public static void DeleteFramebuffer(uint framebuffer) => glDeleteFramebuffers(1, &framebuffer);

    public static uint BuildProgram(string vertex, string fragment)
    {
        uint vs = Compile(VERTEX_SHADER, vertex);
        uint fs = Compile(FRAGMENT_SHADER, fragment);

        uint program = glCreateProgram();
        glAttachShader(program, vs);
        glAttachShader(program, fs);
        glLinkProgram(program);

        int ok;
        glGetProgramiv(program, LINK_STATUS, &ok);
        if (ok == 0) throw new InvalidOperationException("Shader link error: " + InfoLog(program, false));

        glDeleteShader(vs);
        glDeleteShader(fs);
        return program;
    }

    private static uint Compile(uint type, string source)
    {
        uint shader = glCreateShader(type);
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        int length = utf8.Length;
        fixed (byte* p = utf8)
        {
            byte** sources = stackalloc byte*[1];
            sources[0] = p;
            glShaderSource(shader, 1, sources, &length);
        }
        glCompileShader(shader);

        int ok;
        glGetShaderiv(shader, COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            string kind = type == VERTEX_SHADER ? "vertex" : "fragment";
            throw new InvalidOperationException($"{kind} shader compile error: " + InfoLog(shader, true));
        }
        return shader;
    }

    private static string InfoLog(uint obj, bool shader)
    {
        byte* log = stackalloc byte[4096];
        int length;
        if (shader) glGetShaderInfoLog(obj, 4096, &length, log);
        else glGetProgramInfoLog(obj, 4096, &length, log);
        return Encoding.UTF8.GetString(log, Math.Max(0, length));
    }
}
